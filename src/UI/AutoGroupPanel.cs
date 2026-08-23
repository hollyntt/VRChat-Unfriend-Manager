using System.Numerics;
using System.Text;
using ImGuiNET;
using VRCUFM.AppSystem;
using VRCUFM.Core;

namespace VRCUFM.UI;

public static class AutoGroupPanel
{
    static readonly string[] GroupTags = { "group_0", "group_1", "group_2", "group_3" };
    static readonly string[] Actions = { "add", "move", "unfavorite", "clear_group", "clear_all" };
    static readonly string[] ActionLabels =
    {
        "Put in group",
        "Move to group",
        "Remove from favorites",
        "Clear this group",
        "Clear every group"
    };
    static readonly string[] DayUnits = { "Days", "Months", "Years" };
    static readonly string[] TimeUnits = { "Minutes", "Hours", "Days" };

    static string AsciiOnly(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            if (ch >= 32 && ch <= 126) sb.Append(ch);
            else sb.Append('?');
        }
        return sb.ToString();
    }

    public static void Draw()
    {
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Text("Auto-group friends");
        ImGui.Separator();
        ImGui.TextDisabled("Automatically organize favorites: put people in groups, remove them, or clear groups.");

        bool enabled = Program.config.AutoGroupEnabled;
        if (ImGui.Checkbox("Enable auto-grouping", ref enabled))
        {
            Program.config.AutoGroupEnabled = enabled;
            Program.SaveConfig();
            if (enabled) AutoGroupService.Start();
            else AutoGroupService.Stop();
        }

        if (!Program.config.AutoGroupEnabled)
            return;

        ImGui.SameLine();
        ImGui.TextDisabled("Every");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(64);
        int mins = Program.config.AutoGroupIntervalMinutes;
        if (ImGui.DragInt("##agInt", ref mins, 1f, 5, 240, "%d"))
        {
            Program.config.AutoGroupIntervalMinutes = Math.Clamp(mins, 5, 240);
            Program.SaveConfig();
        }
        ImGui.SameLine();
        ImGui.TextDisabled("min");

        if (ImGui.Button("Run rules now"))
            _ = Task.Run(() => AutoGroupService.RunOnceAsync());

        ImGui.SameLine();
        if (ImGui.Button("Clear every group now"))
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await Program.api.ClearAllFriendFavoritesAsync();
                    Program.favorites.Clear();
                    Program.favByGroup.Clear();
                    Program.ShowToast("Favorites", "Cleared all friend favorites");
                    Program.status = "Cleared all friend favorites";
                }
                catch (Exception ex)
                {
                    Program.ShowToast("Auto-Group failed", ex.Message);
                }
            });
        }

        Program.config.AutoGroupRules ??= new List<AutoGroupRule>();
        var rules = Program.config.AutoGroupRules;

        ImGui.Spacing();
        ImGui.TextDisabled("Rules run top to bottom. First match wins.");
        if (ImGui.Button("Add rule"))
        {
            rules.Add(new AutoGroupRule { Name = $"Rule {rules.Count + 1}" });
            Program.SaveConfig();
        }

        int removeIdx = -1;
        int moveUp = -1, moveDown = -1;
        for (int i = 0; i < rules.Count; i++)
        {
            var rule = rules[i];
            ImGui.PushID(i);

            // Priority rank + reorder
            ImGui.TextDisabled($"#{i + 1}");
            ImGui.SameLine();
            if (ImGui.SmallButton("^") && i > 0) moveUp = i;
            ImGui.SameLine();
            if (ImGui.SmallButton("v") && i < rules.Count - 1) moveDown = i;
            ImGui.SameLine();

            bool open = ImGui.TreeNode($"##ag{i}");
            ImGui.SameLine();
            bool en = rule.Enabled;
            if (ImGui.Checkbox("##en", ref en)) { rule.Enabled = en; Program.SaveConfig(); }
            ImGui.SameLine();
            string name = rule.Name ?? "Rule";
            ImGui.SetNextItemWidth(140);
            if (ImGui.InputText("##name", ref name, 64)) { rule.Name = name; Program.SaveConfig(); }

            ImGui.SameLine();
            int aIdx = Array.IndexOf(Actions, (rule.Action ?? "add").ToLowerInvariant());
            if (aIdx < 0) aIdx = 0;
            ImGui.SetNextItemWidth(170);
            if (ImGui.Combo("##act", ref aIdx, ActionLabels, ActionLabels.Length))
            {
                rule.Action = Actions[aIdx];
                Program.SaveConfig();
            }

            string act = (rule.Action ?? "add").ToLowerInvariant();
            // Group picker for add / move / clear_group
            if (act is "add" or "move" or "clear_group")
            {
                ImGui.SameLine();
                ImGui.TextDisabled("->");
                ImGui.SameLine();
                int gIdx = Array.IndexOf(GroupTags, rule.TargetGroupTag);
                if (gIdx < 0) gIdx = 0;
                string[] labels = GroupTags.Select(t =>
                {
                    string label = Program.favGroupNames.TryGetValue(t, out var n) ? AsciiOnly(n) : t;
                    return $"{label} ({t})";
                }).ToArray();
                ImGui.SetNextItemWidth(170);
                if (ImGui.Combo("##grp", ref gIdx, labels, labels.Length))
                {
                    rule.TargetGroupTag = GroupTags[gIdx];
                    Program.SaveConfig();
                }
            }

            ImGui.SameLine();
            if (ImGui.SmallButton("Remove"))
                removeIdx = i;

            if (open)
            {
                if (act is "clear_all" or "clear_group")
                {
                    ImGui.TextDisabled(act == "clear_all"
                        ? "Each run: empties all friend favorite groups."
                        : "Each run: empties only the group you picked.");
                }
                else
                {
                    if (act == "add")
                    {
                        bool skip = rule.SkipIfAlreadyFavorited;
                        if (ImGui.Checkbox("Skip people already in a group", ref skip))
                        { rule.SkipIfAlreadyFavorited = skip; Program.SaveConfig(); }
                    }

                    ImGui.TextDisabled("Only apply to friends who match ALL checked filters:");
                    DrawScoreHigh(rule);
                    DrawScoreLow(rule);
                    DrawInactive(rule);
                    DrawActive(rule);
                    DrawHighTime(rule);
                    DrawLowTime(rule);
                }

                ImGui.TreePop();
            }
            ImGui.PopID();
        }

        if (removeIdx >= 0 && removeIdx < rules.Count)
        {
            rules.RemoveAt(removeIdx);
            Program.SaveConfig();
        }

        if (moveUp > 0 && moveUp < rules.Count)
        {
            (rules[moveUp - 1], rules[moveUp]) = (rules[moveUp], rules[moveUp - 1]);
            Program.SaveConfig();
        }
        else if (moveDown >= 0 && moveDown < rules.Count - 1)
        {
            (rules[moveDown], rules[moveDown + 1]) = (rules[moveDown + 1], rules[moveDown]);
            Program.SaveConfig();
        }
    }

    static void DrawScoreHigh(AutoGroupRule rule)
    {
        bool v = rule.UseHighScore;
        if (ImGui.Checkbox("Score at least", ref v)) { rule.UseHighScore = v; Program.SaveConfig(); }
        if (rule.UseHighScore)
        {
            ImGui.SameLine();
            ImGui.SetNextItemWidth(64);
            int n = rule.ScoreMin;
            if (ImGui.DragInt("##hs", ref n, 1f, 0, 100, "%d"))
            { rule.ScoreMin = Math.Clamp(n, 0, 100); Program.SaveConfig(); }
        }
    }

    static void DrawScoreLow(AutoGroupRule rule)
    {
        bool v = rule.UseLowScore;
        if (ImGui.Checkbox("Score at most", ref v)) { rule.UseLowScore = v; Program.SaveConfig(); }
        if (rule.UseLowScore)
        {
            ImGui.SameLine();
            ImGui.SetNextItemWidth(64);
            int n = rule.ScoreMax;
            if (ImGui.DragInt("##ls", ref n, 1f, 0, 100, "%d"))
            { rule.ScoreMax = Math.Clamp(n, 0, 100); Program.SaveConfig(); }
        }
    }

    static void DrawInactive(AutoGroupRule rule)
    {
        bool v = rule.UseInactive;
        if (ImGui.Checkbox("Hasn't logged in for", ref v)) { rule.UseInactive = v; Program.SaveConfig(); }
        if (rule.UseInactive)
        {
            ImGui.SameLine();
            ImGui.SetNextItemWidth(56);
            int n = rule.InactiveValue;
            if (ImGui.DragInt("##iv", ref n, 1f, 1, 999, "%d"))
            { rule.InactiveValue = Math.Max(1, n); Program.SaveConfig(); }
            ImGui.SameLine();
            ImGui.SetNextItemWidth(90);
            int u = rule.InactiveUnit;
            if (ImGui.Combo("##iu", ref u, DayUnits, DayUnits.Length))
            { rule.InactiveUnit = u; Program.SaveConfig(); }
        }
    }

    static void DrawActive(AutoGroupRule rule)
    {
        bool v = rule.UseActive;
        if (ImGui.Checkbox("Logged in within last", ref v)) { rule.UseActive = v; Program.SaveConfig(); }
        if (rule.UseActive)
        {
            ImGui.SameLine();
            ImGui.SetNextItemWidth(64);
            int n = rule.ActiveWithinDays;
            if (ImGui.DragInt("##ad", ref n, 1f, 1, 365, "%d"))
            { rule.ActiveWithinDays = Math.Clamp(n, 1, 365); Program.SaveConfig(); }
            ImGui.SameLine();
            ImGui.TextDisabled("days");
        }
    }

    static void DrawHighTime(AutoGroupRule rule)
    {
        bool v = rule.UseHighTime;
        if (ImGui.Checkbox("Played together at least", ref v)) { rule.UseHighTime = v; Program.SaveConfig(); }
        if (rule.UseHighTime)
        {
            ImGui.SameLine();
            ImGui.SetNextItemWidth(56);
            int n = rule.HighTimeValue;
            if (ImGui.DragInt("##ht", ref n, 1f, 0, 99999, "%d"))
            { rule.HighTimeValue = Math.Max(0, n); Program.SaveConfig(); }
            ImGui.SameLine();
            ImGui.SetNextItemWidth(90);
            int u = rule.HighTimeUnit;
            if (ImGui.Combo("##htu", ref u, TimeUnits, TimeUnits.Length))
            { rule.HighTimeUnit = u; Program.SaveConfig(); }
        }
    }

    static void DrawLowTime(AutoGroupRule rule)
    {
        bool v = rule.UseLowTime;
        if (ImGui.Checkbox("Played together less than", ref v)) { rule.UseLowTime = v; Program.SaveConfig(); }
        if (rule.UseLowTime)
        {
            ImGui.SameLine();
            ImGui.SetNextItemWidth(56);
            int n = rule.LowTimeValue;
            if (ImGui.DragInt("##lt", ref n, 1f, 0, 99999, "%d"))
            { rule.LowTimeValue = Math.Max(0, n); Program.SaveConfig(); }
            ImGui.SameLine();
            ImGui.SetNextItemWidth(90);
            int u = rule.LowTimeUnit;
            if (ImGui.Combo("##ltu", ref u, TimeUnits, TimeUnits.Length))
            { rule.LowTimeUnit = u; Program.SaveConfig(); }
        }
    }
}
