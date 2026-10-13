using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace VRCUFM.Core;

internal static class LinuxTrayService
{
    private static readonly string SocketPath = Path.Combine(Path.GetTempPath(), $"vum_tray_{Environment.ProcessId}.sock");
    private static readonly string IconDirectoryPath = Path.Combine(Path.GetTempPath(), $"vrcufm-tray-{Environment.ProcessId}");
    private const string ScriptPrefix = "vum_tray_";
    private static Process? _process;
    private static Socket? _socket;
    private static Thread? _listenerThread;

    internal static void Run(bool autostart)
    {
        if (autostart) PlatformService.HideMainWindow();

        string scriptPath = Path.Combine(Path.GetTempPath(), $"vum_tray_{Environment.ProcessId}.py");
        string? iconPath = FindIconPath();
        string script = CreateHelperScript(SocketPath, iconPath ?? "", IconDirectoryPath);

        try
        {
            CleanupStaleTrayResources();
            File.WriteAllText(scriptPath, script);
            if (File.Exists(SocketPath)) File.Delete(SocketPath);

            _socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            _socket.Bind(new UnixDomainSocketEndPoint(SocketPath));
            _socket.Listen(4);
            _listenerThread = new Thread(ListenForCommands) { IsBackground = true };
            _listenerThread.Start();

            var startInfo = new ProcessStartInfo
            {
                FileName = "python3",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            startInfo.ArgumentList.Add(scriptPath);

            _process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("python3 did not start the tray helper.");

            Task<string> standardOutput = _process.StandardOutput.ReadToEndAsync();
            Task<string> standardError = _process.StandardError.ReadToEndAsync();
            _process.WaitForExit();

            int exitCode = _process.ExitCode;
            string output = standardOutput.GetAwaiter().GetResult();
            string error = standardError.GetAwaiter().GetResult();

            if (exitCode == 42)
            {
                const string message = "Unavailable: install Python GTK 3 and Ayatana AppIndicator bindings";
                PlatformService.TrayStatus = message;
                Console.WriteLine($"[TRAY] {message}");
            }
            else if (exitCode != 0)
            {
                PlatformService.TrayStatus = $"Unavailable: tray helper exited with code {exitCode}";
                Console.WriteLine($"[TRAY] Linux tray helper exited with code {exitCode}.");
                if (!string.IsNullOrWhiteSpace(output)) Console.WriteLine(output.Trim());
                if (!string.IsNullOrWhiteSpace(error)) Console.WriteLine(error.Trim());
            }
            else
            {
                PlatformService.TrayStatus = "Tray icon stopped";
            }
        }
        catch (Exception ex)
        {
            PlatformService.TrayStatus = $"Unavailable: {ex.Message}";
            Console.WriteLine($"[TRAY] Linux tray failed: {ex.Message}");
        }
        finally
        {
            _process?.Dispose();
            _process = null;
            _socket?.Dispose();
            _socket = null;
            _listenerThread?.Join(1000);
            _listenerThread = null;
            try { File.Delete(scriptPath); } catch (IOException ex) { Console.WriteLine($"[TRAY] Could not remove helper script: {ex.Message}"); }
            try { File.Delete(SocketPath); } catch (IOException ex) { Console.WriteLine($"[TRAY] Could not remove tray socket: {ex.Message}"); }
            try { if (Directory.Exists(IconDirectoryPath)) Directory.Delete(IconDirectoryPath, true); }
            catch (IOException ex) { Console.WriteLine($"[TRAY] Could not remove tray icon directory: {ex.Message}"); }
        }
    }

    internal static void Stop()
    {
        try
        {
            if (_process is { HasExited: false }) _process.Kill();
        }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception ex)
        {
            Console.WriteLine($"[TRAY] Could not stop Linux tray helper: {ex.Message}");
        }

        try { _socket?.Close(); }
        catch (SocketException ex) { Console.WriteLine($"[TRAY] Could not close Linux tray socket: {ex.Message}"); }
    }

    private static void ListenForCommands()
    {
        while (PlatformService.TrayRunning)
        {
            Socket? client = null;
            try
            {
                client = _socket?.Accept();
                if (client is null) return;

                var buffer = new byte[64];
                int count = client.Receive(buffer);
                string command = Encoding.UTF8.GetString(buffer, 0, count).Trim();
                if (command == "ready") PlatformService.TrayStatus = "Tray icon registered";
                else if (command == "show") PlatformService.ShowMainWindow();
                else if (command == "exit") Program.shouldExit = true;
            }
            catch (SocketException) when (!PlatformService.TrayRunning) { return; }
            catch (ObjectDisposedException) when (!PlatformService.TrayRunning) { return; }
            catch (SocketException ex) { Console.WriteLine($"[TRAY] Linux tray command socket failed: {ex.Message}"); }
            finally { client?.Dispose(); }
        }
    }

    private static string? FindIconPath()
    {
        string[] possibleIcons =
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon.png"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon.ico"),
            Path.Combine(Directory.GetCurrentDirectory(), "icon.png"),
            Path.Combine(Directory.GetCurrentDirectory(), "icon.ico")
        };

        return possibleIcons.FirstOrDefault(File.Exists);
    }

    private static void CleanupStaleTrayResources()
    {
        string tempDirectory = Path.GetTempPath();
        var activeScripts = new HashSet<string>(StringComparer.Ordinal);

        foreach (string scriptPath in Directory.EnumerateFiles(tempDirectory, $"{ScriptPrefix}*.py"))
        {
            if (!TryGetOwningProcessId(scriptPath, out int processId))
                continue;

            if (processId == Environment.ProcessId)
            {
                StopOrphanedHelper(scriptPath);
                DeleteStaleFile(scriptPath);
                DeleteStaleFile(Path.ChangeExtension(scriptPath, ".sock"));
                continue;
            }

            if (IsProcessRunning(processId))
            {
                activeScripts.Add(Path.GetFullPath(scriptPath));
                continue;
            }

            if (!StopOrphanedHelper(scriptPath))
            {
                activeScripts.Add(Path.GetFullPath(scriptPath));
                continue;
            }

            DeleteStaleFile(scriptPath);
            DeleteStaleFile(Path.ChangeExtension(scriptPath, ".sock"));
        }

        foreach (string socketPath in Directory.EnumerateFiles(tempDirectory, $"{ScriptPrefix}*.sock"))
        {
            if (TryGetOwningProcessId(socketPath, out int processId) && !IsProcessRunning(processId))
                DeleteStaleFile(socketPath);
        }

        foreach (string iconDirectory in Directory.EnumerateDirectories(tempDirectory, "vrcufm-tray-*"))
        {
            string suffix = Path.GetFileName(iconDirectory)["vrcufm-tray-".Length..];
            if (int.TryParse(suffix, out int processId))
            {
                if (IsProcessRunning(processId)) continue;
            }
            else if (activeScripts.Count > 0)
            {
                continue;
            }

            try { Directory.Delete(iconDirectory, true); }
            catch (IOException ex) { Console.WriteLine($"[TRAY] Could not remove stale tray icon directory: {ex.Message}"); }
        }
    }

    private static bool TryGetOwningProcessId(string resourcePath, out int processId)
    {
        string name = Path.GetFileNameWithoutExtension(resourcePath);
        if (name.StartsWith(ScriptPrefix, StringComparison.Ordinal) &&
            int.TryParse(name.AsSpan(ScriptPrefix.Length), out processId))
            return true;

        processId = 0;
        return false;
    }

    private static bool IsProcessRunning(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool StopOrphanedHelper(string scriptPath)
    {
        if (!Directory.Exists("/proc")) return true;

        string fullScriptPath = Path.GetFullPath(scriptPath);
        foreach (string processDirectory in Directory.EnumerateDirectories("/proc"))
        {
            string processIdText = Path.GetFileName(processDirectory);
            if (!int.TryParse(processIdText, out int processId) || processId == Environment.ProcessId)
                continue;

            try
            {
                string[] arguments = File.ReadAllText(Path.Combine(processDirectory, "cmdline"))
                    .Split('\0', StringSplitOptions.RemoveEmptyEntries);
                if (!arguments.Any(argument => string.Equals(argument, fullScriptPath, StringComparison.Ordinal)))
                    continue;

                using Process process = Process.GetProcessById(processId);
                process.Kill();
                if (!process.WaitForExit(3000))
                {
                    Console.WriteLine($"[TRAY] Orphaned helper process {processId} did not stop promptly.");
                    return false;
                }
                return true;
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception ex)
            {
                Console.WriteLine($"[TRAY] Could not stop orphaned helper process: {ex.Message}");
                return false;
            }
        }

        return true;
    }

    private static void DeleteStaleFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException ex) { Console.WriteLine($"[TRAY] Could not remove stale tray file '{path}': {ex.Message}"); }
    }

    private static string CreateHelperScript(string socketPath, string iconPath, string iconDirectoryPath)
    {
        string pySocketPath = JsonSerializer.Serialize(socketPath);
        string pyIconPath = JsonSerializer.Serialize(iconPath);
        string pyIconDirectoryPath = JsonSerializer.Serialize(iconDirectoryPath);
        return $$"""
            import os, shutil, socket, sys

            SOCK = {{pySocketPath}}
            ICON = {{pyIconPath}}
            ICON_DIRECTORY = {{pyIconDirectoryPath}}
            TITLE = 'VRChat Unfriend Manager'

            def send_command(command):
                try:
                    with socket.socket(socket.AF_UNIX, socket.SOCK_STREAM) as client:
                        client.connect(SOCK)
                        client.sendall(command.encode())
                except OSError as ex:
                    print(f'Could not contact app: {ex}', file=sys.stderr)

            try:
                import gi
                gi.require_version('Gtk', '3.0')
                try:
                    gi.require_version('AyatanaAppIndicator3', '0.1')
                    from gi.repository import AyatanaAppIndicator3 as AppIndicator
                except (ImportError, ValueError):
                    gi.require_version('AppIndicator3', '0.1')
                    from gi.repository import AppIndicator3 as AppIndicator

                from gi.repository import Gtk
            except (ImportError, ValueError) as appindicator_error:
                try:
                    os.environ['PYSTRAY_BACKEND'] = 'appindicator'
                    import pystray
                    from PIL import Image
                except ImportError as pystray_error:
                    print(
                        'No Linux tray backend is available. Install GTK 3 and '
                        'Ayatana AppIndicator Python bindings, or pystray and Pillow. '
                        f'AppIndicator: {appindicator_error}; pystray: {pystray_error}',
                        file=sys.stderr)
                    sys.exit(42)

                image = Image.open(ICON).convert('RGBA') if ICON and os.path.exists(ICON) else Image.new('RGBA', (64, 64), (100, 70, 180, 255))
                menu = pystray.Menu(
                    pystray.MenuItem('Show', lambda icon, item: send_command('show'), default=True),
                    pystray.MenuItem('Exit', lambda icon, item: (send_command('exit'), icon.stop())),
                )
                send_command('ready')
                pystray.Icon('vrcufm', image, TITLE, menu).run()
            else:
                icon_directory = ICON_DIRECTORY
                os.makedirs(icon_directory, exist_ok=True)
                try:
                    icon_name = 'vrcufm'
                    if ICON and os.path.exists(ICON):
                        extension = os.path.splitext(ICON)[1].lower()
                        if extension not in ('.png', '.svg', '.xpm'):
                            extension = '.png'
                        icon_path = os.path.join(icon_directory, icon_name + extension)
                        shutil.copyfile(ICON, icon_path)
                        icon_name = os.path.splitext(os.path.basename(icon_path))[0]

                    indicator = AppIndicator.Indicator.new(
                        'vrcufm',
                        icon_name,
                        AppIndicator.IndicatorCategory.APPLICATION_STATUS)
                    indicator.set_icon_theme_path(icon_directory)

                    menu = Gtk.Menu()
                    show_item = Gtk.MenuItem(label='Show')
                    show_item.connect('activate', lambda item: send_command('show'))
                    menu.append(show_item)
                    menu.append(Gtk.SeparatorMenuItem())
                    exit_item = Gtk.MenuItem(label='Exit')
                    exit_item.connect('activate', lambda item: (send_command('exit'), Gtk.main_quit()))
                    menu.append(exit_item)
                    menu.show_all()

                    indicator.set_menu(menu)
                    indicator.set_title(TITLE)
                    indicator.set_status(AppIndicator.IndicatorStatus.ACTIVE)
                    send_command('ready')
                    Gtk.main()
                finally:
                    shutil.rmtree(icon_directory, ignore_errors=True)
            """;
    }
}
