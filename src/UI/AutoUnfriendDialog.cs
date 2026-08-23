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
public static class AutoUnfriendDialog
{
public static void Draw()
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
            ImGui.TextColored(new Vector4(1f, 0.4f, 0.4f, 1f), "!  Auto-Unfriend Scheduled Run");
            ImGui.Separator();
            ImGui.Spacing();
            ImGui.TextWrapped($"The scheduler is about to unfriend {Program.pendingAutoCount} friend{(Program.pendingAutoCount == 1 ? "" : "s")}.");
            ImGui.Spacing();
            string modeName = Program.config.AutoUnfriendMode switch { 0 => "All friends", 1 => "Current filter", 2 => "Marked only", _ => "Unknown" };
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
}
