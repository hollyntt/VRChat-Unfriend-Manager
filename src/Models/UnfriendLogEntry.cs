namespace VRCUFM.AppSystem;

public class UnfriendLogEntry
{
    public string UserId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public DateTime UnfriendedAt { get; set; }
    public string Reason { get; set; } = "";
    public long TimeSpentMsBefore { get; set; }
    public string? LastLoginBefore { get; set; }
}
