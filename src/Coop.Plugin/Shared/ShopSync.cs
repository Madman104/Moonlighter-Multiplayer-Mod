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
    /// <summary>
    /// Shop slots that changed, as sent between the games. Keys: "s{i}" = showcase i,
    /// "c{c}:{i}" = storage chest c, slot i. Request = "send me every slot" (guest just joined).
    /// </summary>
    [Serializable]
    public class ShopSnapshot
    {
        public bool request;
        public List<string> keys = new List<string>();
        public List<ItemSaveInfo> items = new List<ItemSaveInfo>();
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
        // Last state both games agreed on, per slot (signature). A slot that differs from it
        // here was changed here and not sent yet.
        private static readonly Dictionary<string, string> Base = new Dictionary<string, string>();
        private static readonly Dictionary<string, ItemSaveInfo> PendingRemote = new Dictionary<string, ItemSaveInfo>();
        private static bool _baseReady, _resendAll;
        private static float _lastVisitorsAt;
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
            Base.Clear();
            PendingRemote.Clear();
            _baseReady = false;
            _resendAll = false;
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
                        if (snap == null) break;
                        if (snap.request) { _resendAll = true; break; }
                        for (int i = 0; i < snap.keys.Count && i < snap.items.Count; i++)
                            PendingRemote[snap.keys[i]] = snap.items[i]; // newest wins per slot
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
                    _lastVisitorsAt = Time.realtimeSinceStartup;
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
            if (refs == null) { if (!s.IsHost) { VisitorPuppets.Clear(); _hostCashWaiting = false; } return; }
            // Before the guest has loaded the host's world, their town is their own: never mix it in.
            if (!s.IsHost && !WorldSync.InHostWorld) return;
            float now = Time.realtimeSinceStartup;

            if (s.IsHost && now >= _nextVisitors)
            {
                _nextVisitors = now + VisitorInterval;
                SendVisitors(s, refs);
            }
            // Host left town or stopped sending: no customers, nothing at the register.
            if (!s.IsHost && now - _lastVisitorsAt > 1.5f) { VisitorPuppets.Clear(); _hostCashWaiting = false; }

            if (now < _nextState) return;
            _nextState = now + StateInterval;

            var cur = Read(refs);
            if (!_baseReady)
            {
                // Both games start from the same shop (the guest loaded the host's world), so the
                // current state is the agreed one. The guest asks for every slot in case the host
                // changed something while the guest was loading.
                foreach (var kv in cur) Base[kv.Key] = Sig(kv.Value);
                _baseReady = true;
                if (!s.IsHost) Send(s, new ShopSnapshot { request = true });
                return;
            }

            // Don't change items under an open chest/showcase/price panel; apply once it closes.
            bool busy = GUIManager.Instance != null && GUIManager.Instance.currentInventory != null;
            if (PendingRemote.Count > 0 && !busy)
            {
                ApplyRemote(refs, s.IsHost, cur);
                cur = Read(refs);
            }

            var changed = new ShopSnapshot();
            foreach (var kv in cur)
            {
                string sig = Sig(kv.Value);
                if (!_resendAll && Base.TryGetValue(kv.Key, out var agreed) && agreed == sig) continue;
                Base[kv.Key] = sig;
                changed.keys.Add(kv.Key);
                changed.items.Add(kv.Value);
            }
            _resendAll = false;
            if (changed.keys.Count > 0) Send(s, changed);
        }

        private static void Send(PeerSession s, ShopSnapshot snap)
        {
            try
            {
                FsJSONManager.Serialize(snap, out fsData data);
                s.SendGame(MessageType.ShopState, new ShopState { Data = PackedText.Pack(data.ToString()) });
            }
            catch (Exception e) { CoopPlugin.Log.LogWarning("Could not send the shop: " + e.Message); }
        }

        private static string Sig(ItemSaveInfo i)
            => ItemFactory.IsEmpty(i) ? "" : $"{i.name}|{i.plusLevel}|{i.quantity}|{i.sellingPrize}|{i.enchantmentLevel}|{i.enchantmentEffectName}|{i.curseName}";

        /// <summary>Every shop slot here, by key.</summary>
        private static Dictionary<string, ItemSaveInfo> Read(ShopReferences refs)
        {
            var map = new Dictionary<string, ItemSaveInfo>();
            var sm = ShopManager.Instance;
            for (int i = 0; i < sm.itemPoints.Count; i++)
            {
                var slot = sm.itemPoints[i];
                map["s" + i] = slot != null && slot.itemStack != null ? new ItemSaveInfo(slot.itemStack) : new ItemSaveInfo();
            }
            for (int c = 0; c < refs.shopChests.Count; c++)
            {
                var chest = refs.shopChests[c];
                if (chest == null) continue;
                var content = chest.ChestContent;
                for (int i = 0; i < content.Count; i++)
                    map["c" + c + ":" + i] = content[i] != null ? new ItemSaveInfo(content[i]) : new ItemSaveInfo();
            }
            return map;
        }

        private static void ApplyRemote(ShopReferences refs, bool isHost, Dictionary<string, ItemSaveInfo> cur)
        {
            var sm = ShopManager.Instance;
            var incoming = new List<KeyValuePair<string, ItemSaveInfo>>(PendingRemote);
            PendingRemote.Clear();
            _applyingRemote = true;
            try
            {
                foreach (var kv in incoming)
                {
                    string key = kv.Key;
                    var want = kv.Value;
                    if (!cur.TryGetValue(key, out var have)) continue;
                    string localSig = Sig(have);
                    Base.TryGetValue(key, out var agreed);
                    // Changed here too and not sent yet: the host's version wins.
                    if (localSig != agreed && isHost) continue;
                    if (localSig == Sig(want)) { Base[key] = localSig; continue; }

                    ItemStack built = null;
                    if (!ItemFactory.IsEmpty(want))
                    {
                        built = ItemFactory.Build(want);
                        if (built == null)
                        {
                            // Can't make this item here (missing DLC?): keep ours, don't destroy it.
                            CoopPlugin.Log.LogWarning("Shop item not available here: " + want.name);
                            continue;
                        }
                    }
                    if (key[0] == 's')
                    {
                        int i = int.Parse(key.Substring(1));
                        if (i >= sm.itemPoints.Count || sm.itemPoints[i] == null) { ItemFactory.Destroy(built); continue; }
                        var slot = sm.itemPoints[i];
                        var old = slot.itemStack;
                        if (old != null) { slot.itemStack = null; slot.SetItem(null); ItemFactory.Destroy(old); }
                        if (built != null) slot.SetItem(built);
                    }
                    else
                    {
                        int colon = key.IndexOf(':');
                        int c = int.Parse(key.Substring(1, colon - 1));
                        int i = int.Parse(key.Substring(colon + 1));
                        var chest = c < refs.shopChests.Count ? refs.shopChests[c] : null;
                        if (chest == null || i >= chest.ChestContent.Count) { ItemFactory.Destroy(built); continue; }
                        var old = chest.ChestContent[i];
                        if (old != null) { chest.SetItem(null, i); ItemFactory.Destroy(old); }
                        if (built != null) chest.SetItem(built, i);
                    }
                    Base[key] = Sig(want);
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
