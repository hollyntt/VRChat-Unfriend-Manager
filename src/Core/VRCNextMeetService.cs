using System.Collections.Concurrent;
using VRCUFM.Filesystem;

namespace VRCUFM.Core;

/// <summary>
/// Read-only access to VRCNext user_tracking meet counts.
/// Schema (from VRCNext TimelineService):
///   total meets = meet_again_count + CASE WHEN first_meet_date != '' THEN 1 ELSE 0 END
/// Events: first_meet, meet_again.
/// </summary>
public static class VRCNextMeetService
{
    private static string DbPath => Path.Combine(Paths.VrcNextBase, "VRCNData.db");
    public static bool IsAvailable => File.Exists(DbPath);

    public sealed class MeetInfo
    {
        public string UserId { get; init; } = "";
        public DateTime? FirstMeetDate { get; init; }
        public int MeetAgainCount { get; init; }
        /// <summary>Total distinct meets (first + again).</summary>
        public int TotalMeets { get; init; }
        public string Label { get; init; } = "Never met";
    }

    static readonly ConcurrentDictionary<string, MeetInfo> _cache = new(StringComparer.OrdinalIgnoreCase);
    static DateTime _cacheAt = DateTime.MinValue;
    static readonly object _lock = new();
    const int CacheSeconds = 60;

    public static MeetInfo? Get(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId) || !IsAvailable) return null;
        EnsureLoaded();
        return _cache.TryGetValue(userId, out var info) ? info : null;
    }

    public static int TotalMeets(string userId) => Get(userId)?.TotalMeets ?? 0;

    public static string Label(string userId) => Get(userId)?.Label ?? "Never met";

    public static bool HasMetBefore(string userId)
    {
        var m = Get(userId);
        return m != null && m.TotalMeets > 0;
    }

    static void EnsureLoaded()
    {
        if ((DateTime.UtcNow - _cacheAt).TotalSeconds < CacheSeconds && _cache.Count > 0)
            return;

        lock (_lock)
        {
            if ((DateTime.UtcNow - _cacheAt).TotalSeconds < CacheSeconds && _cache.Count > 0)
                return;

            try
            {
                var next = new ConcurrentDictionary<string, MeetInfo>(StringComparer.OrdinalIgnoreCase);
                using var connection = new Microsoft.Data.Sqlite.SqliteConnection(
                    $"Data Source={DbPath};Mode=ReadOnly;Cache=Shared");
                connection.Open();

                using var cmd = connection.CreateCommand();
                // VRCNext: total = meet_again_count + (first_meet_date present ? 1 : 0)
                cmd.CommandText = @"
                    SELECT user_id,
                           first_meet_date,
                           COALESCE(meet_again_count, 0),
                           COALESCE(meet_again_count, 0) + CASE WHEN first_meet_date IS NOT NULL AND first_meet_date != '' THEN 1 ELSE 0 END
                    FROM   user_tracking
                    WHERE  user_id IS NOT NULL AND user_id != ''";

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var uid = reader.GetString(0).Replace("usr_", "", StringComparison.OrdinalIgnoreCase);
                    DateTime? first = null;
                    if (!reader.IsDBNull(1))
                    {
                        var s = reader.GetString(1);
                        if (!string.IsNullOrWhiteSpace(s) && DateTime.TryParse(s, out var dt))
                            first = dt.ToUniversalTime();
                    }
                    int again = reader.IsDBNull(2) ? 0 : Convert.ToInt32(reader.GetValue(2));
                    int total = reader.IsDBNull(3) ? 0 : Convert.ToInt32(reader.GetValue(3));

                    string label = total <= 0
                        ? "Never met"
                        : total == 1
                            ? "First meet"
                            : $"Met {total}x";

                    next[uid] = new MeetInfo
                    {
                        UserId = uid,
                        FirstMeetDate = first,
                        MeetAgainCount = again,
                        TotalMeets = total,
                        Label = label
                    };
                }

                _cache.Clear();
                foreach (var kv in next)
                    _cache[kv.Key] = kv.Value;
                _cacheAt = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[VRCNextMeet] DB read failed: {ex.Message}");
            }
        }
    }

    /// <summary>Force reload on next access (e.g. after long idle).</summary>
    public static void Invalidate() => _cacheAt = DateTime.MinValue;
}