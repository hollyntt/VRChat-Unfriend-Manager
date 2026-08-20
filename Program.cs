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
                    status = $"Welcome back, {name}";
                    await Refresh();
                    if (config.AutoUnfriendEnabled) SchedulerService.StartAutoScheduler();
                    if (config.AutoDeclineFriendRequests) SchedulerService.StartAutoDeclineChecker();
                }
                else
                {
                    status = string.IsNullOrEmpty(config.Username)
                        ? "Please log in"
                        : "Session expired — please log in again";
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

                if (sessionRestored || isLoggedIn) UIRenderer.DrawMainUI();
                else UIRenderer.DrawLoginScreen();

                api.Draw2FADialog();
                UIRenderer.DrawAutoUnfriendConfirmDialog();
                ImGui.End();
                rlImGui.End();
                Raylib.EndDrawing();
            }

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
                selected.Clear();
                await Refresh();
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
                status = "Session expired — please re-login";
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

        internal static void ShowUnfriendToast(string displayName) => ShowToast("Unfriended", $"{displayName} has been removed.");

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

        internal static async Task CheckForUpdatesAsync()
        {
            checkingForUpdate = true;
            updateAvailable = false;
            latestVersion = "";
            downloadUrl = "";
            expectedHash = "";

            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("VRChat-Unfriend-Manager-Updater");
                client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
                client.Timeout = TimeSpan.FromSeconds(30);

                var response = await client.GetAsync(
                    "https://api.github.com/repos/hollyntt/VRChat-Unfriend-Manager/releases/latest");
                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                string tag = root.GetProperty("tag_name").GetString() ?? "";
                latestVersion = tag.TrimStart('v', 'V').Trim();
                string local = (AppVersion ?? "unknown").TrimStart('v', 'V').Trim();
                Console.WriteLine($"[Updater] Local: '{local}'  Latest: '{latestVersion}'");

                if (string.IsNullOrEmpty(latestVersion) ||
                    string.Equals(latestVersion, local, StringComparison.OrdinalIgnoreCase))
                {
                    updateAvailable = false;
                    return;
                }

                bool isWin = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
                bool isLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
                string? preferredZip = null;
                string? anyZip = null;

                foreach (var asset in root.GetProperty("assets").EnumerateArray())
                {
                    var name = asset.GetProperty("name").GetString() ?? "";
                    var url = asset.GetProperty("browser_download_url").GetString() ?? "";
                    if (string.IsNullOrEmpty(url)) continue;

                    if (name.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase) ||
                        name.EndsWith(".sha256.txt", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            var hr = await client.GetAsync(url);
                            if (hr.IsSuccessStatusCode)
                            {
                                var text = (await hr.Content.ReadAsStringAsync()).Trim();
                                expectedHash = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                                    .FirstOrDefault() ?? "";
                            }
                        }
                        catch { }
                        continue;
                    }

                    if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        continue;

                    anyZip ??= url;
                    string lower = name.ToLowerInvariant();
                    if (isWin && (lower.Contains("win") || lower.Contains("windows") || lower.Contains("vrcufm")))
                        preferredZip = url;
                    else if (isLinux && (lower.Contains("linux") || lower.Contains("ubuntu")))
                        preferredZip = url;
                }

                downloadUrl = preferredZip ?? anyZip ?? "";
                updateAvailable = !string.IsNullOrEmpty(downloadUrl);
                Console.WriteLine(updateAvailable
                    ? $"[Updater] Update available → {downloadUrl}"
                    : "[Updater] Release has no .zip asset");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Updater] Check failed: {ex.Message}");
            }
            finally { checkingForUpdate = false; }
        }

        internal static async Task DownloadAndInstallUpdateAsync()
        {
            if (string.IsNullOrEmpty(downloadUrl)) return;

            downloading = true;
            downloadProgress = 0f;

            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("VRChat-Unfriend-Manager-Updater");
                client.Timeout = TimeSpan.FromMinutes(10);

                string tempZip = Path.Combine(Path.GetTempPath(), $"VRCUFM_update_{Environment.ProcessId}.zip");
                string stagingDir = Path.Combine(Path.GetTempPath(), $"VRCUFM_staging_{Environment.ProcessId}");

                using (var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();
                    long totalBytes = response.Content.Headers.ContentLength ?? 0;
                    await using var contentStream = await response.Content.ReadAsStreamAsync();
                    await using var fs = new FileStream(tempZip, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 81920, true);

                    byte[] buffer = new byte[81920];
                    long totalRead = 0;
                    int read;
                    while ((read = await contentStream.ReadAsync(buffer)) > 0)
                    {
                        await fs.WriteAsync(buffer.AsMemory(0, read));
                        totalRead += read;
                        if (totalBytes > 0)
                            downloadProgress = Math.Clamp((float)totalRead / totalBytes, 0f, 1f);
                    }

                    if (!string.IsNullOrEmpty(expectedHash))
                    {
                        fs.Position = 0;
                        using var sha = SHA256.Create();
                        byte[] hash = await sha.ComputeHashAsync(fs);
                        string actual = Convert.ToHexString(hash).ToLowerInvariant();
                        string expected = expectedHash.Trim().ToLowerInvariant().Replace(" ", "");
                        if (expected.Contains('*'))
                            expected = expected.Split('*')[0];
                        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                        {
                            try { File.Delete(tempZip); } catch { }
                            MessageBox.Show(
                                $"Hash mismatch.\nExpected: {expectedHash}\nGot: {actual}",
                                "Update Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            return;
                        }
                    }
                }

                downloadProgress = 1f;

                if (Directory.Exists(stagingDir)) Directory.Delete(stagingDir, true);
                Directory.CreateDirectory(stagingDir);
                ZipFile.ExtractToDirectory(tempZip, stagingDir);
                try { File.Delete(tempZip); } catch { }

                bool isWin = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

                // Flat release zip contains VRCUFM.exe at root
                string[] exeNames = isWin
                    ? new[] { "VRCUFM.exe", "VRChatUnfriendManager.exe", "Unfriendmaxxing.exe" }
                    : new[] { "VRCUFM", "VRChatUnfriendManager", "Unfriendmaxxing" };

                string? newExe = null;
                foreach (var name in exeNames)
                {
                    string direct = Path.Combine(stagingDir, name);
                    if (File.Exists(direct))
                    {
                        newExe = direct;
                        break;
                    }
                    newExe = Directory.GetFiles(stagingDir, name, SearchOption.AllDirectories).FirstOrDefault();
                    if (newExe != null) break;
                }
                if (newExe == null && isWin)
                {
                    newExe = Directory.GetFiles(stagingDir, "*.exe", SearchOption.AllDirectories)
                        .OrderByDescending(f => new FileInfo(f).Length)
                        .FirstOrDefault();
                }

                if (newExe == null)
                {
                    MessageBox.Show(
                        "Could not find VRCUFM.exe in the update archive.",
                        "Update Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                string payloadRoot = Path.GetDirectoryName(newExe)!;
                string currentExe = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                if (string.IsNullOrEmpty(currentExe))
                {
                    MessageBox.Show("Cannot determine current executable path.", "Update Failed",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                string appDir = Path.GetDirectoryName(currentExe)!;
                string targetExeName = Path.GetFileName(newExe);
                int currentPid = Environment.ProcessId;

                Console.WriteLine($"[Updater] Payload root: {payloadRoot}");
                Console.WriteLine($"[Updater] Install dir:  {appDir}");
                Console.WriteLine($"[Updater] Launch:       {targetExeName}");

                if (isWin)
                {
                    string batPath = Path.Combine(Path.GetTempPath(), $"VRCUFM_update_{currentPid}.bat");
                    string bat = $@"@echo off
setlocal EnableExtensions
set ""PID={currentPid}""
set ""SRC={payloadRoot}""
set ""DST={appDir}""
set ""EXE={targetExeName}""

:wait
tasklist /FI ""PID eq %PID%"" 2>NUL | find ""%PID%"" >NUL
if not errorlevel 1 (
  timeout /t 1 /nobreak >NUL
  goto wait
)
timeout /t 1 /nobreak >NUL

xcopy ""%SRC%\*"" ""%DST%\"" /E /Y /I /Q /H /R
if errorlevel 1 (
  echo [Updater] copy failed
  exit /b 1
)

rmdir /S /Q ""{stagingDir}"" >NUL 2>&1
del ""%~f0"" >NUL 2>&1

start """" ""%DST%\%EXE%""
".Replace("\r\n", "\n").Replace("\n", "\r\n");

                    File.WriteAllText(batPath, bat);
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/C \"{batPath}\"",
                        WindowStyle = ProcessWindowStyle.Hidden,
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        WorkingDirectory = Path.GetTempPath()
                    });
                }
                else
                {
                    string bashPath = Path.Combine(Path.GetTempPath(), $"VRCUFM_update_{currentPid}.sh");
                    string Q(string s) => "\"" + s.Replace("\"", "\\\"") + "\"";
                    string bash = $@"#!/bin/bash
set -e
PID_TO_WAIT={currentPid}
SRC={Q(payloadRoot)}
DST={Q(appDir)}
EXE={Q(targetExeName)}
STAGING={Q(stagingDir)}
SCRIPT={Q(bashPath)}

while kill -0 ""$PID_TO_WAIT"" 2>/dev/null; do sleep 1; done
sleep 1
cp -rf ""$SRC""/. ""$DST""/
chmod +x ""$DST/$EXE"" || true
rm -rf ""$STAGING""
rm -f ""$SCRIPT""
cd ""$DST"" && nohup ""./$EXE"" >/dev/null 2>&1 &
".Trim();
                    File.WriteAllText(bashPath, bash);
                    try
                    {
                        Process.Start(new ProcessStartInfo("chmod", $"+x {Q(bashPath)}")
                        {
                            UseShellExecute = false
                        })?.WaitForExit(2000);
                    }
                    catch { }

                    Process.Start(new ProcessStartInfo("/bin/bash", bashPath)
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                }

                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Updater] Install failed: {ex}");
                MessageBox.Show($"Update failed:\n{ex.Message}", "Update Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                downloading = false;
                downloadProgress = 0f;
            }
        }
        #endregion

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
