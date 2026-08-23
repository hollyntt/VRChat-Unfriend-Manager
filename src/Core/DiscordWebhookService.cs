using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace VRCUFM.Core;

public static class DiscordWebhookService
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public static bool IsConfigured =>
        Program.config.DiscordWebhookEnabled
        && !string.IsNullOrWhiteSpace(Program.config.DiscordWebhookUrl)
        && Program.config.DiscordWebhookUrl.StartsWith("https://discord.com/api/webhooks/", StringComparison.OrdinalIgnoreCase);

    public static Task NotifyUnfriendAsync(string displayName, string? reason = null, int? remaining = null)
    {
        if (!IsConfigured || !Program.config.DiscordNotifyUnfriend) return Task.CompletedTask;
        var fields = new List<(string, string, bool)>
        {
            ("User", displayName, true),
        };
        if (!string.IsNullOrEmpty(reason))
            fields.Add(("Reason", reason, true));
        if (remaining.HasValue)
            fields.Add(("Friends left", remaining.Value.ToString(), true));
        return SendEmbedAsync("Unfriended", $"Removed **{Escape(displayName)}**", 0xE74C3C, fields);
    }

    public static Task NotifyBulkUnfriendAsync(int count)
    {
        if (!IsConfigured || !Program.config.DiscordNotifyUnfriend) return Task.CompletedTask;
        return SendEmbedAsync(
            "Bulk unfriend",
            $"Removed **{count}** friend(s).",
            0xE74C3C,
            new[] { ("Count", count.ToString(), true) });
    }

    public static Task NotifyAutoGroupAsync(string summary)
    {
        if (!IsConfigured || !Program.config.DiscordNotifyAutoGroup) return Task.CompletedTask;
        return SendEmbedAsync("Auto-group", summary, 0x9B59B6, null);
    }

    public static Task NotifyLoginAsync(string username)
    {
        if (!IsConfigured || !Program.config.DiscordNotifyLogin) return Task.CompletedTask;
        return SendEmbedAsync("Signed in", $"Logged in as **{Escape(username)}**", 0x2ECC71, null);
    }

    public static Task NotifyUpdateAsync(string version)
    {
        if (!IsConfigured || !Program.config.DiscordNotifyUpdate) return Task.CompletedTask;
        return SendEmbedAsync("Update", $"Update available / applied: **{Escape(version)}**", 0x3498DB, null);
    }

    public static Task NotifyCustomAsync(string title, string description, int color = 0x5865F2)
    {
        if (!IsConfigured) return Task.CompletedTask;
        return SendEmbedAsync(title, description, color, null);
    }

    public static async Task<bool> SendTestAsync()
    {
        if (string.IsNullOrWhiteSpace(Program.config.DiscordWebhookUrl))
            return false;
        try
        {
            await SendEmbedAsync(
                "VRCUFM",
                "Webhook connected. You'll get notifications here.",
                0x5865F2,
                new[] { ("Status", "OK", true) },
                force: true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    static async Task SendEmbedAsync(
        string title,
        string description,
        int color,
        IEnumerable<(string name, string value, bool inline)>? fields,
        bool force = false)
    {
        if (!force && !IsConfigured) return;
        var url = Program.config.DiscordWebhookUrl?.Trim();
        if (string.IsNullOrEmpty(url)) return;

        var embed = new Dictionary<string, object?>
        {
            ["title"] = title,
            ["description"] = description,
            ["color"] = color,
            ["timestamp"] = DateTime.UtcNow.ToString("o"),
            ["footer"] = new Dictionary<string, object>
            {
                ["text"] = "VRCUFM"
            }
        };

        if (fields != null)
        {
            embed["fields"] = fields.Select(f => new Dictionary<string, object>
            {
                ["name"] = f.name,
                ["value"] = f.value.Length > 1024 ? f.value[..1021] + "..." : f.value,
                ["inline"] = f.inline
            }).ToList();
        }

        var payload = new Dictionary<string, object>
        {
            ["username"] = string.IsNullOrWhiteSpace(Program.config.DiscordWebhookName)
                ? "VRCUFM"
                : Program.config.DiscordWebhookName.Trim(),
            ["embeds"] = new[] { embed }
        };

        if (!string.IsNullOrWhiteSpace(Program.config.DiscordWebhookAvatarUrl))
            payload["avatar_url"] = Program.config.DiscordWebhookAvatarUrl.Trim();

        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        var resp = await Http.PostAsync(url, content);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync();
            Console.WriteLine($"[Discord] {resp.StatusCode}: {body}");
        }
    }

    static string Escape(string s) =>
        (s ?? "").Replace("\\", "\\\\").Replace("*", "\\*").Replace("_", "\\_").Replace("`", "\\`");
}
