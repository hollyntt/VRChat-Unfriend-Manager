using System.Runtime.InteropServices;

namespace VRCUFM.Filesystem;

public static class Paths
{
    public static readonly string AppDataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VRChatUnfriendManager");
    public static readonly string CookieFile = Path.Combine(AppDataFolder, "session.cookie");
    public static readonly string ConfigFile = Path.Combine(AppDataFolder, "user.config");
    public static readonly string AccountsFolder = Path.Combine(AppDataFolder, "accounts");
    public static readonly string UnfriendLogFile = Path.Combine(AppDataFolder, "unfriend_log.json");
    public static readonly string FriendNotesFile = Path.Combine(AppDataFolder, "friend_notes.json");

    public static string VrcxBase => RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VRCX")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VRCX");

    public static string VrcxStartup => Path.Combine(VrcxBase, "startup");

    public static string VrcxDataFile
    {
        get
        {
            string defaultPath = Path.Combine(VrcxBase, "VRCX.sqlite3");
            string configPath = Path.Combine(VrcxBase, "VRCX.json");
            if (File.Exists(configPath))
            {
                try
                {
                    using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(configPath));
                    if (document.RootElement.TryGetProperty("VRCX_DatabaseLocation", out var location)
                        && location.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        var customPath = location.GetString()?.Trim();
                        if (!string.IsNullOrWhiteSpace(customPath) && File.Exists(customPath))
                            return customPath;
                    }
                }
                catch { }
            }

            return defaultPath;
        }
    }

    // VRCX-0 uses its own profile directory and database filename.
    public static string VrcxZeroBase
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
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                configDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Library", "Application Support");
            }
            else
            {
                configDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            }

            return Path.Combine(configDirectory, "VRCX-0");
        }
    }

    public static string VrcxZeroDataFile
    {
        get
        {
            string defaultPath = Path.Combine(VrcxZeroBase, "VRCX-0.sqlite3");
            string pointerPath = Path.Combine(VrcxZeroBase, "VRCX-0.data-dir.json");
            if (File.Exists(pointerPath))
            {
                try
                {
                    using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(pointerPath));
                    if (document.RootElement.TryGetProperty("dataDir", out var dataDir)
                        && dataDir.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        var customPath = dataDir.GetString();
                        if (!string.IsNullOrWhiteSpace(customPath))
                        {
                            var customDb = Path.Combine(customPath, "VRCX-0.sqlite3");
                            if (File.Exists(customDb)) return customDb;
                        }
                    }
                }
                catch { }
            }

            return defaultPath;
        }
    }

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
