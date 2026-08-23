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
public static class SettingsTab
{
public static void Draw()
    {
        float availH = ImGui.GetContentRegionAvail().Y;
        ImGui.BeginChild("##settings_scroll", new Vector2(0, Math.Max(100f, availH)), ImGuiChildFlags.None, ImGuiWindowFlags.AlwaysVerticalScrollbar);

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
                ImGui.TextColored(new Vector4(0.4f, 0.9f, 0.5f, 1f), "[OK] VRCX database found - time together data enabled");
            else
                ImGui.TextColored(new Vector4(1f, 0.6f, 0.3f, 1f), "VRCX.sqlite3 not found - time together will show as '-'");

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
                ImGui.TextColored(new Vector4(0.4f, 0.9f, 0.5f, 1f), "[OK] VRCNData.db found - time together data enabled");
            else
                ImGui.TextColored(new Vector4(1f, 0.6f, 0.3f, 1f), "VRCNData.db not found - time together will show as '-'");

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

        bool autoCheck = Program.config.AutoCheckUpdates;
        if (ImGui.Checkbox("Auto-check for updates", ref autoCheck))
        { Program.config.AutoCheckUpdates = autoCheck; Program.SaveConfig(); }
        bool autoApply = Program.config.AutoApplyUpdates;
        if (ImGui.Checkbox("Auto-apply updates", ref autoApply))
        { Program.config.AutoApplyUpdates = autoApply; Program.SaveConfig(); }
        if (Program.config.AutoApplyUpdates)
            ImGui.TextDisabled("Downloads and installs without asking when a newer release is found.");

        if (ImGui.Button("Check for Updates") && !Program.UpdateChecking && !Program.UpdateDownloading)
            _ = Task.Run(Program.CheckForUpdatesAsync);

        if (Program.UpdateChecking)
            ImGui.Text("Checking for updates...");
        else if (!string.IsNullOrEmpty(Program.UpdateAvailableTag))
        {
            ImGui.TextColored(new Vector4(0.3f, 1f, 0.3f, 1f), $"Update available: {Program.UpdateAvailableTag}");
            if (ImGui.Button("Download & Install Update") && !Program.UpdateDownloading)
                _ = Task.Run(Program.DownloadAndInstallUpdateAsync);
        }
        else if (!string.IsNullOrEmpty(Program.UpdateStatus))
        {
            ImGui.TextDisabled(Program.UpdateStatus);
        }

        if (Program.UpdateDownloading)
            ImGui.ProgressBar(Program.UpdateProgress, new Vector2(-1, 20), $"Downloading... {(int)(Program.UpdateProgress * 100)}%");

        if (!string.IsNullOrEmpty(Program.UpdateError))
        {
            ImGui.TextColored(new Vector4(1f, 0.35f, 0.35f, 1f), Program.UpdateError);
            if (ImGui.SmallButton("Dismiss"))
                Program.ClearUpdateError();
        }

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
            ImGui.TextDisabled("Who to consider:");
            int mode = Program.config.AutoUnfriendMode;
            string[] modes = { "All friends", "Current list filter", "Marked only" };
            ImGui.SetNextItemWidth(200);
            if (ImGui.Combo("##automode", ref mode, modes, modes.Length))
            { Program.config.AutoUnfriendMode = mode; Program.SaveConfig(); }

            ImGui.Spacing();
            ImGui.TextDisabled("Unfriend when matching (all enabled rules):");

            bool lowScore = Program.config.AutoUnfriendLowScore;
            if (ImGui.Checkbox("Low score", ref lowScore))
            { Program.config.AutoUnfriendLowScore = lowScore; Program.SaveConfig(); }
            if (Program.config.AutoUnfriendLowScore)
            {
                ImGui.SameLine();
                ImGui.TextDisabled("<=");
                ImGui.SameLine();
                ImGui.SetNextItemWidth(72);
                int sm = Program.config.AutoUnfriendScoreMax;
                if (ImGui.DragInt("##autoScoreMax", ref sm, 1f, 0, 100, "%d"))
                { Program.config.AutoUnfriendScoreMax = Math.Clamp(sm, 0, 100); Program.SaveConfig(); }
            }

            bool inact = Program.config.AutoUnfriendInactive;
            if (ImGui.Checkbox("Inactive", ref inact))
            { Program.config.AutoUnfriendInactive = inact; Program.SaveConfig(); }
            if (Program.config.AutoUnfriendInactive)
            {
                ImGui.SameLine();
                ImGui.SetNextItemWidth(64);
                int iv = Program.config.AutoUnfriendInactiveValue;
                if (ImGui.DragInt("##autoInactV", ref iv, 1f, 1, 999, "%d"))
                { Program.config.AutoUnfriendInactiveValue = Math.Max(1, iv); Program.SaveConfig(); }
                ImGui.SameLine();
                ImGui.SetNextItemWidth(80);
                int iu = Program.config.AutoUnfriendInactiveUnit;
                string[] iunits = { "Days", "Months", "Years" };
                if (ImGui.Combo("##autoInactU", ref iu, iunits, iunits.Length))
                { Program.config.AutoUnfriendInactiveUnit = iu; Program.SaveConfig(); }
            }

            bool lowTime = Program.config.AutoUnfriendLowTime;
            if (ImGui.Checkbox("Low time together", ref lowTime))
            { Program.config.AutoUnfriendLowTime = lowTime; Program.SaveConfig(); }
            if (Program.config.AutoUnfriendLowTime)
            {
                ImGui.SameLine();
                ImGui.TextDisabled("<");
                ImGui.SameLine();
                ImGui.SetNextItemWidth(64);
                int tv = Program.config.AutoUnfriendLowTimeValue;
                if (ImGui.DragInt("##autoTimeV", ref tv, 1f, 0, 9999, "%d"))
                { Program.config.AutoUnfriendLowTimeValue = Math.Max(0, tv); Program.SaveConfig(); }
                ImGui.SameLine();
                ImGui.SetNextItemWidth(80);
                int tu = Program.config.AutoUnfriendLowTimeUnit;
                string[] tunits = { "Minutes", "Hours", "Days" };
                if (ImGui.Combo("##autoTimeU", ref tu, tunits, tunits.Length))
                { Program.config.AutoUnfriendLowTimeUnit = tu; Program.SaveConfig(); }
            }

            bool autoRef = Program.config.AutoRefreshAfterUnfriend;
            if (ImGui.Checkbox("Refresh friends after unfriend", ref autoRef))
            { Program.config.AutoRefreshAfterUnfriend = autoRef; Program.SaveConfig(); }

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

            // -- Friend-limit trigger ----------------------------------------
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
                ImGui.SetNextItemWidth(90);
                if (ImGui.DragInt("##flthresh", ref threshold, 1f, 1, 1000, "%d"))
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
                    ImGui.TextColored(new Vector4(1f, 0.4f, 0.3f, 1f), "  * At or above threshold - will trigger on next check");
                else
                    ImGui.TextDisabled($"  Checked every {Program.config.FriendLimitPollIntervalMinutes} minutes");
            }
        }

        AutoGroupPanel.Draw();

        DiscordWebhookPanel.Draw();

        OscNotifyPanel.Draw();

        // -- Unfriend History ------------------------------------------------
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
            // Always pair BeginChild/EndChild (ImGui asserts if EndChild is skipped when Begin returns false).
            ImGui.BeginChild("##unfriend_hist", new Vector2(-1, histH), ImGuiChildFlags.Borders);
            ImGui.TextDisabled($"{"Name",-28} {"Date",-14} {"Reason",-10} {"Time Before",-12}");
            ImGui.Separator();
            foreach (var entry in log.OrderByDescending(e => e.UnfriendedAt).Take(50))
            {
                var dt = entry.UnfriendedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
                var time = Program.FormatTimeSpent(entry.TimeSpentMsBefore);
                ImGui.Text($"{entry.DisplayName,-22} {dt,-14} {entry.Reason,-8} {time,-10}");
                ImGui.SameLine();
                if (ImGui.SmallButton("Re-add##" + entry.UserId))
                {
                    var e = entry;
                    _ = Task.Run(() => Program.ReAddFriendAsync(e));
                }
            }
            ImGui.EndChild();
        }

        ImGui.EndChild(); // ##settings_scroll
    }
}
