using System.Net;
using Raylib_cs;
using Image = Raylib_cs.Image;

namespace VRCUFM.AppSystem;

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

        public static void FlushPending()
        {
            List<(string url, Image img)> batch;
            lock (_pendingLoad)
            {
                if (_pendingLoad.Count == 0) return;
                batch = new List<(string, Image)>(_pendingLoad);
                _pendingLoad.Clear();
            }

            foreach (var (url, img) in batch)
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
                        else Raylib.UnloadTexture(tex);
                    }
                }
                catch
                {
                    lock (_cache)
                        if (_cache.TryGetValue(url, out var e)) e.State = State.Failed;
                }
            }
        }

        public static void UnloadAll()
        {
            lock (_cache)
            {
                foreach (var entry in _cache.Values)
                    if (entry.State == State.Ready)
                        Raylib.UnloadTexture(entry.Texture);
                _cache.Clear();
            }
        }
    }