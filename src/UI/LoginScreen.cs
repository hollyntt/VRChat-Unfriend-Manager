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
public static class LoginScreen
{
public static void Draw()
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
        ImGui.TextColored(UITheme.Accent, title);
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
}
