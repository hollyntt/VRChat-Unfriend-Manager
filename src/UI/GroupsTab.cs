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
public static class GroupsTab
{
public static void Draw(int sw, int sh)
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

            ImGui.TextColored(new Vector4(0.75f, 0.55f, 1f, 1f), displayName.Replace("&", "&&"));
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
                        ImGui.BeginChild($"##grp_{tag}", new Vector2(colW, cardH), ImGuiChildFlags.Borders);

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

            ImGui.EndGroup();
            ImGui.SameLine(0, 12);
        }
        ImGui.NewLine();
    }
}
