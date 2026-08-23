using System.Numerics;
using ImGuiNET;
using VRCUFM.Core;

namespace VRCUFM.UI;

public static class OscNotifyPanel
{
    static string _host = "";
    static string _addr = "";
    static bool _init;
    static string _testStatus = "";
    static bool _testing;

    public static void Draw()
    {
        if (!_init)
        {
            _host = string.IsNullOrEmpty(Program.config.OscHost) ? "127.0.0.1" : Program.config.OscHost;
            _addr = string.IsNullOrEmpty(Program.config.OscAddress) ? "/chatbox/input" : Program.config.OscAddress;
            _init = true;
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Text("OSC notifications");
        ImGui.Separator();
        ImGui.TextDisabled("Send short messages over OSC (VRChat chatbox by default).");

        bool enabled = Program.config.OscNotifyEnabled;
        if (ImGui.Checkbox("Enable OSC notifications", ref enabled))
        {
            Program.config.OscNotifyEnabled = enabled;
            Program.SaveConfig();
        }

        if (!Program.config.OscNotifyEnabled)
            return;

        ImGui.Spacing();
        ImGui.Columns(2, "##osc_cols", false);
        ImGui.TextDisabled("Host");
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputText("##osc_host", ref _host, 64))
        {
            Program.config.OscHost = string.IsNullOrWhiteSpace(_host) ? "127.0.0.1" : _host.Trim();
            Program.SaveConfig();
        }
        ImGui.NextColumn();
        ImGui.TextDisabled("Port");
        ImGui.SetNextItemWidth(-1);
        int port = Program.config.OscPort;
        if (ImGui.DragInt("##osc_port", ref port, 1f, 1, 65535, "%d"))
        {
            Program.config.OscPort = Math.Clamp(port, 1, 65535);
            Program.SaveConfig();
        }
        ImGui.Columns(1);

        ImGui.TextDisabled("Address");
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 12);
        if (ImGui.InputText("##osc_addr", ref _addr, 128))
        {
            Program.config.OscAddress = string.IsNullOrWhiteSpace(_addr) ? "/chatbox/input" : _addr.Trim();
            Program.SaveConfig();
        }
        ImGui.TextDisabled("Default /chatbox/input works with VRChat OSC.");

        bool imm = Program.config.OscChatboxImmediate;
        if (ImGui.Checkbox("Send immediately (chatbox)", ref imm))
        { Program.config.OscChatboxImmediate = imm; Program.SaveConfig(); }

        bool sound = Program.config.OscChatboxSound;
        if (ImGui.Checkbox("Play chatbox sound", ref sound))
        { Program.config.OscChatboxSound = sound; Program.SaveConfig(); }

        ImGui.Spacing();
        ImGui.TextDisabled("Notify when");
        bool nU = Program.config.OscNotifyUnfriend;
        if (ImGui.Checkbox("Someone is unfriended##osc", ref nU))
        { Program.config.OscNotifyUnfriend = nU; Program.SaveConfig(); }

        bool nG = Program.config.OscNotifyAutoGroup;
        if (ImGui.Checkbox("Auto-group changes something##osc", ref nG))
        { Program.config.OscNotifyAutoGroup = nG; Program.SaveConfig(); }

        bool nL = Program.config.OscNotifyLogin;
        if (ImGui.Checkbox("You sign in##osc", ref nL))
        { Program.config.OscNotifyLogin = nL; Program.SaveConfig(); }

        ImGui.Spacing();
        if (_testing)
        {
            ImGui.BeginDisabled();
            ImGui.Button("Sending OSC...");
            ImGui.EndDisabled();
        }
        else if (ImGui.Button("Send test OSC"))
        {
            _testing = true;
            _testStatus = "";
            _ = Task.Run(async () =>
            {
                bool ok = await OscNotificationService.SendTestAsync();
                _testStatus = ok ? "Sent - check VRChat chatbox." : "Failed - check host/port.";
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
