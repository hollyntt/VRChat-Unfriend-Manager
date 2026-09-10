using System.Net;
using Raylib_cs;
using Image = Raylib_cs.Image;

namespace VRCUFM.Services;

public static class TextureCache
{
    private enum State { Downloading, Ready, Failed }

    private sealed class Entry
    {
        public State State;
        public Texture2D Texture;
    }

    private static readonly Dictionary<string, Entry> _cache = new();
    private static readonly CookieContainer _cookieContainer = new();
    private static readonly HttpClient _http = new(new HttpClientHandler { CookieContainer = _cookieContainer, UseCookies = true })
    {
        Timeout = TimeSpan.FromSeconds(20),
        DefaultRequestHeaders = { { "User-Agent", "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36" } }
    };

    private static readonly Uri VrcApiBase = new("https://api.vrchat.cloud/");

    public static void SetCookie(string cookieHeader)
    {
        foreach (var part in cookieHeader.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Trim().Split('=', 2);
            if (kv.Length == 2)
                _cookieContainer.Add(VrcApiBase, new Cookie(kv[0].Trim(), kv[1].Trim()));
        }
    }

    public static Texture2D? RequestTexture(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        lock (_cache)
        {
            if (_cache.TryGetValue(url, out var entry))
                return entry.State == State.Ready ? entry.Texture : null;

            _cache[url] = new Entry { State = State.Downloading };
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var bytes = await _http.GetByteArrayAsync(url);
                if (bytes.Length < 4) { MarkFailed(url); return; }

                string fmt = (bytes[0] == 0xFF && bytes[1] == 0xD8) ? ".jpg" :
                             (bytes[0] == 0x89 && bytes[1] == 0x50) ? ".png" :
                             (bytes[0] == 0x47 && bytes[1] == 0x49) ? ".gif" : ".jpg";

                var img = Raylib.LoadImageFromMemory(fmt, bytes);
                if (img.Width == 0) { MarkFailed(url); return; }

                Raylib.ImageResize(ref img, 32, 32);

                lock (_pendingLoad)
                    _pendingLoad.Add((url, img));
            }
            catch { MarkFailed(url); }
        });

        return null;
    }

    private static void MarkFailed(string url)
    {
        lock (_cache)
            if (_cache.TryGetValue(url, out var e)) e.State = State.Failed;
    }

    private static readonly List<(string url, Image img)> _pendingLoad = new();
    private static readonly List<Texture2D> _pendingUnload = new();

    /// <summary>
    /// Must be called every frame on the main/render thread.
    /// Finishes pending texture loads and performs any queued unloads.
    /// </summary>
    public static void FlushPending()
    {
        // 1. Pending loads → GPU textures
        List<(string url, Image img)> loadBatch;
        lock (_pendingLoad)
        {
            if (_pendingLoad.Count == 0)
                loadBatch = null!;
            else
            {
                loadBatch = new List<(string, Image)>(_pendingLoad);
                _pendingLoad.Clear();
            }
        }

        if (loadBatch != null)
        {
            foreach (var (url, img) in loadBatch)
            {
                try
                {
                    var tex = Raylib.LoadTextureFromImage(img);
                    Raylib.UnloadImage(img);
                    lock (_cache)
                    {
                        if (_cache.TryGetValue(url, out var entry))
                        {
                            entry.Texture = tex;
                            entry.State = State.Ready;
                        }
                        else
                        {
                            Raylib.UnloadTexture(tex);
                        }
                    }
                }
                catch
                {
                    lock (_cache)
                        if (_cache.TryGetValue(url, out var e)) e.State = State.Failed;
                }
            }
        }

        // 2. Queued unloads (from UnloadAll) — must stay on main thread
        List<Texture2D> unloadBatch;
        lock (_pendingUnload)
        {
            if (_pendingUnload.Count == 0) return;
            unloadBatch = new List<Texture2D>(_pendingUnload);
            _pendingUnload.Clear();
        }

        foreach (var tex in unloadBatch)
            Raylib.UnloadTexture(tex);
    }

    /// <summary>
    /// Clears the cache and queues textures for unload on the main thread.
    /// Safe to call from any thread (including async Refresh on the thread pool).
    /// </summary>
    public static void UnloadAll()
    {
        lock (_cache)
        {
            foreach (var entry in _cache.Values)
            {
                if (entry.State == State.Ready)
                {
                    lock (_pendingUnload)
                        _pendingUnload.Add(entry.Texture);
                }
            }
            _cache.Clear();
        }
    }
}
