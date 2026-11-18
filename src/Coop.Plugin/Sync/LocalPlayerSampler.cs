using System;
using System.Collections.Generic;
using Coop.Core.Protocol;
using Coop.Core.Session;
using Coop.Core.Sync;
using Coop.Plugin.Runtime;
using UnityEngine;

namespace Coop.Plugin.Sync
{
    /// <summary>
    /// Captures the local Will's sprites 20 times a second and sends them to the other player.
    /// </summary>
    internal sealed class LocalPlayerSampler
    {
        public const float SendInterval = 0.05f;

        private readonly SpriteIdAllocator _ids = new SpriteIdAllocator();
        private SpriteRenderer[] _renderers;
        private HeroMerchant _cachedWill;
        private int _cachedScene = int.MinValue;
        private float _nextSend;
        private uint _tick;
        private bool _loggedCount;

        public static string KeyOf(Sprite s)
        {
            if (s == null) return null;
            return (s.texture != null ? s.texture.name : "") + "/" + s.name;
        }

        /// <summary>The local Will's renderers, in the same slot order the puppet uses.</summary>
        public static SpriteRenderer[] RenderersOf(HeroMerchant will)
        {
            return will.GetComponentsInChildren<SpriteRenderer>(true);
        }

        public void Reset()
        {
            _ids.Reset();
            _renderers = null;
            _cachedWill = null;
            _cachedScene = int.MinValue;
            _tick = 0;
        }

        public void Update(CoopLink session)
        {
            if (session == null || session.State != SessionState.Connected) return;
            float now = Time.realtimeSinceStartup;
            if (now < _nextSend) return;
            _nextSend = now + SendInterval;

            var will = HeroMerchant.Instance;
            int scene = GameInfo.CurrentSceneId();
            if (will == null)
            {
                session.SendPlayerState(new PlayerState { Tick = ++_tick, SceneId = -1, Hp = -1f, Layers = new SpriteLayer[0] });
                return;
            }
            if (will != _cachedWill || scene != _cachedScene || _renderers == null)
            {
                _cachedWill = will;
                _cachedScene = scene;
                _renderers = RenderersOf(will);
            }

            var root = will.transform;
            int count = Math.Min(_renderers.Length, 255);
            if (!_loggedCount) { _loggedCount = true; CoopPlugin.Log.LogInfo("Will has " + _renderers.Length + " sprite renderers"); }
            var layers = new List<SpriteLayer>(count);
            for (int i = 0; i < count; i++)
            {
                var r = _renderers[i];
                // Only visible layers go on the wire, keeping packets small (Steam caps unreliable packets at 1200 bytes).
                if (r == null || r.sprite == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                var layer = new SpriteLayer { Slot = (byte)i };
                {
                    ushort id = _ids.GetOrAdd(KeyOf(r.sprite), out bool isNew);
                    if (isNew) session.SendSpriteDef(new SpriteDef { Id = id, Key = KeyOf(r.sprite) });
                    layer.SpriteId = id;
                    layer.Visible = id != 0 && r.enabled && r.gameObject.activeInHierarchy;
                    // The game may mirror Will with a negative scale instead of flipX.
                    layer.FlipX = r.flipX ^ (r.transform.lossyScale.x < 0);
                    layer.SortingOrder = (short)Mathf.Clamp(r.sortingOrder, short.MinValue, short.MaxValue);
                    var local = r.transform.position - root.position;
                    layer.LocalX = local.x;
                    layer.LocalY = local.y;
                }
                layers.Add(layer);
            }

            Shared.GhostMode.Describe(will, out float hp, out float maxHp, out bool ghost);
            var p = root.position;
            session.SendPlayerState(new PlayerState
            {
                Tick = ++_tick,
                SceneId = scene,
                X = p.x,
                Y = p.y,
                Z = p.z,
                Layers = layers.ToArray(),
                Hp = hp,
                MaxHp = maxHp,
                Ghost = ghost,
            });
        }
    }
}
