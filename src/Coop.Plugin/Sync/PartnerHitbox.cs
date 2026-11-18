using System.Collections.Generic;
using UnityEngine;

namespace Coop.Plugin.Sync
{
    /// <summary>
    /// A body for another player's figure, so the enemies (and their projectiles) this game runs
    /// can hit them. Tagged "Player" like Will, on a spare physics layer that only touches enemy
    /// layers (never doors, items or room triggers). Hits on it are sent to that player
    /// (EnemyHitGuard) instead of hurting the local Will.
    /// </summary>
    internal sealed class PartnerHitbox : MonoBehaviour
    {
        private static readonly string[] EnemyLayers = { "EnemyProjectile", "GroundEnemy", "FlyingEnemy", "Flying" };
        private static int _layer = -2;

        public int Player;
        private GameObject _body;
        private BoxCollider2D _box;

        /// <summary>The player number whose figure this object is part of, or -1.</summary>
        public static int PlayerOf(GameObject go)
        {
            if (go == null) return -1;
            var h = go.GetComponentInParent<PartnerHitbox>();
            return h != null && h._body != null && h._body.activeSelf ? h.Player : -1;
        }

        private static int Layer()
        {
            if (_layer != -2) return _layer;
            _layer = -1;
            for (int i = 31; i >= 8; i--)
                if (string.IsNullOrEmpty(LayerMask.LayerToName(i))) { _layer = i; break; }
            if (_layer < 0) { CoopPlugin.Log.LogWarning("No spare physics layer: partners can't be hit by enemies this game runs"); return _layer; }
            int player = LayerMask.NameToLayer("Player");
            var allowed = new HashSet<int>();
            foreach (var n in EnemyLayers)
            {
                int l = LayerMask.NameToLayer(n);
                if (l >= 0 && (player < 0 || !Physics2D.GetIgnoreLayerCollision(player, l))) allowed.Add(l);
            }
            for (int i = 0; i < 32; i++) Physics2D.IgnoreLayerCollision(_layer, i, !allowed.Contains(i));
            CoopPlugin.Log.LogInfo($"Partner hitbox layer {_layer}, touches {allowed.Count} enemy layers");
            return _layer;
        }

        /// <summary>From the puppet each frame: on only while the partner is alive here.</summary>
        public void SetActive(bool on)
        {
            if (on && _body == null) Build();
            if (_body == null) return;
            if (_body.activeSelf != on) _body.SetActive(on);
            if (on) Fit();
        }

        private void Build()
        {
            int layer = Layer();
            if (layer < 0) return;
            _body = new GameObject("CoopPartnerBody");
            _body.transform.SetParent(transform, false);
            _body.layer = layer;
            try { _body.tag = "Player"; } catch (UnityException) { }
            var rb = _body.AddComponent<Rigidbody2D>();
            rb.isKinematic = true;
            rb.gravityScale = 0f;
            rb.useFullKinematicContacts = true;
            _box = _body.AddComponent<BoxCollider2D>();
            _body.SetActive(false);
            Fit();
        }

        /// <summary>Same size and offset as the local Will's body collider.</summary>
        private void Fit()
        {
            var will = HeroMerchant.Instance;
            if (_box == null || will == null) return;
            var col = will.GetComponent<Collider2D>();
            if (col == null || !col.enabled) { _box.size = new Vector2(10f, 8f); _box.offset = Vector2.zero; return; }
            var b = col.bounds;
            _box.size = new Vector2(Mathf.Max(2f, b.size.x), Mathf.Max(2f, b.size.y));
            _box.offset = (Vector2)(b.center - will.transform.position);
        }
    }
}
