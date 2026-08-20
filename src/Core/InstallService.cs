using System.Diagnostics;
using System.Runtime.InteropServices;
using VRCUFM.AppSystem;
using VRCUFM.Filesystem;
using File = System.IO.File;

namespace VRCUFM.Core;

/// <summary>
/// After login: if not running from the configured install folder, force setup.
/// Install copies the app to a user-writable path so updates work without UAC.
/// </summary>
public static class InstallService
{
    public static string GetCurrentAppDir()
    {
        string exe = Environment.ProcessPath
            ?? Process.GetCurrentProcess().MainModule?.FileName
            ?? "";
        if (string.IsNullOrEmpty(exe))
            return AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return Path.GetDirectoryName(exe) ?? AppContext.BaseDirectory;
    }

    public static string GetCurrentExePath() =>
        Environment.ProcessPath
        ?? Process.GetCurrentProcess().MainModule?.FileName
        ?? Path.Combine(GetCurrentAppDir(), "VRCUFM.exe");

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
        catch { return false; }
    }

    public static bool IsUnderProgramFiles(string dir)
    {
        try
        {
            string full = Path.GetFullPath(dir).TrimEnd('\\', '/');
            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            return (!string.IsNullOrEmpty(pf) && full.StartsWith(Path.GetFullPath(pf), StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrEmpty(pf86) && full.StartsWith(Path.GetFullPath(pf86), StringComparison.OrdinalIgnoreCase));
        }
        catch { return false; }
    }

    static bool SamePath(string a, string b)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(a).TrimEnd('\\', '/'),
                Path.GetFullPath(b).TrimEnd('\\', '/'),
                StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    /// <summary>
    /// True when the running binary is not in the required install location.
    /// Checked after login so the user can still sign in from a loose build.
    /// </summary>
    public static bool NeedsSetup(AppConfig config)
    {
        string current = GetCurrentAppDir();

        // Portable explicitly allowed and still in that folder
        if (config.PortableMode && config.SetupCompleted)
        {
            if (!string.IsNullOrWhiteSpace(config.InstallPath) && SamePath(current, config.InstallPath))
                return false;
            // Portable marked but path drifted (moved folder) → setup again
            if (string.IsNullOrWhiteSpace(config.InstallPath))
            {
                config.InstallPath = current;
                return false;
            }
            return !SamePath(current, config.InstallPath);
        }

        // Proper install: must run from InstallPath
        if (config.SetupCompleted && !string.IsNullOrWhiteSpace(config.InstallPath))
            return !SamePath(current, config.InstallPath);

        // Never finished setup, or InstallPath missing
        // Program Files is never a valid permanent home
        if (IsUnderProgramFiles(current))
            return true;

        // First run / incomplete setup
        return !config.SetupCompleted;
    }

    public static void CopyAppFiles(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);
        foreach (string file in Directory.GetFiles(sourceDir, "*", SearchOption.TopDirectoryOnly))
        {
            string name = Path.GetFileName(file);
            if (name.EndsWith(".log", StringComparison.OrdinalIgnoreCase)) continue;
            if (name.EndsWith(".bak", StringComparison.OrdinalIgnoreCase)) continue;
            File.Copy(file, Path.Combine(targetDir, name), true);
        }
    }

    public static void InstallAndRelaunch(string targetDir, AppConfig config, bool createStartMenu, Action saveConfig)
    {
        targetDir = Path.GetFullPath(targetDir.Trim());
        if (string.IsNullOrWhiteSpace(targetDir))
            throw new InvalidOperationException("Install path is empty.");
        if (!CanWriteToDirectory(targetDir))
            throw new InvalidOperationException(
                "Cannot write to:\n" + targetDir + "\n\nChoose a folder under your user profile (e.g. Local AppData).");

        string sourceDir = GetCurrentAppDir();
        if (!SamePath(sourceDir, targetDir))
            CopyAppFiles(sourceDir, targetDir);

        string exeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "VRCUFM.exe" : "VRCUFM";
        string newExe = Path.Combine(targetDir, exeName);
        if (!File.Exists(newExe))
        {
            string alt = Path.Combine(targetDir, Path.GetFileName(GetCurrentExePath()));
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

        // Already running from target — no relaunch needed
        if (SamePath(sourceDir, targetDir))
            return;

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
        string current = GetCurrentAppDir();
        if (IsUnderProgramFiles(current))
            throw new InvalidOperationException(
                "Cannot use portable mode from Program Files. Install to a user folder instead.");

        config.SetupCompleted = true;
        config.PortableMode = true;
        config.InstallPath = current;
        saveConfig();
    }
}
