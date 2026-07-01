namespace VRCUFM.AppSystem;

public class SafeLimitedUserFriend
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string LastLogin { get; set; } = "";
    public long TimeSpentMs { get; set; } = 0;
    public string ThumbnailUrl { get; set; } = "";
}