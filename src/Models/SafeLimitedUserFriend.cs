namespace VRCUFM.AppSystem;

public class SafeLimitedUserFriend
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string LastLogin { get; set; } = "";
    public long TimeSpentMs { get; set; } = 0;
    public string ThumbnailUrl { get; set; } = "";
    public string Bio { get; set; } = "";

    // ── VRCNext Trusted Score profile fields (filled by enrichment) ──────
    public string DateJoined { get; set; } = "";
    public List<string> Tags { get; set; } = new();
    public int BadgeCount { get; set; } = 0;
    public bool AgeVerified { get; set; } = false;
    public bool IsVrcPlus { get; set; } = false;
    public bool IsEconomyCreator { get; set; } = false;
    public int GroupCount { get; set; } = 0;
    public bool IsRepresentingGroup { get; set; } = false;
    public int UploadedWorlds { get; set; } = 0;
    public int UploadedAvatars { get; set; } = 0;
    public bool ProfileEnriched { get; set; } = false;

    /// <summary>Cached VRCNext Trust Score (0–100). -1 = not computed yet.</summary>
    public int TrustScore { get; set; } = -1;

    /// <summary>Trust rank level 0–4 (Visitor … Trusted/Veteran).</summary>
    public int TrustRankLevel { get; set; } = 0;
}
