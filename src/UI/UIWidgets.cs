using System.Numerics;
using System.Runtime.InteropServices;
using ImGuiNET;
using Raylib_cs;
using VRCUFM.AppSystem;
using VRCUFM.Core;
using VRCUFM.Filesystem;
using VRCUFM.VRChat;
using File = System.IO.File;

namespace VRCUFM.UI;

public static class UIWidgets
{
    public static void Card(string title, Action body)
    {
        ImGui.TextColored(UITheme.Accent, title);
        ImGui.Dummy(new Vector2(0, 6));

        ImGui.PushStyleColor(ImGuiCol.ChildBg, UITheme.Card);
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.25f, 0.22f, 0.35f, 0.45f));
        ImGui.BeginChild("##card_" + title, new Vector2(ImGui.GetContentRegionAvail().X, 0),
            ImGuiChildFlags.Borders | ImGuiChildFlags.AutoResizeY);

        ImGui.Dummy(new Vector2(0, 8));
        float pad = 14f;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + pad);
        ImGui.BeginGroup();
        ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X - pad);
        body();
        ImGui.PopItemWidth();
        ImGui.EndGroup();
        ImGui.Dummy(new Vector2(0, 8));

        ImGui.EndChild();
        ImGui.PopStyleColor(2);
        ImGui.Dummy(new Vector2(0, 12));
    }

    public static void SectionLabel(string text)
    {
        ImGui.TextColored(UITheme.SubText, text);
        ImGui.Spacing();
    }

    public static bool NavButton(string label, bool active, Vector2 size)
    {
        if (active)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(UITheme.Accent.X, UITheme.Accent.Y, UITheme.Accent.Z, 0.22f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(UITheme.Accent.X, UITheme.Accent.Y, UITheme.Accent.Z, 0.30f));
            ImGui.PushStyleColor(ImGuiCol.Text, UITheme.Accent);
        }
        else
        {
            ImGui.PushStyleColor(ImGuiCol.Button, Vector4.Zero);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(UITheme.Accent.X, UITheme.Accent.Y, UITheme.Accent.Z, 0.10f));
            ImGui.PushStyleColor(ImGuiCol.Text, UITheme.Text);
        }
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(UITheme.Accent.X, UITheme.Accent.Y, UITheme.Accent.Z, 0.28f));
        bool clicked = ImGui.Button(label, size);
        ImGui.PopStyleColor(4);
        return clicked;
    }
}
