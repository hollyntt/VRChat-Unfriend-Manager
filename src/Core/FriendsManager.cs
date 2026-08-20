using System.Text.Json;
using VRCUFM.AppSystem;
using VRCUFM.Filesystem;
using File = System.IO.File;

namespace VRCUFM.Core;

public static class FriendsManager
{
    private static readonly List<UnfriendLogEntry> _unfriendLog = new();
    private static readonly Dictionary<string, FriendNote> _friendNotes = new(StringComparer.OrdinalIgnoreCase);
    private static bool _loaded;

    public static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;

        if (File.Exists(Paths.UnfriendLogFile))
        {
            try
            {
                var json = File.ReadAllText(Paths.UnfriendLogFile);
                var log = JsonSerializer.Deserialize<List<UnfriendLogEntry>>(json);
                if (log != null) _unfriendLog.AddRange(log);
            }
            catch { }
        }

        if (File.Exists(Paths.FriendNotesFile))
        {
            try
            {
                var json = File.ReadAllText(Paths.FriendNotesFile);
                var notes = JsonSerializer.Deserialize<List<FriendNote>>(json);
                if (notes != null)
                    foreach (var n in notes)
                        if (!string.IsNullOrEmpty(n.UserId))
                            _friendNotes[n.UserId] = n;
            }
            catch { }
        }
    }

    public static void SaveData()
    {
        try
        {
            Paths.EnsureExists();
            File.WriteAllText(Paths.UnfriendLogFile,
                JsonSerializer.Serialize(_unfriendLog, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }

        try
        {
            Paths.EnsureExists();
            File.WriteAllText(Paths.FriendNotesFile,
                JsonSerializer.Serialize(_friendNotes.Values.ToList(), new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    public static void LogUnfriend(SafeLimitedUserFriend friend, string reason)
    {
        EnsureLoaded();
        _unfriendLog.Add(new UnfriendLogEntry
        {
            UserId = friend.Id,
            DisplayName = friend.DisplayName,
            UnfriendedAt = DateTime.UtcNow,
            Reason = reason,
            TimeSpentMsBefore = friend.TimeSpentMs,
            LastLoginBefore = friend.LastLogin
        });
        SaveData();
    }

    public static IReadOnlyList<UnfriendLogEntry> GetUnfriendLog()
    {
        EnsureLoaded();
        return _unfriendLog;
    }

    public static void ClearUnfriendLog()
    {
        EnsureLoaded();
        _unfriendLog.Clear();
        SaveData();
    }

    public static void SetNote(string userId, string note)
    {
        EnsureLoaded();
        if (string.IsNullOrWhiteSpace(note))
        {
            _friendNotes.Remove(userId);
        }
        else
        {
            _friendNotes[userId] = new FriendNote
            {
                UserId = userId,
                Note = note.Trim(),
                UpdatedAt = DateTime.UtcNow
            };
        }
        SaveData();
    }

    public static string? GetNote(string userId)
    {
        EnsureLoaded();
        return _friendNotes.TryGetValue(userId, out var n) ? n.Note : null;
    }

    // -- Bulk Selection ---------------------------------------------------

    public static void SelectAllInactive(List<SafeLimitedUserFriend> shown, HashSet<int> selected, DateTime cutoff)
    {
        for (int i = 0; i < shown.Count; i++)
            if (string.IsNullOrEmpty(shown[i].LastLogin) || DateTime.Parse(shown[i].LastLogin) < cutoff)
                selected.Add(i);
    }

    public static void SelectAllLowTime(List<SafeLimitedUserFriend> shown, HashSet<int> selected, long thresholdMs)
    {
        for (int i = 0; i < shown.Count; i++)
            if (shown[i].TimeSpentMs < thresholdMs)
                selected.Add(i);
    }

    public static void SelectNonFavorites(List<SafeLimitedUserFriend> shown, HashSet<int> selected, HashSet<string> favorites)
    {
        for (int i = 0; i < shown.Count; i++)
            if (!favorites.Contains(shown[i].Id))
                selected.Add(i);
    }

    public static void InvertSelection(List<SafeLimitedUserFriend> shown, HashSet<int> selected)
    {
        var inverted = new HashSet<int>();
        for (int i = 0; i < shown.Count; i++)
            if (!selected.Contains(i)) inverted.Add(i);
        selected.Clear();
        foreach (var idx in inverted) selected.Add(idx);
    }

    public static void SelectLowScore(List<SafeLimitedUserFriend> shown, HashSet<int> selected, HashSet<string> favorites, int maxScore)
    {
        for (int i = 0; i < shown.Count; i++)
            if (CalculateFriendScore(shown[i], favorites) <= maxScore)
                selected.Add(i);
    }

    // -- Scoring & Stats --------------------------------------------------

    /// <summary>Simple 0-100 score from favorites, activity, time together, bio, notes.</summary>
    public static int CalculateFriendScore(SafeLimitedUserFriend friend, HashSet<string> favorites)
    {
        int score = 0;

        if (favorites != null && favorites.Contains(friend.Id))
            score += 20;

        if (!string.IsNullOrEmpty(friend.LastLogin) && DateTime.TryParse(friend.LastLogin, out var last))
        {
            double days = (DateTime.UtcNow - last.ToUniversalTime()).TotalDays;
            if (days <= 1) score += 30;
            else if (days <= 7) score += 24;
            else if (days <= 30) score += 16;
            else if (days <= 90) score += 8;
            else if (days <= 365) score += 3;
        }

        double hours = friend.TimeSpentMs / 3600000.0;
        if (hours >= 50) score += 30;
        else if (hours >= 20) score += 22;
        else if (hours >= 5) score += 14;
        else if (hours >= 1) score += 8;
        else if (hours > 0) score += 3;

        if (!string.IsNullOrWhiteSpace(friend.Bio))
            score += friend.Bio.Length >= 40 ? 10 : 4;

        var note = GetNote(friend.Id);
        if (!string.IsNullOrWhiteSpace(note))
            score += 5;

        if (score > 100) score = 100;
        if (score < 0) score = 0;
        return score;
    }

    public static FriendStats CalculateStats(
        List<SafeLimitedUserFriend> friends,
        HashSet<string> favorites,
        Dictionary<string, HashSet<string>> favByGroup)
    {
        var stats = new FriendStats { TotalFriends = friends.Count };
        var groupCounts = new Dictionary<string, int>();

        foreach (var f in friends)
        {
            if (!string.IsNullOrEmpty(f.LastLogin))
            {
                var days = (DateTime.UtcNow - DateTime.Parse(f.LastLogin)).TotalDays;
                if (days < 1) stats.OnlineFriends++;
                if (days > 90) stats.InactiveFriends++;
                if (days > 365) stats.GhostFriends++;
            }
            else
            {
                stats.InactiveFriends++;
                stats.GhostFriends++;
            }

            stats.TotalTimeTogetherMs += f.TimeSpentMs;

            foreach (var (tag, ids) in favByGroup)
            {
                if (ids.Contains(f.Id))
                {
                    groupCounts.TryGetValue(tag, out var c);
                    groupCounts[tag] = c + 1;
                }
            }
        }

        stats.FavoritesCount = favorites.Count;
        stats.AverageTimeTogetherMs = friends.Count > 0 ? stats.TotalTimeTogetherMs / friends.Count : 0;
        stats.GroupDistribution = groupCounts;
        stats.InFavoriteGroups = groupCounts.Values.Sum();
        return stats;
    }
}

public class FriendStats
{
    public int TotalFriends { get; set; }
    public int OnlineFriends { get; set; }
    public int InactiveFriends { get; set; }
    public int GhostFriends { get; set; }
    public long TotalTimeTogetherMs { get; set; }
    public double AverageTimeTogetherMs { get; set; }
    public int FavoritesCount { get; set; }
    public int InFavoriteGroups { get; set; }
    public Dictionary<string, int> GroupDistribution { get; set; } = new();
}
