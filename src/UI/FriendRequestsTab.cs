using System.Numerics;
using ImGuiNET;
using Raylib_cs;
using VRChat.API.Model;
using VRCUFM.AppSystem;
using VRCUFM.Core;
using VRCUFM.VRChat;

namespace VRCUFM.UI;

public static class FriendRequestsTab
{
    static string _searchLive = "";
    static string _searchApplied = "";
    static float _searchCd;
    static bool _filterMetBefore;
    static bool _filterHiddenOnly;

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

        // ── Search (debounced) + filters ─────────────────────────────────
        ImGui.SetNextItemWidth(sw * 0.35f);
        ImGui.InputText("Search##reqsearch", ref _searchLive, 128);
        if (!string.IsNullOrEmpty(_searchLive))
        {
            ImGui.SameLine();
            if (ImGui.SmallButton("x##clrreq"))
            {
                _searchLive = "";
                _searchApplied = "";
                _searchCd = 0;
            }
        }

        // Debounce: apply only after 150 ms of no typing
        if (_searchLive != _searchApplied)
        {
            _searchCd += ImGui.GetIO().DeltaTime;
            if (_searchCd > 0.15f)
            {
                _searchApplied = _searchLive;
                _searchCd = 0;
            }
        }
        else
            _searchCd = 0;

        ImGui.SameLine(0, 16);
        ImGui.Checkbox("Met before", ref _filterMetBefore);
        ImGui.SameLine();
        ImGui.Checkbox("Hidden only", ref _filterHiddenOnly);

        ImGui.Spacing();
        if (ImGui.Button("Refresh Requests"))
        {
            FriendRequestEnricher.ClearHiddenCache();
            _ = Program.RefreshFriendRequests();
        }

        ImGui.SameLine();
        ImGui.TextDisabled($"Pending: {Program.incomingFriendRequests.Count}");

        ImGui.Spacing();

        // Build filtered list once per frame from applied search
        var filtered = FilterRequests(Program.incomingFriendRequests);

        float listH = sh - ImGui.GetCursorPosY() - 40;
        ImGui.BeginChild("##reqlist", new Vector2(-1, listH), ImGuiChildFlags.Borders);

        if (filtered.Count == 0)
        {
            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f),
                Program.incomingFriendRequests.Count == 0
                    ? "No incoming friend requests."
                    : "No requests match the current filters.");
        }
        else
        {
            ImGui.TextDisabled($"{"Sender",-28} {"Badge",-12} {"Date",-12} Actions");
            ImGui.Separator();

            foreach (var req in filtered)
            {
                string senderName = req.SenderUsername ?? req.SenderUserId ?? "Unknown";
                string date = req.CreatedAt.ToString("yyyy-MM-dd");
                string badge = FriendRequestEnricher.MeetBadge(req.SenderUserId);
                bool isHidden = FriendRequestEnricher.LooksHidden(req);
                var badgeCol = badge == "New"
                    ? new Vector4(0.55f, 0.55f, 0.65f, 1f)
                    : badge.StartsWith("Met once")
                        ? new Vector4(0.4f, 0.85f, 0.55f, 1f)
                        : new Vector4(0.65f, 0.5f, 0.95f, 1f);

                ImGui.PushID(req.Id);

                if (isHidden)
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.5f, 0.5f, 0.5f, 0.7f));

                ImGui.Text($"{senderName,-28}");
                ImGui.SameLine();
                ImGui.TextColored(badgeCol, $"{badge,-12}");
                ImGui.SameLine();
                ImGui.Text($"{date,-12}");
                if (isHidden)
                {
                    ImGui.SameLine();
                    ImGui.TextDisabled("[Hidden]");
                }
                ImGui.SameLine();

                if (!isHidden)
                {
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
                }
                if (ImGui.SmallButton("Send Request Back"))
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            try
                            {
                                await Program.api.SendFriendRequestAsync(req.SenderUserId ?? "");
                            }
                            catch (Exception ex) when (ex.Message.Contains("created_at") || ex.Message.Contains("Required property"))
                            {
                                Console.WriteLine($"[FriendRequests] Send-back OK (ignored SDK parse error): {senderName}");
                            }
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

                if (isHidden)
                    ImGui.PopStyleColor();

                ImGui.PopID();
            }
        }

        ImGui.EndChild();
    }

    static List<Notification> FilterRequests(List<Notification> source)
    {
        IEnumerable<Notification> q = source;

        if (!string.IsNullOrWhiteSpace(_searchApplied))
        {
            var needle = _searchApplied.Trim().ToLowerInvariant();
            q = q.Where(r =>
                (r.SenderUsername ?? "").ToLowerInvariant().Contains(needle) ||
                (r.SenderUserId ?? "").ToLowerInvariant().Contains(needle));
        }

        if (_filterMetBefore)
            q = q.Where(r => FriendRequestEnricher.IsKnownFromMeets(r.SenderUserId));

        if (_filterHiddenOnly)
            q = q.Where(FriendRequestEnricher.LooksHidden);

        return q.ToList();
    }
}