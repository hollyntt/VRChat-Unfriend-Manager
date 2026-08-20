using System.Text.Json;
using VRCUFM.AppSystem;
using VRCUFM.Filesystem;
using VRCUFM.VRChat;
using File = System.IO.File;

namespace VRCUFM.Core;

public static class TrustScoreService
{
    private const int TrustRankMax = 4;
    private const int TrustBadgeTarget = 4;
    private const int TrustYearTarget = 3;
    private const int TrustYearWeight = 3;
    private const int TrustGroupTarget = 20;
    private const double TrustGroupJoinWeight = 0.8;

    private static readonly object _cacheLock = new();
    private static Dictionary<string, CachedProfile> _cache = new(StringComparer.OrdinalIgnoreCase);
    private static bool _cacheLoaded;
    private static CancellationTokenSource? _enrichCts;
    private static int _enrichDone, _enrichTotal;
    private static bool _enriching;

    public static bool IsEnriching => _enriching;
    public static int EnrichDone => _enrichDone;
    public static int EnrichTotal => _enrichTotal;

    private sealed class CachedProfile
    {
        public string DateJoined { get; set; } = "";
        public List<string> Tags { get; set; } = new();
        public int BadgeCount { get; set; }
        public bool AgeVerified { get; set; }
        public bool IsVrcPlus { get; set; }
        public bool IsEconomyCreator { get; set; }
        public int GroupCount { get; set; }
        public bool IsRepresentingGroup { get; set; }
        public int UploadedWorlds { get; set; }
        public int UploadedAvatars { get; set; }
        public string Bio { get; set; } = "";
        public DateTime CachedAt { get; set; }
    }

    public static void EnsureCacheLoaded()
    {
        lock (_cacheLock)
        {
            if (_cacheLoaded) return;
            _cacheLoaded = true;
            try
            {
                if (File.Exists(Paths.TrustProfileCacheFile))
                {
                    var dict = JsonSerializer.Deserialize<Dictionary<string, CachedProfile>>(File.ReadAllText(Paths.TrustProfileCacheFile));
                    if (dict != null)
                        _cache = new Dictionary<string, CachedProfile>(dict, StringComparer.OrdinalIgnoreCase);
                }
            }
            catch { }
        }
    }

    static void SaveCache()
    {
        try
        {
            Paths.EnsureExists();
            lock (_cacheLock)
                File.WriteAllText(Paths.TrustProfileCacheFile, JsonSerializer.Serialize(_cache, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    public static void ApplyCache(SafeLimitedUserFriend f)
    {
        EnsureCacheLoaded();
        lock (_cacheLock)
        {
            if (!_cache.TryGetValue(f.Id, out var c)) return;
            if ((DateTime.UtcNow - c.CachedAt).TotalDays > 7) return;
            f.DateJoined = c.DateJoined; f.Tags = c.Tags ?? new(); f.BadgeCount = c.BadgeCount;
            f.AgeVerified = c.AgeVerified; f.IsVrcPlus = c.IsVrcPlus; f.IsEconomyCreator = c.IsEconomyCreator;
            f.GroupCount = c.GroupCount; f.IsRepresentingGroup = c.IsRepresentingGroup;
            f.UploadedWorlds = c.UploadedWorlds; f.UploadedAvatars = c.UploadedAvatars;
            if (string.IsNullOrWhiteSpace(f.Bio) && !string.IsNullOrWhiteSpace(c.Bio)) f.Bio = c.Bio;
            f.ProfileEnriched = true; f.TrustScore = -1; f.TrustRankLevel = GetTrustRankLevel(f.Tags);
        }
    }

    public static int GetTrustRankLevel(IList<string>? tags)
    {
        if (tags == null || tags.Count == 0) return 0;
        if (tags.Any(t => t is "system_trust_legend" or "system_trust_veteran")) return 4;
        if (tags.Contains("system_trust_trusted")) return 3;
        if (tags.Contains("system_trust_known")) return 2;
        if (tags.Contains("system_trust_basic")) return 1;
        return 0;
    }

    public static int Calculate(SafeLimitedUserFriend u)
    {
        if (u.TrustScore >= 0) return u.TrustScore;
        var tags = u.Tags ?? new List<string>();
        double years = 0;
        if (!string.IsNullOrEmpty(u.DateJoined) && DateTime.TryParse(u.DateJoined, out var joined))
            years = (DateTime.UtcNow - joined.ToUniversalTime()).TotalDays / 365.25;
        int rankLevel = GetTrustRankLevel(tags);
        u.TrustRankLevel = rankLevel;
        var crit = new List<(double score, double weight)>
        {
            (rankLevel / (double)TrustRankMax, 1),
            (u.AgeVerified ? 1.0 : 0.0, 1),
            (Math.Clamp(years / TrustYearTarget, 0.0, 1.0), TrustYearWeight),
            (u.IsVrcPlus || tags.Contains("system_supporter") ? 1.0 : 0.0, 1),
            (Math.Min(Math.Max(0, u.BadgeCount) / (double)TrustBadgeTarget, 1.0), 1),
            (!string.IsNullOrWhiteSpace(u.Bio) ? 1.0 : 0.0, 1),
            ((u.UploadedWorlds + u.UploadedAvatars >= 1) ? 1.0 : 0.0, 1),
            (Math.Min(Math.Max(0, u.GroupCount) / (double)TrustGroupTarget, 1.0) * TrustGroupJoinWeight
             + (u.IsRepresentingGroup ? 1.0 - TrustGroupJoinWeight : 0.0), 1),
        };
        double tw = crit.Sum(c => c.weight);
        if (tw <= 0) { u.TrustScore = 0; return 0; }
        u.TrustScore = Math.Clamp((int)Math.Round(crit.Sum(c => c.score * c.weight) / tw * 100.0), 0, 100);
        return u.TrustScore;
    }

    public static void CancelEnrichment() { _enrichCts?.Cancel(); _enriching = false; }

    public static void StartEnrichment(List<SafeLimitedUserFriend> friends, APIService api)
    {
        CancelEnrichment();
        EnsureCacheLoaded();
        foreach (var f in friends) { ApplyCache(f); if (f.ProfileEnriched) Calculate(f); }
        var need = friends.Where(f => !f.ProfileEnriched).ToList();
        _enrichTotal = friends.Count; _enrichDone = friends.Count - need.Count;
        if (need.Count == 0) return;
        _enrichCts = new CancellationTokenSource();
        var token = _enrichCts.Token;
        _enriching = true;
        _ = Task.Run(async () =>
        {
            try
            {
                int sinceSave = 0;
                foreach (var f in need)
                {
                    if (token.IsCancellationRequested) break;
                    try
                    {
                        var profile = await api.FetchUserTrustProfileAsync(f.Id);
                        if (profile != null)
                        {
                            ApplyProfile(f, profile); Calculate(f);
                            lock (_cacheLock)
                            {
                                _cache[f.Id] = new CachedProfile {
                                    DateJoined = f.DateJoined, Tags = f.Tags.ToList(), BadgeCount = f.BadgeCount,
                                    AgeVerified = f.AgeVerified, IsVrcPlus = f.IsVrcPlus, IsEconomyCreator = f.IsEconomyCreator,
                                    GroupCount = f.GroupCount, IsRepresentingGroup = f.IsRepresentingGroup,
                                    UploadedWorlds = f.UploadedWorlds, UploadedAvatars = f.UploadedAvatars,
                                    Bio = f.Bio, CachedAt = DateTime.UtcNow
                                };
                            }
                            if (++sinceSave >= 25) { SaveCache(); sinceSave = 0; }
                        }
                        else Calculate(f);
                    }
                    catch (Exception ex) { Console.WriteLine("[TrustScore] " + f.DisplayName + ": " + ex.Message); try { Calculate(f); } catch { } }
                    Interlocked.Increment(ref _enrichDone);
                    try { await Task.Delay(500, token); } catch (OperationCanceledException) { break; }
                }
                SaveCache();
            }
            finally { _enriching = false; }
        }, token);
    }

    static void ApplyProfile(SafeLimitedUserFriend f, UserTrustProfile p)
    {
        f.DateJoined = p.DateJoined; f.Tags = p.Tags ?? new(); f.BadgeCount = p.BadgeCount;
        f.AgeVerified = p.AgeVerified; f.IsVrcPlus = p.IsVrcPlus; f.IsEconomyCreator = p.IsEconomyCreator;
        f.GroupCount = p.GroupCount; f.IsRepresentingGroup = p.IsRepresentingGroup;
        f.UploadedWorlds = p.UploadedWorlds; f.UploadedAvatars = p.UploadedAvatars;
        if (!string.IsNullOrWhiteSpace(p.Bio)) f.Bio = p.Bio;
        f.ProfileEnriched = true; f.TrustScore = -1; f.TrustRankLevel = GetTrustRankLevel(f.Tags);
    }
}

public class UserTrustProfile
{
    public string DateJoined { get; set; } = "";
    public List<string> Tags { get; set; } = new();
    public int BadgeCount { get; set; }
    public bool AgeVerified { get; set; }
    public bool IsVrcPlus { get; set; }
    public bool IsEconomyCreator { get; set; }
    public int GroupCount { get; set; }
    public bool IsRepresentingGroup { get; set; }
    public int UploadedWorlds { get; set; }
    public int UploadedAvatars { get; set; }
    public string Bio { get; set; } = "";
}
