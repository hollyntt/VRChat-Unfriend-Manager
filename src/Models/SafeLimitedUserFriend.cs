namespace VRCUFM.AppSystem;

public class SafeLimitedUserFriend
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string LastLogin { get; set; } = "";
    public long TimeSpentMs { get; set; } = 0;
    public string ThumbnailUrl { get; set; } = "";
    public string Bio { get; set; } = "";

    public string DateJoined { get; set; } = "";
    public List<string> Tags { get; set; } = new();
    public int BadgeCount { get; set; }
    public bool AgeVerified { get; set; }
    public bool IsVrcPlus { get; set; }
    public bool IsEconomyCreator { get; set; }
    public int GroupCount { get; set; }
    public bool IsRepresentingGroup { get; set; }
    public int UploadedWorlds { get; set; }
    public int UploadedAvatars { get; set; }
    public bool ProfileEnriched { get; set; }

    /// <summary>0–100. -1 = not computed yet.</summary>
    public int TrustScore { get; set; } = -1;
    public int TrustRankLevel { get; set; }
}
