using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ImGuiNET;
using Raylib_cs;
using rlImGui_cs;
using VRChat.API.Model;
using VRCUFM.VRChat;
using VRCUFM.Filesystem;
using VRCUFM.AppSystem;
using VRCUFM.Core;
using VRCUFM.UI;
using File = System.IO.File;
using Color = Raylib_cs.Color;
using Image = Raylib_cs.Image;

namespace VRCUFM
{
    class Program
    {
        #region App State (internal for cross-class access)

        internal static string AppVersion
        {
            get
            {
                var assembly = Assembly.GetExecutingAssembly();
                var attr = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                if (attr != null && !string.IsNullOrEmpty(attr.InformationalVersion))
                    return attr.InformationalVersion.Split('+')[0].TrimStart('v');
                return "unknown";
            }
        }

        internal static APIService api = new();
        internal static List<SafeLimitedUserFriend> friends = new();
        internal static HashSet<string> favorites = new(StringComparer.OrdinalIgnoreCase);
        internal static Dictionary<string, HashSet<string>> favByGroup = new();
        internal static Dictionary<string, string> favGroupNames = new(StringComparer.OrdinalIgnoreCase);
        internal static List<SafeLimitedUserFriend> shown = new();
        internal static HashSet<int> selected = new();
        internal static string user = "", pass = "";
        internal static string loggedInAs = "";
        internal static bool remember = true;
        internal static bool hideFavs = true;
        internal static bool inactiveOn = false;
        internal static int inactiveVal = 3;
        internal static int inactiveUnit = 1;
        internal static bool togetherOn = false;
        internal static int togetherVal = 60;
        internal static int togetherUnit = 1;
        internal static string searchText = "";
        internal static int searchField = 0;
        internal static int sort = 0;
        internal static string status = "Starting up...";
        internal static bool working = false;
        internal static bool isUnfriending = false;
        internal static volatile bool pendingAutoConfirm = false;
        internal static volatile int pendingAutoCount = 0;
        internal static TaskCompletionSource<bool>? autoConfirmTcs = null;
        internal static readonly object autoConfirmLock = new();
        internal static DateTime autoConfirmDeadline = DateTime.MaxValue;
        internal static bool isPaused = false;
        internal static int unfriendTotal = 0;
        internal static int unfriendDone = 0;
        internal static CancellationTokenSource? unfriendCts;
        internal static AppConfig config = new();
        internal static CancellationTokenSource? autoCts;
        internal static CancellationTokenSource? autoDeclineCts;
        internal static readonly string[] units = { "Days", "Months", "Years" };
        internal static bool isLoggedIn = false;
        internal static bool needsSetup = false;
        internal static string setupInstallPath = "";
        internal static bool sessionRestored = false;
        internal static bool shouldExit = false;

        internal static List<Notification> incomingFriendRequests = new();
        internal static bool autoDecline = false;
        internal static bool autoSendBack = false;
        internal static bool onlyStrangers = true;

        internal static bool checkingForUpdate = false;
        internal static bool updateAvailable = false;
        internal static string latestVersion = "";
        internal static string downloadUrl = "";
        internal static string expectedHash = "";
        internal static float downloadProgress = 0f;
        internal static bool downloading = false;

        [DllImport("kernel32.dll")] static extern IntPtr GetConsoleWindow();
        [DllImport("kernel32.dll")] static extern bool FreeConsole();
        [UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
        private static unsafe void RaylibLogCallback(int logLevel, sbyte* text, sbyte* args) { }

        #endregion

        public static async Task Main(string[] args)
        {
            bool isAutostart = args.Contains("--autostart");

#if !DEBUG
            unsafe
            {
                Raylib.SetTraceLogCallback(&RaylibLogCallback);
            }
            Raylib.SetTraceLogLevel(TraceLogLevel.None);
            Console.SetOut(TextWriter.Null);
            Console.SetError(TextWriter.Null);
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                FreeConsole();
#endif

            Paths.EnsureExists();
            LoadConfig();
            FriendsManager.EnsureLoaded();

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                PlatformService.InstallLinuxDesktopEntry();

            if (Directory.Exists(Paths.VrcxStartup))
            {
                PlatformService.UpdateVrcxShortcut("desktop", config.VrcxStartupDesktop);
                PlatformService.UpdateVrcxShortcut("vr", config.VrcxStartupVr);
            }

            if (config.RunOnStartup) PlatformService.UpdateStartup(true);

            ConfigFlags flags = ConfigFlags.ResizableWindow | ConfigFlags.HighDpiWindow;
            Raylib.SetConfigFlags(flags);
            Raylib.InitWindow(1280, 800, "VRChat Unfriend Manager");

            PlatformService.EnableMinimizeToTray();

            try
            {
                string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon.png");
                if (!File.Exists(iconPath))
                    iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon.ico");

                if (File.Exists(iconPath))
                {
                    var img = Raylib.LoadImage(iconPath);
                    Raylib.SetWindowIcon(img);
                    Raylib.UnloadImage(img);
                }
            }
            catch { }

            Raylib.SetTargetFPS(60);

            if (config.HideInTaskbar)
            {
                PlatformService.StartTrayThread(isAutostart);
                PlatformService.ApplyTaskbarVisibility(true);
            }

            rlImGui.Setup(true);
            UIRenderer.ApplyTheme();

            user = config.Username;
            remember = config.RememberMe;
            hideFavs = config.ExcludeFavorites;
            inactiveOn = config.InactiveEnabled;
            inactiveVal = config.InactiveValue;
            inactiveUnit = config.InactiveUnitIndex;
            togetherOn = config.TogetherFilterEnabled;
            togetherVal = config.TogetherFilterValue;
            togetherUnit = config.TogetherFilterUnit;
            sort = config.SortOptionIndex;

            autoDecline = config.AutoDeclineFriendRequests;
            autoSendBack = config.AutoSendRequestBack;
            onlyStrangers = config.AutoDeclineOnlyFromStrangers;

            _ = Task.Run(async () =>
            {
                await Task.Delay(300);
                var (restored, name) = await api.RestoreSessionFromDiskOrConfigAsync();
                if (restored && name != null)
                {
                    loggedInAs = name;
                    isLoggedIn = true;
                    sessionRestored = true;
                    _ = DiscordWebhookService.NotifyLoginAsync(loggedInAs);
                    _ = OscNotificationService.NotifyLoginAsync(loggedInAs);
                    status = $"Welcome back, {name}";
                    await Refresh();
                    if (config.AutoUnfriendEnabled) SchedulerService.StartAutoScheduler();
                    StartUpdateAutoCheckLoop();
                    if (config.AutoGroupEnabled) AutoGroupService.Start();
                    if (config.AutoDeclineFriendRequests) SchedulerService.StartAutoDeclineChecker();
                }
                else
                {
                    status = string.IsNullOrEmpty(config.Username)
                        ? "Please log in"
                        : "Session expired - please log in again";
                }
            });

            while (!shouldExit)
            {
                if (PlatformService.ShowRequested)
                {
                    PlatformService.ShowRequested = false;
                    Raylib.ClearWindowState(ConfigFlags.HiddenWindow);
                    Raylib.SetWindowState(ConfigFlags.TopmostWindow);
                    Raylib.ClearWindowState(ConfigFlags.TopmostWindow);
                    if (config.HideInTaskbar) PlatformService.ApplyTaskbarVisibility(true);
                }

                if (!PlatformService.WindowVisible)
                {
                    Raylib.PollInputEvents();
                    Thread.Sleep(50);
                    continue;
                }

                if (Raylib.WindowShouldClose())
                {
                    if (config.HideInTaskbar && PlatformService.IsTrayRunning())
                        PlatformService.HideMainWindow();
                    else
                        shouldExit = true;
                    continue;
                }

                int screenW = Raylib.GetScreenWidth();
                int screenH = Raylib.GetScreenHeight();

                rlImGui.Begin();
                Raylib.BeginDrawing();
                Raylib.ClearBackground(new Color(15, 15, 20, 255));

                TextureCache.FlushPending();

                ImGui.SetNextWindowPos(Vector2.Zero);
                ImGui.SetNextWindowSize(new Vector2(screenW, screenH));
                ImGui.Begin("##main", ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoTitleBar |
                    ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar |
                    ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoBringToFrontOnFocus);

                if (sessionRestored || isLoggedIn)
                {
                    // After login only: force setup if not running from install folder
                    needsSetup = InstallService.NeedsSetup(config);
                    if (needsSetup)
                    {
                        if (string.IsNullOrWhiteSpace(setupInstallPath))
                            setupInstallPath = string.IsNullOrWhiteSpace(config.InstallPath)
                                ? Paths.DefaultInstallDir
                                : config.InstallPath;
                        UIRenderer.DrawSetupScreen();
                    }
                    else
                        UIRenderer.DrawMainUI();
                }
                else UIRenderer.DrawLoginScreen();

                api.Draw2FADialog();
                UIRenderer.DrawAutoUnfriendConfirmDialog();
                ImGui.End();
                rlImGui.End();
                Raylib.EndDrawing();
            }

            AutoGroupService.Stop();
            PlatformService.Cleanup();

            autoCts?.Cancel();
            autoDeclineCts?.Cancel();
            unfriendCts?.Cancel();

            SaveConfig();
            TextureCache.UnloadAll();
            rlImGui.Shutdown();
            Raylib.CloseWindow();

            Environment.Exit(0);
        }

        #region Core Operations

        internal static async Task StartUnfriendProcess()
        {
            isUnfriending = true; isPaused = false;
            unfriendTotal = selected.Count; unfriendDone = 0;
            unfriendCts = new CancellationTokenSource();
            var list = selected.Select(i => shown[i]).ToList();

            try
            {
                for (int i = 0; i < list.Count; i++)
                {
                    while (isPaused && !unfriendCts.Token.IsCancellationRequested)
                        await Task.Delay(200, unfriendCts.Token);

                    if (unfriendCts.Token.IsCancellationRequested) break;

                    var u = list[i];
                    status = $"Unfriending {u.DisplayName}...";
                    try
                    {
                        await api.UnfriendAsync(u.Id);
                        unfriendDone++;
                        FriendsManager.LogUnfriend(u, "manual");
                        ShowUnfriendToast(u.DisplayName);
                        friends.RemoveAll(x => x.Id == u.Id);
                        shown.RemoveAll(x => x.Id == u.Id);
                    }
                    catch (Exception ex) { Console.WriteLine(ex.Message); }

                    if (i < list.Count - 1)
                        await Task.Delay(Random.Shared.Next(7000, 13000), unfriendCts.Token);
                }
            }
            finally
            {
                isUnfriending = false; isPaused = false;
                status = unfriendDone == unfriendTotal ? "All done!" : "Cancelled";
                ShowToast("Unfriend Complete", $"{unfriendDone} users removed");
                _ = DiscordWebhookService.NotifyBulkUnfriendAsync(unfriendDone);
                _ = OscNotificationService.NotifyBulkUnfriendAsync(unfriendDone);
                selected.Clear();
                if (config.AutoRefreshAfterUnfriend)
                    await Refresh();
            }
        }

        internal static async Task ReAddFriendAsync(UnfriendLogEntry entry)
        {
            try
            {
                status = $"Sending friend request to {entry.DisplayName}...";
                await api.SendFriendRequestAsync(entry.UserId);
                FriendsManager.RemoveLogEntry(entry.UserId);
                status = $"Friend request sent to {entry.DisplayName}";
                ShowToast("Re-add", $"Request sent to {entry.DisplayName}");
            }
            catch (Exception ex)
            {
                status = "Re-add failed: " + ex.Message;
                ShowToast("Re-add failed", ex.Message);
            }
        }

        internal static async Task Refresh()
        {
            working = true;
            status = "Loading friends...";
            TextureCache.UnloadAll();

            try
            {
                var (allIds, byGroup) = await api.GetFavoritesDetailedAsync();
                favorites = allIds;
                favByGroup = byGroup;
                favGroupNames = await api.GetFavoriteGroupNamesAsync();
                friends = await api.GetAllFriendsAsync();

                if (VRCNextDataService.IsAvailable || VRCXDataService.IsAvailable)
                {
                    status = "Loading time together data...";
                    var timeMap = await Task.Run(() =>
                    {
                        var merged = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
                        if (VRCNextDataService.IsAvailable)
                            foreach (var kv in VRCNextDataService.LoadTimeSpentSeconds())
                            {
                                merged.TryGetValue(kv.Key, out var ex);
                                merged[kv.Key] = ex + kv.Value;
                            }
                        if (VRCXDataService.IsAvailable)
                            foreach (var kv in VRCXDataService.LoadTimeSpentSeconds())
                            {
                                merged.TryGetValue(kv.Key, out var ex);
                                merged[kv.Key] = ex + kv.Value;
                            }
                        return merged;
                    });
                    foreach (var f in friends)
                        if (timeMap.TryGetValue(f.Id, out var secs))
                            f.TimeSpentMs = secs * 1000L;
                }

                await RefreshFriendRequests();

                status = $"Loaded {friends.Count} friends";
            }
            catch (Exception ex)
            {
                status = "Session expired - please re-login";
                isLoggedIn = false;
                sessionRestored = false;
                Console.WriteLine(ex.Message);
            }
            selected.Clear();
            working = false;
        }

        internal static async Task RefreshFriendRequests()
        {
            try
            {
                incomingFriendRequests = await api.GetIncomingFriendRequestsAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FriendRequests] Refresh error: {ex.Message}");
            }
        }

        #endregion

        #region Utilities

        internal static string Ago(DateTime dt)
        {
            var span = DateTime.UtcNow - dt.ToUniversalTime();
            if (span.TotalDays < 1) return "today";
            if (span.TotalDays < 30) return $"{(int)span.TotalDays}d";
            if (span.TotalDays < 365) return $"{(int)(span.TotalDays / 30.4)}mo";
            return $"{(int)(span.TotalDays / 365.25)}y";
        }

        internal static string FormatTimeSpent(long ms)
        {
            if (ms <= 0) return "-";
            var ts = TimeSpan.FromMilliseconds(ms);
            if (ts.TotalDays >= 1) return $"{(int)ts.TotalDays}d {ts.Hours}h";
            if (ts.TotalHours >= 1) return $"{(int)ts.TotalHours}h {ts.Minutes}m";
            return $"{ts.Minutes}m";
        }

        internal static void ShowUnfriendToast(string displayName)
        {
            ShowToast("Unfriended", $"{displayName} has been removed.");
            _ = DiscordWebhookService.NotifyUnfriendAsync(displayName);
            _ = OscNotificationService.NotifyUnfriendAsync(displayName);
        }

        internal static void ShowToast(string title, string msg)
        {
            Console.WriteLine($"[Toast] {title}: {msg}");
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                try { Process.Start("notify-send", $"\"{title}\" \"{msg}\""); } catch { }
            }
        }

        #endregion

        #region Updates
        private static string? _updateStatus;
        private static string? _updateAvailableTag;
        private static string? _updateDownloadUrl;
        private static bool _updateChecking;
        private static bool _updateDownloading;
        private static float _updateProgress;
        private static string? _updateError;

        public static string? UpdateStatus => _updateStatus;
        public static string? UpdateAvailableTag => _updateAvailableTag;
        public static bool UpdateChecking => _updateChecking;
        public static bool UpdateDownloading => _updateDownloading;
        public static float UpdateProgress => _updateProgress;
        public static string? UpdateError => _updateError;

        public static void ClearUpdateError() => _updateError = null;

        static bool IsRunningAppImage =>
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("APPIMAGE"));

        // XOSC-style: AppImage process -> VRCUFM-x86_64.AppImage; else VRCUFM.zip (win-x64/ / linux-x64/)
        static string? PickReleaseAssetUrl(System.Text.Json.JsonElement assets)
        {
            string? appImage = null;
            string? bundleZip = null;
            string? platformZip = null;
            string? anyZip = null;
            bool isWin = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

            foreach (var a in assets.EnumerateArray())
            {
                var name = a.GetProperty("name").GetString() ?? "";
                var url = a.GetProperty("browser_download_url").GetString();
                if (string.IsNullOrEmpty(url)) continue;

                if (name.Equals("VRCUFM-x86_64.AppImage", StringComparison.OrdinalIgnoreCase))
                    appImage = url;
                else if (name.Equals("VRCUFM.zip", StringComparison.OrdinalIgnoreCase))
                    bundleZip = url;
                else if (name.Equals(isWin ? "VRCUFM-win-x64.zip" : "VRCUFM-linux-x64.zip", StringComparison.OrdinalIgnoreCase))
                    platformZip = url;
                else if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    anyZip ??= url;
            }

            if (IsRunningAppImage)
                return appImage ?? bundleZip ?? platformZip ?? anyZip;
            return bundleZip ?? platformZip ?? anyZip;
        }

        public static async Task CheckForUpdatesAsync()
        {
            if (_updateChecking || _updateDownloading) return;
            _updateChecking = true;
            checkingForUpdate = true;
            updateAvailable = false;
            _updateStatus = "Checking for updates...";
            _updateError = null;
            _updateAvailableTag = null;
            _updateDownloadUrl = null;
            try
            {
                using var http = new HttpClient();
                http.DefaultRequestHeaders.UserAgent.ParseAdd("VRCUFM-Updater");
                http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

                var json = await http.GetStringAsync("https://api.github.com/repos/hollyntt/VRChat-Unfriend-Manager/releases/latest");
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var root = doc.RootElement;
                var tag = root.GetProperty("tag_name").GetString() ?? "";
                var remoteStr = tag.TrimStart('v', 'V').Split('-', '+')[0];
                var localStr = AppVersion.TrimStart('v', 'V').Split('-', '+')[0];

                bool remoteNewer;
                if (Version.TryParse(remoteStr, out var remoteVer) && Version.TryParse(localStr, out var localVer))
                    remoteNewer = remoteVer > localVer;
                else
                    remoteNewer = !string.Equals(remoteStr, localStr, StringComparison.OrdinalIgnoreCase);

                if (!remoteNewer)
                {
                    _updateStatus = "Already on latest (" + tag + ")";
                    updateAvailable = false;
                    return;
                }

                if (!root.TryGetProperty("assets", out var assets))
                {
                    _updateStatus = "Release has no assets";
                    return;
                }

                var url = PickReleaseAssetUrl(assets);
                if (url == null)
                {
                    _updateStatus = "No Windows zip asset found";
                    return;
                }

                _updateAvailableTag = tag;
                _updateDownloadUrl = url;
                _updateStatus = "Update available: " + tag;
                updateAvailable = true;
                latestVersion = tag;
                downloadUrl = url;
            }
            catch (Exception ex)
            {
                _updateStatus = "Update check failed";
                _updateError = ex.Message;
            }
            finally
            {
                _updateChecking = false;
                checkingForUpdate = false;
            }
        }

        public static async Task DownloadAndInstallUpdateAsync()
        {
            if (_updateDownloading) return;
            if (string.IsNullOrEmpty(_updateDownloadUrl))
            {
                _updateError = "No download URL. Check for updates first.";
                return;
            }

            _updateDownloading = true;
            downloading = true;
            _updateProgress = 0;
            downloadProgress = 0;
            _updateError = null;
            _updateStatus = "Downloading...";

            string? zipPath = null;
            string? staging = null;
            try
            {
                bool assetIsAppImage = _updateDownloadUrl?.Contains("AppImage", StringComparison.OrdinalIgnoreCase) == true;
                zipPath = Path.Combine(Path.GetTempPath(), assetIsAppImage ? "VRCUFM_update.AppImage" : "VRCUFM_update.zip");
                staging = Path.Combine(Path.GetTempPath(), "VRCUFM_staging_" + Environment.ProcessId);
                if (Directory.Exists(staging)) Directory.Delete(staging, true);
                Directory.CreateDirectory(staging);

                using (var http = new HttpClient())
                {
                    http.DefaultRequestHeaders.UserAgent.ParseAdd("VRCUFM-Updater");
                    using var resp = await http.GetAsync(_updateDownloadUrl, HttpCompletionOption.ResponseHeadersRead);
                    resp.EnsureSuccessStatusCode();
                    var total = resp.Content.Headers.ContentLength ?? -1;
                    await using var src = await resp.Content.ReadAsStreamAsync();
                    await using var dst = File.Create(zipPath);
                    var buffer = new byte[81920];
                    long readTotal = 0;
                    int n;
                    while ((n = await src.ReadAsync(buffer)) > 0)
                    {
                        await dst.WriteAsync(buffer.AsMemory(0, n));
                        readTotal += n;
                        if (total > 0) { _updateProgress = (float)readTotal / total; downloadProgress = _updateProgress; }
                    }
                }

                _updateProgress = 1f;

                var currentExe = Environment.ProcessPath
                    ?? Path.Combine(AppContext.BaseDirectory,
                        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "VRCUFM.exe" : "VRCUFM");

                // AppImage self-update (XOSC): replace $APPIMAGE binary, chmod, relaunch
                if (IsRunningAppImage && assetIsAppImage)
                {
                    _updateStatus = "Installing AppImage...";
                    string appImagePath = Environment.GetEnvironmentVariable("APPIMAGE")!;
                    string bak = appImagePath + ".bak";
                    if (File.Exists(bak)) File.Delete(bak);
                    if (File.Exists(appImagePath)) File.Move(appImagePath, bak);
                    File.Copy(zipPath!, appImagePath, true);
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "chmod",
                            Arguments = "+x \"" + appImagePath + "\"",
                            UseShellExecute = false,
                        })?.WaitForExit(5000);
                    }
                    catch { }
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = appImagePath,
                        UseShellExecute = true,
                    });
                    _updateStatus = "Installing... app will restart";
                    await Task.Delay(400);
                    Environment.Exit(0);
                    return;
                }

                _updateStatus = "Extracting...";
                System.IO.Compression.ZipFile.ExtractToDirectory(zipPath!, staging!, true);

                // Bundle layout: win-x64/ or linux-x64/ (XOSC-style VRCUFM.zip)
                string platformFolder = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "win-x64" : "linux-x64";
                string platformDir = Path.Combine(staging!, platformFolder);
                string searchRoot = Directory.Exists(platformDir) ? platformDir : staging!;

                string? exePath = Directory.GetFiles(searchRoot, "VRCUFM.exe", SearchOption.AllDirectories).FirstOrDefault()
                    ?? Directory.GetFiles(searchRoot, "VRCUFM", SearchOption.AllDirectories)
                        .FirstOrDefault(f => !f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    ?? Directory.GetFiles(searchRoot, "VRChatUnfriendManager.exe", SearchOption.AllDirectories).FirstOrDefault()
                    ?? Directory.GetFiles(searchRoot, "*.exe", SearchOption.AllDirectories)
                        .OrderByDescending(f => new FileInfo(f).Length).FirstOrDefault();

                if (exePath == null)
                    throw new InvalidOperationException("No executable found in update package.");

                var payloadDir = Path.GetDirectoryName(exePath)!;
                var appDir = Path.GetDirectoryName(currentExe)!;
                var exeName = Path.GetFileName(currentExe);

                var logPath = Path.Combine(Path.GetTempPath(), "VRCUFM_update.log");
                bool isWin = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
                var scriptPath = Path.Combine(Path.GetTempPath(), isWin ? "VRCUFM_apply_update.cmd" : "VRCUFM_apply_update.sh");

                if (isWin)
                {
                    // Regular strings: use \\ for a single backslash in the output script
                    var script =
                        "@echo off" + Environment.NewLine
                        + "setlocal" + Environment.NewLine
                        + "set LOG=" + logPath + Environment.NewLine
                        + "echo time=%date% %time% > \"%LOG%\"" + Environment.NewLine
                        + "echo pid=" + Environment.ProcessId + ">> \"%LOG%\"" + Environment.NewLine
                        + "echo payload=" + payloadDir + ">> \"%LOG%\"" + Environment.NewLine
                        + "echo appDir=" + appDir + ">> \"%LOG%\"" + Environment.NewLine
                        + "echo exe=" + exeName + ">> \"%LOG%\"" + Environment.NewLine
                        + "set SRC=" + payloadDir + Environment.NewLine
                        + "set DST=" + appDir + Environment.NewLine
                        + "set EXE=" + exeName + Environment.NewLine
                        + ":wait" + Environment.NewLine
                        + "tasklist /FI \"PID eq " + Environment.ProcessId + "\" 2>NUL | find /I \"" + Environment.ProcessId + "\" >NUL" + Environment.NewLine
                        + "if not errorlevel 1 (" + Environment.NewLine
                        + "  timeout /t 1 /nobreak >NUL" + Environment.NewLine
                        + "  goto wait" + Environment.NewLine
                        + ")" + Environment.NewLine
                        + "echo process exited>> \"%LOG%\"" + Environment.NewLine
                        + "where robocopy >NUL 2>&1" + Environment.NewLine
                        + "if not errorlevel 1 (" + Environment.NewLine
                        + "  robocopy \"%SRC%\" \"%DST%\" /E /R:8 /W:1 /NFL /NDL /NJH /NJS >> \"%LOG%\" 2>&1" + Environment.NewLine
                        + ") else (" + Environment.NewLine
                        + "  xcopy \"%SRC%\\*\" \"%DST%\\\" /E /Y /C /Q >> \"%LOG%\" 2>&1" + Environment.NewLine
                        + ")" + Environment.NewLine
                        + "if exist \"%DST%\\%EXE%\" (" + Environment.NewLine
                        + "  if exist \"%DST%\\%EXE%.bak\" del /f /q \"%DST%\\%EXE%.bak\"" + Environment.NewLine
                        + "  ren \"%DST%\\%EXE%\" \"%EXE%.bak\" 2>> \"%LOG%\"" + Environment.NewLine
                        + ")" + Environment.NewLine
                        + "copy /Y \"%SRC%\\%EXE%\" \"%DST%\\%EXE%\" >> \"%LOG%\" 2>&1" + Environment.NewLine
                        + "echo copy done>> \"%LOG%\"" + Environment.NewLine
                        + "start \"\" \"%DST%\\%EXE%\"" + Environment.NewLine
                        + "del \"%~f0\"" + Environment.NewLine;
                    await File.WriteAllTextAsync(scriptPath, script);
                }
                else
                {
                    string Slash(string p) => p.Replace("\\", "/");
                    var sh =
                        "#!/bin/bash" + "\n"
                        + "LOG=\"" + Slash(logPath) + "\"" + "\n"
                        + "SRC=\"" + Slash(payloadDir) + "\"" + "\n"
                        + "DST=\"" + Slash(appDir) + "\"" + "\n"
                        + "EXE=\"" + exeName + "\"" + "\n"
                        + "echo update start > \"$LOG\"" + "\n"
                        + "while kill -0 " + Environment.ProcessId + " 2>/dev/null; do sleep 1; done" + "\n"
                        + "echo process exited >> \"$LOG\"" + "\n"
                        + "cp -rf \"$SRC/.\" \"$DST/\" >> \"$LOG\" 2>&1" + "\n"
                        + "chmod +x \"$DST/$EXE\" >> \"$LOG\" 2>&1" + "\n"
                        + "\"$DST/$EXE\" &" + "\n"
                        + "rm -f \"$0\"" + "\n";
                    await File.WriteAllTextAsync(scriptPath, sh);
                    try
                    {
                        System.Diagnostics.Process.Start("chmod", "+x \"" + scriptPath + "\"")?.WaitForExit(3000);
                    }
                    catch { }
                }

                bool needAdmin = false;
                try
                {
                    var probe = Path.Combine(appDir, ".write_probe_" + Environment.ProcessId);
                    await File.WriteAllTextAsync(probe, "ok");
                    File.Delete(probe);
                }
                catch { needAdmin = true; }

                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = scriptPath,
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetTempPath(),
                };
                if (needAdmin) psi.Verb = "runas";

                System.Diagnostics.Process.Start(psi);
                _updateStatus = "Installing... app will restart";
                await Task.Delay(400);
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                _updateError = ex.Message;
                _updateStatus = "Update failed";
                try { if (zipPath != null && File.Exists(zipPath)) File.Delete(zipPath); } catch { }
                try { if (staging != null && Directory.Exists(staging)) Directory.Delete(staging, true); } catch { }
            }
            finally
            {
                _updateDownloading = false;
                downloading = false;
            }
        }
        #endregion



        static int _updateLoopStarted = 0;
        static void StartUpdateAutoCheckLoop()
        {
            if (System.Threading.Interlocked.Exchange(ref _updateLoopStarted, 1) == 1) return;
            _ = Task.Run(async () =>
            {
                await Task.Delay(8000);
                while (!shouldExit)
                {
                    try
                    {
                        if (config.AutoCheckUpdates)
                        {
                            await CheckForUpdatesAsync();
                            if (config.AutoApplyUpdates
                                && !string.IsNullOrEmpty(UpdateAvailableTag)
                                && !UpdateDownloading)
                            {
                                await DownloadAndInstallUpdateAsync();
                            }
                        }
                    }
                    catch { }
                    await Task.Delay(TimeSpan.FromHours(6));
                }
            });
        }

        #region Configuration

        internal static void LoadConfig()
        {
            Paths.EnsureExists();
            if (!File.Exists(Paths.ConfigFile)) return;
            try
            {
                var json = File.ReadAllText(Paths.ConfigFile);
                var c = JsonSerializer.Deserialize<AppConfig>(json);
                if (c != null) config = c;
            }
            catch { }
        }

        internal static void SaveConfig()
        {
            Paths.EnsureExists();

            if (!string.IsNullOrEmpty(user))
                config.Username = user;

            if (remember && !string.IsNullOrEmpty(pass))
                config.EncodedPassword = Convert.ToBase64String(Encoding.UTF8.GetBytes(pass));
            else if (!remember)
                config.EncodedPassword = "";

            config.RememberMe = remember;
            config.ExcludeFavorites = hideFavs;
            config.InactiveEnabled = inactiveOn;
            config.InactiveValue = inactiveVal;
            config.InactiveUnitIndex = inactiveUnit;
            config.TogetherFilterEnabled = togetherOn;
            config.TogetherFilterValue = togetherVal;
            config.TogetherFilterUnit = togetherUnit;
            config.SortOptionIndex = sort;
            config.AutoDeclineFriendRequests = autoDecline;
            config.AutoSendRequestBack = autoSendBack;
            config.AutoDeclineOnlyFromStrangers = onlyStrangers;

            try
            {
                File.WriteAllText(Paths.ConfigFile,
                    JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        #endregion
    }
}
