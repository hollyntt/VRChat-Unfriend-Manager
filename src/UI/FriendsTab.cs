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
public static class FriendsTab
{
    static string _friendsSearchLive = "";
    static string _friendsSearchApplied = "";
    static float _friendsSearchCd;
    static string _noteBuffer = "";
    static string _lastNoteUserId = "";

public static void Draw(int sw, int sh)
    {
        ImGui.Spacing();

        // -- Stats Panel ------------------------------------------------------
        if (Program.config.ShowStatsPanel && Program.friends.Count > 0)
        {
            var stats = FriendsManager.CalculateStats(Program.friends, Program.favorites, Program.favByGroup);
            ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.08f, 0.08f, 0.14f, 1f));
                        ImGui.BeginChild("##stats", new Vector2(-1, 70), ImGuiChildFlags.Borders);

                ImGui.TextColored(new Vector4(0.75f, 0.55f, 1f, 1f), "Friend Stats");
                ImGui.SameLine();
                ImGui.TextDisabled($"  Total: {stats.TotalFriends}  |  Online: {stats.OnlineFriends}  |  Inactive: {stats.InactiveFriends}  |  Ghosts: {stats.GhostFriends}  |  Favorites: {stats.FavoritesCount}");
                ImGui.TextDisabled($"Avg time together: {Program.FormatTimeSpent((long)stats.AverageTimeTogetherMs)}  |  Total: {Program.FormatTimeSpent(stats.TotalTimeTogetherMs)}");

            ImGui.EndChild();
            ImGui.PopStyleColor();
            ImGui.Spacing();
        }

        if (ImGui.Checkbox("Hide Favorites", ref Program.hideFavs))
        { Program.config.ExcludeFavorites = Program.hideFavs; Program.SaveConfig(); }

        ImGui.SameLine(0, 16);
        ImGui.SameLine(0, 20);
        if (ImGui.Checkbox("Inactive >=", ref Program.inactiveOn))
        { Program.config.InactiveEnabled = Program.inactiveOn; Program.SaveConfig(); }
        if (Program.inactiveOn)
        {
            ImGui.SameLine();
            ImGui.SetNextItemWidth(70f);
            if (ImGui.DragInt("##iv", ref Program.inactiveVal, 1f, 1, 9999, "%d"))
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
            if (ImGui.DragInt("##tv", ref Program.togetherVal, 1f, 0, 99999, "%d"))
            { if (Program.togetherVal < 0) Program.togetherVal = 0; Program.config.TogetherFilterValue = Program.togetherVal; Program.SaveConfig(); }
            ImGui.SameLine();
            ImGui.SetNextItemWidth(70f);
            if (ImGui.Combo("##tu", ref Program.togetherUnit, UIShared.TogetherUnits, UIShared.TogetherUnits.Length))
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

        // Score range on its own row (InputInt needs width or only +/- shows)
        bool scoreFilt = Program.config.ScoreFilterEnabled;
        if (ImGui.Checkbox("Score range", ref scoreFilt))
        { Program.config.ScoreFilterEnabled = scoreFilt; Program.SaveConfig(); }
        if (Program.config.ScoreFilterEnabled)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("min");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(72f);
            int smin = Program.config.ScoreFilterMin;
            if (ImGui.DragInt("##smin", ref smin, 1f, 0, 100, "%d"))
            { Program.config.ScoreFilterMin = Math.Clamp(smin, 0, 100); Program.SaveConfig(); }
            ImGui.SameLine();
            ImGui.TextDisabled("max");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(72f);
            int smax = Program.config.ScoreFilterMax;
            if (ImGui.DragInt("##smax", ref smax, 1f, 0, 100, "%d"))
            { Program.config.ScoreFilterMax = Math.Clamp(smax, 0, 100); Program.SaveConfig(); }
            ImGui.SameLine();
            int lo = Math.Min(Program.config.ScoreFilterMin, Program.config.ScoreFilterMax);
            int hi = Math.Max(Program.config.ScoreFilterMin, Program.config.ScoreFilterMax);
            int sMatch = Program.friends.Count(f =>
            {
                if (Program.hideFavs && Program.favorites.Contains(f.Id)) return false;
                int s = FriendsManager.CalculateFriendScore(f, Program.favorites);
                return s >= lo && s <= hi;
            });
            ImGui.TextColored(new Vector4(0.4f, 0.9f, 0.5f, 1f), $"({sMatch} match)");
        }


        // Debounced search
        if (string.IsNullOrEmpty(_friendsSearchLive) && !string.IsNullOrEmpty(Program.searchText))
            _friendsSearchLive = Program.searchText;

        ImGui.Spacing();
        ImGui.SetNextItemWidth(100f);
        ImGui.Combo("##sf", ref Program.searchField, UIShared.SearchFields, UIShared.SearchFields.Length);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(sw * 0.45f);
        ImGui.InputText("Search##sq", ref _friendsSearchLive, 128);
        if (!string.IsNullOrEmpty(_friendsSearchLive))
        {
            ImGui.SameLine();
            if (ImGui.SmallButton("x##clr"))
            {
                _friendsSearchLive = "";
                _friendsSearchApplied = "";
                Program.searchText = "";
                _friendsSearchCd = 0;
            }
        }

        if (_friendsSearchLive != _friendsSearchApplied)
        {
            _friendsSearchCd += ImGui.GetIO().DeltaTime;
            if (_friendsSearchCd > 0.15f)
            {
                _friendsSearchApplied = _friendsSearchLive;
                Program.searchText = _friendsSearchApplied;
                _friendsSearchCd = 0;
            }
        }
        else
            _friendsSearchCd = 0;

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
                ImGui.Text($"{lbl.Replace("&", "&&")} ({cnt})");
                ImGui.SameLine(0, 14);
            }
            ImGui.NewLine();
        }

        ImGui.Spacing();
        ImGui.SetNextItemWidth(180f);
        if (ImGui.Combo("Sort", ref Program.sort, UIShared.Sorts, UIShared.Sorts.Length))
        { Program.config.SortOptionIndex = Program.sort; Program.SaveConfig(); }

        ImGui.Separator();
        ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.7f, 1f), Program.status);
        if (Program.working && !Program.isUnfriending)
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

        if (Program.config.ScoreFilterEnabled)
        {
            int lo = Math.Min(Program.config.ScoreFilterMin, Program.config.ScoreFilterMax);
            int hi = Math.Max(Program.config.ScoreFilterMin, Program.config.ScoreFilterMax);
            temp = temp.Where(f =>
            {
                int s = FriendsManager.CalculateFriendScore(f, Program.favorites);
                return s >= lo && s <= hi;
            }).ToList();
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
            6 => temp.OrderBy(f => FriendsManager.CalculateFriendScore(f, Program.favorites)).ToList(),
            7 => temp.OrderByDescending(f => FriendsManager.CalculateFriendScore(f, Program.favorites)).ToList(),
            _ => temp.OrderBy(f => f.DisplayName).ToList()
        };
        Program.shown = temp;

        float bottomBarH = Program.isUnfriending ? 90f : 50f;
        float listH = sh - ImGui.GetCursorPosY() - bottomBarH - ImGui.GetStyle().WindowPadding.Y * 2 - 60;
        if (listH < 80) listH = 80;

                ImGui.BeginChild("##list", new Vector2(-1, listH), ImGuiChildFlags.Borders);

            ImGui.TextDisabled($"{"  ",-5}{"Name",-30} {"Score",-6} {"Last seen",-8}  {"Together",-9}  Group");
            ImGui.Separator();

            const float IMG_SIZE = 32f;
            const float ROW_H = IMG_SIZE + 4f;

            for (int i = 0; i < Program.shown.Count; i++)
            {
                var f = Program.shown[i];
                var ago = string.IsNullOrEmpty(f.LastLogin) ? "never" : Program.Ago(DateTime.Parse(f.LastLogin));
                var together = Program.FormatTimeSpent(f.TimeSpentMs);
                bool sel = Program.selected.Contains(i);
                int score = FriendsManager.CalculateFriendScore(f, Program.favorites);
                var scoreCol = score < 20 ? new Vector4(1f, 0.3f, 0.3f, 1f) :
                               score < 50 ? new Vector4(0.9f, 0.7f, 0.1f, 1f) :
                               new Vector4(0.4f, 0.9f, 0.5f, 1f);

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

                // Right-click context menu for this friend
                if (ImGui.BeginPopupContextItem("##friend_ctx"))
                {
                    // Ensure this row is selected when menu opens
                    if (!Program.selected.Contains(i))
                    {
                        Program.selected.Clear();
                        Program.selected.Add(i);
                    }

                    ImGui.TextDisabled(f.DisplayName ?? "");
                    ImGui.Separator();

                    if (ImGui.MenuItem("Open VRChat profile"))
                    {
                        try
                        {
                            var url = "https://vrchat.com/home/user/" + f.Id;
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = url,
                                UseShellExecute = true
                            });
                        }
                        catch { }
                    }
                    if (ImGui.MenuItem("Copy user ID"))
                        ImGui.SetClipboardText(f.Id ?? "");
                    if (ImGui.MenuItem("Copy display name"))
                        ImGui.SetClipboardText(f.DisplayName ?? "");

                    ImGui.Separator();

                    bool isFav = Program.favorites.Contains(f.Id);
                    if (isFav)
                    {
                        if (ImGui.MenuItem("Unfavorite"))
                        {
                            var uid = f.Id;
                            var name = f.DisplayName;
                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    await Program.api.RemoveFriendFavoriteAsync(uid);
                                    Program.favorites.Remove(uid);
                                    foreach (var set in Program.favByGroup.Values)
                                        set.Remove(uid);
                                    Program.ShowToast("Unfavorite", name);
                                    Program.status = "Unfavorited " + name;
                                }
                                catch (Exception ex)
                                {
                                    Program.ShowToast("Unfavorite failed", ex.Message);
                                }
                            });
                        }

                        if (ImGui.BeginMenu("Move to group"))
                        {
                            for (int g = 0; g < 4; g++)
                            {
                                string tag = "group_" + g;
                                string label = Program.favGroupNames.TryGetValue(tag, out var gn)
                                    ? gn + " (" + tag + ")" : tag;
                                // ASCII-only for ImGui
                                label = new string(label.Select(c => c >= 32 && c <= 126 ? c : '?').ToArray());
                                if (ImGui.MenuItem(label + "##mv" + g))
                                {
                                    var uid = f.Id;
                                    var name = f.DisplayName;
                                    var t = tag;
                                    _ = Task.Run(async () =>
                                    {
                                        try
                                        {
                                            await Program.api.RemoveFriendFavoriteAsync(uid);
                                            await Program.api.AddFriendFavoriteAsync(uid, t);
                                            Program.favorites.Add(uid);
                                            foreach (var set in Program.favByGroup.Values)
                                                set.Remove(uid);
                                            if (!Program.favByGroup.ContainsKey(t))
                                                Program.favByGroup[t] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                                            Program.favByGroup[t].Add(uid);
                                            Program.ShowToast("Moved", name + " -> " + t);
                                        }
                                        catch (Exception ex)
                                        {
                                            Program.ShowToast("Move failed", ex.Message);
                                        }
                                    });
                                }
                            }
                            ImGui.EndMenu();
                        }
                    }
                    else
                    {
                        if (ImGui.BeginMenu("Add to group"))
                        {
                            for (int g = 0; g < 4; g++)
                            {
                                string tag = "group_" + g;
                                string label = Program.favGroupNames.TryGetValue(tag, out var gn)
                                    ? gn + " (" + tag + ")" : tag;
                                label = new string(label.Select(c => c >= 32 && c <= 126 ? c : '?').ToArray());
                                if (ImGui.MenuItem(label + "##add" + g))
                                {
                                    var uid = f.Id;
                                    var name = f.DisplayName;
                                    var t = tag;
                                    _ = Task.Run(async () =>
                                    {
                                        try
                                        {
                                            await Program.api.AddFriendFavoriteAsync(uid, t);
                                            Program.favorites.Add(uid);
                                            if (!Program.favByGroup.ContainsKey(t))
                                                Program.favByGroup[t] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                                            Program.favByGroup[t].Add(uid);
                                            Program.ShowToast("Favorited", name + " -> " + t);
                                        }
                                        catch (Exception ex)
                                        {
                                            Program.ShowToast("Favorite failed", ex.Message);
                                        }
                                    });
                                }
                            }
                            ImGui.EndMenu();
                        }
                    }

                    ImGui.Separator();
                    if (ImGui.MenuItem("Unfriend"))
                    {
                        Program.selected.Clear();
                        Program.selected.Add(i);
                        ImGui.OpenPopup("##confirm_unfriend");
                    }

                    ImGui.EndPopup();
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

        // -- Notes editor (when exactly 1 selected) ---------------------------
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
        
        if (ImGui.Button("Backup JSON"))
            File.WriteAllText($"backup_{DateTime.Now:yyyyMMdd_HHmmss}.json", JsonSerializer.Serialize(Program.shown, new JsonSerializerOptions { WriteIndented = true }));

        ImGui.SameLine();
        if (ImGui.Button("Bulk Select")) ImGui.OpenPopup("##bulk_menu");
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
            ImGui.SetNextItemWidth(80);
            int bMax = Program.config.ScoreBulkMax;
            if (ImGui.DragInt("Max##bulkScore", ref bMax, 1f, 0, 100, "%d"))
            { Program.config.ScoreBulkMax = Math.Clamp(bMax, 0, 100); Program.SaveConfig(); }
            if (ImGui.MenuItem("Select score 0 to max"))
                FriendsManager.SelectScoreRange(Program.shown, Program.selected, Program.favorites, 0, Program.config.ScoreBulkMax);
            if (ImGui.MenuItem("Select Low Score (<=25)"))
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
}
