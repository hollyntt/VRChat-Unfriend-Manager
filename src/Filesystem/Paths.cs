using System.Runtime.InteropServices;

namespace VRCUFM.Filesystem;

public static class Paths
{
    public static readonly string AppDataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VRChatUnfriendManager");
    public static readonly string CookieFile = Path.Combine(AppDataFolder, "session.cookie");
    public static readonly string ConfigFile = Path.Combine(AppDataFolder, "user.config");
    public static readonly string UnfriendLogFile = Path.Combine(AppDataFolder, "unfriend_log.json");
    public static readonly string FriendNotesFile = Path.Combine(AppDataFolder, "friend_notes.json");

    public static string VrcxBase => RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VRCX")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VRCX");

    public static string VrcxStartup => Path.Combine(VrcxBase, "startup");

    public static string VrcNextBase
    {
        get
        {
            string configDirectory;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                string? xdgConfigHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
                configDirectory = !string.IsNullOrWhiteSpace(xdgConfigHome) && Path.IsPathFullyQualified(xdgConfigHome)
                    ? xdgConfigHome
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
            }
            else
            {
                configDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            }

            return Path.Combine(configDirectory, "VRCNext");
        }
    }

    public static string VrcNextStartup => Path.Combine(VrcNextBase, "AutoStart");
    public static string VrcNextDataFile
    {
        get
        {
            string defaultPath = Path.Combine(VrcNextBase, "VRCNData.db");
            if (!Directory.Exists(VrcNextBase)) return defaultPath;

            string[] candidates = Directory
                .EnumerateFiles(VrcNextBase, "*_VRCNData.db", SearchOption.TopDirectoryOnly)
                .Append(defaultPath)
                .Where(File.Exists)
                .ToArray();

            return candidates
                .OrderByDescending(GetVrcNextDatabaseLastWriteTime)
                .FirstOrDefault() ?? defaultPath;
        }
    }

    private static DateTime GetVrcNextDatabaseLastWriteTime(string databasePath)
    {
        DateTime lastWriteTime = File.GetLastWriteTimeUtc(databasePath);
        string walPath = databasePath + "-wal";
        if (File.Exists(walPath))
        {
            DateTime walLastWriteTime = File.GetLastWriteTimeUtc(walPath);
            if (walLastWriteTime > lastWriteTime) lastWriteTime = walLastWriteTime;
        }

        return lastWriteTime;
    }

    public static string DefaultInstallDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VRCUFM");

    public static void EnsureExists() => Directory.CreateDirectory(AppDataFolder);
}
