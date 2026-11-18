using System;
using UnityEngine;

namespace Coop.Plugin.Shared
{
    /// <summary>Turns the game's ItemSaveInfo back into an item, the way the game loads a save.</summary>
    internal static class ItemFactory
    {
        public static bool IsEmpty(ItemSaveInfo i) => i == null || string.IsNullOrEmpty(i.name);

        /// <summary>True when the stack matches the description (name, level, quantity, price, enchantment).</summary>
        public static bool Same(ItemStack have, ItemSaveInfo want)
        {
            bool wantEmpty = IsEmpty(want);
            if (have == null) return wantEmpty;
            if (wantEmpty) return false;
            var now = new ItemSaveInfo(have);
            return now.name == want.name && now.plusLevel == want.plusLevel && now.quantity == want.quantity
                && now.sellingPrize == want.sellingPrize && now.enchantmentLevel == want.enchantmentLevel
                && now.curseName == want.curseName;
        }

        public static ItemStack Build(ItemSaveInfo info)
        {
            if (IsEmpty(info)) return null;
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
            stack.unitSellingPrice = info.sellingPrize;
            return stack;
        }

        public static void Destroy(ItemStack stack)
        {
            if (stack != null) UnityEngine.Object.Destroy(stack.gameObject);
        }
    }
}
