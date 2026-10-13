using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Raylib_cs;
using VRCUFM.AppSystem;
using VRCUFM.Filesystem;
using File = System.IO.File;

namespace VRCUFM.Core;

public static class PlatformService
{
    #region Window and Tray Integration

    public static bool WindowVisible = true;
    public static bool ShowRequested = false;
    private static bool _trayRunning = false;
    private static Thread? _trayThread;
    private static readonly object _trayLock = new();
    internal static bool TrayRunning => _trayRunning;
    public static volatile string TrayStatus = "Disabled";

    private static IntPtr _originalWndProc = IntPtr.Zero;
    private static GCHandle? _wndProcHandle;
    private static readonly WndProc _wndProcDelegate = WndProcHook;

    delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    const int SW_HIDE = 0, SW_RESTORE = 9;
    const int GWL_WNDPROC = -4;
    const uint WM_SYSCOMMAND = 0x0112;
    const long SC_MINIMIZE = 0xF020;

    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int cmd);
    [DllImport("user32.dll")] static extern IntPtr FindWindow(string? cls, string wnd);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")] static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")] static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
    [DllImport("user32.dll")] static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    public static void ShowMainWindow()
    {
        ShowRequested = true;
        WindowVisible = true;
    }

    public static void HideMainWindow()
    {
        WindowVisible = false;
        Raylib.SetWindowState(ConfigFlags.HiddenWindow);
    }

    public static void ApplyTaskbarVisibility(bool hideFromTaskbar)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
        try
        {
            const int GWL_EXSTYLE = -20;
            const int WS_EX_APPWINDOW = 0x00040000;
            const int WS_EX_TOOLWINDOW = 0x00000080;
            var hwnd = FindWindow(null, "VRChat Unfriend Manager");
            if (hwnd == IntPtr.Zero) return;
            int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
            ex = hideFromTaskbar
                ? (ex & ~WS_EX_APPWINDOW & ~WS_EX_TOOLWINDOW)
                : ((ex | WS_EX_APPWINDOW) & ~WS_EX_TOOLWINDOW);
            SetWindowLong(hwnd, GWL_EXSTYLE, ex);
            ShowWindow(hwnd, SW_HIDE);
            ShowWindow(hwnd, SW_RESTORE);
        }
        catch { }
    }

    public static void EnableMinimizeToTray()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
        try
        {
            var hwnd = FindWindow(null, "VRChat Unfriend Manager");
            if (hwnd == IntPtr.Zero) return;

            _originalWndProc = GetWindowLongPtr(hwnd, GWL_WNDPROC);
            _wndProcHandle = GCHandle.Alloc(_wndProcDelegate);
            SetWindowLongPtr(hwnd, GWL_WNDPROC, Marshal.GetFunctionPointerForDelegate(_wndProcDelegate));

            Console.WriteLine("[Hook] Minimize button hooked successfully");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Hook] Failed: {ex.Message}");
        }
    }

    static IntPtr WndProcHook(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_SYSCOMMAND && wParam.ToInt64() == SC_MINIMIZE)
        {
            HideMainWindow();
            return IntPtr.Zero;
        }
        return CallWindowProc(_originalWndProc, hWnd, msg, wParam, lParam);
    }

    public static void StartTrayThread(bool autostart)
    {
        lock (_trayLock)
        {
            if (_trayRunning) return;
            _trayThread?.Join(3000);
            _trayThread = null;
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) &&
                !RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return;
            _trayRunning = true;
            TrayStatus = "Starting tray icon...";
            Action runTray;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
#if WINDOWS_BUILD
                runTray = () => WindowsTrayService.Run(autostart);
#else
                _trayRunning = false;
                return;
#endif
            }
            else
            {
                runTray = () => LinuxTrayService.Run(autostart);
            }

            _trayThread = new Thread(() =>
            {
                try { runTray(); }
                finally { _trayRunning = false; }
            });
            _trayThread.IsBackground = true;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                _trayThread.SetApartmentState(ApartmentState.STA);
            _trayThread.Start();
        }
    }

    public static void StopTrayThread()
    {
        lock (_trayLock)
        {
            _trayRunning = false;
            TrayStatus = "Disabled";
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
#if WINDOWS_BUILD
                WindowsTrayService.Stop();
#endif
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                LinuxTrayService.Stop();
            }

            _trayThread?.Join(3000);
            _trayThread = null;
        }
    }

    public static bool IsTrayRunning() => _trayRunning;

    public static void Cleanup()
    {
        _wndProcHandle?.Free();
        StopTrayThread();
    }

    #endregion

    #region Startup, Shortcuts, and Desktop Entries

    public static void UpdateStartup(bool enable)
    {
        var exePath = Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrEmpty(exePath)) return;
        string cmdArgs = $"\"{exePath}\" --autostart";

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);
                if (enable) key?.SetValue("VRChatUnfriendManager", cmdArgs);
                else key?.DeleteValue("VRChatUnfriendManager", false);
            }
            catch { }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            try
            {
                string autostartDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "autostart");
                Directory.CreateDirectory(autostartDir);
                string desktopFile = Path.Combine(autostartDir, "VRChatUnfriendManager.desktop");
                if (enable)
                    File.WriteAllText(desktopFile, $"[Desktop Entry]\nType=Application\nName=VRChat Unfriend Manager\nExec={cmdArgs}\nTerminal=false\n");
                else if (File.Exists(desktopFile))
                    File.Delete(desktopFile);
            }
            catch { }
        }
    }

    public static void UpdateStartMenuShortcut(bool enable, string? targetExeOverride = null)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                string startMenuDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                    "VRChat Unfriend Manager");
                string lnkPath = Path.Combine(startMenuDir, "VRChat Unfriend Manager.lnk");

                if (enable)
                {
                    var exePath = targetExeOverride;
                    if (string.IsNullOrEmpty(exePath))
                        exePath = Process.GetCurrentProcess().MainModule?.FileName;
                    if (string.IsNullOrEmpty(exePath)) return;
                    Directory.CreateDirectory(startMenuDir);
                    CreateShellLink(lnkPath, exePath, Path.GetDirectoryName(exePath) ?? "");
                }
                else
                {
                    if (File.Exists(lnkPath)) File.Delete(lnkPath);
                    try { Directory.Delete(startMenuDir); } catch { }
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                if (enable)
                    InstallLinuxDesktopEntry();
                else
                {
                    string desktopPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                        ".local", "share", "applications",
                        "vrchat-unfriend-manager.desktop");
                    if (File.Exists(desktopPath)) File.Delete(desktopPath);
                    string desktopDir = Path.GetDirectoryName(desktopPath)!;
                    try { Process.Start(new ProcessStartInfo("update-desktop-database", desktopDir) { UseShellExecute = false }); } catch { }
                }
            }
        }
        catch { }
    }

    public static void InstallLinuxDesktopEntry()
    {
        try
        {
            string exePath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
            if (string.IsNullOrEmpty(exePath)) return;

            string exeDir = Path.GetDirectoryName(exePath) ?? "";

            string? iconSrc = null;
            var searchDirs = new List<string> { exeDir, Directory.GetCurrentDirectory() };
            var dir = exeDir;
            for (int i = 0; i < 3; i++)
            {
                dir = Path.GetDirectoryName(dir) ?? "";
                if (!string.IsNullOrEmpty(dir)) searchDirs.Add(dir);
            }
            foreach (var d in searchDirs)
                foreach (var name in new[] { "icon.png", "icon.ico" })
                {
                    var p = Path.Combine(d, name);
                    if (File.Exists(p)) { iconSrc = Path.GetFullPath(p); break; }
                }

            string iconName = "vrchat-unfriend-manager";
            string iconDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "icons", "hicolor", "256x256", "apps");
            Directory.CreateDirectory(iconDir);
            string iconDest = Path.Combine(iconDir, $"{iconName}.png");

            if (iconSrc != null && (!File.Exists(iconDest) || File.GetLastWriteTimeUtc(iconSrc) > File.GetLastWriteTimeUtc(iconDest)))
            {
                if (iconSrc.EndsWith(".ico", StringComparison.OrdinalIgnoreCase))
                {
                    bool converted = false;
                    try
                    {
                        var psi = new ProcessStartInfo("convert", $"\"{iconSrc}[0]\" \"{iconDest}\"") { UseShellExecute = false, RedirectStandardError = true };
                        var proc = Process.Start(psi);
                        proc?.WaitForExit(5000);
                        converted = proc?.ExitCode == 0 && File.Exists(iconDest);
                    }
                    catch { }

                    if (!converted)
                    {
                        try
                        {
                            var psi = new ProcessStartInfo("magick", $"\"{iconSrc}[0]\" \"{iconDest}\"") { UseShellExecute = false, RedirectStandardError = true };
                            var proc = Process.Start(psi);
                            proc?.WaitForExit(5000);
                            converted = proc?.ExitCode == 0 && File.Exists(iconDest);
                        }
                        catch { }
                    }
                }
                else
                {
                    File.Copy(iconSrc, iconDest, true);
                }
            }

            string desktopDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "applications");
            Directory.CreateDirectory(desktopDir);
            string desktopPath = Path.Combine(desktopDir, $"{iconName}.desktop");

            string iconLine = File.Exists(iconDest) ? iconName : "application-x-executable";
            string desktop =
                "[Desktop Entry]\n" +
                "Type=Application\n" +
                "Name=VRChat Unfriend Manager\n" +
                "Comment=Manage and unfriend VRChat friends\n" +
                $"Exec={exePath}\n" +
                $"Icon={iconLine}\n" +
                "Categories=Utility;\n" +
                "Terminal=false\n" +
                "StartupNotify=true\n";

            if (!File.Exists(desktopPath) || File.ReadAllText(desktopPath) != desktop)
            {
                File.WriteAllText(desktopPath, desktop);
                try { Process.Start(new ProcessStartInfo("update-desktop-database", desktopDir) { UseShellExecute = false }); } catch { }
            }
        }
        catch { }
    }

    public static void UpdateVrcxShortcut(string subfolder, bool enable, string? startupBase = null)
    {
        try
        {
            var targetDir = Path.Combine(startupBase ?? Paths.VrcxStartup, subfolder);
            Directory.CreateDirectory(targetDir);
            var exePath = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(exePath)) return;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                string lnkPath = Path.Combine(targetDir, "VRChatUnfriendManager.lnk");
                if (File.Exists(lnkPath)) File.Delete(lnkPath);
                if (enable) CreateShellLink(lnkPath, exePath, "--autostart");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                string linkPath = Path.Combine(targetDir, "VRChatUnfriendManager");
                if (File.Exists(linkPath)) File.Delete(linkPath);
                if (enable) File.CreateSymbolicLink(linkPath, exePath);
            }
        }
        catch { }
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")] class ShellLink { }
    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }
    [ComImport, Guid("0000010b-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPersistFile
    {
        void GetClassID(out Guid pClassID);
        void IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, bool fRemember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
    }

    static void CreateShellLink(string lnkPath, string targetPath, string arguments)
    {
        var link = (IShellLinkW)new ShellLink();
        link.SetPath(targetPath);
        link.SetArguments(arguments);
        link.SetWorkingDirectory(Path.GetDirectoryName(targetPath) ?? "");
        ((IPersistFile)link).Save(lnkPath, false);
    }

    #endregion
}
