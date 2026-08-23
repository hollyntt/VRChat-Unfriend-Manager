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

/// <summary>XOSC-style shell: sidebar nav + animated content pages.</summary>
public static class UIRenderer
{
    public static void ApplyTheme() => UITheme.ApplyTheme();

    public static void DrawSetupScreen() => SetupScreen.Draw();
    public static void DrawLoginScreen() => LoginScreen.Draw();
    public static void DrawAutoUnfriendConfirmDialog() => AutoUnfriendDialog.Draw();

    public static void DrawMainUI()
    {
        UITheme.ApplyTheme();
        int w = Raylib.GetScreenWidth();
        int h = Raylib.GetScreenHeight();
        float sw = UITheme.SidebarWidth;

        // Sidebar
        ImGui.PushStyleColor(ImGuiCol.ChildBg, UITheme.Sidebar);
        ImGui.BeginChild("##sidebar", new Vector2(sw, h), ImGuiChildFlags.Borders);

        ImGui.Dummy(new Vector2(0, 22));
        ImGui.SetCursorPosX(20);
        ImGui.TextColored(UITheme.Accent, "VRCUFM");
        ImGui.SetCursorPosX(20);
        ImGui.TextColored(UITheme.SubText, "v" + Program.AppVersion);
        if (Program.isLoggedIn)
        {
            ImGui.SetCursorPosX(20);
            ImGui.TextColored(UITheme.Success, Program.loggedInAs);
        }

        ImGui.Dummy(new Vector2(0, 28));

        for (int i = 0; i < UIShared.NavLabels.Length; i++)
        {
            bool active = UIAnim.Page == i;
            ImGui.SetCursorPosX(12);
            if (UIWidgets.NavButton(UIShared.NavLabels[i], active, new Vector2(sw - 24, 36)))
                UIAnim.RequestPage(i);
            ImGui.Dummy(new Vector2(0, 4));
        }

        // Bottom actions
        float bottom = ImGui.GetWindowHeight() - 100;
        if (bottom > ImGui.GetCursorPosY())
            ImGui.SetCursorPosY(bottom);

        ImGui.SetCursorPosX(12);
        if (UIWidgets.NavButton("Refresh", false, new Vector2(sw - 24, 32)) && !Program.working)
            _ = Task.Run(Program.Refresh);

        ImGui.SetCursorPosX(12);
        if (UIWidgets.NavButton("Logout", false, new Vector2(sw - 24, 32)))
        {
            try { File.Delete(Paths.CookieFile); } catch { }
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

        ImGui.EndChild();
        ImGui.PopStyleColor();

        // Content
        ImGui.SameLine(0, 0);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, UITheme.Bg);
        ImGui.BeginChild("##content", new Vector2(w - sw, h), ImGuiChildFlags.Borders);

        ImGui.Dummy(new Vector2(0, 12));
        ImGui.SetCursorPosX(20);
        ImGui.TextColored(UITheme.Text, UIShared.NavLabels[Math.Clamp(UIAnim.Page, 0, UIShared.NavLabels.Length - 1)]);
        ImGui.SetCursorPosX(20);
        ImGui.TextColored(UITheme.SubText, Program.status ?? "");
        ImGui.Dummy(new Vector2(0, 6));
        ImGui.Separator();
        ImGui.Dummy(new Vector2(0, 8));

        float contentH = ImGui.GetContentRegionAvail().Y - 8;
        ImGui.BeginChild("##page", new Vector2(-1, contentH), ImGuiChildFlags.Borders);

        UIAnim.BeginPageContent();
        ImGui.SetCursorPosX(16);
        ImGui.BeginGroup();
        ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X - 24);

        int page = UIAnim.Page;
        int cw = (int)(w - sw);
        int ch = h;
        switch (page)
        {
            case 0: FriendsTab.Draw(cw, ch); break;
            case 1: GroupsTab.Draw(cw, ch); break;
            case 2: FriendRequestsTab.Draw(cw, ch); break;
            case 3: SettingsTab.Draw(); break;
        }

        ImGui.PopItemWidth();
        ImGui.EndGroup();
        UIAnim.EndPageContent();

        ImGui.EndChild(); // page
        ImGui.EndChild(); // content
        ImGui.PopStyleColor();
    }
}
