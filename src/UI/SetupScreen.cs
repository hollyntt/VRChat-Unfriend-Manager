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
public static class SetupScreen
{
public static void Draw()
    {
        int sw = Raylib.GetScreenWidth();
        int sh = Raylib.GetScreenHeight();
        float formW = Math.Min(480f, sw * 0.9f);
        float formH = 360f;
        float ox = (sw - formW) * 0.5f;
        float oy = (sh - formH) * 0.5f;

        ImGui.SetCursorPos(new Vector2(ox, oy));
        ImGui.BeginChild("##setup_card", new Vector2(formW, formH), ImGuiChildFlags.Borders);

        ImGui.Spacing();
        ImGui.Text("Install required");
        ImGui.Separator();
        ImGui.Spacing();

        string cur = InstallService.GetCurrentAppDir();
        ImGui.TextWrapped(
            "This build is not running from your install folder. " +
            "Install to a user folder so updates can replace files without Admin.");
        ImGui.Spacing();
        ImGui.TextDisabled("Currently running from:");
        ImGui.TextWrapped(cur);
        ImGui.Spacing();

        if (InstallService.IsUnderProgramFiles(cur))
        {
            ImGui.TextColored(new Vector4(1f, 0.55f, 0.25f, 1f),
                "Program Files is not supported for auto-updates.");
            ImGui.Spacing();
        }

        ImGui.Text("Install location");
        string path = Program.setupInstallPath ?? "";
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputText("##install_path", ref path, 512))
            Program.setupInstallPath = path;

        if (ImGui.Button("Browse..."))
        {
#if WINDOWS_BUILD
            try
            {
                using var dlg = new System.Windows.Forms.FolderBrowserDialog
                {
                    Description = "Choose VRCUFM install folder",
                    UseDescriptionForTitle = true,
                    SelectedPath = Directory.Exists(Program.setupInstallPath)
                        ? Program.setupInstallPath
                        : Paths.DefaultInstallDir
                };
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK
                    && !string.IsNullOrWhiteSpace(dlg.SelectedPath))
                    Program.setupInstallPath = dlg.SelectedPath;
            }
            catch (Exception ex)
            {
                Program.status = "Folder dialog failed: " + ex.Message;
            }
#else
            // Linux / AppImage: no WinForms folder dialog - path is edited in the text field
            Program.status = "Type or paste the install path above (Browse is Windows-only).";
#endif
        }
        ImGui.SameLine();
        if (ImGui.Button("Use recommended"))
            Program.setupInstallPath = Paths.DefaultInstallDir;

        ImGui.Spacing();
        bool startMenu = Program.config.StartMenuShortcut;
        if (ImGui.Checkbox("Create Start Menu shortcut", ref startMenu))
            Program.config.StartMenuShortcut = startMenu;

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        float btnW = (formW - 48f) * 0.5f;
        if (ImGui.Button("Install here", new Vector2(btnW, 36)))
        {
            try
            {
                Program.status = "Installing...";
                InstallService.InstallAndRelaunch(
                    Program.setupInstallPath,
                    Program.config,
                    Program.config.StartMenuShortcut,
                    Program.SaveConfig);
                // If no relaunch (already in place), clear setup flag
                Program.needsSetup = InstallService.NeedsSetup(Program.config);
                Program.status = "Install complete";
            }
            catch (Exception ex)
            {
                Program.status = "Install failed: " + ex.Message;
                Program.status = "Install failed: " + ex.Message;
            }
        }
        ImGui.SameLine();
        if (ImGui.Button("Run portable", new Vector2(btnW, 36)))
        {
            try
            {
                InstallService.MarkPortable(Program.config, Program.SaveConfig);
                Program.needsSetup = false;
                Program.status = "Running portable from " + cur;
            }
            catch (Exception ex)
            {
                Program.status = "Portable mode: " + ex.Message;
            }
        }

        ImGui.Spacing();
        ImGui.TextDisabled("Recommended: " + Paths.DefaultInstallDir);
        if (!string.IsNullOrEmpty(Program.status))
            ImGui.TextColored(new Vector4(0.7f, 0.6f, 0.9f, 1f), Program.status);

        ImGui.EndChild();
    }
}
