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

        public int Index;
        public CoopLink Session;
        public float LastStateAt;

        public static RemotePlayerPuppet Spawn(CoopLink session, int index)
        {
            var go = new GameObject("@CoopRemoteWill" + index);
            DontDestroyOnLoad(go);
            var p = go.AddComponent<RemotePlayerPuppet>();
            p.Session = session;
            p.Index = index;
            p.LastStateAt = Time.realtimeSinceStartup;
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

        // Each player numbers their own sprites: only take this player's.
        private void OnSpriteDef(SpriteDef def)
        {
            if (Session.LastFrom == Index) _names.Define(def.Id, def.Key);
        }

        private void OnState(PlayerState s)
        {
            if (Session.LastFrom != Index) return;
            LastStateAt = Time.realtimeSinceStartup;
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

            // Only visible layers are sent; any slot not in this update is hidden.
            foreach (var slot in _slots) slot.enabled = false;
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

        /// <summary>Highest a name may sit above the feet (world units = game pixels).</summary>
        private const float MaxHeadHeight = 34f;
        private static bool _loggedTop;

        private float VisibleTop(SpriteRenderer r)
        {
            var sprite = r.sprite;
            if (sprite == null) return r.bounds.max.y;
            float maxY = float.MinValue;
            foreach (var v in sprite.vertices) if (v.y > maxY) maxY = v.y;
            float top = maxY == float.MinValue ? r.bounds.max.y : r.transform.TransformPoint(new Vector3(0f, maxY, 0f)).y;
            if (!_loggedTop)
            {
                _loggedTop = true;
                CoopPlugin.Log.LogInfo($"Name placement: visible top {top - transform.position.y:0.#}, frame top {r.bounds.max.y - transform.position.y:0.#} above the feet");
            }
            return top;
        }

        private static int _bodySlot = -1;

        /// <summary>Slot of Will's body sprite: same prefab on both games, so same order as the local Will.</summary>
        private static int BodySlot()
        {
            if (_bodySlot >= 0) return _bodySlot;
            var hm = HeroMerchant.Instance;
            if (hm == null || hm.heroMerchantController == null || hm.heroMerchantController.heroMerchantArt == null) return -1;
            var bodyRenderer = hm.heroMerchantController.heroMerchantArt.GetComponentInChildren<SpriteRenderer>();
            _bodySlot = System.Array.IndexOf(LocalPlayerSampler.RenderersOf(hm), bodyRenderer);
            return _bodySlot;
        }

        private void SetAllVisible(bool v)
        {
            foreach (var r in _slots) r.enabled = v;
        }

        private void OnGUI()
        {
            if (!_shown || Camera.main == null || !CoopConfig.ShowNames.Value) return;
            // Just above Will's head: the top of the body sprite's visible pixels (its outline
            // points), not its frame, which has empty space above the head. Capped near the feet.
            int body = BodySlot();
            float top = body >= 0 && body < _slots.Count && _slots[body].enabled
                ? VisibleTop(_slots[body]) + 3f
                : transform.position.y + MaxHeadHeight;
            top = Mathf.Min(top, transform.position.y + MaxHeadHeight);
            var screen = Camera.main.WorldToScreenPoint(new Vector3(transform.position.x, top, transform.position.z));
            if (screen.z < 0) return;
            var rect = new Rect(screen.x - 120, Screen.height - screen.y - 24, 240, 22);
            var style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            // The name's colour shows their health (green -> yellow -> red; grey = ghost).
            var (tr, tg, tb) = NameTint.For(_latest.Hp, _latest.MaxHp, _latest.Ghost);
            var shadow = new GUIStyle(style);
            shadow.normal.textColor = new Color(0f, 0f, 0f, 0.8f);
            GUI.Label(new Rect(rect.x + 1, rect.y + 1, rect.width, rect.height), PlayerNames.Name(Index), shadow);
            style.normal.textColor = new Color(tr, tg, tb);
            GUI.Label(rect, PlayerNames.Name(Index), style);
        }
    }
}
