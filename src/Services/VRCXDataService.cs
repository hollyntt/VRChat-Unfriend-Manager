using VRCUFM.Filesystem;

namespace VRCUFM.VRChat;

public static class VRCXDataService
{
    private static string DbPath => Path.Combine(Paths.VrcxBase, "VRCX.sqlite3");
    public static bool IsAvailable => File.Exists(DbPath);

    public static Dictionary<string, long> LoadTimeSpentSeconds()
    {
        var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        if (!IsAvailable) return result;

        try
        {
            using var connection = new Microsoft.Data.Sqlite.SqliteConnection(
                $"Data Source={DbPath};Mode=ReadOnly;Cache=Shared");
            connection.Open();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                SELECT user_id, created_at, type
                FROM   gamelog_join_leave
                WHERE  user_id IS NOT NULL AND user_id != ''
                ORDER  BY user_id ASC, created_at ASC";

            using var reader = cmd.ExecuteReader();
            var pendingJoin = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

            while (reader.Read())
            {
                var userId = reader.GetString(0);
                if (!DateTime.TryParse(reader.GetString(1), out var ts)) continue;
                var type = reader.GetString(2);

                if (type == "OnPlayerJoined")
                    pendingJoin[userId] = ts;
                else if (type == "OnPlayerLeft" && pendingJoin.TryGetValue(userId, out var joinTime))
                {
                    var secs = (long)(ts - joinTime).TotalSeconds;
                    if (secs > 0)
                    {
                        result.TryGetValue(userId, out var existing);
                        result[userId] = existing + secs;
                    }
                    pendingJoin.Remove(userId);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[VRCX] DB read failed: {ex.Message}");
        }

        return result;
    }
}
