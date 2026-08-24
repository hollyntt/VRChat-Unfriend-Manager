using VRChat.API.Model;
using VRCUFM.Filesystem;

namespace VRCUFM.Core;

public static class FriendRequestEnricher
{
    public static HashSet<string> HiddenRequestIds { get; } = new(StringComparer.OrdinalIgnoreCase);

    public static string MeetBadge(string? userId)
    {
        if (string.IsNullOrEmpty(userId)) return "New";
        var info = VRCNextMeetService.Get(userId);
        if (info == null || info.TotalMeets <= 0) return "New";
        if (info.TotalMeets == 1) return "Met once";
        return $"Met {info.TotalMeets}x";
    }

    public static bool IsKnownFromMeets(string? userId)
    {
        if (string.IsNullOrEmpty(userId)) return false;
        return VRCNextMeetService.HasMetBefore(userId);
    }

    public static bool LooksHidden(Notification req)
    {
        if (req?.Id != null && HiddenRequestIds.Contains(req.Id))
            return true;
        var details = (req?.Details ?? "").ToLowerInvariant();
        var msg = (req?.Message ?? "").ToLowerInvariant();
        return details.Contains("hidden") || msg.Contains("hidden");
    }

    public static void ClearHiddenCache() => HiddenRequestIds.Clear();
}
