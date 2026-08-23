using System.Diagnostics;
using VRChat.API.Model;
using VRCUFM.AppSystem;
using VRCUFM.Core;
using VRCUFM.VRChat;

namespace VRCUFM.Core;

public static class SchedulerService
{
    public static void StartAutoScheduler()
    {
        Program.autoCts?.Cancel();
        Program.autoCts = new CancellationTokenSource();
        var token = Program.autoCts.Token;

        // ── Scheduled-time loop ─────────────────────────────────────────────
        _ = Task.Run(async () =>
        {
            if (Program.config.AutoUnfriendEnabled && Program.config.AutoUnfriendLastRun != null)
            {
                var lastExpected = GetLastExpectedRun();
                bool missedRun = lastExpected.HasValue && lastExpected.Value < DateTime.Now && Program.config.AutoUnfriendLastRun < lastExpected.Value;

                if (missedRun)
                {
                    Console.WriteLine($"[SCHEDULER] Missed run detected (expected {lastExpected:g}), running now");
                    await RunAutoUnfriendAsync(token);
                    if (token.IsCancellationRequested) return;
                }
            }

            while (!token.IsCancellationRequested && Program.config.AutoUnfriendEnabled)
            {
                var target = GetNextScheduledRun();
                if (!target.HasValue) break;
                if (target.Value <= DateTime.Now)
                {
                    if (Program.config.AutoUnfriendScheduleType == 3)
                    {
                        // One-time past date — disable and stop
                        Program.config.AutoUnfriendEnabled = false;
                        Program.SaveConfig();
                        break;
                    }
                    // For recurring, if we're somehow past target, just run now and recalc
                }
                else
                {
                    try { await Task.Delay(target.Value - DateTime.Now, token); }
                    catch (OperationCanceledException) { break; }
                }

                if (token.IsCancellationRequested) break;

                await RunAutoUnfriendAsync(token);

                if (Program.config.AutoUnfriendScheduleType == 3)
                {
                    Program.config.AutoUnfriendEnabled = false;
                    Program.SaveConfig();
                    break;
                }
            }
        }, token);

        // ── Friend-limit watcher loop ───────────────────────────────────────
        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested && Program.config.AutoUnfriendEnabled)
            {
                int intervalMin = Math.Clamp(Program.config.FriendLimitPollIntervalMinutes, 1, 60);
                try { await Task.Delay(TimeSpan.FromMinutes(intervalMin), token); }
                catch (OperationCanceledException) { break; }

                if (token.IsCancellationRequested) break;
                if (!Program.config.FriendLimitTriggerEnabled) continue;

                int count = Program.friends.Count;
                if (count >= Program.config.FriendLimitThreshold)
                {
                    Console.WriteLine($"[LIMIT-TRIGGER] Friend count {count} >= threshold {Program.config.FriendLimitThreshold}, firing auto-unfriend");
                    Program.ShowToast("Friend Limit Reached", $"{count} friends — running auto-unfriend now.");
                    await RunAutoUnfriendAsync(token);
                }
            }
        }, token);
    }

    public static async Task RunAutoUnfriendAsync(CancellationToken token)
    {
        try
        {
            await Program.Refresh();

            List<SafeLimitedUserFriend> pool = Program.config.AutoUnfriendMode switch
            {
                0 => Program.friends.ToList(),
                1 => Program.shown.ToList(),
                2 => Program.selected.Count > 0
                    ? Program.selected.Where(i => i < Program.shown.Count).Select(i => Program.shown[i]).ToList()
                    : new List<SafeLimitedUserFriend>(),
                _ => Program.shown.ToList()
            };
            List<SafeLimitedUserFriend> toUnfriend = pool
                .Where(f => FriendsManager.MatchesAutoUnfriendCriteria(f, Program.config, Program.favorites))
                .ToList();

            if (toUnfriend.Count == 0)
            {
                Program.ShowToast("Auto-Unfriend", "Nothing to unfriend");
                Program.config.AutoUnfriendLastRun = DateTime.Now;
                Program.SaveConfig();
                return;
            }

            Program.autoConfirmTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Program.pendingAutoCount = toUnfriend.Count;
            Program.autoConfirmDeadline = DateTime.Now.AddSeconds(15);
            Program.pendingAutoConfirm = true;

            var confirmTask = Program.autoConfirmTcs.Task;

            bool confirmed = false;
            try
            {
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                confirmed = await confirmTask.WaitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException)
            {
                confirmed = true;
                lock (Program.autoConfirmLock)
                {
                    Program.autoConfirmTcs?.TrySetResult(true);
                    Program.autoConfirmTcs = null;
                }
                Program.pendingAutoConfirm = false;
            }

            if (!confirmed) { Program.ShowToast("Auto-Unfriend", "Cancelled"); return; }

            foreach (var u in toUnfriend)
            {
                if (token.IsCancellationRequested) break;
                try
                {
                    await Program.api.UnfriendAsync(u.Id);
                    FriendsManager.LogUnfriend(u, "auto");
                    Program.ShowUnfriendToast(u.DisplayName);
                    await Task.Delay(Random.Shared.Next(7000, 13000), token);
                }
                catch { }
            }

            Program.ShowToast("Auto-Unfriend", $"Removed {toUnfriend.Count} friends");
            Program.config.AutoUnfriendLastRun = DateTime.Now;
            Program.SaveConfig();
            await Program.Refresh();
        }
        catch { }
    }

    public static void StartAutoDeclineChecker()
    {
        Program.autoDeclineCts?.Cancel();
        Program.autoDeclineCts = new CancellationTokenSource();
        var token = Program.autoDeclineCts.Token;

        _ = Task.Run(async () =>
        {
            Console.WriteLine("[AutoDecline] Checker started");

            while (!token.IsCancellationRequested)
            {
                if (!Program.isLoggedIn)
                {
                    Console.WriteLine("[AutoDecline] Waiting for login...");
                    try { await Task.Delay(5000, token); } catch (OperationCanceledException) { break; }
                    continue;
                }

                if (Program.config.AutoDeclineFriendRequests)
                {
                    try
                    {
                        var requests = await Program.api.GetIncomingFriendRequestsAsync();
                        Program.incomingFriendRequests = requests;

                        if (requests.Count > 0)
                        {
                            Console.WriteLine($"[AutoDecline] Processing {requests.Count} request(s)");

                            var freshFriends = await Program.api.GetAllFriendsAsync();
                            Program.friends = freshFriends;
                            var friendIds = new HashSet<string>(freshFriends.Select(f => f.Id), StringComparer.OrdinalIgnoreCase);

                            // Merge both time databases for best coverage
                            Dictionary<string, long>? timeMap = null;
                            if (Program.config.AutoDeclineOnlyFromStrangers)
                            {
                                timeMap = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
                                if (VRCNextDataService.IsAvailable)
                                {
                                    var nextMap = VRCNextDataService.LoadTimeSpentSeconds();
                                    foreach (var kv in nextMap)
                                    {
                                        timeMap.TryGetValue(kv.Key, out var existing);
                                        timeMap[kv.Key] = existing + kv.Value;
                                    }
                                }
                                if (VRCXDataService.IsAvailable)
                                {
                                    var xMap = VRCXDataService.LoadTimeSpentSeconds();
                                    foreach (var kv in xMap)
                                    {
                                        timeMap.TryGetValue(kv.Key, out var existing);
                                        timeMap[kv.Key] = existing + kv.Value;
                                    }
                                }
                                if (timeMap.Count == 0) timeMap = null;
                            }

                            long minTimeSeconds = Program.config.MinTimeTogetherMinutes * 60L;
                            int declined = 0;
                            foreach (var req in requests)
                            {
                                if (token.IsCancellationRequested) break;

                                string senderId = req.SenderUserId ?? "";
                                string senderName = req.SenderUsername ?? senderId;

                                if (string.IsNullOrEmpty(senderId))
                                {
                                    Console.WriteLine($"[AutoDecline] Skipping request with empty sender ID");
                                    continue;
                                }

                                if (Program.config.AutoDeclineOnlyFromStrangers)
                                {
                                    bool isKnown = await Program.api.IsKnownPlayerAsync(senderId, friendIds, timeMap, minTimeSeconds);
                                    if (isKnown)
                                    {
                                        Console.WriteLine($"[AutoDecline] Skipping {senderName} — known player");
                                        continue;
                                    }
                                }

                                try
                                {
                                    await Program.api.DeclineFriendRequestAsync(req.Id);
                                    declined++;
                                    Console.WriteLine($"[AutoDecline] Declined request from {senderName}");

                                    if (Program.config.AutoSendRequestBack)
                                    {
                                        try
                                        {
                                            await Program.api.SendFriendRequestAsync(senderId);
                                            Console.WriteLine($"[AutoDecline] Sent request back to {senderName}");
                                        }
                                        catch (Exception ex)
                                        {
                                            Console.WriteLine($"[AutoDecline] Failed to send request back to {senderName}: {ex.Message}");
                                        }
                                    }

                                    await Task.Delay(2500, token);
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"[AutoDecline] Failed to decline {senderName}: {ex.Message}");
                                }
                            }

                            if (declined > 0)
                            {
                                Program.incomingFriendRequests = await Program.api.GetIncomingFriendRequestsAsync();
                                Program.ShowToast("Auto-Decline", $"Declined {declined} friend request(s)");
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[AutoDecline] Cycle error: {ex.Message}");
                    }
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(60), token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            Console.WriteLine("[AutoDecline] Checker stopped");
        }, token);
    }

    public static DateTime? GetNextScheduledRun()
    {
        var now = DateTime.Now;
        int h = Program.config.AutoUnfriendHour, mi = Program.config.AutoUnfriendMinute;
        try
        {
            switch (Program.config.AutoUnfriendScheduleType)
            {
                case 0:
                    var daily = new DateTime(now.Year, now.Month, now.Day, h, mi, 0);
                    if (daily <= now) daily = daily.AddDays(1);
                    return daily;

                case 1:
                    int targetWeekday = Math.Clamp(Program.config.AutoUnfriendMonthDay, 0, 6);
                    var weekly = new DateTime(now.Year, now.Month, now.Day, h, mi, 0);
                    int daysToAdd = (targetWeekday - (int)weekly.DayOfWeek + 7) % 7;
                    if (daysToAdd == 0 && weekly.TimeOfDay <= now.TimeOfDay)
                        daysToAdd = 7;
                    return weekly.AddDays(daysToAdd);

                case 2:
                    int mday = Math.Clamp(Program.config.AutoUnfriendMonthDay, 1, 28);
                    var monthly = new DateTime(now.Year, now.Month, mday, h, mi, 0);
                    if (monthly <= now) monthly = monthly.AddMonths(1);
                    return monthly;

                case 3:
                    var once = new DateTime(Program.config.AutoUnfriendYear, Program.config.AutoUnfriendMonth, Program.config.AutoUnfriendDay, h, mi, 0);
                    if (once < now) return null; // Past one-time date is invalid
                    return once;

                default: return null;
            }
        }
        catch { return null; }
    }

    public static DateTime? GetLastExpectedRun()
    {
        var now = DateTime.Now;
        int h = Program.config.AutoUnfriendHour, mi = Program.config.AutoUnfriendMinute;
        try
        {
            switch (Program.config.AutoUnfriendScheduleType)
            {
                case 0:
                    var daily = new DateTime(now.Year, now.Month, now.Day, h, mi, 0);
                    if (daily > now) daily = daily.AddDays(-1);
                    return daily;

                case 1:
                    int targetWeekday = Math.Clamp(Program.config.AutoUnfriendMonthDay, 0, 6);
                    var weekly = new DateTime(now.Year, now.Month, now.Day, h, mi, 0);
                    int daysBack = ((int)weekly.DayOfWeek - targetWeekday + 7) % 7;
                    if (daysBack == 0 && weekly.TimeOfDay > now.TimeOfDay)
                        daysBack = 7;
                    return weekly.AddDays(-daysBack);

                case 2:
                    int mday = Math.Clamp(Program.config.AutoUnfriendMonthDay, 1, 28);
                    var monthly = new DateTime(now.Year, now.Month, mday, h, mi, 0);
                    if (monthly > now) monthly = monthly.AddMonths(-1);
                    return monthly;

                case 3:
                    return new DateTime(Program.config.AutoUnfriendYear, Program.config.AutoUnfriendMonth, Program.config.AutoUnfriendDay, h, mi, 0);

                default: return null;
            }
        }
        catch { return null; }
    }
}
