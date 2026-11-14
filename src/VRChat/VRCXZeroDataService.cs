using VRCUFM.Filesystem;

namespace VRCUFM.VRChat;

public static class VRCXZeroDataService
{
    private static string DbPath => Paths.VrcxZeroDataFile;
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

            // VRCX-0 stores the completed duration on OnPlayerLeft rows in
            // milliseconds. This avoids pairing events across instances and
            // also includes sessions repaired by VRCX-0's maintenance tasks.
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                SELECT user_id, time
                FROM   gamelog_join_leave
                WHERE  user_id IS NOT NULL AND trim(user_id) != ''
                   AND type = 'OnPlayerLeft'
                   AND time IS NOT NULL AND time > 0";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var userId = reader.GetString(0).Replace("usr_", "", StringComparison.OrdinalIgnoreCase);
                var milliseconds = reader.GetInt64(1);
                var seconds = milliseconds / 1000L;
                if (seconds <= 0) continue;

                result.TryGetValue(userId, out var existing);
                result[userId] = existing + seconds;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[VRCX-0] DB read failed: {ex.Message}");
        }

        return result;
    }
}
