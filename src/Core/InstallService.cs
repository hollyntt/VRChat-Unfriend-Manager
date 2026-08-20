using System.Diagnostics;
using System.Runtime.InteropServices;
using VRCUFM.AppSystem;
using VRCUFM.Filesystem;
using File = System.IO.File;

namespace VRCUFM.Core;

/// <summary>
/// First-run setup: copy the app into a user-chosen folder (default
/// %LocalAppData%\VRCUFM) so later updates can overwrite without UAC.
/// </summary>
public static class InstallService
{
    public static string GetCurrentAppDir()
    {
        string exe = Environment.ProcessPath
            ?? Process.GetCurrentProcess().MainModule?.FileName
            ?? "";
        if (string.IsNullOrEmpty(exe)) return AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return Path.GetDirectoryName(exe) ?? AppContext.BaseDirectory;
    }

    public static string GetCurrentExePath()
    {
        return Environment.ProcessPath
            ?? Process.GetCurrentProcess().MainModule?.FileName
            ?? Path.Combine(GetCurrentAppDir(), "VRCUFM.exe");
    }

    public static bool CanWriteToDirectory(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            string test = Path.Combine(dir, ".vrcufm_write_test_" + Environment.ProcessId);
            File.WriteAllText(test, "ok");
            File.Delete(test);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsUnderProgramFiles(string dir)
    {
        try
        {
            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string full = Path.GetFullPath(dir).TrimEnd('\\', '/');
            return (!string.IsNullOrEmpty(pf) && full.StartsWith(Path.GetFullPath(pf), StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrEmpty(pf86) && full.StartsWith(Path.GetFullPath(pf86), StringComparison.OrdinalIgnoreCase));
        }
        catch { return false; }
    }

    /// <summary>
    /// True when the user should see the setup screen.
    /// Existing configs are migrated once so we don't nag returning users.
    /// </summary>
    public static bool NeedsSetup(AppConfig config)
    {
        if (config.SetupCompleted) return false;

        // Returning user (has session/config history) — treat current dir as install, no nag
        if (!string.IsNullOrEmpty(config.Username) || config.RememberMe || File.Exists(Paths.CookieFile))
        {
            config.SetupCompleted = true;
            config.PortableMode = true;
            config.InstallPath = GetCurrentAppDir();
            return false;
        }

        return true;
    }

    /// <summary>Copy every file from the running app directory into targetDir.</summary>
    public static void CopyAppFiles(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);

        foreach (string file in Directory.GetFiles(sourceDir, "*", SearchOption.TopDirectoryOnly))
        {
            string name = Path.GetFileName(file);
            // Skip obvious junk
            if (name.EndsWith(".log", StringComparison.OrdinalIgnoreCase)) continue;
            if (name.EndsWith(".bak", StringComparison.OrdinalIgnoreCase)) continue;
            File.Copy(file, Path.Combine(targetDir, name), overwrite: true);
        }

        // Shallow subfolders (rare for our publish layout, but safe)
        foreach (string sub in Directory.GetDirectories(sourceDir))
        {
            string name = Path.GetFileName(sub);
            if (name is "logs" or "cache" or "temp") continue;
            CopyDirectory(sub, Path.Combine(targetDir, name));
        }
    }

    static void CopyDirectory(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (string file in Directory.GetFiles(src))
            File.Copy(file, Path.Combine(dst, Path.GetFileName(file)), true);
        foreach (string sub in Directory.GetDirectories(src))
            CopyDirectory(sub, Path.Combine(dst, Path.GetFileName(sub)));
    }

    /// <summary>
    /// Install into targetDir, mark config, optional Start Menu shortcut, relaunch.
    /// </summary>
    public static void InstallAndRelaunch(string targetDir, AppConfig config, bool createStartMenu, Action saveConfig)
    {
        targetDir = Path.GetFullPath(targetDir.Trim());
        if (string.IsNullOrWhiteSpace(targetDir))
            throw new InvalidOperationException("Install path is empty.");

        if (!CanWriteToDirectory(targetDir))
            throw new InvalidOperationException(
                "Cannot write to:\n" + targetDir + "\n\nChoose a folder under your user profile (e.g. Local AppData).");

        string sourceDir = GetCurrentAppDir();
        if (string.Equals(Path.GetFullPath(sourceDir), targetDir, StringComparison.OrdinalIgnoreCase))
        {
            // Already there — just mark setup done
            config.SetupCompleted = true;
            config.PortableMode = false;
            config.InstallPath = targetDir;
            config.StartMenuShortcut = createStartMenu;
            saveConfig();
            if (createStartMenu)
                PlatformService.UpdateStartMenuShortcut(true, GetCurrentExePath());
            return;
        }

        CopyAppFiles(sourceDir, targetDir);

        string exeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "VRCUFM.exe" : "VRCUFM";
        string newExe = Path.Combine(targetDir, exeName);
        if (!File.Exists(newExe))
        {
            // Fallback: whatever we are running as
            string curName = Path.GetFileName(GetCurrentExePath());
            string alt = Path.Combine(targetDir, curName);
            if (File.Exists(alt)) newExe = alt;
            else throw new InvalidOperationException("Install copy is missing the executable.");
        }

        config.SetupCompleted = true;
        config.PortableMode = false;
        config.InstallPath = targetDir;
        config.StartMenuShortcut = createStartMenu;
        saveConfig();

        if (createStartMenu)
            PlatformService.UpdateStartMenuShortcut(true, newExe);

        Process.Start(new ProcessStartInfo
        {
            FileName = newExe,
            WorkingDirectory = targetDir,
            UseShellExecute = true
        });

        Environment.Exit(0);
    }

    public static void MarkPortable(AppConfig config, Action saveConfig)
    {
        config.SetupCompleted = true;
        config.PortableMode = true;
        config.InstallPath = GetCurrentAppDir();
        saveConfig();
    }
}
