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

            // First-run / migrate-from-Program-Files setup
            needsSetup = InstallService.NeedsSetup(config);
            if (string.IsNullOrWhiteSpace(setupInstallPath))
                setupInstallPath = string.IsNullOrWhiteSpace(config.InstallPath)
                    ? Paths.DefaultInstallDir
                    : config.InstallPath;

            // Already under Program Files with setup marked portable? Still OK.
            // Fresh Program Files install without setup → force setup so updates can work.
            if (!needsSetup && InstallService.IsUnderProgramFiles(InstallService.GetCurrentAppDir())
                && !config.PortableMode
                && string.IsNullOrWhiteSpace(config.InstallPath))
            {
                needsSetup = true;
                setupInstallPath = Paths.DefaultInstallDir;
            }


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

                if (needsSetup) UIRenderer.DrawSetupScreen();
                else if (sessionRestored || isLoggedIn) UIRenderer.DrawMainUI();
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
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Unfriendmaxxing-Updater");
                var response = await client.GetAsync(
                    "https://api.github.com/repos/hollyntt/VRChat-Unfriend-Manager/releases/latest");
                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                string tag = root.GetProperty("tag_name").GetString() ?? "";
                latestVersion = tag.TrimStart('v').Trim();
                Console.WriteLine($"[Updater] Local: '{AppVersion}' Latest: '{latestVersion}'");

                if (string.Equals(latestVersion, AppVersion, StringComparison.OrdinalIgnoreCase))
                {
                    updateAvailable = false;
                    return;
                }

                foreach (var asset in root.GetProperty("assets").EnumerateArray())
                {
                    var name = asset.GetProperty("name").GetString() ?? "";
                    var url  = asset.GetProperty("browser_download_url").GetString() ?? "";
                    if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        downloadUrl = url;
                        updateAvailable = true;
                    }
                    else if (name.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            var hr = await client.GetAsync(url);
                            expectedHash = (await hr.Content.ReadAsStringAsync()).Trim();
                        }
                        catch { }
                    }
                }
            }
            catch { }
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
                var tempZip = Path.Combine(Path.GetTempPath(), "VRCUFM_update.zip");

                using var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();
                long totalBytes = response.Content.Headers.ContentLength ?? 0;

                using var contentStream = await response.Content.ReadAsStreamAsync();
                using var fs = new FileStream(tempZip, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 8192, true);
                byte[] buffer = new byte[8192];
                long totalRead = 0;
                int read;
                while ((read = await contentStream.ReadAsync(buffer)) > 0)
                {
                    await fs.WriteAsync(buffer.AsMemory(0, read));
                    totalRead += read;
                    if (totalBytes > 0) downloadProgress = (float)totalRead / totalBytes;
                }

                if (!string.IsNullOrEmpty(expectedHash))
                {
                    fs.Seek(0, SeekOrigin.Begin);
                    using var sha = SHA256.Create();
                    byte[] hash = await sha.ComputeHashAsync(fs);
                    string actual = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                    if (!string.Equals(actual, expectedHash, StringComparison.OrdinalIgnoreCase))
                    {
                        fs.Close();
                        File.Delete(tempZip);
                        MessageBox.Show($"Hash mismatch.\nExpected: {expectedHash}\nGot: {actual}",
                            "Update Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                }
                fs.Close();
                string stagingDir = Path.Combine(Path.GetTempPath(), "VRCUFM_staging");
                if (Directory.Exists(stagingDir)) Directory.Delete(stagingDir, true);
                Directory.CreateDirectory(stagingDir);
                ZipFile.ExtractToDirectory(tempZip, stagingDir);
                File.Delete(tempZip);

                bool isWin = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
                string[] exeNames = isWin
                    ? new[] { "VRChatUnfriendManager.exe", "Unfriendmaxxing.exe" }
                    : new[] { "VRChatUnfriendManager", "Unfriendmaxxing" };

                string? newExeInStaging = null;
                foreach (var name in exeNames)
                {
                    newExeInStaging = Directory.GetFiles(stagingDir, name, SearchOption.AllDirectories).FirstOrDefault();
                    if (newExeInStaging != null) break;
                }
                if (newExeInStaging == null && isWin)
                    newExeInStaging = Directory.GetFiles(stagingDir, "*.exe", SearchOption.AllDirectories).FirstOrDefault();

                if (newExeInStaging == null)
                {
                    MessageBox.Show("Could not find executable in the update archive.",
                        "Update Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                string currentExe = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                if (string.IsNullOrEmpty(currentExe))
                {
                    MessageBox.Show("Cannot determine current executable path.", "Update Failed",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                string appDir = Path.GetDirectoryName(currentExe)!;
                int currentPid = Environment.ProcessId;

                if (isWin)
                {
                    string psScriptPath = Path.Combine(Path.GetTempPath(), "VRCUFM_update.ps1");
                    string psScript = $@"
param([int]$pidToWait, [string]$sourceDir, [string]$destDir, [string]$exeName)

Wait-Process -Id $pidToWait -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2

Copy-Item -Path ""$sourceDir\*"" -Destination $destDir -Recurse -Force

Remove-Item -Recurse -Force $sourceDir
Remove-Item -Force ""{psScriptPath}""

Start-Process -FilePath ""$destDir\$exeName""
".Trim();
                    File.WriteAllText(psScriptPath, psScript);

                    string exeRelativeName = Path.GetFileName(newExeInStaging);
                    string args = $"-NoProfile -ExecutionPolicy Bypass -File \"{psScriptPath}\" -pidToWait {currentPid} -sourceDir \"{stagingDir}\" -destDir \"{appDir}\" -exeName \"{exeRelativeName}\"";
                    Process.Start(new ProcessStartInfo("powershell.exe", args)
                    {
                        WindowStyle = ProcessWindowStyle.Hidden,
                        CreateNoWindow = true,
                        UseShellExecute = false
                    });
                }
                else
                {
                    string bashScriptPath = Path.Combine(Path.GetTempPath(), "VRCUFM_update.sh");
                    string bashScript = $@"#!/bin/bash
PID_TO_WAIT={currentPid}
SOURCE_DIR=""{stagingDir.Replace("\"", "\\\"")}""
DEST_DIR=""{appDir.Replace("\"", "\\\"")}""
EXE_NAME=""{Path.GetFileName(newExeInStaging).Replace("\"", "\\\"")}""
SCRIPT_PATH=""{bashScriptPath.Replace("\"", "\\\"")}""

while kill -0 $PID_TO_WAIT 2>/dev/null; do sleep 1; done
sleep 2

cp -rf ""$SOURCE_DIR""/* ""$DEST_DIR""

rm -rf ""$SOURCE_DIR""
rm -f ""$SCRIPT_PATH""

cd ""$DEST_DIR"" && chmod +x ""$EXE_NAME"" && ./""$EXE_NAME"" &
".Trim();
                    File.WriteAllText(bashScriptPath, bashScript);

                    Process.Start("chmod", $"+x \"{bashScriptPath}\"").WaitForExit(500);
                    Process.Start(new ProcessStartInfo("/bin/bash", bashScriptPath)
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                }

                Environment.Exit(0);
            }
            catch (Exception ex)
            {
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
