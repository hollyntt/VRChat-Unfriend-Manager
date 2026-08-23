using VRCUFM.AppSystem;
using VRCUFM.VRChat;

namespace VRCUFM.Core;

public static class AutoGroupService
{
    static CancellationTokenSource? _cts;
    static int _running;

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

    public static async Task RunOnceAsync(CancellationToken token = default)
    {
        if (System.Threading.Interlocked.Exchange(ref _running, 1) == 1) return;
        try
        {
            if (!Program.isLoggedIn) return;
            var rules = (Program.config.AutoGroupRules ?? new List<AutoGroupRule>())
                .Where(r => r != null && r.Enabled).ToList();
            if (rules.Count == 0) return;

            int added = 0, removed = 0, moved = 0, cleared = 0;

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

                    string tag = string.IsNullOrWhiteSpace(rule.TargetGroupTag) ? "group_0" : rule.TargetGroupTag.Trim();
                    bool isFav = allIds.Contains(friend.Id);
                    bool alreadyInTarget = byGroup.TryGetValue(tag, out var set) && set.Contains(friend.Id);

                    try
                    {
                        if (action == "unfavorite")
                        {
                            if (!isFav) break;
                            await Program.api.RemoveFriendFavoriteAsync(friend.Id);
                            allIds.Remove(friend.Id);
                            foreach (var kv in byGroup.Values) kv.Remove(friend.Id);
                            removed++;
                            Console.WriteLine($"[AutoGroup] Unfavorited {friend.DisplayName} ({rule.Name})");
                            try { await Task.Delay(1500, token); } catch { break; }
                        }
                        else if (action == "move")
                        {
                            if (alreadyInTarget) break;
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
                            if (alreadyInTarget) break;
                            if (rule.SkipIfAlreadyFavorited && isFav) break;

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
                        Console.WriteLine($"[AutoGroup] Failed {friend.DisplayName}: {ex.Message}");
                    }
                    break;
                }
            }

            Program.favorites = allIds;
            Program.favByGroup = byGroup;

            int total = added + removed + moved + cleared;
            if (total > 0)
            {
                var parts = new List<string>();
                if (added > 0) parts.Add($"{added} added");
                if (moved > 0) parts.Add($"{moved} moved");
                if (removed > 0) parts.Add($"{removed} unfavorited");
                if (cleared > 0) parts.Add($"{cleared} group clear(s)");
                var msg = string.Join(", ", parts);
                Program.ShowToast("Auto-Group", msg);
                Program.status = "Auto-Group: " + msg;
                _ = DiscordWebhookService.NotifyAutoGroupAsync(msg);
                _ = OscNotificationService.NotifyAutoGroupAsync(msg);
            }
        }
        finally
        {
            System.Threading.Interlocked.Exchange(ref _running, 0);
        }
    }
}
