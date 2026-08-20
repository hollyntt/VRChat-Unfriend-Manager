using System.Text.Json;
using VRCUFM.AppSystem;
using VRCUFM.VRChat;
using File = System.IO.File;

namespace VRCUFM.Core;

/// <summary>
/// VRCNext-style Trusted Score (0-100). Self-contained cache path so a stale Paths.cs cannot break builds.
/// </summary>
public static class TrustScoreService
{
    private const int TrustRankMax = 4;
    private const int TrustBadgeTarget = 4;
    private const int TrustYearTarget = 3;
    private const int TrustYearWeight = 3;
    private const int TrustGroupTarget = 20;
    private const double TrustGroupJoinWeight = 0.8;

    // Own path — do NOT depend on Paths.TrustProfileCacheFile (partial merges kept dropping it).
    private static readonly string CacheFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "VRChatUnfriendManager",
        "trust_profiles.json");

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
                if (File.Exists(CacheFile))
                {
                    var dict = JsonSerializer.Deserialize<Dictionary<string, CachedProfile>>(File.ReadAllText(CacheFile));
                    if (dict != null)
                        _cache = new Dictionary<string, CachedProfile>(dict, StringComparer.OrdinalIgnoreCase);
                }
            }
            catch { /* ignore corrupt cache */ }
        }
    }

    static void SaveCache()
    {
        try
        {
            var dir = Path.GetDirectoryName(CacheFile);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            lock (_cacheLock)
                File.WriteAllText(CacheFile, JsonSerializer.Serialize(_cache, new JsonSerializerOptions { WriteIndented = true }));
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
            ApplyProfile(f, c);
        }
        f.TrustScore = Calculate(f);
    }

    static void ApplyProfile(SafeLimitedUserFriend f, CachedProfile c)
    {
        f.DateJoined = c.DateJoined;
        f.Tags = c.Tags ?? new List<string>();
        f.BadgeCount = c.BadgeCount;
        f.AgeVerified = c.AgeVerified;
        f.IsVrcPlus = c.IsVrcPlus;
        f.IsEconomyCreator = c.IsEconomyCreator;
        f.GroupCount = c.GroupCount;
        f.IsRepresentingGroup = c.IsRepresentingGroup;
        f.UploadedWorlds = c.UploadedWorlds;
        f.UploadedAvatars = c.UploadedAvatars;
        if (!string.IsNullOrEmpty(c.Bio)) f.Bio = c.Bio;
        f.ProfileEnriched = true;
    }

    public static int Calculate(SafeLimitedUserFriend f)
    {
        // Trust rank from tags (system_trust_*)
        double rankScore = 0;
        if (f.Tags != null)
        {
            if (f.Tags.Any(t => t.Contains("system_trust_veteran", StringComparison.OrdinalIgnoreCase))) rankScore = 1.0;
            else if (f.Tags.Any(t => t.Contains("system_trust_trusted", StringComparison.OrdinalIgnoreCase))) rankScore = 0.75;
            else if (f.Tags.Any(t => t.Contains("system_trust_known", StringComparison.OrdinalIgnoreCase))) rankScore = 0.5;
            else if (f.Tags.Any(t => t.Contains("system_trust_positive", StringComparison.OrdinalIgnoreCase))) rankScore = 0.25;
        }

        double ageVerified = f.AgeVerified ? 1.0 : 0.0;

        double years = 0;
        if (!string.IsNullOrEmpty(f.DateJoined) && DateTime.TryParse(f.DateJoined, out var joined))
        {
            years = Math.Max(0, (DateTime.UtcNow - joined.ToUniversalTime()).TotalDays / 365.25);
        }
        double ageScore = Math.Min(years / TrustYearTarget, 1.0);

        double vrcPlus = f.IsVrcPlus || (f.Tags?.Any(t => t.Contains("system_supporter", StringComparison.OrdinalIgnoreCase)) == true) ? 1.0 : 0.0;
        double badges = Math.Min(f.BadgeCount / (double)TrustBadgeTarget, 1.0);
        double bio = string.IsNullOrWhiteSpace(f.Bio) ? 0.0 : 1.0;
        double content = (f.UploadedWorlds + f.UploadedAvatars) >= 1 || f.IsEconomyCreator ? 1.0 : 0.0;
        double groups = Math.Min(f.GroupCount / (double)TrustGroupTarget, 1.0) * TrustGroupJoinWeight
                        + (f.IsRepresentingGroup ? (1.0 - TrustGroupJoinWeight) : 0.0);

        // Weighted sum / total weight * 100  (VRCNext getTrustScorePct)
        double weighted =
            rankScore * 1.0 +
            ageVerified * 1.0 +
            ageScore * TrustYearWeight +
            vrcPlus * 1.0 +
            badges * 1.0 +
            bio * 1.0 +
            content * 1.0 +
            groups * 1.0;

        double totalWeight = 1 + 1 + TrustYearWeight + 1 + 1 + 1 + 1 + 1; // 10
        int pct = (int)Math.Round(Math.Clamp(weighted / totalWeight * 100.0, 0, 100));
        return pct;
    }

    public static void StartEnrichment(IReadOnlyList<SafeLimitedUserFriend> friends, Func<string, Task<UserTrustProfile?>> fetchProfile)
    {
        _enrichCts?.Cancel();
        _enrichCts = new CancellationTokenSource();
        var token = _enrichCts.Token;
        _enriching = true;
        _enrichDone = 0;
        _enrichTotal = friends.Count;

        _ = Task.Run(async () =>
        {
            try
            {
                EnsureCacheLoaded();
                foreach (var f in friends)
                {
                    if (token.IsCancellationRequested) break;

                    bool needFetch;
                    lock (_cacheLock)
                    {
                        needFetch = !_cache.TryGetValue(f.Id, out var c) || (DateTime.UtcNow - c.CachedAt).TotalDays > 7;
                    }

                    if (!needFetch)
                    {
                        ApplyCache(f);
                        Interlocked.Increment(ref _enrichDone);
                        continue;
                    }

                    try
                    {
                        var profile = await fetchProfile(f.Id).ConfigureAwait(false);
                        if (profile != null)
                        {
                            var cached = new CachedProfile
                            {
                                DateJoined = profile.DateJoined ?? "",
                                Tags = profile.Tags ?? new List<string>(),
                                BadgeCount = profile.BadgeCount,
                                AgeVerified = profile.AgeVerified,
                                IsVrcPlus = profile.IsVrcPlus,
                                IsEconomyCreator = profile.IsEconomyCreator,
                                GroupCount = profile.GroupCount,
                                IsRepresentingGroup = profile.IsRepresentingGroup,
                                UploadedWorlds = profile.UploadedWorlds,
                                UploadedAvatars = profile.UploadedAvatars,
                                Bio = profile.Bio ?? "",
                                CachedAt = DateTime.UtcNow
                            };
                            lock (_cacheLock) _cache[f.Id] = cached;
                            ApplyProfile(f, cached);
                            f.TrustScore = Calculate(f);
                            SaveCache();
                        }
                        else
                        {
                            f.TrustScore = Calculate(f);
                        }
                    }
                    catch
                    {
                        f.TrustScore = Calculate(f);
                    }

                    Interlocked.Increment(ref _enrichDone);
                    try { await Task.Delay(500, token).ConfigureAwait(false); } catch { break; }
                }
            }
            finally
            {
                _enriching = false;
            }
        }, token);
    }

    public static void CancelEnrichment()
    {
        try { _enrichCts?.Cancel(); } catch { }
        _enriching = false;
    }
}

/// <summary>Profile payload returned by APIService.FetchUserTrustProfileAsync.</summary>
public sealed class UserTrustProfile
{
    public string? DateJoined { get; set; }
    public List<string>? Tags { get; set; }
    public int BadgeCount { get; set; }
    public bool AgeVerified { get; set; }
    public bool IsVrcPlus { get; set; }
    public bool IsEconomyCreator { get; set; }
    public int GroupCount { get; set; }
    public bool IsRepresentingGroup { get; set; }
    public int UploadedWorlds { get; set; }
    public int UploadedAvatars { get; set; }
    public string? Bio { get; set; }
}
