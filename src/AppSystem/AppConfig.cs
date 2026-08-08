namespace VRCUFM.AppSystem;

public class AppConfig
{
    public string Username { get; set; } = "";
    public string EncodedPassword { get; set; } = "";
    public string Cookie { get; set; } = "";
    public bool RememberMe { get; set; } = true;
    public bool ExcludeFavorites { get; set; } = true;
    public bool InactiveEnabled { get; set; } = false;
    public int InactiveValue { get; set; } = 3;
    public int InactiveUnitIndex { get; set; } = 1;
    public bool TogetherFilterEnabled { get; set; } = false;
    public int TogetherFilterValue { get; set; } = 60;
    public int TogetherFilterUnit { get; set; } = 1;
    public int SortOptionIndex { get; set; } = 0;
    public bool AutoUnfriendEnabled { get; set; } = false;
    public int AutoUnfriendHour { get; set; } = 3;
    public int AutoUnfriendMinute { get; set; } = 0;
    public int AutoUnfriendMode { get; set; } = 0;
    public int AutoUnfriendScheduleType { get; set; } = 0;
    public int AutoUnfriendMonthDay { get; set; } = 1;
    public int AutoUnfriendYear { get; set; } = DateTime.Now.Year;
    public int AutoUnfriendMonth { get; set; } = DateTime.Now.Month;
    public int AutoUnfriendDay { get; set; } = DateTime.Now.Day;
    public DateTime? AutoUnfriendLastRun { get; set; } = null;
    public bool RunOnStartup { get; set; } = false;
    public bool VrcxStartupDesktop { get; set; } = false;
    public bool VrcxStartupVr { get; set; } = false;
    public bool VrcNextStartupDesktop { get; set; } = false;
    public bool VrcNextStartupVr { get; set; } = false;
    public bool HideInTaskbar { get; set; } = false;
    public List<string> ExcludedFavGroups { get; set; } = new();
    public bool AutoDeclineFriendRequests { get; set; } = false;
    public bool AutoSendRequestBack { get; set; } = false;
    public bool AutoDeclineOnlyFromStrangers { get; set; } = true;

    // ── Friend-limit trigger ────────────────────────────────────────────────
    /// <summary>
    /// When true, RunAutoUnfriendAsync fires immediately if the friend count
    /// reaches or exceeds FriendLimitThreshold, regardless of the schedule.
    /// </summary>
    public bool FriendLimitTriggerEnabled { get; set; } = false;

    /// <summary>
    /// The friend count at which the limit trigger fires.
    /// VRChat's hard cap is 1000; default here is 975 (25 slots as buffer).
    /// </summary>
    public int FriendLimitThreshold { get; set; } = 975;

    /// <summary>
    /// How often (in minutes) the limit watcher polls the friend count.
    /// Min 1, max 60. Default 2.
    /// </summary>
    public int FriendLimitPollIntervalMinutes { get; set; } = 2;

    public bool StartMenuShortcut { get; set; }
}