using System.Net;
using System.Numerics;
using System.Text;
using System.Text.Json;
using ImGuiNET;
using Raylib_cs;
using VRChat.API.Api;
using VRChat.API.Client;
using VRChat.API.Model;
using VRCUFM.AppSystem;
using VRCUFM.Core;
using VRCUFM.Filesystem;
using File = System.IO.File;

namespace VRCUFM.VRChat;

public partial class APIService
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

    public APIService()
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
                Console.WriteLine("[AUTH] Logged in via VRCX cookie");
                return (true, name ?? "VRCX User");
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
                return (true, name ?? "Unknown");
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
                    return (true, name ?? "Unknown");
                }
            }
        }

        if (!string.IsNullOrEmpty(Program.config.Username) && !string.IsNullOrEmpty(Program.config.EncodedPassword))
        {
            var p = Encoding.UTF8.GetString(Convert.FromBase64String(Program.config.EncodedPassword));
            var (success, name, error) = await LoginWithCredentialsAsync(Program.config.Username, p);
            if (success) return (true, name ?? "Unknown");
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
                if (!reResp.IsSuccessStatusCode)
                {
                    client.DefaultRequestHeaders.Authorization = null;
                    return (false, null, $"Post-2FA login failed: {reResp.StatusCode}");
                }
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
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int offset = 0; ; offset += 100)
        {
            var page = await Friends.GetFriendsAsync(offset: offset, n: 100, offline: false);
            foreach (var u in page)
            {
                if (seen.Add(u.Id)) list.Add(MapFriend(u));
            }
            if (page.Count < 100) break;
        }
        for (int offset = 0; ; offset += 100)
        {
            var page = await Friends.GetFriendsAsync(offset: offset, n: 100, offline: true);
            foreach (var u in page)
            {
                if (seen.Add(u.Id)) list.Add(MapFriend(u));
            }
            if (page.Count < 100) break;
        }
        return list;
    }

    private static SafeLimitedUserFriend MapFriend(LimitedUserFriend u) => new()
    {
        Id = u.Id,
        DisplayName = u.DisplayName ?? "Unknown",
        LastLogin = u.LastLogin?.ToString("o") ?? "",
        ThumbnailUrl = u.CurrentAvatarThumbnailImageUrl ?? u.CurrentAvatarImageUrl ?? u.ProfilePicOverrideThumbnail ?? u.ProfilePicOverride ?? "",
        Bio = u.Bio ?? "",
    };
    public async Task<(HashSet<string> allIds, Dictionary<string, HashSet<string>> byGroup)> GetFavoritesDetailedAsync()
    {
        var allIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var byGroup = new Dictionary<string, HashSet<string>>();

        for (int offset = 0; ; offset += 100)
        {
            var page = await Favorites.GetFavoritesAsync(type: "friend", n: 100, offset: offset);
            foreach (var f in page)
            {
                allIds.Add(f.FavoriteId);
                var tag = f.Tags?.FirstOrDefault() ?? "group_0";
                if (!byGroup.ContainsKey(tag)) byGroup[tag] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                byGroup[tag].Add(f.FavoriteId);
            }
            if (page.Count < 100) break;
        }
        return (allIds, byGroup);
    }

    public async Task<Dictionary<string, string>> GetFavoriteGroupNamesAsync()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

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
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await FetchFriendRequestBatchAsync(result, seen, hidden: false);
        await FetchFriendRequestBatchAsync(result, seen, hidden: true);

        Console.WriteLine($"[FriendRequests] Found {result.Count} incoming request(s) " +
                          $"({FriendRequestEnricher.HiddenRequestIds.Count} hidden)");
        return result;
    }

    private async Task FetchFriendRequestBatchAsync(
        List<Notification> result,
        HashSet<string> seen,
        bool hidden)
    {
        var http = GetAuthClient();

        for (int offset = 0; ; offset += 100)
        {
            var url = $"https://api.vrchat.cloud/api/1/auth/user/notifications" +
                      $"?type=friendRequest&n=100&offset={offset}" +
                      (hidden ? "&hidden=true" : "");

            HttpResponseMessage resp;
            string body;
            try
            {
                resp = await http.GetAsync(url);
                body = await resp.Content.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FriendRequests] HTTP error (hidden={hidden}) offset {offset}: {ex.Message}");
                break;
            }

            if (!resp.IsSuccessStatusCode)
            {
                Console.WriteLine($"[FriendRequests] Non-success {resp.StatusCode} (hidden={hidden}) offset {offset}: {body}");
                break;
            }

            List<Notification>? page = null;
            try
            {
                using var doc = JsonDocument.Parse(body);
                page = new List<Notification>();
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    var senderUserId = el.TryGetProperty("senderUserId", out var suidP)
                        ? suidP.GetString() : null;
                    if (string.IsNullOrEmpty(senderUserId) || senderUserId == CurrentUserId)
                        continue;

                    var id = el.TryGetProperty("id", out var idP)
                        ? idP.GetString() ?? "" : "";

                    var n = new Notification(
                        id: id,
                        senderUserId: senderUserId,
                        senderUsername: el.TryGetProperty("senderUsername", out var sunP)
                            ? sunP.GetString() : null,
                        type: NotificationType.FriendRequest,
                        message: el.TryGetProperty("message", out var msgP)
                            ? msgP.GetString() ?? "" : "",
                        details: "",
                        seen: false,
                        createdAt: el.TryGetProperty("createdAt", out var caP)
                            && DateTime.TryParse(caP.GetString(), out var dt) ? dt : DateTime.UtcNow
                    );

                    if (hidden)
                        FriendRequestEnricher.HiddenRequestIds.Add(id);
                    else if (el.TryGetProperty("hidden", out var hidP) && hidP.GetBoolean())
                        FriendRequestEnricher.HiddenRequestIds.Add(id);

                    if (seen.Add(id))
                        page.Add(n);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FriendRequests] Parse error (hidden={hidden}) offset {offset}: {ex.Message}");
                break;
            }

            result.AddRange(page);
            if (page.Count < 100) break;
        }
    }

    public async Task DeclineFriendRequestAsync(string notificationId)
    {
        var http = GetAuthClient();
        var delResp = await http.DeleteAsync($"https://api.vrchat.cloud/api/1/auth/user/notifications/{notificationId}");
        if (!delResp.IsSuccessStatusCode)
        {
            var hideResp = await http.PutAsync(
                $"https://api.vrchat.cloud/api/1/auth/user/notifications/{notificationId}/hide",
                new StringContent("{}", Encoding.UTF8, "application/json"));
            if (!hideResp.IsSuccessStatusCode)
                Console.WriteLine($"[FriendRequests] Decline failed for {notificationId}: {delResp.StatusCode}");
        }
    }

    public async Task SendFriendRequestAsync(string userId)
    {
        await Friends.FriendAsync(userId);
    }

    /// <summary>
    /// Checks whether a user is "known" to the current user.
    /// A user is known if they are currently a friend, or if they appear in the
    /// time-spent databases with at least <paramref name="minTimeSeconds"/>.
    /// </summary>
    public async Task<bool> IsKnownPlayerAsync(string userId, HashSet<string> friendIds, Dictionary<string, long>? timeMap, long minTimeSeconds = 0)
    {
        if (friendIds.Contains(userId)) return true;
        if (timeMap != null && timeMap.TryGetValue(userId, out var secs) && secs >= minTimeSeconds) return true;
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
