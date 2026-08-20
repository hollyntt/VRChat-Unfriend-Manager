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

    /// <summary>
    /// Minimum minutes spent together before someone counts as "known"
    /// for stranger-declination purposes.
    /// </summary>
    public int MinTimeTogetherMinutes { get; set; } = 5;

    public bool StartMenuShortcut { get; set; }
    public bool ShowStatsPanel { get; set; } = true;

    public bool SetupCompleted { get; set; } = false;
    public string InstallPath { get; set; } = "";
    public bool PortableMode { get; set; } = false;
}

