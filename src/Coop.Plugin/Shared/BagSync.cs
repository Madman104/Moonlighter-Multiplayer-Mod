using System;
using System.Collections.Generic;
using Coop.Core.Protocol;
using Coop.Core.Session;
using Coop.Plugin.Runtime;
using FullSerializer;
using UnityEngine;

namespace Coop.Plugin.Shared
{
    /// <summary>
    /// Stage 4b (first version): one shared bag. Whenever a player's bag changes, their game
    /// sends the whole bag (as the game's own ItemSaveInfo list); the other game updates only
    /// the slots that differ. On connect the host's bag wins. Equipment stays per player.
    /// Last change wins if both players change the bag at the same moment.
    /// </summary>
    internal static class BagSync
    {
        public const float SendDelay = 0.25f;

        private static bool _applyingRemote;
        private static bool _dirty;
        private static float _sendAt;
        private static bool _hooked;

        public static void Attach(PeerSession session)
        {
            session.GameMessageReceived += OnGameMessage;
            session.StateChanged += st =>
            {
                // The host's bag becomes the shared bag.
                if (st == SessionState.Connected && session.IsHost) MarkDirty();
            };
            if (_hooked) return;
            _hooked = true;
            HeroMerchantInventory.OnItemAddedToInventory += (s, i) => MarkDirty();
            HeroMerchantInventory.OnItemRemovedFromInventory += (s, i) => MarkDirty();
            HeroMerchantInventory.OnItemChanged += i => MarkDirty();
        }

        private static void MarkDirty()
        {
            if (_applyingRemote) return;
            if (!_dirty) _sendAt = Time.realtimeSinceStartup + SendDelay; // batch a burst of changes
            _dirty = true;
        }

        private static bool Connected(out PeerSession s)
        {
            s = CoopManager.Instance != null ? CoopManager.Instance.Session : null;
            return s != null && s.State == SessionState.Connected;
        }

        private static int BagSize => Constants.GetInt("bagSize");

        public static void Tick()
        {
            if (!_dirty || Time.realtimeSinceStartup < _sendAt) return;
            _dirty = false;
            var inv = HeroMerchant.Instance != null ? HeroMerchant.Instance.heroMerchantInventory : null;
            if (inv == null || !Connected(out var s)) return;
            try
            {
                var bag = new List<ItemSaveInfo>();
                for (int i = 0; i < BagSize; i++)
                {
                    var stack = inv.GetItem(i);
                    bag.Add(stack != null ? new ItemSaveInfo(stack) : new ItemSaveInfo());
                }
                FsJSONManager.Serialize(bag, out fsData data);
                s.SendGame(MessageType.BagSnapshot, new BagSnapshot { Json = data.ToString() });
            }
            catch (Exception e)
            {
                CoopPlugin.Log.LogWarning("Could not send the bag: " + e.Message);
            }
        }

        private static void OnGameMessage(MessageType type, object msg)
        {
            if (type != MessageType.BagSnapshot) return;
            var inv = HeroMerchant.Instance != null ? HeroMerchant.Instance.heroMerchantInventory : null;
            if (inv == null) return;
            List<ItemSaveInfo> bag = null;
            try { FsJSONManager.Deserialize(((BagSnapshot)msg).Json, ref bag, false); }
            catch (Exception e) { CoopPlugin.Log.LogWarning("Could not read the partner's bag: " + e.Message); return; }
            if (bag == null) return;

            _applyingRemote = true;
            try
            {
                for (int i = 0; i < BagSize && i < bag.Count; i++)
                {
                    var want = bag[i];
                    var have = inv.GetItem(i);
                    if (Same(have, want) || inv.IsSlotBlocked(i)) continue;
                    if (have != null)
                    {
                        inv.SetItem(null, i);
                        UnityEngine.Object.Destroy(have.gameObject);
                    }
                    var stack = Build(want);
                    if (stack != null) inv.SetItem(stack, i);
                }
            }
            catch (Exception e)
            {
                CoopPlugin.Log.LogWarning("Could not apply the partner's bag: " + e.Message);
            }
            finally
            {
                _applyingRemote = false;
                _dirty = false; // our bag now equals theirs
            }
        }

        private static bool Same(ItemStack have, ItemSaveInfo want)
        {
            bool wantEmpty = want == null || string.IsNullOrEmpty(want.name);
            if (have == null) return wantEmpty;
            if (wantEmpty) return false;
            var now = new ItemSaveInfo(have);
            return now.name == want.name && now.plusLevel == want.plusLevel && now.quantity == want.quantity
                && now.enchantmentLevel == want.enchantmentLevel && now.curseName == want.curseName;
        }

        /// <summary>Recreates an item from its saved description using the game's item database.</summary>
        private static ItemStack Build(ItemSaveInfo info)
        {
            if (info == null || string.IsNullOrEmpty(info.name)) return null;
            var master = ItemDatabase.GetItemByName(info.name, info.plusLevel);
            if (master == null) return null;
            var stack = ItemStack.Create(master, Math.Max(1, info.quantity));
            if (info.enchantmentLevel > 0)
            {
                var equipment = stack.GetComponent<Equipment>();
                var recipe = info.enchantmentType == 2
                    ? RecipeManager.Instance.GetEnchantmentRecipeToItemAndEffect(master.name, info.enchantmentEffectName, master.plusLevel)
                    : RecipeManager.Instance.GetEnchantment(master.name, 0, master.plusLevel);
                if (equipment != null && recipe != null)
                    for (int j = 0; j < info.enchantmentLevel; j++) equipment.EnchantWith(recipe);
            }
            return stack;
        }
    }
}
