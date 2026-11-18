# Stage 4b (shared inventory) — research notes

Nothing built yet. From the decompiled `HeroMerchantInventory.cs`:

- **Change events** are static and cover every change:
  - `OnItemAddedToInventory(ItemStack, int slot)`
  - `OnItemRemovedFromInventory(ItemStack, int slot)`
  - `OnItemChanged(int slot)`
  - `OnItemEquipped` / `OnItemUnequipped(ItemStack, EquipmentSlot)`

  These are a good basis for a "bag changed" diff.
- **Slot API:** `SetItem(ItemStack, int index)`, `GetItem(int)`, `GetFreeSlot()`, plus slot states `BlockSlot`, `SecureSlot`, `SetAffectedSlot` and `DenySlot` (cursed-item mechanics).
- **Saving:** `ItemSaveInfo(ItemStack)` already exists (used by `RoomSaveInfo` for chest contents). Use it to serialize a stack (FsJSONManager), the same way as dungeon layouts.

## Suggested approach
1. The host's bag is the truth. On connect, and after every host bag change, send `BagSnapshot{slots: ItemSaveInfo json[]}` (bag only, not equipment: design says equipment is per player).
2. The guest applies the snapshot with `SetItem` under an "applying remote" flag, so it doesn't echo back.
3. Guest-side changes (pickup, move, drop, sell) are sent to the host as requests. Simplest first version: the guest sends its whole bag after a local change, and the host merges it as "guest's slot wins for slots the guest changed". A proper version would send per-slot requests.
4. Loot: the host already owns drops (stage 3a). Items the guest walks over need either host-side spawning of the same pickups on the guest's game, or "guest pickup → host adds to shared bag".
5. Still to research: the world pickup class (no class named like `*Pickup`/`LootItem` was found). Search for whatever calls `HeroMerchantInventory.SetItem`/`AddItem` on touch.
