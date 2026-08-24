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

    public bool FriendLimitTriggerEnabled { get; set; } = false;
    public int FriendLimitThreshold { get; set; } = 975;
    public int FriendLimitPollIntervalMinutes { get; set; } = 2;
    public int MinTimeTogetherMinutes { get; set; } = 5;

    public bool StartMenuShortcut { get; set; }
    public bool ShowStatsPanel { get; set; } = true;

    public bool SetupCompleted { get; set; } = false;
    public string InstallPath { get; set; } = "";
    public bool PortableMode { get; set; } = false;

    public bool AutoUnfriendLowScore { get; set; } = false;
    public int AutoUnfriendScoreMax { get; set; } = 25;
    public bool AutoUnfriendInactive { get; set; } = true;
    public int AutoUnfriendInactiveValue { get; set; } = 3;
    public int AutoUnfriendInactiveUnit { get; set; } = 1;
    public bool AutoUnfriendLowTime { get; set; } = false;
    public int AutoUnfriendLowTimeValue { get; set; } = 60;
    public int AutoUnfriendLowTimeUnit { get; set; } = 1;

    public bool ScoreFilterEnabled { get; set; } = false;
    public int ScoreFilterMin { get; set; } = 0;
    public int ScoreFilterMax { get; set; } = 100;
    public int ScoreBulkMax { get; set; } = 25;

    public bool AutoCheckUpdates { get; set; } = true;
    public bool AutoApplyUpdates { get; set; } = false;
    public bool AutoRefreshAfterUnfriend { get; set; } = true;

    // Auto-group friends into VRChat favorite groups
    public bool AutoGroupEnabled { get; set; } = false;
    public int AutoGroupIntervalMinutes { get; set; } = 30;
    public List<AutoGroupRule> AutoGroupRules { get; set; } = new();


    // Discord webhook
    public bool DiscordWebhookEnabled { get; set; } = false;
    public string DiscordWebhookUrl { get; set; } = "";
    public string DiscordWebhookName { get; set; } = "VRCUFM";
    public string DiscordWebhookAvatarUrl { get; set; } = "";
    public bool DiscordNotifyUnfriend { get; set; } = true;
    public bool DiscordNotifyAutoGroup { get; set; } = true;
    public bool DiscordNotifyLogin { get; set; } = false;
    public bool DiscordNotifyUpdate { get; set; } = true;


    // OSC notifications (VRChat chatbox etc.)
    public bool OscNotifyEnabled { get; set; } = false;
    public string OscHost { get; set; } = "127.0.0.1";
    public int OscPort { get; set; } = 9000;
    public string OscAddress { get; set; } = "/chatbox/input";
    public bool OscChatboxImmediate { get; set; } = true;
    public bool OscChatboxSound { get; set; } = true;
    public bool OscNotifyUnfriend { get; set; } = true;
    public bool OscNotifyAutoGroup { get; set; } = true;
    public bool OscNotifyLogin { get; set; } = false;
}
