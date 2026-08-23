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

/// <summary>Simple page transition (fade + slide) in the XOSC spirit.</summary>
public static class UIAnim
{
    static int _currentPage;
    static int _targetPage;
    static float _t = 1f; // 0 = start of transition, 1 = settled
    const float Speed = 3.2f; // slower fade/slide (~0.6s full transition)

    public static int Page => _currentPage;
    public static float Progress => _t;
    public static bool Busy => _t < 0.999f;

    public static void RequestPage(int page)
    {
        if (page == _targetPage) return;
        _targetPage = page;
        _t = 0f;
    }

    public static void Tick()
    {
        float dt = ImGui.GetIO().DeltaTime;
        if (dt <= 0 || dt > 0.1f) dt = 1f / 60f;
        if (_t < 1f)
        {
            _t = Math.Min(1f, _t + dt * Speed);
            if (_t >= 0.5f && _currentPage != _targetPage)
                _currentPage = _targetPage;
        }
        else
            _currentPage = _targetPage;
    }

    /// <summary>Ease out cubic 0..1</summary>
    public static float Ease(float x)
    {
        float u = 1f - x;
        return 1f - u * u * u;
    }

    public static void BeginPageContent()
    {
        Tick();
        float e = Ease(_t < 0.5f ? _t * 2f : (_t - 0.5f) * 2f);
        // First half: fade out / slide left; second half: fade in / slide from right
        float alpha;
        float slide;
        if (_t < 0.5f)
        {
            alpha = 1f - Ease(_t * 2f);
            slide = -24f * Ease(_t * 2f);
        }
        else
        {
            alpha = Ease((_t - 0.5f) * 2f);
            slide = 24f * (1f - Ease((_t - 0.5f) * 2f));
        }
        ImGui.PushStyleVar(ImGuiStyleVar.Alpha, Math.Clamp(alpha, 0.01f, 1f));
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + slide);
        // store nothing — EndPageContent pops
        _pushed = true;
    }

    static bool _pushed;

    public static void EndPageContent()
    {
        if (_pushed)
        {
            ImGui.PopStyleVar(); // Alpha
            _pushed = false;
        }
    }
}
