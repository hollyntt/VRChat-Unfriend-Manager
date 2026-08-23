using System.Numerics;
using ImGuiNET;
using VRCUFM.Core;

namespace VRCUFM.UI;

public static class DiscordWebhookPanel
{
    static string _urlBuf = "";
    static string _nameBuf = "";
    static string _avatarBuf = "";
    static bool _bufsInit;
    static string _testStatus = "";
    static bool _testing;

    public static void Draw()
    {
        if (!_bufsInit)
        {
            _urlBuf = Program.config.DiscordWebhookUrl ?? "";
            _nameBuf = string.IsNullOrEmpty(Program.config.DiscordWebhookName) ? "VRCUFM" : Program.config.DiscordWebhookName;
            _avatarBuf = Program.config.DiscordWebhookAvatarUrl ?? "";
            _bufsInit = true;
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Text("Discord notifications");
        ImGui.Separator();
        ImGui.TextDisabled("Send clean embeds to a channel when things happen.");

        bool enabled = Program.config.DiscordWebhookEnabled;
        if (ImGui.Checkbox("Enable Discord webhook", ref enabled))
        {
            Program.config.DiscordWebhookEnabled = enabled;
            Program.SaveConfig();
        }

        if (!Program.config.DiscordWebhookEnabled)
            return;

        ImGui.Spacing();
        ImGui.TextDisabled("Webhook URL");
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 12);
        if (ImGui.InputText("##dwh_url", ref _urlBuf, 256))
        {
            Program.config.DiscordWebhookUrl = _urlBuf.Trim();
            Program.SaveConfig();
        }
        if (!string.IsNullOrEmpty(_urlBuf) &&
            !_urlBuf.StartsWith("https://discord.com/api/webhooks/", StringComparison.OrdinalIgnoreCase))
        {
            ImGui.TextColored(new Vector4(0.95f, 0.45f, 0.35f, 1f),
                "URL should start with https://discord.com/api/webhooks/");
        }

        ImGui.Spacing();
        ImGui.Columns(2, "##dwh_cols", false);
        ImGui.TextDisabled("Bot name");
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputText("##dwh_name", ref _nameBuf, 64))
        {
            Program.config.DiscordWebhookName = string.IsNullOrWhiteSpace(_nameBuf) ? "VRCUFM" : _nameBuf.Trim();
            Program.SaveConfig();
        }
        ImGui.NextColumn();
        ImGui.TextDisabled("Avatar URL (optional)");
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputText("##dwh_avatar", ref _avatarBuf, 256))
        {
            Program.config.DiscordWebhookAvatarUrl = _avatarBuf.Trim();
            Program.SaveConfig();
        }
        ImGui.Columns(1);

        ImGui.Spacing();
        ImGui.TextDisabled("Notify when");
        bool nUnfriend = Program.config.DiscordNotifyUnfriend;
        if (ImGui.Checkbox("Someone is unfriended", ref nUnfriend))
        { Program.config.DiscordNotifyUnfriend = nUnfriend; Program.SaveConfig(); }

        bool nGroup = Program.config.DiscordNotifyAutoGroup;
        if (ImGui.Checkbox("Auto-group changes something", ref nGroup))
        { Program.config.DiscordNotifyAutoGroup = nGroup; Program.SaveConfig(); }

        bool nLogin = Program.config.DiscordNotifyLogin;
        if (ImGui.Checkbox("You sign in", ref nLogin))
        { Program.config.DiscordNotifyLogin = nLogin; Program.SaveConfig(); }

        bool nUpdate = Program.config.DiscordNotifyUpdate;
        if (ImGui.Checkbox("Update is found or applied", ref nUpdate))
        { Program.config.DiscordNotifyUpdate = nUpdate; Program.SaveConfig(); }

        ImGui.Spacing();
        if (_testing)
        {
            ImGui.BeginDisabled();
            ImGui.Button("Sending...");
            ImGui.EndDisabled();
        }
        else if (ImGui.Button("Send test message"))
        {
            _testing = true;
            _testStatus = "";
            _ = Task.Run(async () =>
            {
                bool ok = await DiscordWebhookService.SendTestAsync();
                _testStatus = ok ? "Sent - check your Discord channel." : "Failed - check the webhook URL.";
                _testing = false;
            });
        }

        if (!string.IsNullOrEmpty(_testStatus))
        {
            ImGui.SameLine();
            var col = _testStatus.StartsWith("Sent")
                ? new Vector4(0.4f, 0.9f, 0.5f, 1f)
                : new Vector4(0.95f, 0.45f, 0.35f, 1f);
            ImGui.TextColored(col, _testStatus);
        }
    }
}
