using System.Diagnostics;
using System.Drawing;
using System.Net;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;
using ImGuiNET;
using Microsoft.Data.Sqlite;
using Raylib_cs;
using rlImGui_cs;
using VRChat.API.Api;
using VRChat.API.Client;
using VRChat.API.Model;
using File = System.IO.File;
using Color = Raylib_cs.Color;
using Image = Raylib_cs.Image;

namespace Unfriendmaxxing
{
    public static class Paths
    {
        public static readonly string AppDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VRChatUnfriendManager");
        public static readonly string CookieFile = Path.Combine(AppDataFolder, "session.cookie");
        public static readonly string ConfigFile = Path.Combine(AppDataFolder, "user.config");

        public static string VrcxBase => RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VRCX")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VRCX");

        public static string VrcxStartup => Path.Combine(VrcxBase, "startup");

        public static void EnsureExists() => Directory.CreateDirectory(AppDataFolder);
    }

    public class SafeLimitedUserFriend
    {
        public string Id { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string LastLogin { get; set; } = "";
        public long TimeSpentMs { get; set; } = 0;
        public string ThumbnailUrl { get; set; } = "";
    }

    public class AppConfig
    {
        public string Username { get; set; } = "";
        public string EncodedPassword { get; set; } = "";
        public string Cookie { get; set; } = "";
        public bool RememberMe { get; set; } = true;
        public bool ExcludeFavorites { get; set; } = true;
        public bool InactiveEnabled { get; set; } = false;
        public int InactiveValue { get; set; } = 3;
        public int InactiveUnitIndex { get; set; } = 1;
        public bool TogetherFilterEnabled { get; set; } = false;
        public int TogetherFilterValue { get; set; } = 60;
        public int TogetherFilterUnit { get; set; } = 1;
        public int SortOptionIndex { get; set; } = 0;
        public bool AutoUnfriendEnabled { get; set; } = false;
        public int AutoUnfriendHour { get; set; } = 3;
        public int AutoUnfriendMinute { get; set; } = 0;
        public int AutoUnfriendMode { get; set; } = 0;
        public int AutoUnfriendScheduleType { get; set; } = 0;
        public int AutoUnfriendMonthDay { get; set; } = 1;
        public int AutoUnfriendYear { get; set; } = DateTime.Now.Year;
        public int AutoUnfriendMonth { get; set; } = DateTime.Now.Month;
        public int AutoUnfriendDay { get; set; } = DateTime.Now.Day;
        public DateTime? AutoUnfriendLastRun { get; set; } = null;
        public bool RunOnStartup { get; set; } = false;
        public bool VrcxStartupDesktop { get; set; } = false;
        public bool VrcxStartupVr { get; set; } = false;
        public bool HideInTaskbar { get; set; } = true;
        public List<string> ExcludedFavGroups { get; set; } = new();

        public bool AutoDeclineFriendRequests { get; set; } = false;
        public bool AutoSendRequestBack { get; set; } = false;
        public bool AutoDeclineOnlyFromStrangers { get; set; } = true;
    }

    public class VRChatApiService
    {
        private const string UA = "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36";
        private static readonly Uri BaseUri = new("https://api.vrchat.cloud/api/1/");
        private readonly HttpClient client;
        private readonly CookieContainer cookies = new();
        private Configuration? cfg;
        private TaskCompletionSource<string?>? tfaTcs;
        private string tfaCode = "";
        private bool show2FADialog = false;
        private HttpClient? _authClient;

        public VRChatApiService()
        {
            var handler = new HttpClientHandler { CookieContainer = cookies, UseCookies = true, AllowAutoRedirect = true };
            client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd(UA);
        }

        private void SaveCookies()
        {
            Paths.EnsureExists();
            var authCookie = cookies.GetCookies(BaseUri)["auth"];
            var tfaCookie = cookies.GetCookies(BaseUri)["twoFactorAuth"];
            if (authCookie == null) return;

            var fullCookie = $"auth={authCookie.Value}";
            if (tfaCookie != null && !string.IsNullOrEmpty(tfaCookie.Value))
                fullCookie += $"; twoFactorAuth={tfaCookie.Value}";

            try { File.WriteAllText(Paths.CookieFile, fullCookie); } catch { }
            Program.config.Cookie = fullCookie;
            Program.SaveConfig();

            TextureCache.SetCookie(fullCookie);
            RebuildAuthClient(fullCookie);
        }

        private void RebuildAuthClient(string cookieHeader)
        {
            _authClient = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            _authClient.DefaultRequestHeaders.UserAgent.ParseAdd(UA);
            _authClient.DefaultRequestHeaders.Add("Cookie", cookieHeader);
        }

        private HttpClient GetAuthClient()
        {
            if (_authClient != null) return _authClient;

            var cookie = Program.config.Cookie;
            if (!string.IsNullOrWhiteSpace(cookie))
                RebuildAuthClient(cookie);
            else
                throw new InvalidOperationException("Not logged in — no cookie available");

            return _authClient!;
        }

        private string? _lastParsedDisplayName;

        private async Task<bool> TestSessionAsync()
        {
            if (cfg == null) return false;
            try
            {
                using var test = new HttpClient();
                test.DefaultRequestHeaders.UserAgent.ParseAdd(UA);
                if (cfg.DefaultHeaders.TryGetValue("Cookie", out var c))
                    test.DefaultRequestHeaders.Add("Cookie", c);
                var r = await test.GetAsync("https://api.vrchat.cloud/api/1/auth/user");
                var body = await r.Content.ReadAsStringAsync();
                if (!r.IsSuccessStatusCode) return false;
                if (!body.Contains("\"id\"", StringComparison.OrdinalIgnoreCase)) return false;
                var (displayName, userId) = ParseUserFromJson(body);
                if (!string.IsNullOrWhiteSpace(userId)) CurrentUserId = userId;
                _lastParsedDisplayName = displayName;
                return true;
            }
            catch { return false; }
        }

        private async Task<string?> GetCurrentDisplayNameAsync()
        {
            if (!string.IsNullOrWhiteSpace(_lastParsedDisplayName))
            {
                var n = _lastParsedDisplayName;
                _lastParsedDisplayName = null;
                return n;
            }
            if (cfg == null) return null;
            try
            {
                var user = await new AuthenticationApi(cfg).GetCurrentUserAsync();
                CurrentUserId = user?.Id;
                var name = user?.DisplayName;
                if (!string.IsNullOrWhiteSpace(name)) return name;
                return user?.Username;
            }
            catch { return null; }
        }

        public async Task<(bool success, string? displayName)> RestoreSessionFromDiskOrConfigAsync()
        {
            show2FADialog = false;

            var vrcxCookie = TryGetVrcxCookie();
            if (vrcxCookie != null)
            {
                cfg = new Configuration { UserAgent = UA };
                cfg.DefaultHeaders["Cookie"] = vrcxCookie;
                if (await TestSessionAsync())
                {
                    Program.config.Cookie = vrcxCookie;
                    Program.SaveConfig();
                    TextureCache.SetCookie(vrcxCookie);
                    RebuildAuthClient(vrcxCookie);
                    var name = await GetCurrentDisplayNameAsync();
                    if (!string.IsNullOrWhiteSpace(name)) return (true, name);
                }
            }

            if (!string.IsNullOrWhiteSpace(Program.config.Cookie) && Program.config.Cookie.Contains("auth="))
            {
                cfg = new Configuration { UserAgent = UA };
                cfg.DefaultHeaders["Cookie"] = Program.config.Cookie.Trim();
                if (await TestSessionAsync())
                {
                    TextureCache.SetCookie(Program.config.Cookie.Trim());
                    RebuildAuthClient(Program.config.Cookie.Trim());
                    var name = await GetCurrentDisplayNameAsync();
                    if (!string.IsNullOrWhiteSpace(name)) return (true, name);
                }
            }

            if (File.Exists(Paths.CookieFile))
            {
                var cookie = await File.ReadAllTextAsync(Paths.CookieFile);
                if (!string.IsNullOrWhiteSpace(cookie) && cookie.Contains("auth="))
                {
                    cfg = new Configuration { UserAgent = UA };
                    cfg.DefaultHeaders["Cookie"] = cookie.Trim();
                    if (await TestSessionAsync())
                    {
                        Program.config.Cookie = cookie.Trim();
                        Program.SaveConfig();
                        TextureCache.SetCookie(cookie.Trim());
                        RebuildAuthClient(cookie.Trim());
                        var name = await GetCurrentDisplayNameAsync();
                        if (!string.IsNullOrWhiteSpace(name)) return (true, name);
                    }
                }
            }

            if (!string.IsNullOrEmpty(Program.config.Username) && !string.IsNullOrEmpty(Program.config.EncodedPassword))
            {
                var p = Encoding.UTF8.GetString(Convert.FromBase64String(Program.config.EncodedPassword));
                var (success, name, error) = await LoginWithCredentialsAsync(Program.config.Username, p);
                if (success && !string.IsNullOrWhiteSpace(name)) return (true, name);
            }

            return (false, null);
        }

        public async Task<(bool success, string? displayName, string? error)> LoginWithCredentialsAsync(string username, string password)
        {
            try
            {
                client.DefaultRequestHeaders.Clear();
                client.DefaultRequestHeaders.Add("User-Agent", UA);

                var authString = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authString);

                var response = await client.GetAsync("https://api.vrchat.cloud/api/1/auth/user");
                var body = await response.Content.ReadAsStringAsync();

                if (body.Contains("requiresTwoFactorAuth"))
                {
                    show2FADialog = true;
                    tfaTcs = new TaskCompletionSource<string?>();

                    var code = await tfaTcs.Task;

                    if (string.IsNullOrEmpty(code))
                    {
                        client.DefaultRequestHeaders.Authorization = null;
                        return (false, null, "2FA Cancelled");
                    }

                    client.DefaultRequestHeaders.Authorization = null;

                    var verifyJson = JsonSerializer.Serialize(new { code = code });
                    var verifyContent = new StringContent(verifyJson, Encoding.UTF8, "application/json");

                    var verifyResp = await client.PostAsync("https://api.vrchat.cloud/api/1/auth/twofactorauth/totp/verify", verifyContent);

                    if (!verifyResp.IsSuccessStatusCode)
                        return (false, null, "2FA Verification Failed");

                    client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authString);
                    var reResp = await client.GetAsync("https://api.vrchat.cloud/api/1/auth/user");
                    body = await reResp.Content.ReadAsStringAsync();
                    client.DefaultRequestHeaders.Authorization = null;
                }
                else if (!response.IsSuccessStatusCode)
                {
                    client.DefaultRequestHeaders.Authorization = null;
                    return (false, null, $"Login failed: {response.StatusCode}");
                }

                client.DefaultRequestHeaders.Authorization = null;

                var cookieCollection = cookies.GetCookies(BaseUri);
                Cookie? authCookie = null;
                foreach (Cookie c in cookieCollection) if (c.Name == "auth") authCookie = c;

                if (authCookie == null)
                    return (false, null, "Login succeeded but 'auth' cookie was not set.");

                string fullCookie = $"auth={authCookie.Value}";
                var tfaCookie = cookies.GetCookies(BaseUri)["twoFactorAuth"];
                if (tfaCookie != null) fullCookie += $"; twoFactorAuth={tfaCookie.Value}";

                cfg = new Configuration();
                cfg.UserAgent = UA;
                cfg.DefaultHeaders ??= new Dictionary<string, string>();
                cfg.DefaultHeaders["Cookie"] = fullCookie;

                SaveCookies();

                var (displayName, userId) = ParseUserFromJson(body);
                if (!string.IsNullOrWhiteSpace(userId)) CurrentUserId = userId;

                if (string.IsNullOrWhiteSpace(displayName))
                {
                    try
                    {
                        var authApi = new AuthenticationApi(cfg);
                        var sdkUser = await authApi.GetCurrentUserAsync();
                        displayName = sdkUser?.DisplayName ?? sdkUser?.Username;
                        CurrentUserId = sdkUser?.Id ?? CurrentUserId;
                    }
                    catch { }
                }

                if (string.IsNullOrWhiteSpace(displayName))
                    return (false, null, "Logged in but could not read display name.");

                return (true, displayName, null);
            }
            catch (Exception ex)
            {
                client.DefaultRequestHeaders.Authorization = null;
                return (false, null, $"Error: {ex.Message}");
            }
        }

        private static (string? displayName, string? userId) ParseUserFromJson(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                string? name = null, id = null;

                if (root.TryGetProperty("displayName", out var dn) && dn.ValueKind == JsonValueKind.String)
                    name = dn.GetString();
                else if (root.TryGetProperty("username", out var un) && un.ValueKind == JsonValueKind.String)
                    name = un.GetString();

                if (root.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String)
                    id = idProp.GetString();

                return (name, id);
            }
            catch { }
            return (null, null);
        }

        public void Draw2FADialog()
        {
            if (!show2FADialog || tfaTcs == null) return;

            ImGui.OpenPopup("2FA Required");
            ImGui.SetNextWindowPos(new Vector2(Raylib.GetScreenWidth() / 2f, Raylib.GetScreenHeight() / 2f), ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));

            if (ImGui.BeginPopupModal("2FA Required", ref show2FADialog, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoMove))
            {
                ImGui.Text("Two-Factor Authentication Required");
                ImGui.Separator();
                ImGui.TextWrapped("Enter your 2FA code:");
                ImGui.SetNextItemWidth(200);
                ImGui.InputText("##2fa", ref tfaCode, 10, ImGuiInputTextFlags.CharsDecimal);

                if ((ImGui.IsItemFocused() && Raylib.IsKeyPressed(KeyboardKey.Enter)) || ImGui.Button("Submit"))
                {
                    tfaTcs.SetResult(tfaCode.Trim());
                    tfaTcs = null;
                    show2FADialog = false;
                    ImGui.CloseCurrentPopup();
                }

                ImGui.SameLine();
                if (ImGui.Button("Cancel"))
                {
                    tfaTcs.SetResult(null);
                    tfaTcs = null;
                    show2FADialog = false;
                    ImGui.CloseCurrentPopup();
                }

                ImGui.EndPopup();
            }
        }

        public FriendsApi Friends => cfg != null ? new FriendsApi(cfg) : throw new InvalidOperationException("Not logged in");
        public FavoritesApi Favorites => cfg != null ? new FavoritesApi(cfg) : throw new InvalidOperationException("Not logged in");
        public NotificationsApi Notifications => cfg != null ? new NotificationsApi(cfg) : throw new InvalidOperationException("Not logged in");

        public string? CurrentUserId { get; private set; }

        public async Task UnfriendAsync(string id)
        {
            await Friends.UnfriendAsync(id);
        }

        public async Task<List<SafeLimitedUserFriend>> GetAllFriendsAsync()
        {
            var list = new List<SafeLimitedUserFriend>();
            for (int offset = 0; ; offset += 100)
            {
                var page = await Friends.GetFriendsAsync(offset: offset, n: 100, offline: false);
                list.AddRange(page.Select(u => new SafeLimitedUserFriend
                {
                    Id = u.Id,
                    DisplayName = u.DisplayName ?? "Unknown",
                    LastLogin = u.LastLogin?.ToString("o") ?? "",
                    ThumbnailUrl = u.CurrentAvatarThumbnailImageUrl ?? u.CurrentAvatarImageUrl ?? u.ProfilePicOverrideThumbnail ?? u.ProfilePicOverride ?? "",
                }));
                if (page.Count < 100) break;
            }
            for (int offset = 0; ; offset += 100)
            {
                var page = await Friends.GetFriendsAsync(offset: offset, n: 100, offline: true);
                list.AddRange(page.Select(u => new SafeLimitedUserFriend
                {
                    Id = u.Id,
                    DisplayName = u.DisplayName ?? "Unknown",
                    LastLogin = u.LastLogin?.ToString("o") ?? "",
                    ThumbnailUrl = u.CurrentAvatarThumbnailImageUrl ?? u.CurrentAvatarImageUrl ?? u.ProfilePicOverrideThumbnail ?? u.ProfilePicOverride ?? "",
                }));
                if (page.Count < 100) break;
            }
            return list;
        }

        public async Task<(HashSet<string> allIds, Dictionary<string, HashSet<string>> byGroup)> GetFavoritesDetailedAsync()
        {
            var allIds = new HashSet<string>();
            var byGroup = new Dictionary<string, HashSet<string>>();

            for (int i = 0; i < 4; i++) byGroup[$"group_{i}"] = new HashSet<string>();

            for (int offset = 0; ; offset += 100)
            {
                var page = await Favorites.GetFavoritesAsync(type: "friend", n: 100, offset: offset);
                foreach (var f in page)
                {
                    allIds.Add(f.FavoriteId);
                    var tag = f.Tags?.FirstOrDefault() ?? "group_0";
                    if (!byGroup.ContainsKey(tag)) byGroup[tag] = new HashSet<string>();
                    byGroup[tag].Add(f.FavoriteId);
                }
                if (page.Count < 100) break;
            }
            return (allIds, byGroup);
        }

        public async Task<Dictionary<string, string>> GetFavoriteGroupNamesAsync()
        {
            var result = new Dictionary<string, string>();

            if (!string.IsNullOrEmpty(CurrentUserId))
            {
                try
                {
                    var url = $"https://api.vrchat.cloud/api/1/favorite/group?type=friend&n=10&offset=0&ownerId={CurrentUserId}";
                    var resp = await GetAuthClient().GetAsync(url);
                    var body = await resp.Content.ReadAsStringAsync();

                    if (resp.IsSuccessStatusCode)
                    {
                        using var doc = JsonDocument.Parse(body);
                        foreach (var g in doc.RootElement.EnumerateArray())
                        {
                            var tag = g.TryGetProperty("name", out var n) ? n.GetString() : null;
                            var displayName = g.TryGetProperty("displayName", out var dn) ? dn.GetString() : null;
                            if (!string.IsNullOrEmpty(tag))
                                result[tag] = !string.IsNullOrWhiteSpace(displayName) ? displayName : tag;
                        }
                    }
                }
                catch { }
            }

            if (result.Count == 0)
            {
                try
                {
                    var favGroups = await Favorites.GetFavoriteGroupsAsync(n: 10, offset: 0, ownerId: CurrentUserId);
                    foreach (var g in favGroups)
                    {
                        var tag = g.Tags?.FirstOrDefault() ?? g.Name ?? "";
                        if (!string.IsNullOrEmpty(tag))
                            result[tag] = !string.IsNullOrWhiteSpace(g.DisplayName) ? g.DisplayName : tag;
                    }
                }
                catch { }
            }

            for (int i = 0; i < 4; i++)
            {
                var key = $"group_{i}";
                if (!result.ContainsKey(key)) result[key] = $"Group {i + 1}";
            }

            return result;
        }

        public async Task<List<Notification>> GetIncomingFriendRequestsAsync()
        {
            var result = new List<Notification>();
            var http = GetAuthClient();

            for (int offset = 0; ; offset += 100)
            {
                var url = $"https://api.vrchat.cloud/api/1/auth/user/notifications?type=friendRequest&n=100&offset={offset}";
                HttpResponseMessage resp;
                string body;
                try
                {
                    resp = await http.GetAsync(url);
                    body = await resp.Content.ReadAsStringAsync();
                }
                catch { break; }

                if (!resp.IsSuccessStatusCode) break;

                try
                {
                    using var doc = JsonDocument.Parse(body);
                    foreach (var el in doc.RootElement.EnumerateArray())
                    {
                        var n = new Notification(
                            id: el.TryGetProperty("id", out var idP) ? idP.GetString() ?? "" : "",
                            senderUserId: el.TryGetProperty("senderUserId", out var suidP) ? suidP.GetString() : null,
                            senderUsername: el.TryGetProperty("senderUsername", out var sunP) ? sunP.GetString() : null,
                            type: NotificationType.FriendRequest,
                            message: el.TryGetProperty("message", out var msgP) ? msgP.GetString() ?? "" : "",
                            details: "",
                            seen: false,
                            createdAt: el.TryGetProperty("createdAt", out var caP) && DateTime.TryParse(caP.GetString(), out var dt) ? dt : DateTime.UtcNow
                        );

                        if (!string.IsNullOrEmpty(n.SenderUserId) && n.SenderUserId != CurrentUserId)
                            result.Add(n);
                    }
                }
                catch { break; }
            }
            return result;
        }

        public async Task DeclineFriendRequestAsync(string notificationId)
        {
            var http = GetAuthClient();
            await http.PutAsync($"https://api.vrchat.cloud/api/1/auth/user/notifications/{notificationId}/hide", new StringContent("{}", Encoding.UTF8, "application/json"));
        }

        public async Task SendFriendRequestAsync(string userId)
        {
            await Friends.FriendAsync(userId);
        }

        public async Task<bool> IsKnownPlayerAsync(string userId, HashSet<string> friendIds, Dictionary<string, long>? timeMap)
        {
            if (friendIds.Contains(userId)) return true;
            if (timeMap != null && timeMap.TryGetValue(userId, out var secs) && secs > 0) return true;
            return false;
        }

        public string? TryGetVrcxCookie()
        {
            try
            {
                string dbPath = Path.Combine(Paths.VrcxBase, "VRCX.sqlite3");
                if (!File.Exists(dbPath)) return null;

                using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath};Mode=ReadOnly;Cache=Shared");
                conn.Open();

                string? b64 = null;
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT value FROM cookies WHERE key='default' LIMIT 1";
                    b64 = cmd.ExecuteScalar() as string;
                }

                if (string.IsNullOrWhiteSpace(b64)) return null;

                var json = Encoding.UTF8.GetString(Convert.FromBase64String(b64));
                using var doc = JsonDocument.Parse(json);

                string? auth = null, tfa = null;
                foreach (var cookie in doc.RootElement.EnumerateArray())
                {
                    string? name = cookie.TryGetProperty("Name", out var n) ? n.GetString() : null;
                    string? value = cookie.TryGetProperty("Value", out var v) ? v.GetString() : null;
                    if (name == "auth") auth = value;
                    else if (name == "twoFactorAuth") tfa = value;
                }

                if (string.IsNullOrWhiteSpace(auth) || !auth.StartsWith("authcookie_")) return null;

                var cookie2 = $"auth={auth}";
                if (!string.IsNullOrWhiteSpace(tfa)) cookie2 += $"; twoFactorAuth={tfa}";

                return cookie2;
            }
            catch { return null; }
        }
    }

    public static class TextureCache
    {
        private enum State { Downloading, Ready, Failed }

        private sealed class Entry
        {
            public State State;
            public Texture2D Texture;
        }

        private static readonly Dictionary<string, Entry> _cache = new();
        private static readonly CookieContainer _cookieContainer = new();
        private static readonly HttpClient _http = new(new HttpClientHandler { CookieContainer = _cookieContainer, UseCookies = true })
        {
            Timeout = TimeSpan.FromSeconds(20),
            DefaultRequestHeaders = { { "User-Agent", "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36" } }
        };

        private static readonly Uri VrcApiBase = new("https://api.vrchat.cloud/");

        public static void SetCookie(string cookieHeader)
        {
            foreach (var part in cookieHeader.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = part.Trim().Split('=', 2);
                if (kv.Length == 2)
                    _cookieContainer.Add(VrcApiBase, new Cookie(kv[0].Trim(), kv[1].Trim()));
            }
        }

        public static Texture2D? RequestTexture(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;

            lock (_cache)
            {
                if (_cache.TryGetValue(url, out var entry))
                    return entry.State == State.Ready ? entry.Texture : null;

                _cache[url] = new Entry { State = State.Downloading };
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    var bytes = await _http.GetByteArrayAsync(url);
                    if (bytes.Length < 4) { MarkFailed(url); return; }

                    string fmt = (bytes[0] == 0xFF && bytes[1] == 0xD8) ? ".jpg" :
                                 (bytes[0] == 0x89 && bytes[1] == 0x50) ? ".png" :
                                 (bytes[0] == 0x47 && bytes[1] == 0x49) ? ".gif" : ".jpg";

                    var img = Raylib.LoadImageFromMemory(fmt, bytes);
                    if (img.Width == 0) { MarkFailed(url); return; }

                    Raylib.ImageResize(ref img, 32, 32);

                    lock (_pendingLoad)
                        _pendingLoad.Add((url, img));
                }
                catch { MarkFailed(url); }
            });

            return null;
        }

        private static void MarkFailed(string url)
        {
            lock (_cache)
                if (_cache.TryGetValue(url, out var e)) e.State = State.Failed;
        }

        private static readonly List<(string url, Image img)> _pendingLoad = new();

        public static void FlushPending()
        {
            List<(string url, Image img)> batch;
            lock (_pendingLoad)
            {
                if (_pendingLoad.Count == 0) return;
                batch = new List<(string, Image)>(_pendingLoad);
                _pendingLoad.Clear();
            }

            foreach (var (url, img) in batch)
            {
                try
                {
                    var tex = Raylib.LoadTextureFromImage(img);
                    Raylib.UnloadImage(img);
                    lock (_cache)
                    {
                        if (_cache.TryGetValue(url, out var entry))
                        {
                            entry.Texture = tex;
                            entry.State = State.Ready;
                        }
                        else Raylib.UnloadTexture(tex);
                    }
                }
                catch
                {
                    lock (_cache)
                        if (_cache.TryGetValue(url, out var e)) e.State = State.Failed;
                }
            }
        }

        public static void UnloadAll()
        {
            lock (_cache)
            {
                foreach (var entry in _cache.Values)
                    if (entry.State == State.Ready)
                        Raylib.UnloadTexture(entry.Texture);
                _cache.Clear();
            }
        }
    }

    public static class VrcxDataService
    {
        private static string DbPath => Path.Combine(Paths.VrcxBase, "VRCX.sqlite3");
        public static bool IsAvailable => File.Exists(DbPath);

        public static Dictionary<string, long> LoadTimeSpentSeconds()
        {
            var result = new Dictionary<string, long>();
            if (!IsAvailable) return result;

            try
            {
                using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={DbPath};Mode=ReadOnly;Cache=Shared");
                connection.Open();

                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    SELECT user_id, created_at, type
                    FROM   gamelog_join_leave
                    WHERE  user_id IS NOT NULL AND user_id != ''
                    ORDER  BY user_id ASC, created_at ASC";

                using var reader = cmd.ExecuteReader();

                var pendingJoin = new Dictionary<string, DateTime>();

                while (reader.Read())
                {
                    var userId = reader.GetString(0);
                    if (!DateTime.TryParse(reader.GetString(1), out var ts)) continue;
                    var type = reader.GetString(2);

                    if (type == "OnPlayerJoined")
                        pendingJoin[userId] = ts;
                    else if (type == "OnPlayerLeft" && pendingJoin.TryGetValue(userId, out var joinTime))
                    {
                        var secs = (long)(ts - joinTime).TotalSeconds;
                        if (secs > 0)
                        {
                            result.TryGetValue(userId, out var existing);
                            result[userId] = existing + secs;
                        }
                        pendingJoin.Remove(userId);
                    }
                }
            }
            catch { }

            return result;
        }
    }

    class Program
    {
        static VRChatApiService api = new();
        static List<SafeLimitedUserFriend> friends = new();
        static HashSet<string> favorites = new();
        static Dictionary<string, HashSet<string>> favByGroup = new();
        static Dictionary<string, string> favGroupNames = new();
        static List<SafeLimitedUserFriend> shown = new();
        static HashSet<int> selected = new();
        static string user = "", pass = "";
        static string loggedInAs = "";
        static bool remember = true;
        static bool hideFavs = true;
        static bool inactiveOn = false;
        static int inactiveVal = 3;
        static int inactiveUnit = 1;
        static bool togetherOn = false;
        static int togetherVal = 60;
        static int togetherUnit = 1;
        static string searchText = "";
        static int searchField = 0;
        static int sort = 0;
        static string status = "Starting up...";
        static bool working = false;
        static bool isUnfriending = false;
        static volatile bool pendingAutoConfirm = false;
        static volatile int pendingAutoCount = 0;
        static TaskCompletionSource<bool>? autoConfirmTcs = null;
        static readonly object autoConfirmLock = new();
        static DateTime autoConfirmDeadline = DateTime.MaxValue;
        static bool isPaused = false;
        static int unfriendTotal = 0;
        static int unfriendDone = 0;
        static CancellationTokenSource? unfriendCts;
        public static AppConfig config = new();
        static CancellationTokenSource? autoCts;
        static CancellationTokenSource? autoDeclineCts;
        static readonly string[] units = { "Days", "Months", "Years" };
        static readonly string[] sorts = { "Oldest", "Newest", "A-Z", "Z-A", "Most Time", "Least Time" };
        static readonly string[] autoModes = { "Inactive Only (3+ mo)", "All Shown", "Marked Only" };
        static bool isLoggedIn = false;
        static bool sessionRestored = false;
        static bool shouldExit = false;

        static List<Notification> incomingFriendRequests = new();
        static bool autoDecline = false;
        static bool autoSendBack = false;
        static bool onlyStrangers = true;

        // Update Manager
        static bool checkingForUpdate = false;
        static bool updateAvailable = false;
        static string latestVersion = "";
        static string downloadUrl = "";
        static float downloadProgress = 0f;
        static bool downloading = false;

        [DllImport("kernel32.dll")] static extern IntPtr GetConsoleWindow();
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int cmd);
        [DllImport("user32.dll")] static extern IntPtr FindWindow(string? cls, string wnd);
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")] static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")] static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
        [DllImport("user32.dll")] static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        const int SW_HIDE = 0, SW_RESTORE = 9;
        const int GWL_WNDPROC = -4;
        const uint WM_SYSCOMMAND = 0x0112;
        const long SC_MINIMIZE = 0xF020;

        static IntPtr originalWndProc = IntPtr.Zero;
        static GCHandle? wndProcHandle;
        static readonly WndProc wndProcDelegate = WndProcHook;
        delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        static void EnableMinimizeToTray()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
            try
            {
                var hwnd = FindWindow(null, "VRChat Unfriend Manager");
                if (hwnd == IntPtr.Zero) return;

                originalWndProc = GetWindowLongPtr(hwnd, GWL_WNDPROC);
                wndProcHandle = GCHandle.Alloc(wndProcDelegate);
                SetWindowLongPtr(hwnd, GWL_WNDPROC, Marshal.GetFunctionPointerForDelegate(wndProcDelegate));
            }
            catch { }
        }

        static IntPtr WndProcHook(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WM_SYSCOMMAND && wParam.ToInt64() == SC_MINIMIZE)
            {
                HideMainWindow();
                return IntPtr.Zero;
            }
            return CallWindowProc(originalWndProc, hWnd, msg, wParam, lParam);
        }

        static bool windowVisible = true;
        static bool _showRequested = false;
        static bool _trayRunning = false;
        static Thread? trayThread;
        static NotifyIcon? _notifyIcon;
        static readonly object _trayLock = new();

        static void ShowMainWindow()
        {
            _showRequested = true;
            windowVisible = true;
        }

        static void HideMainWindow()
        {
            windowVisible = false;
            Raylib.SetWindowState(ConfigFlags.HiddenWindow);
        }

        static void ApplyTaskbarVisibility(bool hideFromTaskbar)
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

        static void StartTrayThread(bool autostart)
        {
            lock (_trayLock)
            {
                if (_trayRunning) return;
                _trayRunning = true;
                trayThread = new Thread(() =>
                {
                    RunWindowsTray(autostart);
                    _trayRunning = false;
                });
                trayThread.IsBackground = true;
                trayThread.SetApartmentState(ApartmentState.STA);
                trayThread.Start();
            }
        }

        static void StopTrayThread()
        {
            lock (_trayLock)
            {
                _trayRunning = false;
                if (_notifyIcon != null)
                {
                    _notifyIcon.Visible = false;
                    _notifyIcon.Dispose();
                    _notifyIcon = null;
                }
            }
        }

        static Icon LoadTrayIcon()
        {
            return SystemIcons.Application;
        }

        static void RunWindowsTray(bool autostart)
        {
            if (autostart) HideMainWindow();
            ApplicationConfiguration.Initialize();

            _notifyIcon = new NotifyIcon
            {
                Icon = LoadTrayIcon(),
                Text = "VRChat Unfriend Manager",
                Visible = true
            };

            var menu = new ContextMenuStrip();
            menu.Items.Add("Show", null, (_, _) => ShowMainWindow());
            menu.Items.Add("Exit", null, (_, _) => { shouldExit = true; Application.ExitThread(); });

            _notifyIcon.ContextMenuStrip = menu;
            _notifyIcon.DoubleClick += (_, _) => ShowMainWindow();

            Application.Run();
        }

        public static async Task Main(string[] args)
        {
            bool isAutostart = args.Contains("--autostart");

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                try { var h = GetConsoleWindow(); if (h != IntPtr.Zero) ShowWindow(h, SW_HIDE); } catch { }
            }

            Console.WriteLine("VRChat Unfriend Manager Starting...");
            Paths.EnsureExists();
            LoadConfig();

            ConfigFlags flags = ConfigFlags.ResizableWindow | ConfigFlags.HighDpiWindow;
            Raylib.SetConfigFlags(flags);
            Raylib.InitWindow(1280, 800, "VRChat Unfriend Manager");

            EnableMinimizeToTray();

            Raylib.SetTargetFPS(60);

            if (config.HideInTaskbar)
            {
                StartTrayThread(isAutostart);
                ApplyTaskbarVisibility(true);
            }

            rlImGui.Setup(true);
            ApplyTheme();

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
                    if (config.AutoUnfriendEnabled) StartAutoScheduler();
                    if (config.AutoDeclineFriendRequests) StartAutoDeclineChecker();
                }
            });

            while (!shouldExit)
            {
                if (_showRequested)
                {
                    _showRequested = false;
                    Raylib.ClearWindowState(ConfigFlags.HiddenWindow);
                }

                if (!windowVisible)
                {
                    Raylib.PollInputEvents();
                    Thread.Sleep(50);
                    continue;
                }

                if (Raylib.WindowShouldClose())
                {
                    if (config.HideInTaskbar && _trayRunning)
                        HideMainWindow();
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
                ImGui.Begin("##main", ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize);

                if (sessionRestored || isLoggedIn) DrawMainUI();
                else DrawLoginScreen();

                api.Draw2FADialog();
                DrawAutoUnfriendConfirmDialog();
                ImGui.End();
                rlImGui.End();
                Raylib.EndDrawing();
            }

            wndProcHandle?.Free();
            autoCts?.Cancel();
            autoDeclineCts?.Cancel();
            unfriendCts?.Cancel();

            SaveConfig();
            TextureCache.UnloadAll();
            rlImGui.Shutdown();
            Raylib.CloseWindow();

            StopTrayThread();
            Environment.Exit(0);
        }

        static void ApplyTheme()
        {
            var style = ImGui.GetStyle();
            style.WindowRounding = 6f;
            style.FrameRounding = 4f;
            style.ScrollbarRounding = 4f;
            style.GrabRounding = 4f;
            style.TabRounding = 4f;
            style.WindowPadding = new Vector2(12, 12);
            style.FramePadding = new Vector2(6, 4);
            style.ItemSpacing = new Vector2(8, 6);

            var colors = style.Colors;
            colors[(int)ImGuiCol.WindowBg] = new Vector4(0.10f, 0.10f, 0.14f, 1f);
            colors[(int)ImGuiCol.ChildBg] = new Vector4(0.08f, 0.08f, 0.12f, 1f);
            colors[(int)ImGuiCol.PopupBg] = new Vector4(0.12f, 0.12f, 0.16f, 1f);
            colors[(int)ImGuiCol.Border] = new Vector4(0.25f, 0.25f, 0.35f, 1f);
            colors[(int)ImGuiCol.FrameBg] = new Vector4(0.16f, 0.16f, 0.22f, 1f);
            colors[(int)ImGuiCol.FrameBgHovered] = new Vector4(0.22f, 0.22f, 0.30f, 1f);
            colors[(int)ImGuiCol.FrameBgActive] = new Vector4(0.28f, 0.28f, 0.38f, 1f);
            colors[(int)ImGuiCol.TitleBg] = new Vector4(0.08f, 0.08f, 0.12f, 1f);
            colors[(int)ImGuiCol.TitleBgActive] = new Vector4(0.12f, 0.12f, 0.18f, 1f);
            colors[(int)ImGuiCol.Tab] = new Vector4(0.14f, 0.14f, 0.20f, 1f);
            colors[(int)ImGuiCol.TabHovered] = new Vector4(0.35f, 0.25f, 0.55f, 1f);
            colors[(int)ImGuiCol.TabSelected] = new Vector4(0.45f, 0.30f, 0.70f, 1f);
            colors[(int)ImGuiCol.Header] = new Vector4(0.30f, 0.20f, 0.50f, 0.6f);
            colors[(int)ImGuiCol.HeaderHovered] = new Vector4(0.40f, 0.27f, 0.65f, 0.8f);
            colors[(int)ImGuiCol.HeaderActive] = new Vector4(0.50f, 0.35f, 0.75f, 1f);
            colors[(int)ImGuiCol.Button] = new Vector4(0.30f, 0.20f, 0.50f, 1f);
            colors[(int)ImGuiCol.ButtonHovered] = new Vector4(0.45f, 0.30f, 0.70f, 1f);
            colors[(int)ImGuiCol.ButtonActive] = new Vector4(0.55f, 0.40f, 0.80f, 1f);
            colors[(int)ImGuiCol.SliderGrab] = new Vector4(0.55f, 0.40f, 0.80f, 1f);
            colors[(int)ImGuiCol.SliderGrabActive] = new Vector4(0.70f, 0.55f, 0.90f, 1f);
            colors[(int)ImGuiCol.CheckMark] = new Vector4(0.70f, 0.55f, 0.90f, 1f);
            colors[(int)ImGuiCol.ScrollbarBg] = new Vector4(0.08f, 0.08f, 0.12f, 1f);
            colors[(int)ImGuiCol.ScrollbarGrab] = new Vector4(0.30f, 0.20f, 0.50f, 1f);
            colors[(int)ImGuiCol.ScrollbarGrabHovered] = new Vector4(0.45f, 0.30f, 0.65f, 1f);
            colors[(int)ImGuiCol.ScrollbarGrabActive] = new Vector4(0.55f, 0.40f, 0.75f, 1f);
            colors[(int)ImGuiCol.Separator] = new Vector4(0.25f, 0.25f, 0.35f, 1f);
            colors[(int)ImGuiCol.Text] = new Vector4(0.90f, 0.88f, 0.95f, 1f);
            colors[(int)ImGuiCol.TextDisabled] = new Vector4(0.50f, 0.48f, 0.55f, 1f);
        }

        static void DrawLoginScreen()
        {
            int sw = Raylib.GetScreenWidth();
            int sh = Raylib.GetScreenHeight();
            float formW = Math.Min(360f, sw * 0.85f);
            float formH = 310f;
            float ox = (sw - formW) * 0.5f;
            float oy = (sh - formH) * 0.5f;
            float pad = 16f;
            float fieldW = formW - pad * 2;

            ImGui.SetCursorPos(new Vector2(ox, oy));
            ImGui.BeginChild("##login_card", new Vector2(formW, formH), ImGuiChildFlags.Borders);

            ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.75f, 0.55f, 1f, 1f), "VRChat Unfriend Manager");
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            ImGui.Text(status);

            ImGui.Spacing();
            ImGui.InputText("Username", ref user, 100);
            ImGui.InputText("Password", ref pass, 100, ImGuiInputTextFlags.Password);
            ImGui.Checkbox("Remember me", ref remember);

            if (ImGui.Button("Login"))
            {
                _ = Task.Run(async () =>
                {
                    var (success, name, error) = await api.LoginWithCredentialsAsync(user, pass);
                    if (success && name != null)
                    {
                        loggedInAs = name;
                        isLoggedIn = true;
                        sessionRestored = true;
                        status = $"Logged in as {name}";
                        await Refresh();
                    }
                    else status = error ?? "Login failed";
                });
            }

            ImGui.EndChild();
        }

        static void DrawMainUI()
        {
            ImGui.TextColored(new Vector4(0.75f, 0.55f, 1f, 1f), "VRChat Unfriend Manager");
            if (isLoggedIn)
            {
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.4f, 0.9f, 0.5f, 1f), $"  •  {loggedInAs}");
                ImGui.SameLine();
                float logoutW = ImGui.CalcTextSize("Logout").X + 16;
                ImGui.SetCursorPosX(Raylib.GetScreenWidth() - logoutW - ImGui.GetStyle().WindowPadding.X);
                if (ImGui.Button("Logout"))
                {
                    File.Delete(Paths.CookieFile);
                    config.Cookie = "";
                    SaveConfig();
                    api = new VRChatApiService();
                    friends.Clear(); favorites.Clear(); selected.Clear();
                    incomingFriendRequests.Clear();
                    autoDeclineCts?.Cancel();
                    autoDeclineCts = null;
                    loggedInAs = ""; isLoggedIn = false; sessionRestored = false;
                    status = "Logged out";
                }
            }
            ImGui.Separator();

            if (ImGui.BeginTabBar("##tabs"))
            {
                if (ImGui.BeginTabItem("Friends"))
                {
                    DrawFriendsTab(Raylib.GetScreenWidth(), Raylib.GetScreenHeight());
                    ImGui.EndTabItem();
                }
                if (ImGui.BeginTabItem("Groups"))
                {
                    DrawGroupsTab(Raylib.GetScreenWidth(), Raylib.GetScreenHeight());
                    ImGui.EndTabItem();
                }
                if (ImGui.BeginTabItem("Friend Requests"))
                {
                    DrawFriendRequestsTab(Raylib.GetScreenWidth(), Raylib.GetScreenHeight());
                    ImGui.EndTabItem();
                }
                if (ImGui.BeginTabItem("Settings"))
                {
                    DrawSettingsTab();
                    ImGui.EndTabItem();
                }
                ImGui.EndTabBar();
            }
        }

        static readonly string[] togetherUnits = { "min", "hr", "days" };
        static readonly string[] searchFields = { "Name", "Group" };

        static void DrawFriendsTab(int sw, int sh)
        {
            ImGui.Spacing();

            if (ImGui.Checkbox("Hide Favorites", ref hideFavs))
            { config.ExcludeFavorites = hideFavs; SaveConfig(); }

            ImGui.SameLine(0, 20);
            if (ImGui.Checkbox("Inactive >=", ref inactiveOn))
            { config.InactiveEnabled = inactiveOn; SaveConfig(); }
            if (inactiveOn)
            {
                ImGui.SameLine();
                ImGui.SetNextItemWidth(70f);
                if (ImGui.InputInt("##iv", ref inactiveVal, 1, 0))
                { if (inactiveVal < 1) inactiveVal = 1; config.InactiveValue = inactiveVal; SaveConfig(); }
                ImGui.SameLine();
                ImGui.SetNextItemWidth(80f);
                if (ImGui.Combo("##iu", ref inactiveUnit, units, units.Length))
                { config.InactiveUnitIndex = inactiveUnit; SaveConfig(); }
                ImGui.SameLine();
                var inCutoff = inactiveUnit switch
                {
                    0 => DateTime.UtcNow.AddDays(-inactiveVal),
                    1 => DateTime.UtcNow.AddMonths(-inactiveVal),
                    _ => DateTime.UtcNow.AddYears(-inactiveVal)
                };
                int inMatch = friends.Count(f =>
                    (!hideFavs || !favorites.Contains(f.Id)) &&
                    (string.IsNullOrEmpty(f.LastLogin) || DateTime.Parse(f.LastLogin) < inCutoff));
                ImGui.TextColored(new Vector4(0.4f, 0.9f, 0.5f, 1f), $"({inMatch} match)");
            }

            if (ImGui.Checkbox("Together <", ref togetherOn))
            { config.TogetherFilterEnabled = togetherOn; SaveConfig(); }
            if (togetherOn)
            {
                ImGui.SameLine();
                ImGui.SetNextItemWidth(70f);
                if (ImGui.InputInt("##tv", ref togetherVal, 1, 0))
                { if (togetherVal < 0) togetherVal = 0; config.TogetherFilterValue = togetherVal; SaveConfig(); }
                ImGui.SameLine();
                ImGui.SetNextItemWidth(70f);
                if (ImGui.Combo("##tu", ref togetherUnit, togetherUnits, togetherUnits.Length))
                { config.TogetherFilterUnit = togetherUnit; SaveConfig(); }
                ImGui.SameLine();
                long tThreshMs = togetherUnit switch
                {
                    0 => togetherVal * 60_000L,
                    1 => togetherVal * 3_600_000L,
                    _ => togetherVal * 86_400_000L
                };
                int tMatch = friends.Count(f =>
                    (!hideFavs || !favorites.Contains(f.Id)) && f.TimeSpentMs < tThreshMs);
                ImGui.TextColored(new Vector4(0.4f, 0.9f, 0.5f, 1f), $"({tMatch} match)");
            }

            ImGui.Spacing();
            ImGui.SetNextItemWidth(100f);
            ImGui.Combo("##sf", ref searchField, searchFields, searchFields.Length);
            ImGui.SameLine();
            ImGui.SetNextItemWidth(sw * 0.45f);
            ImGui.InputText("Search##sq", ref searchText, 128);
            if (!string.IsNullOrEmpty(searchText))
            {
                ImGui.SameLine();
                if (ImGui.SmallButton("x##clr")) searchText = "";
            }

            if (favByGroup.Any(kv => kv.Value.Count > 0 || favGroupNames.ContainsKey(kv.Key)))
            {
                ImGui.Spacing();
                ImGui.TextDisabled("Exclude groups:");
                ImGui.SameLine();
                foreach (var tag in favByGroup.Keys.OrderBy(t => t))
                {
                    bool excl = config.ExcludedFavGroups.Contains(tag);
                    string lbl = favGroupNames.TryGetValue(tag, out var gn) ? gn : tag;
                    int cnt = favByGroup[tag].Count;
                    if (ImGui.Checkbox($"##{tag}_excl", ref excl))
                    {
                        if (excl) { if (!config.ExcludedFavGroups.Contains(tag)) config.ExcludedFavGroups.Add(tag); }
                        else config.ExcludedFavGroups.Remove(tag);
                        SaveConfig();
                    }
                    ImGui.SameLine();
                    ImGui.Text($"{lbl} ({cnt})");
                    ImGui.SameLine(0, 14);
                }
                ImGui.NewLine();
            }

            ImGui.Spacing();
            ImGui.SetNextItemWidth(180f);
            if (ImGui.Combo("Sort", ref sort, sorts, sorts.Length))
            { config.SortOptionIndex = sort; SaveConfig(); }

            ImGui.Separator();
            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.7f, 1f), status);

            var excludedIds = new HashSet<string>();
            foreach (var tag in config.ExcludedFavGroups)
                if (favByGroup.TryGetValue(tag, out var eids))
                    foreach (var id in eids) excludedIds.Add(id);

            shown.Clear();
            var temp = friends.ToList();
            if (hideFavs) temp = temp.Where(f => !favorites.Contains(f.Id)).ToList();
            if (excludedIds.Count > 0) temp = temp.Where(f => !excludedIds.Contains(f.Id)).ToList();
            if (inactiveOn && inactiveVal > 0)
            {
                var cutoff = inactiveUnit switch
                {
                    0 => DateTime.UtcNow.AddDays(-inactiveVal),
                    1 => DateTime.UtcNow.AddMonths(-inactiveVal),
                    _ => DateTime.UtcNow.AddYears(-inactiveVal)
                };
                temp = temp.Where(f => string.IsNullOrEmpty(f.LastLogin) || DateTime.Parse(f.LastLogin) < cutoff).ToList();
            }
            if (togetherOn && togetherVal >= 0)
            {
                long thMs = togetherUnit switch
                {
                    0 => togetherVal * 60_000L,
                    1 => togetherVal * 3_600_000L,
                    _ => togetherVal * 86_400_000L
                };
                temp = temp.Where(f => f.TimeSpentMs < thMs).ToList();
            }
            if (!string.IsNullOrWhiteSpace(searchText))
            {
                var q = searchText.Trim().ToLowerInvariant();
                if (searchField == 1)
                {
                    temp = temp.Where(f =>
                    {
                        foreach (var (tag, ids) in favByGroup.OrderBy(kv => kv.Key))
                            if (ids.Contains(f.Id))
                            {
                                var gn2 = favGroupNames.TryGetValue(tag, out var g) ? g : tag;
                                return gn2.ToLowerInvariant().Contains(q);
                            }
                        return false;
                    }).ToList();
                }
                else temp = temp.Where(f => f.DisplayName.ToLowerInvariant().Contains(q)).ToList();
            }

            temp = sort switch
            {
                0 => temp.OrderBy(f => string.IsNullOrEmpty(f.LastLogin) ? DateTime.MinValue : DateTime.Parse(f.LastLogin)).ToList(),
                1 => temp.OrderByDescending(f => string.IsNullOrEmpty(f.LastLogin) ? DateTime.MinValue : DateTime.Parse(f.LastLogin)).ToList(),
                2 => temp.OrderBy(f => f.DisplayName).ToList(),
                3 => temp.OrderByDescending(f => f.DisplayName).ToList(),
                4 => temp.OrderByDescending(f => f.TimeSpentMs).ToList(),
                5 => temp.OrderBy(f => f.TimeSpentMs).ToList(),
                _ => temp.OrderBy(f => f.DisplayName).ToList()
            };
            shown = temp;

            float bottomBarH = isUnfriending ? 90f : 50f;
            float listH = sh - ImGui.GetCursorPosY() - bottomBarH - ImGui.GetStyle().WindowPadding.Y * 2 - 60;
            if (listH < 80) listH = 80;

            if (ImGui.BeginChild("##list", new Vector2(-1, listH), ImGuiChildFlags.Borders))
            {
                ImGui.TextDisabled($"{"  ",-5}{"Name",-32} {"Last seen",8}  {"Together",9}  Group");
                ImGui.Separator();

                const float IMG_SIZE = 32f;
                const float ROW_H = IMG_SIZE + 4f;

                for (int i = 0; i < shown.Count; i++)
                {
                    var f = shown[i];
                    var ago = string.IsNullOrEmpty(f.LastLogin) ? "never" : Ago(DateTime.Parse(f.LastLogin));
                    var together = FormatTimeSpent(f.TimeSpentMs);
                    bool sel = selected.Contains(i);

                    string groupLabel = "";
                    foreach (var (tag, ids) in favByGroup.OrderBy(kv => kv.Key))
                        if (ids.Contains(f.Id))
                        { groupLabel = favGroupNames.TryGetValue(tag, out var gn3) ? gn3 : tag; break; }

                    ImGui.PushID(i);
                    var rowStart = ImGui.GetCursorScreenPos();

                    if (ImGui.Selectable($"##s{i}", sel, ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowOverlap, new Vector2(0, ROW_H)))
                    {
                        if (Raylib.IsKeyDown(KeyboardKey.LeftControl))
                            _ = sel ? selected.Remove(i) : selected.Add(i);
                        else
                        {
                            selected.Clear();
                            selected.Add(i);
                        }
                    }

                    ImGui.SetCursorScreenPos(rowStart);
                    var tex = TextureCache.RequestTexture(f.ThumbnailUrl);
                    if (tex.HasValue && tex.Value.Id != 0)
                        ImGui.Image((nint)tex.Value.Id, new Vector2(IMG_SIZE, IMG_SIZE));
                    else
                    {
                        var dl = ImGui.GetWindowDrawList();
                        dl.AddRectFilled(rowStart, rowStart + new Vector2(IMG_SIZE, IMG_SIZE), ImGui.GetColorU32(new Vector4(0.2f, 0.2f, 0.3f, 1f)));
                        dl.AddText(rowStart + new Vector2(8, 8), ImGui.GetColorU32(new Vector4(0.5f, 0.5f, 0.6f, 1f)), "?");
                    }

                    ImGui.SameLine();
                    float textY = rowStart.Y + (ROW_H - ImGui.GetTextLineHeight()) * 0.5f;
                    ImGui.SetCursorScreenPos(new Vector2(ImGui.GetCursorScreenPos().X, textY));
                    ImGui.Text($"{f.DisplayName,-30} {ago,8}  {together,9}  {groupLabel}");

                    ImGui.PopID();
                }
                ImGui.EndChild();
            }

            ImGui.Spacing();
            if (ImGui.Button("Mark All")) { for (int i = 0; i < shown.Count; i++) selected.Add(i); }
            ImGui.SameLine();
            if (ImGui.Button("Unmark All")) selected.Clear();
            ImGui.SameLine();
            if (ImGui.Button("Refresh")) _ = Refresh();
            ImGui.SameLine();
            if (ImGui.Button("Backup JSON"))
                File.WriteAllText($"backup_{DateTime.Now:yyyyMMdd_HHmmss}.json", JsonSerializer.Serialize(shown, new JsonSerializerOptions { WriteIndented = true }));

            ImGui.SameLine();
            string btnLabel = isUnfriending ? (isPaused ? "Resume" : "Pause") : $"Unfriend ({selected.Count})";
            bool canUnfriend = selected.Count > 0 || isUnfriending;
            if (!canUnfriend) ImGui.BeginDisabled();
            if (ImGui.Button(btnLabel))
            {
                if (isUnfriending) isPaused = !isPaused;
                else if (selected.Count > 0) ImGui.OpenPopup("##confirm_unfriend");
            }
            if (!canUnfriend) ImGui.EndDisabled();

            if (ImGui.BeginPopupModal("##confirm_unfriend", ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGui.Text($"Permanently unfriend {selected.Count} user(s)?");
                ImGui.Spacing();
                if (ImGui.Button("Yes, do it", new Vector2(120, 0)))
                {
                    _ = Task.Run(StartUnfriendProcess);
                    ImGui.CloseCurrentPopup();
                }
                ImGui.SameLine();
                if (ImGui.Button("Cancel", new Vector2(80, 0))) ImGui.CloseCurrentPopup();
                ImGui.EndPopup();
            }
        }

        static void DrawGroupsTab(int sw, int sh)
        {
            ImGui.Spacing();
            ImGui.TextWrapped("These are your VRChat native favorite groups. Membership is managed inside VRChat. Use the toggles to exclude a group from the Friends list.");
            ImGui.Spacing();

            if (ImGui.Button("Refresh Groups")) _ = Refresh();
            ImGui.SameLine();
            ImGui.TextDisabled($"  {favByGroup.Count} group(s) detected, {favGroupNames.Count} named");
            ImGui.Separator();
            ImGui.Spacing();

            if (favByGroup.Count == 0)
            {
                ImGui.TextColored(new Vector4(1f, 0.6f, 0.3f, 1f), "No favorite groups found.");
                return;
            }

            float colW = Math.Max((sw - 50f) / Math.Max(favByGroup.Count, 1), 180f);

            foreach (var tag in favByGroup.Keys.OrderBy(t => t))
            {
                var ids = favByGroup[tag];
                string displayName = favGroupNames.TryGetValue(tag, out var n) ? n : tag;
                bool excluded = config.ExcludedFavGroups.Contains(tag);

                ImGui.BeginGroup();

                ImGui.TextColored(new Vector4(0.75f, 0.55f, 1f, 1f), displayName);
                ImGui.SameLine();
                ImGui.TextDisabled($"[{tag}] ({ids.Count})");
                ImGui.SameLine();
                if (ImGui.Checkbox($"Exclude##{tag}", ref excluded))
                {
                    if (excluded) { if (!config.ExcludedFavGroups.Contains(tag)) config.ExcludedFavGroups.Add(tag); }
                    else config.ExcludedFavGroups.Remove(tag);
                    SaveConfig();
                }

                float cardH = Math.Min(ids.Count * (ImGui.GetTextLineHeightWithSpacing() + 6) + 12, sh * 0.5f);
                if (ImGui.BeginChild($"##grp_{tag}", new Vector2(colW, cardH), ImGuiChildFlags.Borders))
                {
                    foreach (var id in ids)
                    {
                        var f = friends.FirstOrDefault(x => x.Id == id);
                        if (f != null)
                        {
                            var tex = TextureCache.RequestTexture(f.ThumbnailUrl);
                            if (tex.HasValue && tex.Value.Id != 0)
                                ImGui.Image((nint)tex.Value.Id, new Vector2(24, 24));
                            else
                                ImGui.Dummy(new Vector2(24, 24));
                            ImGui.SameLine();
                            ImGui.Text(f.DisplayName);
                            ImGui.SameLine();
                            ImGui.TextDisabled($"  {FormatTimeSpent(f.TimeSpentMs)}");
                        }
                        else
                        {
                            ImGui.TextDisabled(id);
                        }
                    }
                    ImGui.EndChild();
                }

                ImGui.EndGroup();
                ImGui.SameLine(0, 12);
            }
            ImGui.NewLine();
        }

        static void DrawFriendRequestsTab(int sw, int sh)
        {
            ImGui.Spacing();

            ImGui.Text("Auto-Decline Incoming Friend Requests");
            ImGui.Separator();

            if (ImGui.Checkbox("Enable Auto-Decline", ref autoDecline))
            {
                config.AutoDeclineFriendRequests = autoDecline;
                SaveConfig();
                if (autoDecline) StartAutoDeclineChecker();
                else { autoDeclineCts?.Cancel(); autoDeclineCts = null; }
            }
            ImGui.SameLine();
            ImGui.TextDisabled("(checks every 60s)");

            if (autoDecline)
            {
                ImGui.Indent();
                if (ImGui.Checkbox("Only decline requests from strangers", ref onlyStrangers))
                {
                    config.AutoDeclineOnlyFromStrangers = onlyStrangers;
                    SaveConfig();
                }
                if (ImGui.Checkbox("Automatically send friend request back", ref autoSendBack))
                {
                    config.AutoSendRequestBack = autoSendBack;
                    SaveConfig();
                }
                ImGui.Unindent();
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            if (ImGui.Button("Refresh Requests"))
                _ = RefreshFriendRequests();

            ImGui.SameLine();
            ImGui.TextDisabled($"Pending: {incomingFriendRequests.Count}");

            ImGui.Spacing();

            float listH = sh - ImGui.GetCursorPosY() - 40;
            if (ImGui.BeginChild("##reqlist", new Vector2(-1, listH), ImGuiChildFlags.Borders))
            {
                if (incomingFriendRequests.Count == 0)
                {
                    ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), "No incoming friend requests.");
                }
                else
                {
                    ImGui.TextDisabled($"{"Sender",-32} {"Date",-12} Actions");
                    ImGui.Separator();

                    foreach (var req in incomingFriendRequests.ToList())
                    {
                        string senderName = req.SenderUsername ?? req.SenderUserId ?? "Unknown";
                        string date = req.CreatedAt.ToString("yyyy-MM-dd");

                        ImGui.Text($"{senderName,-32} {date,-12}");
                        ImGui.SameLine();

                        ImGui.PushID(req.Id);
                        if (ImGui.SmallButton("Decline"))
                        {
                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    await api.DeclineFriendRequestAsync(req.Id);
                                    await RefreshFriendRequests();
                                }
                                catch { }
                            });
                        }
                        ImGui.SameLine();
                        if (ImGui.SmallButton("Send Request Back"))
                        {
                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    await api.SendFriendRequestAsync(req.SenderUserId ?? "");
                                    await api.DeclineFriendRequestAsync(req.Id);
                                    await RefreshFriendRequests();
                                }
                                catch { }
                            });
                        }
                        ImGui.PopID();
                    }
                }
                ImGui.EndChild();
            }
        }

        static void DrawAutoUnfriendConfirmDialog()
        {
            if (!pendingAutoConfirm) return;

            if (DateTime.Now >= autoConfirmDeadline)
            {
                pendingAutoConfirm = false;
                lock (autoConfirmLock)
                {
                    var tcs = autoConfirmTcs;
                    autoConfirmTcs = null;
                    tcs?.TrySetResult(true);
                }
                return;
            }

            ImGui.OpenPopup("##auto_confirm");
            ImGui.SetNextWindowPos(new Vector2(Raylib.GetScreenWidth() / 2f, Raylib.GetScreenHeight() / 2f), ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
            ImGui.SetNextWindowSize(new Vector2(380, 0), ImGuiCond.Appearing);

            bool open = true;
            if (ImGui.BeginPopupModal("##auto_confirm", ref open, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoTitleBar))
            {
                ImGui.TextColored(new Vector4(1f, 0.4f, 0.4f, 1f), "⚠  Auto-Unfriend Scheduled Run");
                ImGui.Separator();
                ImGui.Spacing();
                ImGui.TextWrapped($"The scheduler is about to unfriend {pendingAutoCount} friend{(pendingAutoCount == 1 ? "" : "s")}.");
                ImGui.Spacing();
                string modeName = config.AutoUnfriendMode switch { 0 => "Inactive Only", 1 => "All Shown", 2 => "Marked Only", _ => "Unknown" };
                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), $"Mode: {modeName}");
                ImGui.Spacing();

                int secsLeft = Math.Max(0, (int)Math.Ceiling((autoConfirmDeadline - DateTime.Now).TotalSeconds));
                float fraction = 1f - (secsLeft / 15f);
                var barCol = secsLeft > 8 ? new Vector4(0.3f, 0.8f, 0.3f, 1f) : secsLeft > 4 ? new Vector4(0.9f, 0.7f, 0.1f, 1f) : new Vector4(1f, 0.3f, 0.2f, 1f);

                ImGui.TextColored(barCol, $"Starting automatically in {secsLeft}s...");
                ImGui.PushStyleColor(ImGuiCol.PlotHistogram, barCol);
                ImGui.ProgressBar(fraction, new Vector2(-1, 6), "");
                ImGui.PopStyleColor();

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                float btnW = 110;
                ImGui.SetCursorPosX((ImGui.GetContentRegionAvail().X - btnW * 2 - 10) / 2f + ImGui.GetCursorPosX());
                if (ImGui.Button("Yes, unfriend", new Vector2(btnW, 0)))
                {
                    pendingAutoConfirm = false;
                    lock (autoConfirmLock)
                    {
                        var tcs = autoConfirmTcs;
                        autoConfirmTcs = null;
                        tcs?.TrySetResult(true);
                    }
                    ImGui.CloseCurrentPopup();
                }
                ImGui.SameLine();
                if (ImGui.Button("Cancel", new Vector2(btnW, 0)))
                {
                    pendingAutoConfirm = false;
                    lock (autoConfirmLock)
                    {
                        var tcs = autoConfirmTcs;
                        autoConfirmTcs = null;
                        tcs?.TrySetResult(false);
                    }
                    ImGui.CloseCurrentPopup();
                }
                ImGui.EndPopup();
            }
            else if (!open)
            {
                pendingAutoConfirm = false;
                lock (autoConfirmLock)
                {
                    var tcs = autoConfirmTcs;
                    autoConfirmTcs = null;
                    tcs?.TrySetResult(false);
                }
            }
        }

        static void DrawSettingsTab()
        {
            ImGui.Spacing();
            ImGui.Text("Startup Options");
            ImGui.Separator();

            bool runOnStartup = config.RunOnStartup;
            if (ImGui.Checkbox("Run on system startup", ref runOnStartup))
            {
                config.RunOnStartup = runOnStartup;
                SaveConfig();
                UpdateStartup(runOnStartup);
            }

            bool hideInTaskbar = config.HideInTaskbar;
            if (ImGui.Checkbox("Enable System Tray (Hide from taskbar)", ref hideInTaskbar))
            {
                config.HideInTaskbar = hideInTaskbar;
                SaveConfig();

                if (hideInTaskbar)
                {
                    StartTrayThread(false);
                    ApplyTaskbarVisibility(true);
                }
                else
                {
                    StopTrayThread();
                    ApplyTaskbarVisibility(false);
                    if (!windowVisible) ShowMainWindow();
                }
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Text("Updates");
            ImGui.Separator();

            if (ImGui.Button("Check for Updates"))
                _ = Task.Run(CheckForUpdatesAsync);

            if (checkingForUpdate)
                ImGui.Text("Checking for updates...");
            else if (updateAvailable)
            {
                ImGui.TextColored(new Vector4(0.3f, 1f, 0.3f, 1f), $"Update available: {latestVersion}");
                if (ImGui.Button("Download & Install Update"))
                    _ = Task.Run(DownloadAndInstallUpdateAsync);
            }

            if (downloading)
                ImGui.ProgressBar(downloadProgress, new Vector2(-1, 20), $"Downloading... {(int)(downloadProgress * 100)}%");

            ImGui.Spacing();
            ImGui.Text("Auto-Unfriend Scheduler");
            ImGui.Separator();

            bool autoEnabled = config.AutoUnfriendEnabled;
            if (ImGui.Checkbox("Enable Auto-Unfriend", ref autoEnabled))
            {
                config.AutoUnfriendEnabled = autoEnabled;
                SaveConfig();
                if (autoEnabled) StartAutoScheduler();
                else { autoCts?.Cancel(); autoCts = null; }
            }
        }

        static async Task CheckForUpdatesAsync()
        {
            checkingForUpdate = true;
            updateAvailable = false;
            latestVersion = "";
            downloadUrl = "";

            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Unfriendmaxxing-Updater");

                var response = await client.GetAsync("https://api.github.com/repos/hollyntt/VRChat-Unfriend-Manager/releases/latest");
                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                latestVersion = root.GetProperty("tag_name").GetString() ?? "";

                var assets = root.GetProperty("assets");
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.GetProperty("name").GetString() ?? "";
                    if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        downloadUrl = asset.GetProperty("browser_download_url").GetString() ?? "";
                        updateAvailable = true;
                        break;
                    }
                }
            }
            catch { }
            finally { checkingForUpdate = false; }
        }

        static async Task DownloadAndInstallUpdateAsync()
        {
            if (string.IsNullOrEmpty(downloadUrl)) return;

            downloading = true;
            downloadProgress = 0f;

            try
            {
                using var client = new HttpClient();
                var tempPath = Path.Combine(Path.GetTempPath(), "Unfriendmaxxing_new.exe");

                using var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? 0;
                using var contentStream = await response.Content.ReadAsStreamAsync();
                using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                var buffer = new byte[8192];
                long totalRead = 0;
                int read;

                while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, read);
                    totalRead += read;
                    if (totalBytes > 0)
                        downloadProgress = (float)totalRead / totalBytes;
                }

                var currentExe = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                if (!string.IsNullOrEmpty(currentExe))
                {
                    var backup = currentExe + ".bak";
                    if (File.Exists(backup)) File.Delete(backup);
                    File.Move(currentExe, backup);
                    File.Move(tempPath, currentExe);

                    MessageBox.Show("Update installed successfully!\nThe program will now restart.", "Update Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    Application.Restart();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Update failed: {ex.Message}", "Update Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                downloading = false;
                downloadProgress = 0f;
            }
        }

        static async Task Refresh()
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

                if (VrcxDataService.IsAvailable)
                {
                    status = "Loading VRCX time data...";
                    var timeMap = await Task.Run(() => VrcxDataService.LoadTimeSpentSeconds());
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

        static async Task RefreshFriendRequests()
        {
            try
            {
                incomingFriendRequests = await api.GetIncomingFriendRequestsAsync();
            }
            catch { }
        }

        static void StartAutoDeclineChecker()
        {
            autoDeclineCts?.Cancel();
            autoDeclineCts = new CancellationTokenSource();
            var token = autoDeclineCts.Token;

            _ = Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    if (config.AutoDeclineFriendRequests && isLoggedIn)
                    {
                        try
                        {
                            incomingFriendRequests = await api.GetIncomingFriendRequestsAsync();
                        }
                        catch { }
                    }
                    await Task.Delay(60000, token);
                }
            }, token);
        }

        static string Ago(DateTime dt)
        {
            var span = DateTime.UtcNow - dt.ToUniversalTime();
            if (span.TotalDays < 1) return "today";
            if (span.TotalDays < 30) return $"{(int)span.TotalDays}d";
            if (span.TotalDays < 365) return $"{(int)(span.TotalDays / 30.4)}mo";
            return $"{(int)(span.TotalDays / 365.25)}y";
        }

        static string FormatTimeSpent(long ms)
        {
            if (ms <= 0) return "-";
            var ts = TimeSpan.FromMilliseconds(ms);
            if (ts.TotalDays >= 1) return $"{(int)ts.TotalDays}d {ts.Hours}h";
            if (ts.TotalHours >= 1) return $"{(int)ts.TotalHours}h {ts.Minutes}m";
            return $"{ts.Minutes}m";
        }

        static void ShowUnfriendToast(string displayName) => ShowToast("Unfriended", $"{displayName} has been removed.");

        static void ShowToast(string title, string msg)
        {
            Console.WriteLine($"[Toast] {title}: {msg}");
        }

        static void InstallLinuxDesktopEntry() { }
        static void UpdateStartup(bool enable) { }
        static void UpdateVrcxShortcut(string subfolder, bool enable) { }

        static void LoadConfig()
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

        public static void SaveConfig()
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
                File.WriteAllText(Paths.ConfigFile, JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        static void StartAutoScheduler()
        {
            autoCts?.Cancel();
            autoCts = new CancellationTokenSource();
            var token = autoCts.Token;

            _ = Task.Run(async () =>
            {
                while (!token.IsCancellationRequested && config.AutoUnfriendEnabled)
                {
                    var target = GetNextScheduledRun();
                    if (!target.HasValue || target.Value <= DateTime.Now) break;

                    try { await Task.Delay(target.Value - DateTime.Now, token); }
                    catch (OperationCanceledException) { break; }

                    if (token.IsCancellationRequested) break;

                    await RunAutoUnfriendAsync(token);
                }
            }, token);
        }

        static async Task RunAutoUnfriendAsync(CancellationToken token)
        {
            try
            {
                await Refresh();

                List<SafeLimitedUserFriend> toUnfriend = config.AutoUnfriendMode switch
                {
                    0 => shown.Where(f => string.IsNullOrEmpty(f.LastLogin) || DateTime.Parse(f.LastLogin) < DateTime.UtcNow.AddMonths(-3)).ToList(),
                    1 => shown.ToList(),
                    2 => selected.Count > 0 ? selected.Where(i => i < shown.Count).Select(i => shown[i]).ToList() : shown.ToList(),
                    _ => new List<SafeLimitedUserFriend>()
                };

                if (toUnfriend.Count == 0) return;

                autoConfirmTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                pendingAutoCount = toUnfriend.Count;
                autoConfirmDeadline = DateTime.Now.AddSeconds(15);
                pendingAutoConfirm = true;

                var confirmed = await autoConfirmTcs.Task;

                if (!confirmed) return;

                foreach (var u in toUnfriend)
                {
                    if (token.IsCancellationRequested) break;
                    try
                    {
                        await api.UnfriendAsync(u.Id);
                        await Task.Delay(Random.Shared.Next(7000, 13000), token);
                    }
                    catch { }
                }

                await Refresh();
            }
            catch { }
        }

        static async Task StartUnfriendProcess()
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
                    }
                    catch { }

                    if (i < list.Count - 1)
                        await Task.Delay(Random.Shared.Next(7000, 13000), unfriendCts.Token);
                }
            }
            finally
            {
                isUnfriending = false; isPaused = false;
                status = unfriendDone == unfriendTotal ? "All done!" : "Cancelled";
                selected.Clear();
                await Refresh();
            }
        }

        static DateTime? GetNextScheduledRun()
        {
            var now = DateTime.Now;
            int h = config.AutoUnfriendHour, mi = config.AutoUnfriendMinute;
            try
            {
                switch (config.AutoUnfriendScheduleType)
                {
                    case 0:
                        var daily = new DateTime(now.Year, now.Month, now.Day, h, mi, 0);
                        if (daily <= now) daily = daily.AddDays(1);
                        return daily;
                    default: return null;
                }
            }
            catch { return null; }
        }
    }
}