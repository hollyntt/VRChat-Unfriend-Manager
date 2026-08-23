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

public static class UITheme
{
    public static Vector4 Accent = new(0.55f, 0.35f, 0.95f, 1f);
    public static Vector4 Bg = new(0.09f, 0.09f, 0.12f, 1f);
    public static Vector4 Sidebar = new(0.07f, 0.07f, 0.10f, 1f);
    public static Vector4 Card = new(0.12f, 0.12f, 0.16f, 1f);
    public static Vector4 Text = new(0.90f, 0.88f, 0.95f, 1f);
    public static Vector4 SubText = new(0.50f, 0.48f, 0.58f, 1f);
    public static Vector4 Success = new(0.35f, 0.85f, 0.50f, 1f);
    public static Vector4 Warning = new(0.95f, 0.70f, 0.30f, 1f);
    public static Vector4 Danger = new(0.95f, 0.35f, 0.35f, 1f);

    public static float SidebarWidth = 200f;

    public static void ApplyTheme()
    {
        var s = ImGui.GetStyle();
        s.WindowRounding = 10f;
        s.ChildRounding = 8f;
        s.FrameRounding = 6f;
        s.PopupRounding = 8f;
        s.ScrollbarRounding = 6f;
        s.GrabRounding = 6f;
        s.TabRounding = 6f;
        s.WindowPadding = new Vector2(0, 0);
        s.FramePadding = new Vector2(10, 6);
        s.ItemSpacing = new Vector2(10, 8);
        s.ItemInnerSpacing = new Vector2(8, 6);
        s.ScrollbarSize = 10f;
        s.GrabMinSize = 12f;

        var c = s.Colors;
        c[(int)ImGuiCol.WindowBg] = Bg;
        c[(int)ImGuiCol.ChildBg] = new Vector4(0, 0, 0, 0);
        c[(int)ImGuiCol.PopupBg] = new Vector4(Card.X, Card.Y, Card.Z, 0.98f);
        c[(int)ImGuiCol.Border] = new Vector4(0.22f, 0.20f, 0.30f, 0.6f);
        c[(int)ImGuiCol.FrameBg] = new Vector4(0.16f, 0.15f, 0.22f, 1f);
        c[(int)ImGuiCol.FrameBgHovered] = new Vector4(0.22f, 0.20f, 0.30f, 1f);
        c[(int)ImGuiCol.FrameBgActive] = new Vector4(0.28f, 0.24f, 0.38f, 1f);
        c[(int)ImGuiCol.TitleBg] = Sidebar;
        c[(int)ImGuiCol.TitleBgActive] = Sidebar;
        c[(int)ImGuiCol.Header] = new Vector4(Accent.X, Accent.Y, Accent.Z, 0.25f);
        c[(int)ImGuiCol.HeaderHovered] = new Vector4(Accent.X, Accent.Y, Accent.Z, 0.40f);
        c[(int)ImGuiCol.HeaderActive] = new Vector4(Accent.X, Accent.Y, Accent.Z, 0.55f);
        c[(int)ImGuiCol.Button] = new Vector4(Accent.X, Accent.Y, Accent.Z, 0.55f);
        c[(int)ImGuiCol.ButtonHovered] = new Vector4(Accent.X, Accent.Y, Accent.Z, 0.75f);
        c[(int)ImGuiCol.ButtonActive] = Accent;
        c[(int)ImGuiCol.CheckMark] = Accent;
        c[(int)ImGuiCol.SliderGrab] = Accent;
        c[(int)ImGuiCol.SliderGrabActive] = new Vector4(0.75f, 0.55f, 1f, 1f);
        c[(int)ImGuiCol.ScrollbarBg] = new Vector4(0.06f, 0.06f, 0.08f, 1f);
        c[(int)ImGuiCol.ScrollbarGrab] = new Vector4(Accent.X, Accent.Y, Accent.Z, 0.35f);
        c[(int)ImGuiCol.ScrollbarGrabHovered] = new Vector4(Accent.X, Accent.Y, Accent.Z, 0.55f);
        c[(int)ImGuiCol.ScrollbarGrabActive] = new Vector4(Accent.X, Accent.Y, Accent.Z, 0.75f);
        c[(int)ImGuiCol.Separator] = new Vector4(0.25f, 0.22f, 0.35f, 0.5f);
        c[(int)ImGuiCol.Text] = Text;
        c[(int)ImGuiCol.TextDisabled] = SubText;
        c[(int)ImGuiCol.Tab] = new Vector4(0.14f, 0.13f, 0.18f, 1f);
        c[(int)ImGuiCol.TabHovered] = new Vector4(Accent.X, Accent.Y, Accent.Z, 0.45f);
        // Tab highlight (name varies by ImGui.NET generation)
        c[(int)ImGuiCol.Tab] = new Vector4(Accent.X * 0.5f, Accent.Y * 0.5f, Accent.Z * 0.5f, 0.55f);
    }
}
