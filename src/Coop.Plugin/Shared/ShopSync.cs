using System;
using System.Collections.Generic;
using Coop.Core.Protocol;
using Coop.Core.Session;
using Coop.Core.Sync;
using Coop.Plugin.Runtime;
using Coop.Plugin.Sync;
using FullSerializer;
using HarmonyLib;
using UnityEngine;

namespace Coop.Plugin.Shared
{
    /// <summary>The shop's showcases (with prices) and storage chests, as sent between the games.</summary>
    [Serializable]
    public class ShopSnapshot
    {
        public List<ItemSaveInfo> showcases = new List<ItemSaveInfo>();
        public List<ChestSaveInfo> chests = new List<ChestSaveInfo>();
    }

    /// <summary>
    /// Stage 4c: one shop for both players (the guest plays in the host's world, so both have
    /// the same shop). Either player can stock showcases, set prices, use the chests and use
    /// the cash register; changes are sent both ways (last change wins).
    /// Customers exist only in the host's game; the guest sees drawn copies of them, and the
    /// guest's cash register sells through the host's. Either player can open the shop.
    /// </summary>
    internal static class ShopSync
    {
        public const float StateInterval = 0.5f;
        public const float VisitorInterval = 0.1f;
        public const ushort VisitorSpriteBase = 40000; // Will's sprite ids stay below this

        private static bool _applyingRemote;
        private static string _last;
        private static ShopSnapshot _pendingRemote;
        private static float _nextState, _nextVisitors;
        private static bool _hostCashWaiting;

        private static readonly SpriteIdAllocator VisitorSprites = new SpriteIdAllocator(VisitorSpriteBase);
        private static readonly Dictionary<Agent, ushort> VisitorIds = new Dictionary<Agent, ushort>();
        private static ushort _nextVisitorId = 1;

        public static void Apply(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(VisitorGenerator), nameof(VisitorGenerator.EnableSpawn)),
                prefix: new HarmonyMethod(typeof(ShopSync), nameof(BlockOnGuest)));
            harmony.Patch(AccessTools.Method(typeof(ShopManager), nameof(ShopManager.OpenShop)),
                prefix: new HarmonyMethod(typeof(ShopSync), nameof(OpenShopPrefix)),
                postfix: new HarmonyMethod(typeof(ShopSync), nameof(OpenShopPostfix)));
            harmony.Patch(AccessTools.Method(typeof(CashPoint), nameof(CashPoint.TryToSellItem)),
                prefix: new HarmonyMethod(typeof(ShopSync), nameof(SellPrefix)));
            harmony.Patch(AccessTools.Method(typeof(CashPoint), nameof(CashPoint.IsInstantActionBlocked)),
                prefix: new HarmonyMethod(typeof(ShopSync), nameof(CashBlockedPrefix)));
        }

        public static void Attach(PeerSession session)
        {
            _last = null;
            _pendingRemote = null;
            _hostCashWaiting = false;
            VisitorSprites.Reset();
            VisitorIds.Clear();
            _nextVisitorId = 1;
            VisitorPuppets.Attach(session);
            session.GameMessageReceived += OnGameMessage;
        }

        private static bool Connected(out PeerSession s)
        {
            s = CoopManager.Instance != null ? CoopManager.Instance.Session : null;
            return s != null && s.State == SessionState.Connected;
        }

        private static bool IsGuest() => Connected(out var s) && !s.IsHost;

        private static ShopReferences Refs()
        {
            var gm = GameManager.Instance;
            var sm = ShopManager.Instance;
            if (gm == null || !gm.IsTownSceneLoaded() || gm.IsDungeonSceneLoaded() || sm == null) return null;
            return sm.references;
        }

        // ---- customers only on the host ----

        private static bool BlockOnGuest() => !IsGuest();

        private static bool OpenShopPrefix()
        {
            if (_applyingRemote || !IsGuest()) return true;
            Connected(out var s);
            s.SendGame(MessageType.ShopOpen, null);
            CoopToast.Show("Opening the shop...");
            return false;
        }

        private static void OpenShopPostfix()
        {
            if (_applyingRemote || !Connected(out var s) || !s.IsHost) return;
            s.SendGame(MessageType.ShopOpen, null);
        }

        // ---- cash register: the guest sells through the host's register ----

        private static bool SellPrefix()
        {
            if (!IsGuest()) return true;
            Connected(out var s);
            s.SendGame(MessageType.CashSell, null);
            return false;
        }

        private static bool CashBlockedPrefix(ref bool __result)
        {
            if (!IsGuest()) return true;
            __result = !_hostCashWaiting;
            return false;
        }

        // ---- messages ----

        private static void OnGameMessage(MessageType type, object msg)
        {
            switch (type)
            {
                case MessageType.ShopState:
                    try
                    {
                        ShopSnapshot snap = null;
                        FsJSONManager.Deserialize(PackedText.Unpack(((ShopState)msg).Data), ref snap, false);
                        if (snap != null) _pendingRemote = snap;
                    }
                    catch (Exception e) { CoopPlugin.Log.LogWarning("Could not read the partner's shop: " + e.Message); }
                    break;
                case MessageType.ShopOpen:
                    OpenLocally();
                    break;
                case MessageType.CashSell:
                    if (Connected(out var hs) && hs.IsHost) HostSell();
                    break;
                case MessageType.EntityStates:
                    var es = (EntityStates)msg;
                    if (es.Group != EntityStates.ShopVisitors || !IsGuest() || !WorldSync.InHostWorld) return;
                    _hostCashWaiting = es.Extra == 1;
                    VisitorPuppets.Show(es);
                    break;
            }
        }

        private static void OpenLocally()
        {
            var sm = ShopManager.Instance;
            if (sm == null || Refs() == null || !sm.IsShopClosed) return;
            _applyingRemote = true;
            try { sm.OpenShop(); }
            catch (Exception e) { CoopPlugin.Log.LogWarning("Could not open the shop: " + e.Message); }
            finally { _applyingRemote = false; }
            CoopPlugin.Log.LogInfo("Co-op: the shop is open");
        }

        private static void HostSell()
        {
            var refs = Refs();
            if (refs == null || refs.shopCash == null || refs.shopCash.visitorBuying == null) return;
            try { refs.shopCash.TryToSellItem(); }
            catch (Exception e) { CoopPlugin.Log.LogWarning("Could not sell for your partner: " + e.Message); }
        }

        // ---- shop state both ways ----

        public static void Tick()
        {
            if (!Connected(out var s)) return;
            var refs = Refs();
            if (refs == null) { if (!s.IsHost) VisitorPuppets.Clear(); return; }
            // Before the guest has loaded the host's world, their town is their own: never mix it in.
            if (!s.IsHost && !WorldSync.InHostWorld) { _pendingRemote = null; _last = null; return; }
            float now = Time.realtimeSinceStartup;

            if (s.IsHost && now >= _nextVisitors)
            {
                _nextVisitors = now + VisitorInterval;
                SendVisitors(s, refs);
            }

            if (now < _nextState) return;
            _nextState = now + StateInterval;

            // Don't change items under an open chest/showcase/price panel; apply once it closes.
            bool busy = GUIManager.Instance != null && GUIManager.Instance.currentInventory != null;
            if (_pendingRemote != null && !busy)
            {
                var snap = _pendingRemote;
                _pendingRemote = null;
                ApplySnapshot(refs, snap);
                _last = Serialize(Build(refs));
                return;
            }

            string json = Serialize(Build(refs));
            if (json == null || json == _last) return;
            bool first = _last == null;
            _last = json;
            // The host's shop wins when the session starts: the guest only waits for it.
            if (first && !s.IsHost) return;
            s.SendGame(MessageType.ShopState, new ShopState { Data = PackedText.Pack(json) });
        }

        private static ShopSnapshot Build(ShopReferences refs)
        {
            var snap = new ShopSnapshot();
            var sm = ShopManager.Instance;
            foreach (var slot in sm.itemPoints)
                snap.showcases.Add(slot != null && slot.itemStack != null ? new ItemSaveInfo(slot.itemStack) : new ItemSaveInfo());
            foreach (var chest in refs.shopChests)
                snap.chests.Add(chest != null ? new ChestSaveInfo(chest) : new ChestSaveInfo());
            return snap;
        }

        private static string Serialize(ShopSnapshot snap)
        {
            try
            {
                FsJSONManager.Serialize(snap, out fsData data);
                return data.ToString();
            }
            catch (Exception e)
            {
                CoopPlugin.Log.LogWarning("Could not describe the shop: " + e.Message);
                return null;
            }
        }

        private static void ApplySnapshot(ShopReferences refs, ShopSnapshot snap)
        {
            var sm = ShopManager.Instance;
            _applyingRemote = true;
            try
            {
                for (int i = 0; i < sm.itemPoints.Count && i < snap.showcases.Count; i++)
                {
                    var slot = sm.itemPoints[i];
                    var want = snap.showcases[i];
                    if (slot == null || ItemFactory.Same(slot.itemStack, want)) continue;
                    var old = slot.itemStack;
                    if (old != null)
                    {
                        slot.itemStack = null;
                        slot.SetItem(null);
                        ItemFactory.Destroy(old);
                    }
                    var stack = ItemFactory.Build(want);
                    if (stack != null) slot.SetItem(stack);
                }
                for (int c = 0; c < refs.shopChests.Count && c < snap.chests.Count; c++)
                {
                    var chest = refs.shopChests[c];
                    var want = snap.chests[c];
                    if (chest == null || want == null || want.items == null) continue;
                    var content = chest.ChestContent;
                    for (int i = 0; i < want.items.Count && i < content.Count; i++)
                    {
                        var have = content[i];
                        if (ItemFactory.Same(have, want.items[i])) continue;
                        if (have != null)
                        {
                            chest.SetItem(null, i);
                            ItemFactory.Destroy(have);
                        }
                        var stack = ItemFactory.Build(want.items[i]);
                        if (stack != null) chest.SetItem(stack, i);
                    }
                }
            }
            catch (Exception e)
            {
                CoopPlugin.Log.LogWarning("Could not apply the partner's shop: " + e.Message);
            }
            finally
            {
                _applyingRemote = false;
            }
        }

        // ---- customers drawn on the guest ----

        private static void SendVisitors(PeerSession s, ShopReferences refs)
        {
            var gen = ShopManager.Instance.visitorGenerator;
            var list = gen != null ? gen.generatedVisitors : null;
            var states = new List<EntityState>();
            if (list != null)
            {
                foreach (var agent in list)
                {
                    if (agent == null || !agent.gameObject.activeInHierarchy) continue;
                    if (!VisitorIds.TryGetValue(agent, out var id))
                    {
                        id = _nextVisitorId++;
                        if (_nextVisitorId == 0) _nextVisitorId = 1;
                        VisitorIds[agent] = id;
                    }
                    states.Add(Describe(s, agent, id));
                }
            }
            bool waiting = refs.shopCash != null && refs.shopCash.visitorBuying != null;
            s.SendFrequent(MessageType.EntityStates, new EntityStates
            {
                Group = EntityStates.ShopVisitors,
                Extra = (byte)(waiting ? 1 : 0),
                Entities = states.ToArray(),
            });
        }

        private static EntityState Describe(PeerSession s, Agent agent, ushort id)
        {
            var root = agent.transform;
            var renderers = agent.GetComponentsInChildren<SpriteRenderer>(false);
            var layers = new List<SpriteLayer>();
            for (int i = 0; i < renderers.Length && i < 255; i++)
            {
                var r = renderers[i];
                if (r == null || r.sprite == null || !r.enabled) continue;
                string key = LocalPlayerSampler.KeyOf(r.sprite);
                ushort sid = VisitorSprites.GetOrAdd(key, out bool isNew);
                if (sid == 0) continue;
                if (isNew) s.SendSpriteDef(new SpriteDef { Id = sid, Key = key });
                var local = r.transform.position - root.position;
                layers.Add(new SpriteLayer
                {
                    Slot = (byte)i,
                    SpriteId = sid,
                    Visible = true,
                    FlipX = r.flipX ^ (r.transform.lossyScale.x < 0),
                    SortingOrder = (short)Mathf.Clamp(r.sortingOrder, short.MinValue, short.MaxValue),
                    LocalX = local.x,
                    LocalY = local.y,
                });
            }
            var p = root.position;
            return new EntityState { Id = id, X = p.x, Y = p.y, Layers = layers.ToArray() };
        }
    }
}
