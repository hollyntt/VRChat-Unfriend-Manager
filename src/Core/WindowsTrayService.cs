#if WINDOWS_BUILD
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace VRCUFM.Core;

internal static class WindowsTrayService
{
    private const uint WmQuit = 0x0012;
    private static NotifyIcon? _notifyIcon;
    private static uint _threadId;

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostThreadMessage(uint threadId, uint message, IntPtr wParam, IntPtr lParam);

    internal static void Run(bool autostart)
    {
        if (autostart) PlatformService.HideMainWindow();
        _threadId = GetCurrentThreadId();

        try
        {
            ApplicationConfiguration.Initialize();
            _notifyIcon = new NotifyIcon
            {
                Icon = LoadTrayIcon(),
                Text = "VRChat Unfriend Manager",
                Visible = true,
            };

            var menu = new ContextMenuStrip();
            menu.Items.Add("Show", null, (_, _) => PlatformService.ShowMainWindow());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, (_, _) =>
            {
                Program.shouldExit = true;
                Application.ExitThread();
            });

            _notifyIcon.ContextMenuStrip = menu;
            _notifyIcon.DoubleClick += (_, _) => PlatformService.ShowMainWindow();
            Console.WriteLine("[TRAY] Windows tray icon created successfully");

            Application.Run();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TRAY] Windows tray failed: {ex.Message}");
        }
        finally
        {
            _notifyIcon?.Dispose();
            _notifyIcon = null;
            _threadId = 0;
        }
    }

    internal static void Stop()
    {
        _notifyIcon?.Dispose();
        _notifyIcon = null;

        if (_threadId != 0 && !PostThreadMessage(_threadId, WmQuit, IntPtr.Zero, IntPtr.Zero))
            Console.WriteLine($"[TRAY] Could not stop Windows tray message loop (error {Marshal.GetLastWin32Error()})");
    }

    private static Icon LoadTrayIcon()
    {
        string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
        string[] possibleIcons =
        {
            Path.Combine(baseDirectory, "icon.ico"),
            Path.Combine(baseDirectory, "icon.png"),
            Path.Combine(Directory.GetCurrentDirectory(), "icon.ico"),
            Path.Combine(Directory.GetCurrentDirectory(), "icon.png")
        };

        foreach (string path in possibleIcons)
        {
            if (!File.Exists(path)) continue;
            try
            {
                if (path.EndsWith(".ico", StringComparison.OrdinalIgnoreCase))
                    return new Icon(path);

                using var bitmap = new Bitmap(path);
                return Icon.FromHandle(bitmap.GetHicon());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TRAY] Could not load icon '{path}': {ex.Message}");
            }
        }

        return SystemIcons.Application;
    }
}
#endif
