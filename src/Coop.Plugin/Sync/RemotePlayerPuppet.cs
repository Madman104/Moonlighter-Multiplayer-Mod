using System.Collections.Generic;
using Coop.Core.Protocol;
using Coop.Core.Session;
using Coop.Core.Sync;
using Coop.Plugin.Runtime;
using UnityEngine;

namespace Coop.Plugin.Sync
{
    /// <summary>
    /// Draws the other player's Will. Visual only: it has no HeroMerchant component
    /// (the game finds "the" Will with FindObjectOfType) and no colliders.
    /// </summary>
    internal sealed class RemotePlayerPuppet : MonoBehaviour
    {
        private static readonly Color Tint = new Color(0.75f, 0.85f, 1f);
        private const float SpriteCacheRefreshSeconds = 2f;

        private readonly SnapshotInterpolator _positions = new SnapshotInterpolator(0.1);
        private readonly SpriteNameMap _names = new SpriteNameMap();
        private readonly List<SpriteRenderer> _slots = new List<SpriteRenderer>();
        private Dictionary<string, Sprite> _spriteCache;
        private float _nextCacheRefresh;
        private PlayerState _latest;
        private bool _shown;

        public string Label = "P2";
        public PeerSession Session;

        public static RemotePlayerPuppet Spawn(PeerSession session)
        {
            var go = new GameObject("@CoopRemoteWill");
            DontDestroyOnLoad(go);
            var p = go.AddComponent<RemotePlayerPuppet>();
            p.Session = session;
            p.Label = session.IsHost ? "P2" : "P1";
            session.PlayerStateReceived += p.OnState;
            session.SpriteDefReceived += p.OnSpriteDef;
            return p;
        }

        public void Despawn()
        {
            if (Session != null)
            {
                Session.PlayerStateReceived -= OnState;
                Session.SpriteDefReceived -= OnSpriteDef;
            }
            Destroy(gameObject);
        }

        private void OnSpriteDef(SpriteDef def) => _names.Define(def.Id, def.Key);

        private void OnState(PlayerState s)
        {
            if (_latest != null && s.SceneId != _latest.SceneId) _positions.Clear();
            _latest = s;
            _positions.Add(Time.realtimeSinceStartup, s.X, s.Y, s.Z);
        }

        private void LateUpdate()
        {
            bool show = _latest != null && Session != null
                && PuppetVisibility.ShouldShow(GameInfo.CurrentSceneId(), _latest.SceneId, Session.State);
            if (!show)
            {
                if (_shown) SetAllVisible(false);
                _shown = false;
                return;
            }
            _shown = true;

            if (_positions.TrySample(Time.realtimeSinceStartup, out var x, out var y, out var z))
                transform.position = new Vector3(x, y, z);

            var localWill = HeroMerchant.Instance;
            var localRenderers = localWill != null ? LocalPlayerSampler.RenderersOf(localWill) : null;

            foreach (var layer in _latest.Layers)
            {
                var r = SlotRenderer(layer.Slot, localRenderers);
                var sprite = layer.Visible ? Resolve(_names.GetKey(layer.SpriteId)) : null;
                r.enabled = sprite != null;
                if (sprite == null) continue;
                r.sprite = sprite;
                r.flipX = layer.FlipX;
                r.sortingOrder = layer.SortingOrder;
                r.transform.localPosition = new Vector3(layer.LocalX, layer.LocalY, 0f);
            }
            for (int i = _latest.Layers.Length; i < _slots.Count; i++) _slots[i].enabled = false;
        }

        private SpriteRenderer SlotRenderer(int slot, SpriteRenderer[] localRenderers)
        {
            while (_slots.Count <= slot)
            {
                var child = new GameObject("slot" + _slots.Count);
                child.transform.SetParent(transform, false);
                var r = child.AddComponent<SpriteRenderer>();
                r.color = Tint;
                // Same structure as the local Will: borrow its sorting layer and material.
                int i = _slots.Count;
                if (localRenderers != null && i < localRenderers.Length && localRenderers[i] != null)
                {
                    r.sortingLayerID = localRenderers[i].sortingLayerID;
                    r.sharedMaterial = localRenderers[i].sharedMaterial;
                }
                _slots.Add(r);
            }
            return _slots[slot];
        }

        private Sprite Resolve(string key)
        {
            if (key == null) return null;
            if (_spriteCache == null || (!_spriteCache.ContainsKey(key) && Time.realtimeSinceStartup >= _nextCacheRefresh))
            {
                _nextCacheRefresh = Time.realtimeSinceStartup + SpriteCacheRefreshSeconds;
                _spriteCache = new Dictionary<string, Sprite>();
                foreach (var s in Resources.FindObjectsOfTypeAll<Sprite>())
                {
                    var k = LocalPlayerSampler.KeyOf(s);
                    if (k != null && !_spriteCache.ContainsKey(k)) _spriteCache[k] = s;
                }
            }
            return _spriteCache.TryGetValue(key, out var sprite) ? sprite : null;
        }

        private void SetAllVisible(bool v)
        {
            foreach (var r in _slots) r.enabled = v;
        }

        private void OnGUI()
        {
            if (!_shown || Camera.main == null) return;
            float top = transform.position.y;
            foreach (var r in _slots)
                if (r.enabled) top = Mathf.Max(top, r.bounds.max.y);
            var screen = Camera.main.WorldToScreenPoint(new Vector3(transform.position.x, top, transform.position.z));
            if (screen.z < 0) return;
            var rect = new Rect(screen.x - 20, Screen.height - screen.y - 22, 40, 20);
            var style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            style.normal.textColor = Tint;
            GUI.Label(rect, Label, style);
        }
    }
}
