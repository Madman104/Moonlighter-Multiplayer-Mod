using System;
using System.Collections.Generic;
using Coop.Core.Protocol;
using Coop.Core.Session;
using Coop.Plugin.Runtime;
using UnityEngine;

namespace Coop.Plugin.Sync
{
    /// <summary>
    /// Sprites this game doesn't have loaded (another player's weapon you don't own, a customer
    /// whose art isn't in memory) are fetched from the game that draws them: we ask by key, the
    /// owner sends a PNG of that sprite's rectangle with its pivot, and we build a sprite from it.
    /// Each key is asked for at most every RetrySeconds; answers are cached for the session.
    /// </summary>
    internal static class SpriteTransfer
    {
        private const float RetrySeconds = 15f;
        private const int MaxRequestsPerSecond = 30;

        private static readonly Dictionary<string, float> Asked = new Dictionary<string, float>();
        private static readonly Dictionary<string, float> Answered = new Dictionary<string, float>();
        private static float _windowStart;
        private static int _inWindow;
        private static bool _loggedFirst;

        public static void Attach(CoopLink session)
        {
            Asked.Clear();
            Answered.Clear();
            session.GameMessageReceived += (type, msg) =>
            {
                if (type == MessageType.SpriteRequest) Answer(session, session.LastFrom, ((SpriteRequest)msg).Key);
                else if (type == MessageType.SpriteData) Receive((SpriteData)msg);
            };
        }

        private static bool Connected(out CoopLink s)
        {
            s = CoopManager.Instance != null ? CoopManager.Instance.Session : null;
            return s != null && s.State == SessionState.Connected;
        }

        /// <summary>From SpriteLookup when a key isn't found here.</summary>
        public static void Request(string key)
        {
            if (string.IsNullOrEmpty(key) || !Connected(out var s)) return;
            float now = Time.realtimeSinceStartup;
            if (Asked.TryGetValue(key, out float at) && now - at < RetrySeconds) return;
            if (now - _windowStart >= 1f) { _windowStart = now; _inWindow = 0; }
            if (_inWindow >= MaxRequestsPerSecond) return;
            _inWindow++;
            Asked[key] = now;
            s.SendGame(MessageType.SpriteRequest, new SpriteRequest { Key = key });
        }

        // ---- the game that has it ----

        private static void Answer(CoopLink s, int to, string key)
        {
            var sprite = SpriteLookup.FindLocal(key);
            if (sprite == null) return; // not ours either: someone else will answer
            float now = Time.realtimeSinceStartup;
            if (Answered.TryGetValue(key + "@" + to, out float at) && now - at < RetrySeconds) return;
            Answered[key + "@" + to] = now;
            var data = Encode(key, sprite);
            if (data == null) return;
            if (s.IsHost) s.SendGameTo(to, MessageType.SpriteData, data);
            else s.SendGame(MessageType.SpriteData, data); // the host passes it on
        }

        private static SpriteData Encode(string key, Sprite sprite)
        {
            var tex = sprite.texture;
            if (tex == null) return null;
            Rect r;
            Vector2 offset;
            try { r = sprite.textureRect; offset = sprite.textureRectOffset; }
            catch (Exception) { r = sprite.rect; offset = Vector2.zero; } // tightly packed: whole rect
            int w = Mathf.Max(1, Mathf.RoundToInt(r.width)), h = Mathf.Max(1, Mathf.RoundToInt(r.height));
            RenderTexture rt = null;
            var prev = RenderTexture.active;
            Texture2D crop = null;
            try
            {
                // Atlas textures can't be read directly: copy to a render texture and read back.
                rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                Graphics.Blit(tex, rt);
                RenderTexture.active = rt;
                crop = new Texture2D(w, h, TextureFormat.RGBA32, false);
                crop.ReadPixels(new Rect(Mathf.Round(r.x), Mathf.Round(r.y), w, h), 0, 0);
                crop.Apply();
                var png = crop.EncodeToPNG();
                if (png == null || png.Length > SpriteData.MaxPngBytes) return null;
                var pivot = sprite.pivot - offset; // pivot in pixels inside the cropped picture
                if (!_loggedFirst) { _loggedFirst = true; CoopPlugin.Log.LogInfo($"Sending sprite pictures to other players (first: {key}, {png.Length} bytes)"); }
                return new SpriteData { Key = key, PivotX = pivot.x / w, PivotY = pivot.y / h, PixelsPerUnit = sprite.pixelsPerUnit, Png = png };
            }
            catch (Exception e)
            {
                CoopPlugin.Log.LogWarning("Could not send sprite " + key + ": " + e.Message);
                return null;
            }
            finally
            {
                RenderTexture.active = prev;
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
                if (crop != null) UnityEngine.Object.Destroy(crop);
            }
        }

        // ---- the game that needs it ----

        private static void Receive(SpriteData d)
        {
            if (string.IsNullOrEmpty(d.Key) || d.Png == null || d.Png.Length == 0 || SpriteLookup.HasReceived(d.Key)) return;
            try
            {
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!tex.LoadImage(d.Png)) { UnityEngine.Object.Destroy(tex); return; }
                tex.filterMode = FilterMode.Point; // pixel art
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.name = "Coop:" + d.Key;
                var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(d.PivotX, d.PivotY),
                    d.PixelsPerUnit > 0f ? d.PixelsPerUnit : 1f, 0, SpriteMeshType.FullRect);
                sprite.name = d.Key;
                SpriteLookup.AddReceived(d.Key, sprite);
            }
            catch (Exception e) { CoopPlugin.Log.LogWarning("Could not use a received sprite: " + e.Message); }
        }
    }
}
