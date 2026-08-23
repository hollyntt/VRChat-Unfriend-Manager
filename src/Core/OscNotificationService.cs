using System.Net;
using System.Net.Sockets;
using System.Text;

namespace VRCUFM.Core;

/// <summary>
/// Lightweight OSC (UDP) sender for VRChat-style notifications.
/// Default: /chatbox/input  (string message, bool immediate, bool playSound)
/// </summary>
public static class OscNotificationService
{
    public static bool IsConfigured =>
        Program.config.OscNotifyEnabled
        && Program.config.OscPort > 0
        && Program.config.OscPort < 65536;

    public static Task NotifyUnfriendAsync(string displayName)
    {
        if (!IsConfigured || !Program.config.OscNotifyUnfriend) return Task.CompletedTask;
        return SendChatAsync($"[VRCUFM] Unfriended {displayName}");
    }

    public static Task NotifyBulkUnfriendAsync(int count)
    {
        if (!IsConfigured || !Program.config.OscNotifyUnfriend) return Task.CompletedTask;
        return SendChatAsync($"[VRCUFM] Unfriended {count} friends");
    }

    public static Task NotifyAutoGroupAsync(string summary)
    {
        if (!IsConfigured || !Program.config.OscNotifyAutoGroup) return Task.CompletedTask;
        return SendChatAsync($"[VRCUFM] Auto-group: {summary}");
    }

    public static Task NotifyLoginAsync(string username)
    {
        if (!IsConfigured || !Program.config.OscNotifyLogin) return Task.CompletedTask;
        return SendChatAsync($"[VRCUFM] Signed in as {username}");
    }

    public static Task NotifyCustomAsync(string message)
    {
        if (!IsConfigured) return Task.CompletedTask;
        return SendChatAsync(message);
    }

    public static async Task<bool> SendTestAsync()
    {
        if (Program.config.OscPort <= 0) return false;
        try
        {
            await SendChatAsync("[VRCUFM] OSC notifications connected", force: true);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine("[OSC] " + ex.Message);
            return false;
        }
    }

    static Task SendChatAsync(string message, bool force = false)
    {
        if (!force && !IsConfigured) return Task.CompletedTask;
        message = (message ?? "").Trim();
        if (message.Length > 140) message = message[..137] + "...";

        string address = string.IsNullOrWhiteSpace(Program.config.OscAddress)
            ? "/chatbox/input"
            : Program.config.OscAddress.Trim();

        // VRChat chatbox: s T T  (message, true=send, true=play notification sound optional)
        bool immediate = Program.config.OscChatboxImmediate;
        bool playSound = Program.config.OscChatboxSound;

        return Task.Run(() =>
        {
            byte[] packet = BuildOscMessage(address,
                ("s", message),
                ("T", immediate),
                ("T", playSound));
            using var udp = new UdpClient();
            string host = string.IsNullOrWhiteSpace(Program.config.OscHost)
                ? "127.0.0.1"
                : Program.config.OscHost.Trim();
            udp.Send(packet, packet.Length, host, Program.config.OscPort);
        });
    }

    /// <summary>Build a simple OSC message with string and bool (T/F) args.</summary>
    static byte[] BuildOscMessage(string address, params (string tag, object value)[] args)
    {
        var chunks = new List<byte[]>();
        chunks.Add(PadOscString(address));

        var tagStr = "," + string.Concat(args.Select(a =>
            a.tag == "s" ? "s" : (a.value is bool b ? (b ? "T" : "F") : "T")));
        // rebuild tags properly
        var sb = new StringBuilder(",");
        foreach (var a in args)
        {
            if (a.tag == "s") sb.Append('s');
            else sb.Append((bool)a.value ? 'T' : 'F');
        }
        chunks.Add(PadOscString(sb.ToString()));

        foreach (var a in args)
        {
            if (a.tag == "s")
                chunks.Add(PadOscString(a.value?.ToString() ?? ""));
            // T/F have no payload
        }

        int len = chunks.Sum(c => c.Length);
        var buf = new byte[len];
        int o = 0;
        foreach (var c in chunks)
        {
            Buffer.BlockCopy(c, 0, buf, o, c.Length);
            o += c.Length;
        }
        return buf;
    }

    static byte[] PadOscString(string s)
    {
        var raw = Encoding.UTF8.GetBytes(s ?? "");
        int pad = (4 - ((raw.Length + 1) % 4)) % 4;
        var buf = new byte[raw.Length + 1 + pad];
        Buffer.BlockCopy(raw, 0, buf, 0, raw.Length);
        // rest zero including null terminator
        return buf;
    }
}
