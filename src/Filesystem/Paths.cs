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
    public static readonly string TrustProfileCacheFile = Path.Combine(AppDataFolder, "trust_profiles.json");

    public static string VrcxBase => RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VRCX")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VRCX");

    public static string VrcxStartup => Path.Combine(VrcxBase, "startup");

    public static string VrcNextBase => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VRCNext");

    public static string VrcNextStartup => Path.Combine(VrcNextBase, "AutoStart");

    public static void EnsureExists() => Directory.CreateDirectory(AppDataFolder);
}
