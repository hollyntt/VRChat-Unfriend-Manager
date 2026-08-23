using System.Numerics;
using System.Runtime.InteropServices;
using ImGuiNET;
using Raylib_cs;
using VRCUFM.AppSystem;
using VRCUFM.Core;
using VRCUFM.Filesystem;
using VRCUFM.VRChat;
using File = System.IO.File;

namespace VRCUFM.UI;

internal static class UIShared
{
    public static readonly string[] TogetherUnits = { "min", "hr", "days" };
    public static readonly string[] SearchFields = { "Name", "Group" };
    public static readonly string[] Sorts = { "Oldest", "Newest", "A-Z", "Z-A", "Most Time", "Least Time", "Lowest Score", "Highest Score" };
    public static readonly string[] NavLabels = { "Friends", "Groups", "Requests", "Settings" };
}
