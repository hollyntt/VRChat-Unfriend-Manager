using VRCUFM.AppSystem;
using VRCUFM.VRChat;

namespace VRCUFM.Core;

public static class AutoGroupService
{
    static CancellationTokenSource? _cts;
    static int _running;
    static DateTime _nextAllowedRunUtc = DateTime.MinValue;
    static readonly TimeSpan MinimumRunInterval = TimeSpan.FromMinutes(2);
    public static volatile string LastRunStatus = "Not run yet";

    public static void Start()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        _ = Task.Run(async () =>
        {
            try { await Task.Delay(15000, token); } catch { return; }
            while (!token.IsCancellationRequested && Program.config.AutoGroupEnabled)
            {
                try { await RunOnceAsync(token); }
                catch (Exception ex) { Console.WriteLine("[AutoGroup] " + ex.Message); }

                int mins = Math.Clamp(Program.config.AutoGroupIntervalMinutes, 5, 240);
                try { await Task.Delay(TimeSpan.FromMinutes(mins), token); }
                catch (OperationCanceledException) { break; }
            }
        }, token);
    }

    public static void Stop()
    {
        _cts?.Cancel();
        _cts = null;
    }

    private static async Task RunActivityTierGroupingAsync(CancellationToken token)
    {
        var (allIds, byGroup) = await Program.api.GetFavoritesDetailedAsync();
        int moved = 0, failed = 0;
        var mutations = new List<(SafeLimitedUserFriend Friend, int Score, string? Target, string? Current)>();
        const int yellowMinimumScore = 20;
        const int greenMinimumScore = 50;
        const int thirdGroupMinimumScore = 60;
        const int fourthGroupMinimumScore = 70;
        const string yellowGroup = "group_0";
        const string greenGroup = "group_1";
        const string thirdGroup = "group_2";
        const string fourthGroup = "group_3";
        Program.config.AutoGroupPlacements ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var friend in Program.friends.ToList())
        {
            if (token.IsCancellationRequested) break;
            int score = FriendsManager.CalculateFriendScore(friend, allIds);
            string? target = score >= fourthGroupMinimumScore
                ? fourthGroup
                : score >= thirdGroupMinimumScore
                    ? thirdGroup
                    : score >= greenMinimumScore
                        ? greenGroup
                        : score >= yellowMinimumScore
                            ? yellowGroup
                            : null;
            string placement = target switch
            {
                fourthGroup => "Group 4 (70+)",
                thirdGroup => "Group 3 (60-69)",
                greenGroup => "Group 2 (green)",
                yellowGroup => "Group 1 (yellow)",
                _ => "Ungrouped (red)"
            };
            var current = byGroup.FirstOrDefault(pair => pair.Value.Contains(friend.Id)).Key;
            if (target != null && byGroup.TryGetValue(target, out var targetMembers) && targetMembers.Contains(friend.Id))
                current = target;
            // Prefer the live VRChat group, while retaining the last known placement
            // for this account when favorite metadata is temporarily incomplete.
            var remembered = Program.config.AutoGroupPlacements.TryGetValue(friend.Id, out var saved)
                ? saved
                : null;
            var effectiveCurrent = string.IsNullOrWhiteSpace(current) ? remembered : current;
            if (target == null && !allIds.Contains(friend.Id))
            {
                Program.config.AutoGroupPlacements.Remove(friend.Id);
                continue;
            }
            if (target != null && string.Equals(effectiveCurrent, target, StringComparison.OrdinalIgnoreCase))
            {
                Program.config.AutoGroupPlacements[friend.Id] = target;
                continue;
            }
            mutations.Add((friend, score, target, effectiveCurrent));
        }

        foreach (var mutation in mutations)
        {
            if (token.IsCancellationRequested) break;
            var friend = mutation.Friend;
            try
            {
                if (mutation.Target == null)
                {
                    await Program.api.RemoveFriendFavoriteAsync(friend.Id);
                    allIds.Remove(friend.Id);
                    foreach (var group in byGroup.Values) group.Remove(friend.Id);
                }
                else
                {
                    if (allIds.Contains(friend.Id))
                        await Program.api.RemoveFriendFavoriteAsync(friend.Id);
                    await Program.api.AddFriendFavoriteAsync(friend.Id, mutation.Target);
                    allIds.Add(friend.Id);
                    foreach (var group in byGroup.Values) group.Remove(friend.Id);
                    if (!byGroup.TryGetValue(mutation.Target, out var targetSet))
                        byGroup[mutation.Target] = targetSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    targetSet.Add(friend.Id);
                }
                moved++;
                if (mutation.Target == null)
                    Program.config.AutoGroupPlacements.Remove(friend.Id);
                else
                    Program.config.AutoGroupPlacements[friend.Id] = mutation.Target;
                Program.SaveConfig();
                var placement = mutation.Target switch
                {
                    fourthGroup => "Group 4 (70+)",
                    thirdGroup => "Group 3 (60-69)",
                    greenGroup => "Group 2 (green)",
                    yellowGroup => "Group 1 (yellow)",
                    _ => "Ungrouped (red)"
                };
                Console.WriteLine($"[AutoGroup] {friend.DisplayName}: score {mutation.Score} -> {placement}");
                _ = DiscordWebhookService.NotifyAutoGroupPlacementAsync(friend.DisplayName, mutation.Score, placement);
                // A shorter delay preserves safety while making normal runs responsive.
                await Task.Delay(TimeSpan.FromSeconds(3), token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                failed++;
                if (IsRateLimited(ex))
                {
                    _nextAllowedRunUtc = DateTime.UtcNow + TimeSpan.FromMinutes(10);
                    LastRunStatus = "VRChat rate-limited this run; waiting 10 minutes before retrying.";
                    break;
                }
                Console.WriteLine($"[AutoGroup] Tier update failed for {friend.DisplayName}: {ex.Message}");
            }
        }

        Program.favorites = allIds;
        Program.favByGroup = byGroup;
        LastRunStatus = $"Score placements: {moved} moved" + (failed > 0 ? $", {failed} failed" : "");
        if (moved > 0)
        {
            Program.status = "Auto-Group: " + LastRunStatus;
            Program.ShowToast("Auto-Group", LastRunStatus);
            _ = DiscordWebhookService.NotifyAutoGroupAsync(LastRunStatus);
            _ = OscNotificationService.NotifyAutoGroupAsync(LastRunStatus);
        }
    }

    private static bool IsRateLimited(Exception ex) =>
        ex.Message.Contains("429", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("too quickly", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("rate limit", StringComparison.OrdinalIgnoreCase);

    public static async Task RunOnceAsync(CancellationToken token = default)
    {
        if (DateTime.UtcNow < _nextAllowedRunUtc)
        {
            var wait = _nextAllowedRunUtc - DateTime.UtcNow;
            LastRunStatus = $"Cooling down after a recent run ({Math.Ceiling(wait.TotalSeconds)}s remaining).";
            return;
        }

        if (System.Threading.Interlocked.Exchange(ref _running, 1) == 1)
        {
            LastRunStatus = "A grouping run is already in progress.";
            return;
        }

        _nextAllowedRunUtc = DateTime.UtcNow + MinimumRunInterval;

        try
        {
            if (!Program.isLoggedIn)
            {
                LastRunStatus = "Skipped: log in before running grouping rules.";
                return;
            }

            // Auto-grouping always uses the activity-tier classifier. The legacy
            // arbitrary rule stack is intentionally no longer used for automatic runs.
            await RunActivityTierGroupingAsync(token);
            return;

#pragma warning disable CS0162
            var rules = (Program.config.AutoGroupRules ?? new List<AutoGroupRule>())
                .Where(r => r != null && r.Enabled).ToList();
            if (rules.Count == 0)
            {
                LastRunStatus = "No enabled grouping rules.";
                return;
            }

            int added = 0, removed = 0, moved = 0, cleared = 0;
            int matchedFriends = 0, skippedFriends = 0, failedActions = 0;
            int rulesWithoutCriteria = rules.Count(rule =>
                !string.Equals(rule.Action?.Trim(), "clear_all", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(rule.Action?.Trim(), "clear_group", StringComparison.OrdinalIgnoreCase) &&
                !rule.UseHighScore && !rule.UseLowScore && !rule.UseInactive &&
                !rule.UseActive && !rule.UseHighTime && !rule.UseLowTime);
            LastRunStatus = $"Checking {Program.friends.Count} friends against {rules.Count} enabled rules...";

            // Group-level actions first (clear_group / clear_all) — ignore per-friend criteria
            foreach (var rule in rules)
            {
                if (token.IsCancellationRequested) break;
                string action = (rule.Action ?? "add").Trim().ToLowerInvariant();

                if (action == "clear_all")
                {
                    try
                    {
                        await Program.api.ClearAllFriendFavoritesAsync();
                        cleared++;
                        Console.WriteLine($"[AutoGroup] Cleared ALL friend favorites ({rule.Name})");
                        try { await Task.Delay(1000, token); } catch { break; }
                    }
                    catch (Exception ex)
                    {
                        failedActions++;
                        Console.WriteLine($"[AutoGroup] clear_all failed: {ex.Message}");
                    }
                    continue;
                }

                if (action == "clear_group")
                {
                    string tag = string.IsNullOrWhiteSpace(rule.TargetGroupTag) ? "group_0" : rule.TargetGroupTag.Trim();
                    try
                    {
                        await Program.api.ClearFriendFavoriteGroupAsync(tag);
                        cleared++;
                        Console.WriteLine($"[AutoGroup] Cleared group {tag} ({rule.Name})");
                        try { await Task.Delay(1000, token); } catch { break; }
                    }
                    catch (Exception ex)
                    {
                        failedActions++;
                        Console.WriteLine($"[AutoGroup] clear_group failed: {ex.Message}");
                    }
                    continue;
                }
            }

            var (allIds, byGroup) = await Program.api.GetFavoritesDetailedAsync();
            Program.favorites = allIds;
            Program.favByGroup = byGroup;

            foreach (var friend in Program.friends.ToList())
            {
                if (token.IsCancellationRequested) break;

                foreach (var rule in rules)
                {
                    string action = (rule.Action ?? "add").Trim().ToLowerInvariant();
                    if (action is "clear_all" or "clear_group")
                        continue; // already handled

                    if (!FriendsManager.MatchesAutoGroupRule(friend, rule, Program.favorites))
                        continue;

                    matchedFriends++;
                    string tag = string.IsNullOrWhiteSpace(rule.TargetGroupTag) ? "group_0" : rule.TargetGroupTag.Trim();
                    bool isFav = allIds.Contains(friend.Id);
                    bool alreadyInTarget = byGroup.TryGetValue(tag, out var set) && set.Contains(friend.Id);

                    try
                    {
                        if (action == "unfavorite")
                        {
                            if (!isFav)
                            {
                                skippedFriends++;
                                break;
                            }
                            await Program.api.RemoveFriendFavoriteAsync(friend.Id);
                            allIds.Remove(friend.Id);
                            foreach (var kv in byGroup.Values) kv.Remove(friend.Id);
                            removed++;
                            Console.WriteLine($"[AutoGroup] Unfavorited {friend.DisplayName} ({rule.Name})");
                            try { await Task.Delay(1500, token); } catch { break; }
                        }
                        else if (action == "move")
                        {
                            if (alreadyInTarget)
                            {
                                skippedFriends++;
                                break;
                            }
                            if (isFav)
                            {
                                await Program.api.RemoveFriendFavoriteAsync(friend.Id);
                                allIds.Remove(friend.Id);
                                foreach (var kv in byGroup.Values) kv.Remove(friend.Id);
                                try { await Task.Delay(800, token); } catch { break; }
                            }
                            await Program.api.AddFriendFavoriteAsync(friend.Id, tag);
                            allIds.Add(friend.Id);
                            if (!byGroup.ContainsKey(tag))
                                byGroup[tag] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            byGroup[tag].Add(friend.Id);
                            moved++;
                            Console.WriteLine($"[AutoGroup] Moved {friend.DisplayName} -> {tag} ({rule.Name})");
                            try { await Task.Delay(1500, token); } catch { break; }
                        }
                        else // add
                        {
                            if (alreadyInTarget || (rule.SkipIfAlreadyFavorited && isFav))
                            {
                                skippedFriends++;
                                break;
                            }

                            await Program.api.AddFriendFavoriteAsync(friend.Id, tag);
                            allIds.Add(friend.Id);
                            if (!byGroup.ContainsKey(tag))
                                byGroup[tag] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            byGroup[tag].Add(friend.Id);
                            added++;
                            Console.WriteLine($"[AutoGroup] Added {friend.DisplayName} -> {tag} ({rule.Name})");
                            try { await Task.Delay(1500, token); } catch { break; }
                        }
                    }
                    catch (Exception ex)
                    {
                        failedActions++;
                        Console.WriteLine($"[AutoGroup] Failed {friend.DisplayName}: {ex.Message}");
                    }
                    break;
                }
            }

            Program.favorites = allIds;
            Program.favByGroup = byGroup;

            int total = added + removed + moved + cleared;
            var changeParts = new List<string>();
            if (total > 0)
            {
                if (added > 0) changeParts.Add($"{added} added");
                if (moved > 0) changeParts.Add($"{moved} moved");
                if (removed > 0) changeParts.Add($"{removed} unfavorited");
                if (cleared > 0) changeParts.Add($"{cleared} group clear(s)");
            }
            string changeSummary = total > 0 ? string.Join(", ", changeParts) : "no changes";
            var summaryParts = new List<string> { changeSummary, $"{matchedFriends} matched" };
            if (skippedFriends > 0) summaryParts.Add($"{skippedFriends} already satisfied");
            if (failedActions > 0) summaryParts.Add($"{failedActions} failed");
            LastRunStatus = string.Join(", ", summaryParts);

            if (matchedFriends == 0 && rulesWithoutCriteria > 0)
                LastRunStatus += $". {rulesWithoutCriteria} enabled rule(s) have no filters selected.";

            if (total > 0)
            {
                Program.ShowToast("Auto-Group", changeSummary);
                Program.status = "Auto-Group: " + changeSummary;
                _ = DiscordWebhookService.NotifyAutoGroupAsync(changeSummary);
                _ = OscNotificationService.NotifyAutoGroupAsync(changeSummary);
            }
        }
        catch (Exception ex)
        {
            // Rate limits require a longer pause before another run is allowed.
            if (ex.Message.Contains("429", StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("too quickly", StringComparison.OrdinalIgnoreCase))
            {
                _nextAllowedRunUtc = DateTime.UtcNow + TimeSpan.FromMinutes(10);
                LastRunStatus = "VRChat rate-limited this run; waiting 10 minutes before retrying.";
            }
            else
            {
                LastRunStatus = $"Run failed: {ex.Message}";
            }
            Console.WriteLine("[AutoGroup] Run failed: " + ex);
        }
        finally
        {
            System.Threading.Interlocked.Exchange(ref _running, 0);
        }
    }
}
