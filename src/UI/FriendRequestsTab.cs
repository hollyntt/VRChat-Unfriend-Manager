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
public static class FriendRequestsTab
{
public static void Draw(int sw, int sh)
    {
        ImGui.Spacing();

        ImGui.Text("Auto-Decline Incoming Friend Requests");
        ImGui.Separator();

        if (ImGui.Checkbox("Enable Auto-Decline", ref Program.autoDecline))
        {
            Program.config.AutoDeclineFriendRequests = Program.autoDecline;
            Program.SaveConfig();
            if (Program.autoDecline) SchedulerService.StartAutoDeclineChecker();
            else { Program.autoDeclineCts?.Cancel(); Program.autoDeclineCts = null; }
        }
        ImGui.SameLine();
        ImGui.TextDisabled("(checks every 60s)");

        if (Program.autoDecline)
        {
            ImGui.Indent();
            if (ImGui.Checkbox("Only decline requests from strangers (not current friends or people you've played with)", ref Program.onlyStrangers))
            {
                Program.config.AutoDeclineOnlyFromStrangers = Program.onlyStrangers;
                Program.SaveConfig();
            }

            ImGui.TextDisabled("Minimum time together to count as 'known':");
            ImGui.SameLine();
            int minMin = Program.config.MinTimeTogetherMinutes;
            ImGui.SetNextItemWidth(80);
            if (ImGui.SliderInt("##mintime", ref minMin, 0, 120, "%d min"))
            {
                Program.config.MinTimeTogetherMinutes = minMin;
                Program.SaveConfig();
            }

            if (ImGui.Checkbox("Automatically send friend request back after decline", ref Program.autoSendBack))
            {
                Program.config.AutoSendRequestBack = Program.autoSendBack;
                Program.SaveConfig();
            }
            ImGui.Unindent();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (ImGui.Button("Refresh Requests"))
            _ = Program.RefreshFriendRequests();

        ImGui.SameLine();
        ImGui.TextDisabled($"Pending: {Program.incomingFriendRequests.Count}");

        ImGui.Spacing();

        float listH = sh - ImGui.GetCursorPosY() - 40;
                ImGui.BeginChild("##reqlist", new Vector2(-1, listH), ImGuiChildFlags.Borders);

            if (Program.incomingFriendRequests.Count == 0)
            {
                ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), "No incoming friend requests.");
            }
            else
            {
                ImGui.TextDisabled($"{"Sender",-32} {"Date",-12} Actions");
                ImGui.Separator();

                foreach (var req in Program.incomingFriendRequests.ToList())
                {
                    string senderName = req.SenderUsername ?? req.SenderUserId ?? "Unknown";
                    string date = req.CreatedAt.ToString("yyyy-MM-dd");

                    ImGui.Text($"{senderName,-32} {date,-12}");
                    ImGui.SameLine();

                    ImGui.PushID(req.Id);
                    if (ImGui.SmallButton("Decline"))
                    {
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await Program.api.DeclineFriendRequestAsync(req.Id);
                                await Program.RefreshFriendRequests();
                                Program.ShowToast("Declined", $"Declined request from {senderName}");
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"[FriendRequests] Manual decline failed: {ex.Message}");
                            }
                        });
                    }
                    ImGui.SameLine();
                    if (ImGui.SmallButton("Send Request Back"))
                    {
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await Program.api.SendFriendRequestAsync(req.SenderUserId ?? "");
                                await Program.api.DeclineFriendRequestAsync(req.Id);
                                await Program.RefreshFriendRequests();
                                Program.ShowToast("Request Sent", $"Sent friend request to {senderName}");
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"[FriendRequests] Send-back failed: {ex.Message}");
                            }
                        });
                    }
                    ImGui.PopID();
                }
            }

            ImGui.EndChild();
    }
}
