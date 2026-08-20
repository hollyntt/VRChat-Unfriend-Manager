using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using ImGuiNET;
using Raylib_cs;
using VRCUFM.AppSystem;
using VRCUFM.Core;
using VRCUFM.Filesystem;
using VRCUFM.VRChat;
using File = System.IO.File;

namespace VRCUFM.UI;

public static class UIRenderer
{
    static readonly string[] togetherUnits = { "min", "hr", "days" };
    static readonly string[] searchFields = { "Name", "Group" };
    static readonly string[] sorts = { "Oldest", "Newest", "A-Z", "Z-A", "Most Time", "Least Time", "Lowest Trust", "Highest Trust" };
    static readonly string[] autoModes = { "Inactive Only (3+ mo)", "All Shown", "Marked Only" };

    static string _noteBuffer = "";
    static string _lastNoteUserId = "";

    public static void ApplyTheme()
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

    public static void DrawLoginScreen()
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
        var title = "VRChat Unfriend Manager";
        ImGui.SetCursorPosX((formW - ImGui.CalcTextSize(title).X) * 0.5f);
        ImGui.TextColored(new Vector4(0.75f, 0.55f, 1f, 1f), title);
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        bool isSigningIn = Program.status == "Signing in...";
        bool isErr = !isSigningIn && (Program.status.Contains("fail", StringComparison.OrdinalIgnoreCase) ||
                                      Program.status.Contains("wrong", StringComparison.OrdinalIgnoreCase) ||
                                      Program.status.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                                      Program.status.Contains("expired", StringComparison.OrdinalIgnoreCase) ||
                                      Program.status.Contains("cookie", StringComparison.OrdinalIgnoreCase));

        var statusColor = isSigningIn ? new Vector4(0.7f, 0.7f, 0.3f, 1f)
                        : isErr ? new Vector4(1f, 0.3f, 0.3f, 1f)
                        : new Vector4(0.5f, 0.5f, 0.6f, 1f);

        ImGui.SetCursorPosX(pad);
        if (isSigningIn)
        {
            int dots = (int)(ImGui.GetTime() * 2) % 4;
            ImGui.TextColored(statusColor, "Signing in" + new string('.', dots));
        }
        else ImGui.TextColored(statusColor, Program.status);

        ImGui.Spacing();

        if (string.IsNullOrEmpty(Program.user) && !string.IsNullOrEmpty(Program.config.Username))
            Program.user = Program.config.Username;

        ImGui.SetCursorPosX(pad);
        ImGui.TextDisabled("Username");
        ImGui.SetCursorPosX(pad);
        ImGui.SetNextItemWidth(fieldW);
        ImGui.InputText("##user", ref Program.user, 100);

        ImGui.Spacing();

        ImGui.SetCursorPosX(pad);
        ImGui.TextDisabled("Password");
        ImGui.SetCursorPosX(pad);
        ImGui.SetNextItemWidth(fieldW);
        ImGui.InputText("##pass", ref Program.pass, 100, ImGuiInputTextFlags.Password);

        ImGui.Spacing();

        ImGui.SetCursorPosX(pad);
        ImGui.Checkbox("Remember me", ref Program.remember);
        ImGui.Spacing();

        bool canLogin = !Program.working && !isSigningIn && !string.IsNullOrWhiteSpace(Program.user) && !string.IsNullOrWhiteSpace(Program.pass);

        ImGui.SetCursorPosX(pad);
        if (!canLogin) ImGui.BeginDisabled();
        if (ImGui.Button(Program.working || isSigningIn ? "Signing in..." : "Login", new Vector2(fieldW, 34)))
        {
            Program.working = true;
            Program.status = "Signing in...";
            _ = Task.Run(async () =>
            {
                var (success, name, error) = await Program.api.LoginWithCredentialsAsync(Program.user, Program.pass);
                if (success && name != null)
                {
                    Program.loggedInAs = name;
                    Program.isLoggedIn = true;
                    Program.sessionRestored = true;
                    if (Program.remember)
                    {
                        Program.config.Username = Program.user;
                        Program.config.EncodedPassword = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(Program.pass));
                        Program.config.RememberMe = true;
                        Program.SaveConfig();
                    }
                    await Program.Refresh();
                    if (Program.config.AutoDeclineFriendRequests) SchedulerService.StartAutoDeclineChecker();
                    Program.status = $"Logged in as {name}";
                }
                else Program.status = error ?? "Login failed";
                Program.working = false;
            });
        }
        if (!canLogin) ImGui.EndDisabled();

        ImGui.EndChild();
    }

    public static void DrawMainUI()
    {
        int sw = Raylib.GetScreenWidth();
        int sh = Raylib.GetScreenHeight();

        ImGui.TextColored(new Vector4(0.75f, 0.55f, 1f, 1f), "VRChat Unfriend Manager");
        if (Program.isLoggedIn)
        {
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.4f, 0.9f, 0.5f, 1f), $"  •  {Program.loggedInAs}");
            ImGui.SameLine();
            float logoutW = ImGui.CalcTextSize("Logout").X + 16;
            ImGui.SetCursorPosX(sw - logoutW - ImGui.GetStyle().WindowPadding.X);
            if (ImGui.Button("Logout"))
            {
                File.Delete(Paths.CookieFile);
                Program.config.Cookie = "";
                Program.SaveConfig();
                Program.api = new APIService();
                Program.friends.Clear(); Program.favorites.Clear(); Program.selected.Clear();
                Program.incomingFriendRequests.Clear();
                Program.autoDeclineCts?.Cancel();
                Program.autoDeclineCts = null;
                Program.loggedInAs = ""; Program.isLoggedIn = false; Program.sessionRestored = false;
                Program.user = ""; Program.pass = "";
                Program.status = "Logged out";
            }
        }
        ImGui.Separator();

        if (ImGui.BeginTabBar("##tabs"))
        {
            if (ImGui.BeginTabItem("Friends"))
            {
                DrawFriendsTab(sw, sh);
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Groups"))
            {
                DrawGroupsTab(sw, sh);
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Friend Requests"))
            {
                DrawFriendRequestsTab(sw, sh);
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

    static void DrawFriendsTab(int sw, int sh)
    {
        ImGui.Spacing();

        // ── Stats Panel ──────────────────────────────────────────────────────
        if (Program.config.ShowStatsPanel && Program.friends.Count > 0)
        {
            var stats = FriendsManager.CalculateStats(Program.friends, Program.favorites, Program.favByGroup);
            ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.08f, 0.08f, 0.14f, 1f));
            if (ImGui.BeginChild("##stats", new Vector2(-1, 70), ImGuiChildFlags.Borders))
            {
                ImGui.TextColored(new Vector4(0.75f, 0.55f, 1f, 1f), "Friend Stats");
                ImGui.SameLine();
                ImGui.TextDisabled($"  Total: {stats.TotalFriends}  |  Online: {stats.OnlineFriends}  |  Inactive: {stats.InactiveFriends}  |  Ghosts: {stats.GhostFriends}  |  Favorites: {stats.FavoritesCount}");
                ImGui.TextDisabled($"Avg time together: {Program.FormatTimeSpent((long)stats.AverageTimeTogetherMs)}  |  Total: {Program.FormatTimeSpent(stats.TotalTimeTogetherMs)}");
                ImGui.EndChild();
            }
            ImGui.PopStyleColor();
            ImGui.Spacing();
        }

        if (ImGui.Checkbox("Hide Favorites", ref Program.hideFavs))
        { Program.config.ExcludeFavorites = Program.hideFavs; Program.SaveConfig(); }

        ImGui.SameLine(0, 20);
        if (ImGui.Checkbox("Inactive >=", ref Program.inactiveOn))
        { Program.config.InactiveEnabled = Program.inactiveOn; Program.SaveConfig(); }
        if (Program.inactiveOn)
        {
            ImGui.SameLine();
            ImGui.SetNextItemWidth(70f);
            if (ImGui.InputInt("##iv", ref Program.inactiveVal, 1, 0))
            { if (Program.inactiveVal < 1) Program.inactiveVal = 1; Program.config.InactiveValue = Program.inactiveVal; Program.SaveConfig(); }
            ImGui.SameLine();
            ImGui.SetNextItemWidth(80f);
            if (ImGui.Combo("##iu", ref Program.inactiveUnit, Program.units, Program.units.Length))
            { Program.config.InactiveUnitIndex = Program.inactiveUnit; Program.SaveConfig(); }
            ImGui.SameLine();
            var inCutoff = Program.inactiveUnit switch
            {
                0 => DateTime.UtcNow.AddDays(-Program.inactiveVal),
                1 => DateTime.UtcNow.AddMonths(-Program.inactiveVal),
                _ => DateTime.UtcNow.AddYears(-Program.inactiveVal)
            };
            int inMatch = Program.friends.Count(f =>
                (!Program.hideFavs || !Program.favorites.Contains(f.Id)) &&
                (string.IsNullOrEmpty(f.LastLogin) || DateTime.Parse(f.LastLogin) < inCutoff));
            ImGui.TextColored(new Vector4(0.4f, 0.9f, 0.5f, 1f), $"({inMatch} match)");
        }

        if (ImGui.Checkbox("Together <", ref Program.togetherOn))
        { Program.config.TogetherFilterEnabled = Program.togetherOn; Program.SaveConfig(); }
        if (Program.togetherOn)
        {
            ImGui.SameLine();
            ImGui.SetNextItemWidth(70f);
            if (ImGui.InputInt("##tv", ref Program.togetherVal, 1, 0))
            { if (Program.togetherVal < 0) Program.togetherVal = 0; Program.config.TogetherFilterValue = Program.togetherVal; Program.SaveConfig(); }
            ImGui.SameLine();
            ImGui.SetNextItemWidth(70f);
            if (ImGui.Combo("##tu", ref Program.togetherUnit, togetherUnits, togetherUnits.Length))
            { Program.config.TogetherFilterUnit = Program.togetherUnit; Program.SaveConfig(); }
            ImGui.SameLine();
            long tThreshMs = Program.togetherUnit switch
            {
                0 => Program.togetherVal * 60_000L,
                1 => Program.togetherVal * 3_600_000L,
                _ => Program.togetherVal * 86_400_000L
            };
            int tMatch = Program.friends.Count(f =>
                (!Program.hideFavs || !Program.favorites.Contains(f.Id)) && f.TimeSpentMs < tThreshMs);
            ImGui.TextColored(new Vector4(0.4f, 0.9f, 0.5f, 1f), $"({tMatch} match)");
        }

        ImGui.Spacing();
        ImGui.SetNextItemWidth(100f);
        ImGui.Combo("##sf", ref Program.searchField, searchFields, searchFields.Length);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(sw * 0.45f);
        ImGui.InputText("Search##sq", ref Program.searchText, 128);
        if (!string.IsNullOrEmpty(Program.searchText))
        {
            ImGui.SameLine();
            if (ImGui.SmallButton("x##clr")) Program.searchText = "";
        }

        if (Program.favByGroup.Any(kv => kv.Value.Count > 0 || Program.favGroupNames.ContainsKey(kv.Key)))
        {
            ImGui.Spacing();
            ImGui.TextDisabled("Exclude groups:");
            ImGui.SameLine();
            foreach (var tag in Program.favByGroup.Keys.OrderBy(t => t))
            {
                bool excl = Program.config.ExcludedFavGroups.Contains(tag);
                string lbl = Program.favGroupNames.TryGetValue(tag, out var gn) ? gn : tag;
                int cnt = Program.favByGroup[tag].Count;
                if (ImGui.Checkbox($"##{tag}_excl", ref excl))
                {
                    if (excl) { if (!Program.config.ExcludedFavGroups.Contains(tag)) Program.config.ExcludedFavGroups.Add(tag); }
                    else Program.config.ExcludedFavGroups.Remove(tag);
                    Program.SaveConfig();
                }
                ImGui.SameLine();
                ImGui.Text($"{lbl} ({cnt})");
                ImGui.SameLine(0, 14);
            }
            ImGui.NewLine();
        }

        ImGui.Spacing();
        ImGui.SetNextItemWidth(180f);
        if (ImGui.Combo("Sort", ref Program.sort, sorts, sorts.Length))
        { Program.config.SortOptionIndex = Program.sort; Program.SaveConfig(); }

        ImGui.Separator();
        ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.7f, 1f), Program.status);
        if (TrustScoreService.IsEnriching)
        {
            ImGui.SameLine();
            int done = TrustScoreService.EnrichDone;
            int total = Math.Max(1, TrustScoreService.EnrichTotal);
            ImGui.TextColored(new Vector4(0.55f, 0.45f, 0.9f, 1f), $"  ·  Trust {done}/{total}");
            ImGui.ProgressBar(done / (float)total, new Vector2(-1, 4), "");
        }
        else if (Program.working && !Program.isUnfriending)
            ImGui.ProgressBar(-1f * (float)(ImGui.GetTime() % 1.0), new Vector2(-1, 6), "");

        var excludedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in Program.config.ExcludedFavGroups)
            if (Program.favByGroup.TryGetValue(tag, out var eids))
                foreach (var id in eids) excludedIds.Add(id);

        Program.shown.Clear();
        var temp = Program.friends.ToList();
        if (Program.hideFavs) temp = temp.Where(f => !Program.favorites.Contains(f.Id)).ToList();
        if (excludedIds.Count > 0) temp = temp.Where(f => !excludedIds.Contains(f.Id)).ToList();
        if (Program.inactiveOn && Program.inactiveVal > 0)
        {
            var cutoff = Program.inactiveUnit switch
            {
                0 => DateTime.UtcNow.AddDays(-Program.inactiveVal),
                1 => DateTime.UtcNow.AddMonths(-Program.inactiveVal),
                _ => DateTime.UtcNow.AddYears(-Program.inactiveVal)
            };
            temp = temp.Where(f => string.IsNullOrEmpty(f.LastLogin) || DateTime.Parse(f.LastLogin) < cutoff).ToList();
        }
        if (Program.togetherOn && Program.togetherVal >= 0)
        {
            long thMs = Program.togetherUnit switch
            {
                0 => Program.togetherVal * 60_000L,
                1 => Program.togetherVal * 3_600_000L,
                _ => Program.togetherVal * 86_400_000L
            };
            temp = temp.Where(f => f.TimeSpentMs < thMs).ToList();
        }
        if (!string.IsNullOrWhiteSpace(Program.searchText))
        {
            var q = Program.searchText.Trim().ToLowerInvariant();
            if (Program.searchField == 1)
            {
                temp = temp.Where(f =>
                {
                    foreach (var (tag, ids) in Program.favByGroup)
                        if (ids.Contains(f.Id))
                        {
                            var gn2 = Program.favGroupNames.TryGetValue(tag, out var g) ? g : tag;
                            if (gn2.ToLowerInvariant().Contains(q)) return true;
                        }
                    return false;
                }).ToList();
            }
            else temp = temp.Where(f => f.DisplayName.ToLowerInvariant().Contains(q)).ToList();
        }

        temp = Program.sort switch
        {
            0 => temp.OrderBy(f => string.IsNullOrEmpty(f.LastLogin) ? DateTime.MinValue : DateTime.Parse(f.LastLogin)).ToList(),
            1 => temp.OrderByDescending(f => string.IsNullOrEmpty(f.LastLogin) ? DateTime.MinValue : DateTime.Parse(f.LastLogin)).ToList(),
            2 => temp.OrderBy(f => f.DisplayName).ToList(),
            3 => temp.OrderByDescending(f => f.DisplayName).ToList(),
            4 => temp.OrderByDescending(f => f.TimeSpentMs).ToList(),
            5 => temp.OrderBy(f => f.TimeSpentMs).ToList(),
            6 => temp.OrderBy(f => TrustScoreService.Calculate(f)).ToList(),
            7 => temp.OrderByDescending(f => TrustScoreService.Calculate(f)).ToList(),
            _ => temp.OrderBy(f => f.DisplayName).ToList()
        };
        Program.shown = temp;

        float bottomBarH = Program.isUnfriending ? 90f : 50f;
        float listH = sh - ImGui.GetCursorPosY() - bottomBarH - ImGui.GetStyle().WindowPadding.Y * 2 - 60;
        if (listH < 80) listH = 80;

        if (ImGui.BeginChild("##list", new Vector2(-1, listH), ImGuiChildFlags.Borders))
        {
            ImGui.TextDisabled($"{"  ",-5}{"Name",-30} {"Trust",-6} {"Last seen",-8}  {"Together",-9}  Group");
            ImGui.Separator();

            const float IMG_SIZE = 32f;
            const float ROW_H = IMG_SIZE + 4f;

            for (int i = 0; i < Program.shown.Count; i++)
            {
                var f = Program.shown[i];
                var ago = string.IsNullOrEmpty(f.LastLogin) ? "never" : Program.Ago(DateTime.Parse(f.LastLogin));
                var together = Program.FormatTimeSpent(f.TimeSpentMs);
                bool sel = Program.selected.Contains(i);
                int score = TrustScoreService.Calculate(f);
                var scoreCol = score >= 80 ? new Vector4(0.35f, 0.95f, 0.45f, 1f) :
                               score >= 60 ? new Vector4(0.40f, 0.85f, 0.95f, 1f) :
                               score >= 40 ? new Vector4(0.9f, 0.7f, 0.1f, 1f) :
                               score >= 20 ? new Vector4(1f, 0.55f, 0.25f, 1f) :
                               new Vector4(1f, 0.3f, 0.3f, 1f);

                string groupLabel = "";
                foreach (var (tag, ids) in Program.favByGroup.OrderBy(kv => kv.Key))
                    if (ids.Contains(f.Id))
                    { groupLabel = Program.favGroupNames.TryGetValue(tag, out var gn3) ? gn3 : tag; break; }

                ImGui.PushID(i);
                var rowStart = ImGui.GetCursorScreenPos();

                if (ImGui.Selectable($"##s{i}", sel, ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowOverlap, new Vector2(0, ROW_H)))
                {
                    if (Raylib.IsKeyDown(KeyboardKey.LeftControl))
                        _ = sel ? Program.selected.Remove(i) : Program.selected.Add(i);
                    else
                    {
                        Program.selected.Clear();
                        Program.selected.Add(i);
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
                ImGui.Text($"{f.DisplayName,-28}");
                ImGui.SameLine();
                ImGui.TextColored(scoreCol, $"{score,4}");
                ImGui.SameLine();
                ImGui.Text($"  {ago,8}  {together,9}  {groupLabel}");

                ImGui.PopID();
            }
            ImGui.EndChild();
        }

        // ── Notes editor (when exactly 1 selected) ───────────────────────────
        if (Program.selected.Count == 1)
        {
            int idx = Program.selected.First();
            if (idx < Program.shown.Count)
            {
                var sf = Program.shown[idx];
                if (_lastNoteUserId != sf.Id)
                {
                    _lastNoteUserId = sf.Id;
                    _noteBuffer = FriendsManager.GetNote(sf.Id) ?? "";
                }
                ImGui.Spacing();
                ImGui.SetNextItemWidth(sw * 0.6f);
                if (ImGui.InputText($"Notes for {sf.DisplayName}##note", ref _noteBuffer, 256))
                {
                    FriendsManager.SetNote(sf.Id, _noteBuffer);
                }
            }
        }
        else
        {
            _lastNoteUserId = "";
        }

        ImGui.Spacing();
        if (ImGui.Button("Mark All")) { for (int i = 0; i < Program.shown.Count; i++) Program.selected.Add(i); }
        ImGui.SameLine();
        if (ImGui.Button("Unmark All")) Program.selected.Clear();
        ImGui.SameLine();
        if (ImGui.Button("Refresh")) _ = Program.Refresh();
        ImGui.SameLine();
        if (ImGui.Button("Backup JSON"))
            File.WriteAllText($"backup_{DateTime.Now:yyyyMMdd_HHmmss}.json", JsonSerializer.Serialize(Program.shown, new JsonSerializerOptions { WriteIndented = true }));

        ImGui.SameLine();
        if (ImGui.Button("Bulk ▼")) ImGui.OpenPopup("##bulk_menu");
        if (ImGui.BeginPopup("##bulk_menu"))
        {
            var inCutoff = Program.inactiveUnit switch
            {
                0 => DateTime.UtcNow.AddDays(-Program.inactiveVal),
                1 => DateTime.UtcNow.AddMonths(-Program.inactiveVal),
                _ => DateTime.UtcNow.AddYears(-Program.inactiveVal)
            };
            long tThreshMs = Program.togetherUnit switch
            {
                0 => Program.togetherVal * 60_000L,
                1 => Program.togetherVal * 3_600_000L,
                _ => Program.togetherVal * 86_400_000L
            };

            if (ImGui.MenuItem("Select All Inactive"))
                FriendsManager.SelectAllInactive(Program.shown, Program.selected, inCutoff);
            if (ImGui.MenuItem("Select All Low Time"))
                FriendsManager.SelectAllLowTime(Program.shown, Program.selected, tThreshMs);
            if (ImGui.MenuItem("Select Non-Favorites"))
                FriendsManager.SelectNonFavorites(Program.shown, Program.selected, Program.favorites);
            if (ImGui.MenuItem("Select Low Trust (≤25)"))
                FriendsManager.SelectLowScore(Program.shown, Program.selected, Program.favorites, 25);
            if (ImGui.MenuItem("Invert Selection"))
                FriendsManager.InvertSelection(Program.shown, Program.selected);
            ImGui.EndPopup();
        }

        ImGui.SameLine();
        string btnLabel = Program.isUnfriending ? (Program.isPaused ? "Resume" : "Pause") : $"Unfriend ({Program.selected.Count})";
        bool canUnfriend = Program.selected.Count > 0 || Program.isUnfriending;
        if (!canUnfriend) ImGui.BeginDisabled();
        if (ImGui.Button(btnLabel))
        {
            if (Program.isUnfriending) Program.isPaused = !Program.isPaused;
            else if (Program.selected.Count > 0) ImGui.OpenPopup("##confirm_unfriend");
        }
        if (!canUnfriend) ImGui.EndDisabled();

        if (ImGui.BeginPopupModal("##confirm_unfriend", ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.Text($"Permanently unfriend {Program.selected.Count} user(s)?");
            ImGui.Spacing();
            if (ImGui.Button("Yes, do it", new Vector2(120, 0)))
            {
                _ = Task.Run(Program.StartUnfriendProcess);
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

        if (ImGui.Button("Refresh Groups")) _ = Program.Refresh();
        ImGui.SameLine();
        ImGui.TextDisabled($"  {Program.favByGroup.Count} group(s) detected, {Program.favGroupNames.Count} named");
        ImGui.Separator();
        ImGui.Spacing();

        if (Program.favByGroup.Count == 0)
        {
            ImGui.TextColored(new Vector4(1f, 0.6f, 0.3f, 1f), "No favorite groups found.");
            return;
        }

        float colW = Math.Max((sw - 50f) / Math.Max(Program.favByGroup.Count, 1), 180f);

        foreach (var tag in Program.favByGroup.Keys.OrderBy(t => t))
        {
            var ids = Program.favByGroup[tag];
            string displayName = Program.favGroupNames.TryGetValue(tag, out var n) ? n : tag;
            bool excluded = Program.config.ExcludedFavGroups.Contains(tag);

            ImGui.BeginGroup();

            ImGui.TextColored(new Vector4(0.75f, 0.55f, 1f, 1f), displayName);
            ImGui.SameLine();
            ImGui.TextDisabled($"[{tag}] ({ids.Count})");
            ImGui.SameLine();
            if (ImGui.Checkbox($"Exclude##{tag}", ref excluded))
            {
                if (excluded) { if (!Program.config.ExcludedFavGroups.Contains(tag)) Program.config.ExcludedFavGroups.Add(tag); }
                else Program.config.ExcludedFavGroups.Remove(tag);
                Program.SaveConfig();
            }

            float cardH = Math.Min(ids.Count * (ImGui.GetTextLineHeightWithSpacing() + 6) + 12, sh * 0.5f);
            if (ImGui.BeginChild($"##grp_{tag}", new Vector2(colW, cardH), ImGuiChildFlags.Borders))
            {
                foreach (var id in ids)
                {
                    var f = Program.friends.FirstOrDefault(x => x.Id == id);
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
                        ImGui.TextDisabled($"  {Program.FormatTimeSpent(f.TimeSpentMs)}");
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

        if (ImGui.Checkbox("Enable Auto-Decline", ref Program.autoDecline))
        {
            Program.config.AutoDeclineFriendRequests = Program.autoDecline;
            Program.SaveConfig();
            if (Program.autoDecline) SchedulerService.StartAutoDeclineChecker();
            else { Program.autoDeclineCts?.Cancel(); Program.autoDeclineCts = null; }
        }
        ImGui.SameLine();
        ImGui.TextDisabled("(checks every 60s)");

        if (Program.autoDecline)
        {
            ImGui.Indent();
            if (ImGui.Checkbox("Only decline requests from strangers (not current friends or people you've played with)", ref Program.onlyStrangers))
            {
                Program.config.AutoDeclineOnlyFromStrangers = Program.onlyStrangers;
                Program.SaveConfig();
            }

            ImGui.TextDisabled("Minimum time together to count as 'known':");
            ImGui.SameLine();
            int minMin = Program.config.MinTimeTogetherMinutes;
            ImGui.SetNextItemWidth(80);
            if (ImGui.SliderInt("##mintime", ref minMin, 0, 120, "%d min"))
            {
                Program.config.MinTimeTogetherMinutes = minMin;
                Program.SaveConfig();
            }

            if (ImGui.Checkbox("Automatically send friend request back after decline", ref Program.autoSendBack))
            {
                Program.config.AutoSendRequestBack = Program.autoSendBack;
                Program.SaveConfig();
            }
            ImGui.Unindent();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (ImGui.Button("Refresh Requests"))
            _ = Program.RefreshFriendRequests();

        ImGui.SameLine();
        ImGui.TextDisabled($"Pending: {Program.incomingFriendRequests.Count}");

        ImGui.Spacing();

        float listH = sh - ImGui.GetCursorPosY() - 40;
        if (ImGui.BeginChild("##reqlist", new Vector2(-1, listH), ImGuiChildFlags.Borders))
        {
            if (Program.incomingFriendRequests.Count == 0)
            {
                ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), "No incoming friend requests.");
            }
            else
            {
                ImGui.TextDisabled($"{"Sender",-32} {"Date",-12} Actions");
                ImGui.Separator();

                foreach (var req in Program.incomingFriendRequests.ToList())
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
                                await Program.api.DeclineFriendRequestAsync(req.Id);
                                await Program.RefreshFriendRequests();
                                Program.ShowToast("Declined", $"Declined request from {senderName}");
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"[FriendRequests] Manual decline failed: {ex.Message}");
                            }
                        });
                    }
                    ImGui.SameLine();
                    if (ImGui.SmallButton("Send Request Back"))
                    {
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await Program.api.SendFriendRequestAsync(req.SenderUserId ?? "");
                                await Program.api.DeclineFriendRequestAsync(req.Id);
                                await Program.RefreshFriendRequests();
                                Program.ShowToast("Request Sent", $"Sent friend request to {senderName}");
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"[FriendRequests] Send-back failed: {ex.Message}");
                            }
                        });
                    }
                    ImGui.PopID();
                }
            }
            ImGui.EndChild();
        }
    }

    public static void DrawAutoUnfriendConfirmDialog()
    {
        if (!Program.pendingAutoConfirm) return;

        if (DateTime.Now >= Program.autoConfirmDeadline)
        {
            Program.pendingAutoConfirm = false;
            lock (Program.autoConfirmLock)
            {
                var tcs = Program.autoConfirmTcs;
                Program.autoConfirmTcs = null;
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
            ImGui.TextWrapped($"The scheduler is about to unfriend {Program.pendingAutoCount} friend{(Program.pendingAutoCount == 1 ? "" : "s")}.");
            ImGui.Spacing();
            string modeName = Program.config.AutoUnfriendMode switch { 0 => "Inactive Only", 1 => "All Shown", 2 => "Marked Only", _ => "Unknown" };
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), $"Mode: {modeName}");
            ImGui.Spacing();

            int secsLeft = Math.Max(0, (int)Math.Ceiling((Program.autoConfirmDeadline - DateTime.Now).TotalSeconds));
            float fraction = 1f - (secsLeft / 15f);
            var barCol = secsLeft > 8
                ? new Vector4(0.3f, 0.8f, 0.3f, 1f)
                : secsLeft > 4
                    ? new Vector4(0.9f, 0.7f, 0.1f, 1f)
                    : new Vector4(1f, 0.3f, 0.2f, 1f);

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
                Program.pendingAutoConfirm = false;
                lock (Program.autoConfirmLock)
                {
                    var tcs = Program.autoConfirmTcs;
                    Program.autoConfirmTcs = null;
                    tcs?.TrySetResult(true);
                }
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel", new Vector2(btnW, 0)))
            {
                Program.pendingAutoConfirm = false;
                lock (Program.autoConfirmLock)
                {
                    var tcs = Program.autoConfirmTcs;
                    Program.autoConfirmTcs = null;
                    tcs?.TrySetResult(false);
                }
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
        else if (!open)
        {
            Program.pendingAutoConfirm = false;
            lock (Program.autoConfirmLock)
            {
                var tcs = Program.autoConfirmTcs;
                Program.autoConfirmTcs = null;
                tcs?.TrySetResult(false);
            }
        }
    }

    static void DrawSettingsTab()
    {
        ImGui.Spacing();
        ImGui.Text("Startup Options");
        ImGui.Separator();

        bool runOnStartup = Program.config.RunOnStartup;
        if (ImGui.Checkbox("Run on system startup", ref runOnStartup))
        {
            Program.config.RunOnStartup = runOnStartup;
            Program.SaveConfig();
            PlatformService.UpdateStartup(runOnStartup);
        }

        bool startMenuShortcut = Program.config.StartMenuShortcut;
        if (ImGui.Checkbox("Create Start Menu shortcut", ref startMenuShortcut))
        {
            Program.config.StartMenuShortcut = startMenuShortcut;
            Program.SaveConfig();
            PlatformService.UpdateStartMenuShortcut(startMenuShortcut);
        }

        bool hideInTaskbar = Program.config.HideInTaskbar;
        if (ImGui.Checkbox("Enable System Tray (Hide from taskbar)", ref hideInTaskbar))
        {
            Program.config.HideInTaskbar = hideInTaskbar;
            Program.SaveConfig();

            if (hideInTaskbar)
            {
                PlatformService.StartTrayThread(false);
                PlatformService.ApplyTaskbarVisibility(true);
            }
            else
            {
                PlatformService.StopTrayThread();
                PlatformService.ApplyTaskbarVisibility(false);
                if (!PlatformService.WindowVisible) PlatformService.ShowMainWindow();
            }
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            ImGui.SameLine();
            ImGui.TextDisabled("(needs: pip install pystray pillow)");
        }

        if (Directory.Exists(Paths.VrcxStartup))
        {
            ImGui.Spacing();
            ImGui.Text("VRCX Integration");
            if (VRCXDataService.IsAvailable)
                ImGui.TextColored(new Vector4(0.4f, 0.9f, 0.5f, 1f), "✓ VRCX database found — time together data enabled");
            else
                ImGui.TextColored(new Vector4(1f, 0.6f, 0.3f, 1f), "VRCX.sqlite3 not found — time together will show as '-'");

            bool vrcxDesktop = Program.config.VrcxStartupDesktop;
            if (ImGui.Checkbox("Launch with VRCX (Desktop)", ref vrcxDesktop))
            {
                Program.config.VrcxStartupDesktop = vrcxDesktop;
                PlatformService.UpdateVrcxShortcut("desktop", vrcxDesktop);
                Program.SaveConfig();
            }

            bool vrcxVr = Program.config.VrcxStartupVr;
            if (ImGui.Checkbox("Launch with VRCX (VR)", ref vrcxVr))
            {
                Program.config.VrcxStartupVr = vrcxVr;
                PlatformService.UpdateVrcxShortcut("vr", vrcxVr);
                Program.SaveConfig();
            }
        }

        if (Directory.Exists(Paths.VrcNextStartup))
        {
            ImGui.Spacing();
            ImGui.Text("VRCNext Integration");
            if (VRCNextDataService.IsAvailable)
                ImGui.TextColored(new Vector4(0.4f, 0.9f, 0.5f, 1f), "✓ VRCNData.db found — time together data enabled");
            else
                ImGui.TextColored(new Vector4(1f, 0.6f, 0.3f, 1f), "VRCNData.db not found — time together will show as '-'");

            bool vrcNextDesktop = Program.config.VrcNextStartupDesktop;
            if (ImGui.Checkbox("Launch with VRCNext (Desktop)", ref vrcNextDesktop))
            {
                Program.config.VrcNextStartupDesktop = vrcNextDesktop;
                PlatformService.UpdateVrcxShortcut("Desktop", vrcNextDesktop, Paths.VrcNextStartup);
                Program.SaveConfig();
            }

            bool vrcNextVr = Program.config.VrcNextStartupVr;
            if (ImGui.Checkbox("Launch with VRCNext (VR)", ref vrcNextVr))
            {
                Program.config.VrcNextStartupVr = vrcNextVr;
                PlatformService.UpdateVrcxShortcut("VR", vrcNextVr, Paths.VrcNextStartup);
                Program.SaveConfig();
            }
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Text("Updates");
        ImGui.Separator();

        if (ImGui.Button("Check for Updates"))
            _ = Task.Run(Program.CheckForUpdatesAsync);

        if (Program.checkingForUpdate)
            ImGui.Text("Checking for updates...");
        else if (Program.updateAvailable)
        {
            ImGui.TextColored(new Vector4(0.3f, 1f, 0.3f, 1f), $"Update available: {Program.latestVersion}");
            if (ImGui.Button("Download & Install Update"))
                _ = Task.Run(Program.DownloadAndInstallUpdateAsync);
        }

        if (Program.downloading)
            ImGui.ProgressBar(Program.downloadProgress, new Vector2(-1, 20), $"Downloading... {(int)(Program.downloadProgress * 100)}%");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Text("Auto-Unfriend Scheduler");
        ImGui.Separator();

        bool autoEnabled = Program.config.AutoUnfriendEnabled;
        if (ImGui.Checkbox("Enable Auto-Unfriend", ref autoEnabled))
        {
            Program.config.AutoUnfriendEnabled = autoEnabled;
            Program.SaveConfig();
            if (autoEnabled) SchedulerService.StartAutoScheduler();
            else { Program.autoCts?.Cancel(); Program.autoCts = null; }
        }

        if (Program.config.AutoUnfriendEnabled)
        {
            ImGui.Spacing();

            ImGui.Text("Repeat:");
            ImGui.SameLine();
            string[] schedTypes = { "Daily", "Weekly", "Monthly", "Once (specific date)" };
            int schedType = Program.config.AutoUnfriendScheduleType;
            ImGui.SetNextItemWidth(200);
            if (ImGui.Combo("##schedtype", ref schedType, schedTypes, schedTypes.Length))
            {
                Program.config.AutoUnfriendScheduleType = schedType;
                Program.SaveConfig(); SchedulerService.StartAutoScheduler();
            }

            if (Program.config.AutoUnfriendScheduleType == 1)
            {
                ImGui.Text("Day of week:");
                ImGui.SameLine();
                string[] weekdays = { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" };
                int weekday = Math.Clamp(Program.config.AutoUnfriendMonthDay, 0, 6);
                ImGui.SetNextItemWidth(160);
                if (ImGui.Combo("##weekday", ref weekday, weekdays, weekdays.Length))
                {
                    Program.config.AutoUnfriendMonthDay = weekday;
                    Program.SaveConfig();
                    SchedulerService.StartAutoScheduler();
                }
            }
            else if (Program.config.AutoUnfriendScheduleType == 2)
            {
                ImGui.Text("Day of month:");
                ImGui.SameLine();
                int md = Program.config.AutoUnfriendMonthDay;
                ImGui.SetNextItemWidth(60);
                if (ImGui.DragInt("##mday", ref md, 0.1f, 1, 28, "%d"))
                {
                    Program.config.AutoUnfriendMonthDay = Math.Clamp(md, 1, 28);
                    Program.SaveConfig(); SchedulerService.StartAutoScheduler();
                }
            }
            else if (Program.config.AutoUnfriendScheduleType == 3)
            {
                ImGui.Text("Date:");
                ImGui.SameLine();
                int dy = Program.config.AutoUnfriendYear;
                int dm = Program.config.AutoUnfriendMonth;
                int dd = Program.config.AutoUnfriendDay;
                ImGui.SetNextItemWidth(40);
                if (ImGui.DragInt("##dd", ref dd, 0.1f, 1, 31, "%02d"))
                { Program.config.AutoUnfriendDay = Math.Clamp(dd, 1, 31); Program.SaveConfig(); SchedulerService.StartAutoScheduler(); }
                ImGui.SameLine(); ImGui.Text("/");
                ImGui.SameLine();
                ImGui.SetNextItemWidth(40);
                if (ImGui.DragInt("##dm", ref dm, 0.1f, 1, 12, "%02d"))
                { Program.config.AutoUnfriendMonth = Math.Clamp(dm, 1, 12); Program.SaveConfig(); SchedulerService.StartAutoScheduler(); }
                ImGui.SameLine(); ImGui.Text("/");
                ImGui.SameLine();
                ImGui.SetNextItemWidth(70);
                if (ImGui.DragInt("##dy", ref dy, 0.2f, DateTime.Now.Year, DateTime.Now.Year + 10, "%d"))
                { Program.config.AutoUnfriendYear = dy; Program.SaveConfig(); SchedulerService.StartAutoScheduler(); }
            }

            ImGui.Spacing();
            ImGui.Text("Time:");
            ImGui.SameLine();
            int h24 = Program.config.AutoUnfriendHour;
            bool isPm = h24 >= 12;
            int h12 = h24 % 12; if (h12 == 0) h12 = 12;
            int m = Program.config.AutoUnfriendMinute;

            ImGui.SetNextItemWidth(60);
            if (ImGui.DragInt("##ah", ref h12, 0.1f, 1, 12, "%02d"))
            {
                h12 = Math.Clamp(h12, 1, 12);
                Program.config.AutoUnfriendHour = (h12 % 12) + (isPm ? 12 : 0);
                Program.SaveConfig(); SchedulerService.StartAutoScheduler();
            }
            ImGui.SameLine(); ImGui.Text(":");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(60);
            if (ImGui.DragInt("##am", ref m, 0.1f, 0, 59, "%02d"))
            {
                Program.config.AutoUnfriendMinute = Math.Clamp(m, 0, 59);
                Program.SaveConfig(); SchedulerService.StartAutoScheduler();
            }
            ImGui.SameLine();
            if (ImGui.Button(isPm ? "PM" : "AM"))
            {
                isPm = !isPm;
                Program.config.AutoUnfriendHour = (h12 % 12) + (isPm ? 12 : 0);
                Program.SaveConfig(); SchedulerService.StartAutoScheduler();
            }

            ImGui.Spacing();
            ImGui.Text("Mode:");
            ImGui.SameLine();
            int mode = Program.config.AutoUnfriendMode;
            ImGui.SetNextItemWidth(230);
            if (ImGui.Combo("##automode", ref mode, autoModes, autoModes.Length))
            { Program.config.AutoUnfriendMode = mode; Program.SaveConfig(); }

            ImGui.Spacing();
            var next = SchedulerService.GetNextScheduledRun();
            if (next.HasValue)
            {
                var col = next.Value < DateTime.Now ? new Vector4(1f, 0.5f, 0.3f, 1f) : new Vector4(0.4f, 0.9f, 0.5f, 1f);
                ImGui.TextColored(col, $"Next run: {next.Value:ddd dd MMM yyyy  hh:mm tt}");
            }
            else
            {
                ImGui.TextColored(new Vector4(1f, 0.5f, 0.3f, 1f), "Next run: invalid date");
            }

            // ── Friend-limit trigger ────────────────────────────────────────
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            bool limitTrigger = Program.config.FriendLimitTriggerEnabled;
            if (ImGui.Checkbox("Trigger immediately when friend limit reached", ref limitTrigger))
            {
                Program.config.FriendLimitTriggerEnabled = limitTrigger;
                Program.SaveConfig();
            }

            if (Program.config.FriendLimitTriggerEnabled)
            {
                ImGui.Spacing();
                ImGui.Text("Trigger at:");
                ImGui.SameLine();
                int threshold = Program.config.FriendLimitThreshold;
                ImGui.SetNextItemWidth(80);
                if (ImGui.InputInt("##flthresh", ref threshold, 1, 10))
                {
                    Program.config.FriendLimitThreshold = Math.Clamp(threshold, 1, 1000);
                    Program.SaveConfig();
                }
                ImGui.SameLine();
                ImGui.TextDisabled("friends  (VRChat hard cap: 1000)");

                ImGui.Text("Check interval:");
                ImGui.SameLine();
                int pollMin = Program.config.FriendLimitPollIntervalMinutes;
                ImGui.SetNextItemWidth(80);
                if (ImGui.SliderInt("##pollmin", ref pollMin, 1, 60, "%d min"))
                {
                    Program.config.FriendLimitPollIntervalMinutes = pollMin;
                    Program.SaveConfig();
                }

                ImGui.Spacing();
                int cur = Program.friends.Count;
                float ratio = Program.config.FriendLimitThreshold > 0
                    ? Math.Min(1f, cur / (float)Program.config.FriendLimitThreshold) : 0f;
                var barCol = ratio >= 1f
                    ? new Vector4(1f, 0.3f, 0.2f, 1f)
                    : ratio >= 0.9f
                        ? new Vector4(0.9f, 0.7f, 0.1f, 1f)
                        : new Vector4(0.3f, 0.8f, 0.3f, 1f);
                ImGui.PushStyleColor(ImGuiCol.PlotHistogram, barCol);
                ImGui.ProgressBar(ratio, new Vector2(220, 8), "");
                ImGui.PopStyleColor();
                ImGui.SameLine();
                ImGui.TextColored(barCol, $"{cur} / {Program.config.FriendLimitThreshold}");

                if (ratio >= 1f)
                    ImGui.TextColored(new Vector4(1f, 0.4f, 0.3f, 1f), "  ● At or above threshold — will trigger on next check");
                else
                    ImGui.TextDisabled($"  Checked every {Program.config.FriendLimitPollIntervalMinutes} minutes");
            }
        }

        // ── Unfriend History ────────────────────────────────────────────────
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Text("Unfriend History");
        ImGui.Separator();

        bool showStats = Program.config.ShowStatsPanel;
        if (ImGui.Checkbox("Show stats panel on Friends tab", ref showStats))
        {
            Program.config.ShowStatsPanel = showStats;
            Program.SaveConfig();
        }

        var log = FriendsManager.GetUnfriendLog();
        ImGui.TextDisabled($"{log.Count} user(s) removed in total");
        if (log.Count > 0 && ImGui.Button("Clear History"))
        {
            FriendsManager.ClearUnfriendLog();
        }

        if (log.Count > 0)
        {
            float histH = Math.Min(log.Count * 22 + 20, 200);
            if (ImGui.BeginChild("##unfriend_hist", new Vector2(-1, histH), ImGuiChildFlags.Borders))
            {
                ImGui.TextDisabled($"{"Name",-28} {"Date",-14} {"Reason",-10} {"Time Before",-12}");
                ImGui.Separator();
                foreach (var entry in log.OrderByDescending(e => e.UnfriendedAt).Take(50))
                {
                    var dt = entry.UnfriendedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
                    var time = Program.FormatTimeSpent(entry.TimeSpentMsBefore);
                    ImGui.Text($"{entry.DisplayName,-28} {dt,-14} {entry.Reason,-10} {time,-12}");
                }
                ImGui.EndChild();
            }
        }
    }
}
