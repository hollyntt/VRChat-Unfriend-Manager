using VRCUFM.Filesystem;

namespace VRCUFM.VRChat;

public static class VRCNextDataService
{
    private static string DbPath => Path.Combine(Paths.VrcNextBase, "VRCNData.db");
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
                SELECT user_id, total_seconds
                FROM   user_tracking
                WHERE  user_id IS NOT NULL AND user_id != ''
                  AND  total_seconds > 0";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var userId = reader.GetString(0).Replace("usr_", "", StringComparison.OrdinalIgnoreCase);
                var secs   = reader.GetInt64(1);
                result.TryGetValue(userId, out var existing);
                result[userId] = existing + secs;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[VRCNext] DB read failed: {ex.Message}");
        }

        return result;
    }
}
