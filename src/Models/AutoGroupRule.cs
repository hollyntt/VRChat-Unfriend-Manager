namespace VRCUFM.AppSystem;

public class AutoGroupRule
{
    public bool Enabled { get; set; } = true;
    public string Name { get; set; } = "New rule";
    public string TargetGroupTag { get; set; } = "group_0";

    /// <summary>add = put in group; unfavorite = remove from favorites; move = unfavorite then add to TargetGroupTag</summary>
    public string Action { get; set; } = "add";

    public bool UseHighScore { get; set; } = false;
    public int ScoreMin { get; set; } = 60;

    public bool UseLowScore { get; set; } = false;
    public int ScoreMax { get; set; } = 25;

    public bool UseInactive { get; set; } = false;
    public int InactiveValue { get; set; } = 3;
    public int InactiveUnit { get; set; } = 1;

    public bool UseActive { get; set; } = false;
    public int ActiveWithinDays { get; set; } = 7;

    public bool UseHighTime { get; set; } = false;
    public int HighTimeValue { get; set; } = 5;
    public int HighTimeUnit { get; set; } = 2;

    public bool UseLowTime { get; set; } = false;
    public int LowTimeValue { get; set; } = 30;
    public int LowTimeUnit { get; set; } = 0;

    /// <summary>If true, skip friends already in any favorite group.</summary>
    public bool SkipIfAlreadyFavorited { get; set; } = true;
}
