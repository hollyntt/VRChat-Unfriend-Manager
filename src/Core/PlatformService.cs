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
    private static NotifyIcon? _notifyIcon;
    private static readonly object _trayLock = new();
    private static Process? _linuxTrayProcess;
    private static System.Net.Sockets.Socket? _linuxTraySocket;
    private static Thread? _linuxTrayListenerThread;
    private static string _linuxSocketPath = Path.Combine(Path.GetTempPath(), $"vum_tray_{Environment.ProcessId}.sock");

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
            _trayRunning = true;
            _trayThread = new Thread(() =>
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    RunWindowsTray(autostart);
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                    RunLinuxTray(autostart);
                _trayRunning = false;
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

            if (_notifyIcon != null)
            {
                try
                {
                    _notifyIcon.Visible = false;
                    _notifyIcon.Dispose();
                    _notifyIcon = null;
                }
                catch { }
            }

            try { _linuxTrayProcess?.Kill(); } catch { }
            _linuxTrayProcess = null;

            try { _linuxTraySocket?.Close(); } catch { }
            _linuxTraySocket = null;

            _trayThread?.Join(3000);
            _trayThread = null;
        }
    }

    static Icon LoadTrayIcon()
    {
        string exeDir = AppDomain.CurrentDomain.BaseDirectory;
        var possibleIcons = new[]
        {
            Path.Combine(exeDir, "icon.ico"),
            Path.Combine(exeDir, "icon.png"),
            Path.Combine(Directory.GetCurrentDirectory(), "icon.ico"),
            Path.Combine(Directory.GetCurrentDirectory(), "icon.png")
        };

        foreach (var path in possibleIcons)
        {
            if (File.Exists(path))
            {
                try
                {
                    if (path.EndsWith(".ico", StringComparison.OrdinalIgnoreCase))
                        return new Icon(path);

                    using var bmp = new Bitmap(path);
                    return Icon.FromHandle(bmp.GetHicon());
                }
                catch { }
            }
        }
        return SystemIcons.Application;
    }

    static void RunWindowsTray(bool autostart)
    {
        if (autostart) HideMainWindow();

        try
        {
            ApplicationConfiguration.Initialize();

            var icon = LoadTrayIcon();

            _notifyIcon = new NotifyIcon
            {
                Icon = icon,
                Text = "VRChat Unfriend Manager",
                Visible = true,
            };

            var menu = new ContextMenuStrip();
            menu.Items.Add("Show", null, (_, _) => ShowMainWindow());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, (_, _) => { Program.shouldExit = true; Application.ExitThread(); });

            _notifyIcon.ContextMenuStrip = menu;
            _notifyIcon.DoubleClick += (_, _) => ShowMainWindow();

            Console.WriteLine("[TRAY] NotifyIcon created successfully");

            Application.Run();

            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TRAY] RunWindowsTray failed: {ex.Message}");
        }
    }

    static void RunLinuxTray(bool autostart)
    {
        if (autostart) HideMainWindow();

        string scriptPath = Path.Combine(Path.GetTempPath(), $"vum_tray_{Environment.ProcessId}.py");

        string iconPath = "icon.png";
        if (!File.Exists(iconPath)) iconPath = "icon.ico";
        string absIconPath = File.Exists(iconPath) ? Path.GetFullPath(iconPath) : "";

        string pySocketPath = _linuxSocketPath.Replace("\\", "\\\\");
        string pyIconPath = absIconPath.Replace("\\", "\\\\");

        string script = $@"
import sys, socket, os, threading
try:
    import pystray
    from PIL import Image, ImageDraw
except ImportError:
    sys.exit(42)

SOCK = ""{pySocketPath}""
ICON = ""{pyIconPath}""

def load_icon():
    if ICON and os.path.exists(ICON):
        try:
            return Image.open(ICON).resize((64, 64)).convert('RGBA')
        except:
            pass
    img = Image.new('RGBA', (64, 64), (80, 40, 140, 255))
    d = ImageDraw.Draw(img)
    d.ellipse([8, 8, 56, 56], fill=(160, 100, 220, 255))
    return img

def send_cmd(cmd):
    try:
        s = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
        s.connect(SOCK)
        s.sendall(cmd.encode())
        s.close()
    except:
        pass

def on_show(icon, item): send_cmd('show')
def on_exit(icon, item):
    send_cmd('exit')
    icon.stop()

menu = pystray.Menu(
    pystray.MenuItem('Show', on_show, default=True),
    pystray.MenuItem('Exit', on_exit),
)
tray = pystray.Icon('VRChat Unfriend Manager', load_icon(), 'VRChat Unfriend Manager', menu)
tray.run()
";
        File.WriteAllText(scriptPath, script);

        if (File.Exists(_linuxSocketPath)) File.Delete(_linuxSocketPath);
        var unixEp = new System.Net.Sockets.UnixDomainSocketEndPoint(_linuxSocketPath);
        _linuxTraySocket = new System.Net.Sockets.Socket(
            System.Net.Sockets.AddressFamily.Unix,
            System.Net.Sockets.SocketType.Stream,
            System.Net.Sockets.ProtocolType.Unspecified);
        _linuxTraySocket.Bind(unixEp);
        _linuxTraySocket.Listen(4);

        _linuxTrayListenerThread = new Thread(() =>
        {
            while (_trayRunning)
            {
                System.Net.Sockets.Socket? client = null;
                try { client = _linuxTraySocket.Accept(); }
                catch { break; }

                try
                {
                    var buf = new byte[64];
                    int n = client.Receive(buf);
                    var cmd = Encoding.UTF8.GetString(buf, 0, n).Trim();
                    if (cmd == "show") ShowMainWindow();
                    else if (cmd == "exit") Program.shouldExit = true;
                }
                catch { }
                finally { try { client?.Close(); } catch { } }
            }
        }) { IsBackground = true };
        _linuxTrayListenerThread.Start();

        var psi = new ProcessStartInfo
        {
            FileName = "python3",
            Arguments = $"\"{scriptPath}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        try
        {
            _linuxTrayProcess = Process.Start(psi);
            _linuxTrayProcess?.WaitForExit();

            int exitCode = _linuxTrayProcess?.ExitCode ?? -1;
            if (exitCode == 42)
            {
                Console.WriteLine("[TRAY] pystray / Pillow not found — tray icon unavailable.");
                Console.WriteLine("[TRAY] Install with:  pip install pystray pillow");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TRAY] Failed to launch tray helper: {ex.Message}");
        }
        finally
        {
            _trayRunning = false;
            try { File.Delete(scriptPath); } catch { }
            try { File.Delete(_linuxSocketPath); } catch { }
            try { _linuxTraySocket?.Close(); } catch { }
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
