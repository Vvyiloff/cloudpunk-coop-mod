using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using CloudpunkCoop.Net;
using CloudpunkCoop.Protocol;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CloudpunkCoop.Game
{
    public sealed class CoopInventoryItem
    {
        public string Id;
        // Native built-in saves contain -1 even for UNEQUIPPED unique/apartment/upgrades.
        // Preserve that sentinel in snapshots; only an accepted mutation normalizes it.
        public int Amount;
        // GLOBAL trader supply snapshot, never a player's owned quantity.
        public int Available;
        public bool Equipped;
    }

    public sealed class CoopInventoryState
    {
        public int Money;
        public List<CoopInventoryItem> Items = new();
        public PersonalConsumableEffects Effects = new();
        public double EffectsClock;
        public long EffectsTicks;
        public long UseSerial;
        public string LastUse = "";
        public List<ConsumableUseReceipt> Uses = new();
    }
    public sealed class ConsumableUseReceipt
    {
        public long Serial;
        public string Item="";
    }

    /// <summary>
    /// Unity main-thread API. Player1 uses native personal fields; Player2 uses a host-owned
    /// ledger. First admission clones the HOST baseline, never a client inventory payload.
    /// Later admissions resume the host sidecar. GuestState/HostState return detached copies.
    /// Parent must reset/configure this subsystem when changing campaign or save slot.
    /// </summary>
    public static class CoopInventory
    {
        [Flags]
        public enum UseProjectionRequirements
        {
            None = 0, Consumable = 1, NativeEvent = 2, Cooldown = 4,
            SpeedIndicator = 8, EyeIndicator = 16, Equipment = 32, StoryAuthority = 64
        }
        private const int MaxItems = 2048, MaxId = 256, MaxValue = 1000000000;
        private const long MaxFileBytes = 1024 * 1024;
        private static readonly JsonSerializerOptions Json = new() { IncludeFields = true, MaxDepth = 16 };
        private static CoopInventoryState _guest, _host;
        private static bool _busy, _poisoned;
        private static bool _effectsDirty;
        private static float _effectPersistAt;
        private static string _sidecar;
        private static readonly DeferredCampaignRefresh EquipmentRefresh = new("equipment");
        internal static bool EquipmentPresentationPending => EquipmentRefresh.Pending;
        internal static string LastEquipmentPresentationError => EquipmentRefresh.LastError;
        private sealed class SidecarDocument
        {
            public int Version = 1;
            public CoopInventoryState Guest;
            public Dictionary<string, int> SharedStock = new(StringComparer.Ordinal);
            public SidecarDocument() { }
        }
        public static bool IsExecutingGuest => _busy;
        public static string LastPersistenceError { get; private set; } = "";
        public static string SidecarPath => _sidecar ?? Path.Combine(BepInEx.Paths.ConfigPath,
            "CloudpunkCoop", "player2-inventory.json");
        public static CoopInventoryState GuestState => WithCurrentStock(_guest);
        public static CoopInventoryState HostState => WithCurrentStock(_host);

        // Parent should supply a distinct HOST path for each campaign/save slot before admission.
        // Only this JSON file is touched; ES2/solo save APIs are never invoked here.
        public static void ConfigureSidecar(string absolutePath)
        {
            MainThread();
            if (_busy || _guest != null) throw new InvalidOperationException("Reset before changing inventory sidecar.");
            if (string.IsNullOrWhiteSpace(absolutePath) || !Path.IsPathFullyQualified(absolutePath) ||
                !absolutePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("A dedicated absolute .json sidecar path is required.");
            _sidecar = Path.GetFullPath(absolutePath);
        }

        public static void Reset()
        {
            MainThread();
            if (_busy) throw new InvalidOperationException("Native guest operation is active.");
            _guest = null; _host = null; _sidecar = null; _poisoned = false;
            _effectsDirty=false;_effectPersistAt=0;
            LastPersistenceError = "";
            EquipmentRefresh.Reset();
        }

        public static CoopInventoryState CaptureLocal()
        {
            MainThread();
            if (_busy) throw new InvalidOperationException("Cannot publish temporary guest fields.");
            var catalog = Catalog();
            return Read(GameRefs.Global, catalog);
        }

        public static void InitializeGuest()
        {
            HostOnly();
            if (_busy || _poisoned) throw new InvalidOperationException("Inventory transaction unavailable.");
            if (_guest != null) return; // Reconnect must not refill or overwrite personal spending.
            CampaignReplication.BindInventory();
            var baseline = CaptureLocal();
            CoopInventoryState candidate = Copy(baseline);
            try
            {
            if (!TestMode && File.Exists(SidecarPath))
            {
                if (new FileInfo(SidecarPath).Length > MaxFileBytes)
                    throw new InvalidDataException("Inventory sidecar exceeds size limit.");
                using var stream = File.OpenRead(SidecarPath);
                var saved = JsonSerializer.Deserialize<SidecarDocument>(stream, Json);
                var catalog = Catalog();
                if (saved == null || saved.Version != 1 || saved.SharedStock == null || saved.SharedStock.Count != catalog.Count)
                    throw new InvalidDataException("Invalid shared inventory sidecar.");
                candidate = saved.Guest;
                Validate(candidate, catalog); // Corrupt/incompatible persistence fails closed.
                foreach (var pair in catalog)
                    if (!saved.SharedStock.TryGetValue(pair.Key, out int stock) || stock < -1 || stock > MaxValue)
                        throw new InvalidDataException("Invalid persisted shared stock.");
                // Restore only host-owned SHARED supply. Never swap supply with Player2 fields.
                foreach (var pair in catalog) pair.Value.totalAmountAvailable = saved.SharedStock[pair.Key];
            }
            }
            catch (Exception ex)
            {
                LastPersistenceError = "Sidecar load failed: " + ex.GetType().Name;
                throw;
            }
            _host = baseline;
            _guest = Copy(candidate);
            Persist();
        }

        /// <summary>Absolute replication only. No rewards, purchase/use callbacks or Equip.
        /// Preflight all fields, commit with rollback, then retry fallible native visuals.</summary>
        public static void ApplyLocal(CoopInventoryState state)
        {
            var assignments = new CampaignFieldTransaction();
            bool equipmentChanged = PrepareLocal(state, assignments);
            assignments.Commit();
            RefreshLocalPresentation(equipmentChanged);
        }

        // Contribute personal and shared fields to the SAME campaign transaction.
        // Catalog lookup, detached DTO validation and every old-field read precede writes.
        internal static bool PrepareLocal(CoopInventoryState state, CampaignFieldTransaction assignments)
        {
            MainThread();
            if (_busy || _poisoned) throw new InvalidOperationException("Inventory transaction unavailable.");
            var catalog = Catalog();
            var detached = CopyValidated(state, catalog);
            var global = GameRefs.Global;
            var before = Read(global, catalog);
            var previous = Index(before);
            bool equipmentChanged = false;
            assignments.Add(value => global.money = value, before.Money, detached.Money);
            foreach (var value in detached.Items)
            {
                var native = catalog[value.Id];
                var old = previous[value.Id];
                assignments.Add(amount => native.amount = amount, old.Amount, value.Amount);
                assignments.Add(stock => native.totalAmountAvailable = stock, old.Available, value.Available);
                assignments.Add(equipped => native.isEquipped = equipped, old.Equipped, value.Equipped);
                equipmentChanged |= value.Equipped != old.Equipped;
            }
            CoopPersonalEffects.Prepare(detached,assignments);
            return equipmentChanged;
        }

        internal static void RefreshLocalPresentation(bool equipmentChanged)
        {
            EquipmentRefresh.Run(equipmentChanged, () =>
            {
                var player = GameRefs.Player;
                if (player == null) throw new InvalidOperationException("Equipment actor unavailable");
                player.SetupEquipment();
            });
        }

        public static void ValidateLocal(CoopInventoryState state)
        {
            MainThread();
            if(_busy || _poisoned) throw new InvalidOperationException("Inventory transaction unavailable.");
            CopyValidated(state,Catalog());
        }

        /// <summary>Record personal host mutations separately. Never grants guest rewards.
        /// Call after purchases/spending/use/equip; no guest client amounts are accepted.</summary>
        public static CoopInventoryState Snapshot()
        {
            HostOnly();
            if (_busy || _poisoned) throw new InvalidOperationException("Cannot snapshot temporary guest fields.");
            _host = CaptureLocal();
            if (_guest != null) Persist(); // Host trades can change shared stock independently.
            return Copy(_host);
        }

        /// <summary>
        /// Parent calls EXACTLY ONCE around a completed, reward-producing host dialog,
        /// with CaptureLocal() taken immediately before it. Do not call for trades or personal
        /// mutations, or include them in this interval. Positive net money/owned amount
        /// deltas only; negative deltas and equipment changes stay personal. A snapshot alone
        /// cannot distinguish a sale from a story reward; the parent owns that hook boundary.
        /// </summary>
        public static void OnSharedReward(CoopInventoryState beforeLocal)
        {
            HostOnly();
            if (_busy) return; // Synchronous native guest callbacks cannot become shared rewards.
            if (_guest == null || _poisoned) throw new InvalidOperationException("Initialize guest before reward baseline capture.");
            var catalog = Catalog();
            var before = CopyValidated(beforeLocal, catalog);
            var after = Read(GameRefs.Global, catalog);
            var next = CopyValidated(_guest, catalog);
            var oldItems = Index(before);
            var newItems = Index(after);
            next.Money = Add(next.Money, Math.Max(0, after.Money - before.Money));
            foreach (var item in next.Items)
            {
                item.Amount = Add(item.Amount, Math.Max(0, newItems[item.Id].Amount - oldItems[item.Id].Amount));
                item.Available = newItems[item.Id].Available; // Shared supply is never rewarded twice.
            }
            // Validate/check overflow before committing any part of the reward.
            _guest = next; _host = after;
            Persist();
        }

        /// <summary>
        /// Host authority only; target is Item.name and trade argument is Character.GetGuid().
        /// Equip is one unit per request. Caller must deduplicate network requests.
        /// No guest OnUse, Equip, ChangeUnits or other native mutation/events run on the host.
        /// Use accepts only audited personal recipes and a fresh validated clock. Equip changes only
        /// the guest ledger; parent applies absolute fields and equipment visuals on the guest.
        /// Parent may also use IsExecutingGuest to suppress campaign hooks/save/autosave.
        /// </summary>
        public static bool ExecuteGuest(byte kind, string target, string argument, int amount, out string reason)
        {
            reason = "";
            try
            {
                HostOnly();
                if (_guest == null || _busy || _poisoned) return Reject("Guest ledger unavailable or transaction active.", out reason);
                if (kind < 3 || kind > 6 || !ValidId(target) || argument == null || argument.Length > MaxId ||
                    amount < 1 || amount > 1000) return Reject("Invalid inventory request.", out reason);
                var catalog = Catalog();
                if (!catalog.TryGetValue(target, out var item)) return Reject("Unknown item ID.", out reason);
                Validate(_guest, catalog);
                if (kind == 5 || kind == 6) return ExecuteTrade(kind, target, argument, amount, catalog, out reason);
                if (argument.Length != 0 || amount != 1) return Reject("Use/equip requires one item and empty argument.", out reason);
                var personal = Index(_guest)[target];
                if (personal.Amount < 1) return Reject("Guest does not own this item.", out reason);
                if (kind == 4 && item.equipSlot == Item.Slot.None) return Reject("Item cannot be equipped.", out reason);
                if (IsStoryItem(target)) return Reject("Story item requires parent campaign authority, not a personal mutation.", out reason);
                // ChangeUnits calls related StoryTrigger.Check(true) even with storyTriggered=false.
                // Personal recipes project timers directly instead of executing use callbacks.
                if (kind == 3)
                {
                    if(!CoopPersonalEffects.HostClockFresh || !item.isConsumable ||
                        !_guest.Effects.TryUse(target,out var effects))
                        return Reject("Unsupported consumable, stale personal clock or category cooldown exceeded.",out reason);
                    var consumed=Copy(_guest);
                    Index(consumed)[target].Amount--;
                    consumed.Effects=effects;consumed.EffectsClock=CoopPersonalEffects.HostElapsed;consumed.EffectsTicks=CoopPersonalEffects.HostTicks;
                    consumed.UseSerial=checked(consumed.UseSerial+1);consumed.LastUse=target;
                    consumed.Uses.Add(new ConsumableUseReceipt{Serial=consumed.UseSerial,Item=target});
                    if(consumed.Uses.Count>64)consumed.Uses.RemoveAt(0);
                    Validate(consumed,catalog);
                    var previousGuest=_guest;
                    _guest=consumed;
                    if(!TryPersist()) {_guest=previousGuest;return Reject("Inventory persistence failed; consumable was not committed.",out reason);}
                    reason="Personal consumable committed without host events or effects.";
                    return true;
                }

                var next = Copy(_guest);
                var nextItems = Index(next);
                var selected = nextItems[target];
                if (selected.Equipped) return Reject("Guest item is already equipped.", out reason);
                CoopInventoryItem previous = null;
                foreach (var value in next.Items)
                {
                    if (!value.Equipped || catalog[value.Id].equipSlot != item.equipSlot) continue;
                    if (previous != null) return Reject("Guest equipment slot is ambiguous.", out reason);
                    previous = value;
                }
                // Verified GameAssembly.dll Item.Equip RVA 0x792130:
                // 0x79220f compares equipSlot (0x58); first equipped item in that slot gets
                // ChangeUnits(+1) at 0x792235 and isEquipped=false at 0x79223a.
                // Target gets ChangeUnits(-1) at 0x792250, isEquipped=true at 0x792255;
                // final call 0x661770 projects equipment. Reproduce ONLY ledger arithmetic,
                // without invoking ChangeUnits/story checks or the host visual projection.
                if (previous != null)
                {
                    if (IsStoryItem(previous.Id)) return Reject("Existing slot contains a story item.", out reason);
                    previous.Amount = Add(previous.Amount, 1);
                    previous.Equipped = false;
                }
                selected.Amount--;
                selected.Equipped = true;
                foreach (var value in next.Items) value.Available = catalog[value.Id].totalAmountAvailable;
                Validate(next, catalog);
                var hostBefore = Read(GameRefs.Global, catalog);
                _busy = true;
                try
                {
                    _guest = next; _host = hostBefore;
                    Persist();
                }
                finally { _busy = false; }
                reason = "Guest equipment ledger updated; apply absolute guest fields and project guest equipment visuals. No host native events executed.";
                if (LastPersistenceError.Length != 0) reason += " Sidecar write failed; do not retry this committed operation.";
                return true;
            }
            catch (Exception ex)
            {
                return Reject("Inventory operation failed: " + ex.GetType().Name +
                    (_poisoned ? "; host restoration failed, inventory disabled." : "; no guest native event was invoked."), out reason);
            }
        }

        private static bool ValidateTrade(byte kind, string target, string guid,
            Dictionary<string, Item> catalog, out Character trader, out string reason)
        {
            trader = null;
            if (!ValidId(guid)) return Reject("Trader GUID required.", out reason);
            if (!RemoteFootAvatar.Fresh || !RemoteFootAvatar.OnFoot ||
                RemoteFootAvatar.Scene != SceneManager.GetActiveScene().buildIndex)
                return Reject("Fresh same-scene guest foot pose required.", out reason);
            var entries = NativeNpcRegistry.Scan(null);
            if (entries.Count > 8192) return Reject("Trader catalog exceeds limit.", out reason);
            foreach (var entry in entries)
            {
                if (entry.IsCrowd || !string.Equals(entry.Id, guid, StringComparison.Ordinal)) continue;
                if (trader != null) return Reject("Ambiguous trader GUID.", out reason);
                trader = entry.Character;
            }
            if (trader == null || !trader.gameObject.activeInHierarchy ||
                trader.gameObject.scene.buildIndex != RemoteFootAvatar.Scene)
                return Reject("Trader is unavailable in guest scene.", out reason);
            // Registry discovery may take time; recheck freshness after it returns.
            if (!RemoteFootAvatar.Fresh || !RemoteFootAvatar.OnFoot ||
                RemoteFootAvatar.Scene != SceneManager.GetActiveScene().buildIndex)
                return Reject("Guest foot pose expired during trader discovery.", out reason);
            float distance = Vector3.Distance(RemoteFootAvatar.LatestPosition, trader.transform.position);
            if (!float.IsFinite(distance) || distance > 5f) return Reject("Guest is outside trader range (5m).", out reason);
            if (kind == 5)
            {
                var offers = trader.itemOffers;
                if (offers == null || offers.Length > MaxItems) return Reject("Trader offers unavailable.", out reason);
                int matches = 0;
                foreach (var offer in offers) if (string.Equals(offer.itemID, target, StringComparison.Ordinal)) matches++;
                if (matches != 1) return Reject("Item offer missing or ambiguous.", out reason);
            }
            else if (!trader.buysItems || !catalog[target].canBeSold || Index(_guest)[target].Amount < 1)
                return Reject("Trader cannot buy this guest item.", out reason);
            reason = "";
            return true;
        }

        private static bool ExecuteTrade(byte kind, string target, string guid, int amount,
            Dictionary<string, Item> catalog, out string reason)
        {
            if (!ValidateTrade(kind, target, guid, catalog, out var trader, out reason)) return false;
            if (IsStoryItem(target)) return Reject("Story item trade requires parent campaign authority.", out reason);
            var native = catalog[target];
            if (!TryTradePrice(native, trader, out int unitPrice)) return Reject("Invalid or out-of-range native price.", out reason);
            var next = WithCurrentStock(_guest);
            var selected = Index(next)[target];
            int stockBefore = native.totalAmountAvailable;
            if (stockBefore < -1 || stockBefore > MaxValue) return Reject("Shared stock is outside native bounds.", out reason);
            long total = (long)unitPrice * amount;
            if (total > MaxValue) return Reject("Trade total exceeds money bounds.", out reason);
            int stockAfter;
            if (kind == 5)
            {
                // FULL OnClick RVA 0x735380..0x7357cf: stock <= 0 returns, including -1.
                // 0x7354b7 checks wallet, 0x7354bf subtracts price; 0x7354df decrements stock,
                // 0x7354fe calls ChangeUnits(+1). Never interpret -1 supply as unlimited.
                if (stockBefore < amount) return Reject("Not enough shared trader stock.", out reason);
                if (next.Money < total) return Reject("Guest cannot afford this trade.", out reason);
                selected.Amount = NativeChangedAmount(selected.Amount, amount);
                next.Money -= (int)total;
                stockAfter = stockBefore - amount;
                // OnClick also checks personal category cooldown if not displayed in inventory.
                // Personal use bypasses native events and never enters this temporary trade scope.
                // Parent's future use projection must enforce those guest cooldowns on purchases;
                // Global.CheckCooldown itself plays HOST UI/audio and must not be invoked.
            }
            else
            {
                // FULL SellItem RVA 0x790570..0x79078f: 0x790623 increments supply,
                // 0x790642 calls ChangeUnits(-1), 0x790685 adds the same native tradePrice.
                // Equipment remains equipped; only positive unequipped units can be sold.
                if (selected.Amount < amount) return Reject("Guest does not own enough sellable units.", out reason);
                if ((long)next.Money + total > MaxValue || (long)stockBefore + amount > MaxValue)
                    return Reject("Sale exceeds money or shared stock bounds.", out reason);
                selected.Amount = NativeChangedAmount(selected.Amount, -amount);
                next.Money += (int)total;
                stockAfter = stockBefore + amount; // Native incl also changes supply -1 to 0.
            }
            selected.Available = stockAfter;
            Validate(next, catalog);
            var hostBefore = Read(GameRefs.Global, catalog);
            var guestBefore = _guest;
            var hostLedgerBefore = _host;
            _busy = true;
            try
            {
                try
                {
                    // All arithmetic/identity checks complete before this single native field write.
                    // No callbacks, money swaps, personal host writes, UI or native trade methods.
                    native.totalAmountAvailable = stockAfter;
                    _guest = next;
                    _host = hostBefore;
                }
                catch
                {
                    _guest = guestBefore; _host = hostLedgerBefore;
                    try { native.totalAmountAvailable = stockBefore; }
                    catch { _poisoned = true; throw; }
                    throw;
                }
                Persist(); // Failure is observable; accepted mutations must never be replayed.
            }
            finally { _busy = false; }
            reason = "Guest " + (kind == 5 ? "purchase" : "sale") + " committed at native unit price " + unitPrice +
                "; replicate personal ledger/shared stock. No host native events.";
            if (LastPersistenceError.Length != 0) reason += " Sidecar write failed; do not retry.";
            return true;
        }

        private static bool TryTradePrice(Item item, Character trader, out int price)
        {
            // TradeOffer.Initialize 0x734f0b..0x734f49 and InventoryItem.Select
            // 0x78d4ce..0x78d511 both use forceBasePrice ? basePrice :
            // cvtdq2ps(basePrice), mulss(priceFactor), cvttss2si (truncate toward zero).
            // There is NO separate sale multiplier. Keep both conversion and product float32.
            price = 0;
            if (item.basePrice < 0 || item.basePrice > MaxValue) return false;
            if (item.forceBasePrice) { price = item.basePrice; return true; }
            float factor = trader.priceFactor;
            if (!float.IsFinite(factor) || factor < 0) return false;
            float product = (float)item.basePrice * factor;
            if (!float.IsFinite(product) || product < 0 || (double)product > MaxValue) return false;
            price = (int)product;
            return true;
        }

        private static int NativeChangedAmount(int value, int delta)
        {
            // ChangeUnits 0x790ca9 adds delta; 0x790caf cmovs selects zero on negative result.
            // This proves a buy from -1 yields 0, NOT 1; preserve snapshots until mutation.
            long changed = (long)value + delta;
            if (changed > MaxValue) throw new InvalidDataException("Owned quantity exceeds bounds.");
            return (int)Math.Max(0, changed);
        }

        /// <summary>Read-only metadata hints for the parent's future guest use projection.
        /// Flags do not enumerate UnityEvent runtime listeners or authorize native host use.
        /// Only the audited recipe allowlist and validated personal clock authorize use.</summary>
        public static UseProjectionRequirements GetUseProjectionRequirements(string id)
        {
            MainThread();
            if (!ValidId(id) || !Catalog().TryGetValue(id, out var item)) throw new ArgumentException("Unknown item ID.");
            var flags = UseProjectionRequirements.None;
            if (item.isConsumable) flags |= UseProjectionRequirements.Consumable;
            if (item.useEvent != null) flags |= UseProjectionRequirements.NativeEvent;
            if (item.cooldown != 0) flags |= UseProjectionRequirements.Cooldown;
            if (item.displaySpeedGainIcon) flags |= UseProjectionRequirements.SpeedIndicator;
            if (item.displayEyeIcon) flags |= UseProjectionRequirements.EyeIndicator;
            if (item.equipSlot != Item.Slot.None) flags |= UseProjectionRequirements.Equipment;
            if (IsStoryItem(id)) flags |= UseProjectionRequirements.StoryAuthority;
            return flags;
        }

        /// <summary>Explicit campaign references, not Item.Category heuristics. Missing or
        /// excessive native metadata fails closed. Parent can use this for story/personal routing.</summary>
        public static bool IsStoryItem(string id)
        {
            MainThread();
            if (!ValidId(id)) throw new ArgumentException("Invalid item ID.");
            var global = GameRefs.Global;
            if (global == null || global.triggers == null || global.Dialogs == null ||
                global.triggers.Count > 32768 || global.Dialogs.Count > 8192)
                throw new InvalidOperationException("Story item metadata unavailable.");
            foreach (var trigger in global.triggers)
            {
                if (trigger == null) throw new InvalidDataException("Missing story trigger.");
                if ((trigger.requiredItem != null && string.Equals(trigger.requiredItem.name, id, StringComparison.Ordinal)) ||
                    (trigger.giveOrTakeItem != null && string.Equals(trigger.giveOrTakeItem.name, id, StringComparison.Ordinal))) return true;
            }
            int count = 0;
            foreach (var dialog in global.Dialogs)
            {
                if (dialog == null || dialog.lines == null || dialog.lines.Count > 8192)
                    throw new InvalidDataException("Missing or excessive dialog item metadata.");
                foreach (var line in dialog.lines)
                {
                    if (++count > 131072 || line == null) throw new InvalidDataException("Invalid dialog item metadata.");
                    if (string.Equals(line.itemGive, id, StringComparison.Ordinal)) return true;
                }
            }
            return false;
        }

        private static Dictionary<string, Item> Catalog()
        {
            var global = GameRefs.Global;
            if (global == null || global.isLoadingSaveGame || global.inventory == null ||
                global.inventory.Length < 1 || global.inventory.Length > MaxItems)
                throw new InvalidOperationException("Native inventory unavailable.");
            var result = new Dictionary<string, Item>(StringComparer.Ordinal);
            foreach (var item in global.inventory)
                if (item == null || !ValidId(item.name) || !result.TryAdd(item.name, item))
                    throw new InvalidDataException("Invalid or ambiguous native item identity.");
            return result;
        }

        private static CoopInventoryState Read(Global global, Dictionary<string, Item> catalog)
        {
            var result = new CoopInventoryState { Money = global.money };
            foreach (var pair in catalog)
                result.Items.Add(new CoopInventoryItem { Id = pair.Key, Amount = pair.Value.amount,
                    Available = pair.Value.totalAmountAvailable, Equipped = pair.Value.isEquipped });
            Validate(result, catalog);
            return result;
        }

        private static void Write(Global global, Dictionary<string, Item> catalog, CoopInventoryState state, bool includeSharedStock = true)
        {
            // Absolute personal/shared replication fields only. No native events anywhere here.
            global.money = state.Money;
            foreach (var value in state.Items)
            {
                var native = catalog[value.Id];
                native.amount = value.Amount;
                if (includeSharedStock) native.totalAmountAvailable = value.Available;
                native.isEquipped = value.Equipped;
            }
        }

        private static void Validate(CoopInventoryState state, Dictionary<string, Item> catalog)
        {
            if (state == null || state.Money < 0 || state.Money > MaxValue || state.Items == null ||
                state.Items.Count > MaxItems || state.Items.Count != catalog.Count)
                throw new InvalidDataException("Invalid inventory state bounds/catalog.");
            if(state.Effects==null || !double.IsFinite(state.EffectsClock) || state.EffectsClock<0 || state.EffectsClock>PersonalClockMessage.MaxElapsedScaledSeconds ||
                state.EffectsTicks<0 || state.UseSerial<0 || state.LastUse==null || state.LastUse.Length>256)
                throw new InvalidDataException("Invalid personal effect state.");
            if(state.Uses==null || state.Uses.Count>64)throw new InvalidDataException("Invalid consumable receipt count.");
            long serial=0;
            foreach(var use in state.Uses)
            {
                if(use==null || use.Serial<=serial || use.Serial>state.UseSerial ||
                    !new PersonalConsumableEffects().TryUse(use.Item,out _))throw new InvalidDataException("Invalid consumable receipt.");
                serial=use.Serial;
            }
            if(state.UseSerial!=serial || serial>0 && state.Uses[^1].Item!=state.LastUse)
                throw new InvalidDataException("Consumable receipt watermark mismatch.");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var value in state.Items)
                if (value == null || !ValidId(value.Id) || !seen.Add(value.Id) || !catalog.ContainsKey(value.Id) ||
                    value.Amount < -1 || value.Amount > MaxValue || value.Available < -1 || value.Available > MaxValue)
                    throw new InvalidDataException("Invalid item identity or quantity: "+value?.Id+" amount="+value?.Amount+" stock="+value?.Available);
        }

        private static CoopInventoryState CopyValidated(CoopInventoryState state, Dictionary<string, Item> catalog)
        { Validate(state, catalog); return Copy(state); }

        private static CoopInventoryState Copy(CoopInventoryState state)
        {
            var copy = new CoopInventoryState { Money = state.Money,Effects=state.Effects,
                EffectsClock=state.EffectsClock,EffectsTicks=state.EffectsTicks,UseSerial=state.UseSerial,LastUse=state.LastUse };
            foreach(var use in state.Uses)copy.Uses.Add(new ConsumableUseReceipt{Serial=use.Serial,Item=use.Item});
            foreach (var item in state.Items) copy.Items.Add(new CoopInventoryItem { Id = item.Id,
                Amount = item.Amount, Available = item.Available, Equipped = item.Equipped });
            return copy;
        }

        private static CoopInventoryState WithCurrentStock(CoopInventoryState state)
        {
            if (state == null) return null;
            MainThread();
            var catalog = Catalog();
            var copy = CopyValidated(state, catalog);
            if(ReferenceEquals(state,_guest)){copy.EffectsClock=CoopPersonalEffects.HostElapsed;copy.EffectsTicks=CoopPersonalEffects.HostTicks;}
            foreach (var value in copy.Items) value.Available = catalog[value.Id].totalAmountAvailable;
            Validate(copy, catalog);
            return copy;
        }

        private static Dictionary<string, CoopInventoryItem> Index(CoopInventoryState state)
        {
            var result = new Dictionary<string, CoopInventoryItem>(StringComparer.Ordinal);
            foreach (var item in state.Items) result.Add(item.Id, item);
            return result;
        }

        private static int Add(int value, int delta)
        {
            long sum = (long)value + delta;
            if (sum < -1 || sum > MaxValue) throw new InvalidDataException("Inventory reward exceeds limit.");
            return (int)sum;
        }

        private static bool ValidId(string id) => !string.IsNullOrWhiteSpace(id) && id.Length <= MaxId && id.IndexOf('\0') < 0;
        private static bool TestMode => string.Equals(Environment.GetEnvironmentVariable("CLOUDPUNK_COOP_TEST"), "true", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(Environment.GetEnvironmentVariable("CLOUDPUNK_COOP_TEST_PERSIST_INVENTORY"),"true",StringComparison.OrdinalIgnoreCase);
        private static bool Reject(string text, out string reason) { reason = text.Length <= 256 ? text : text.Substring(0, 256); return false; }
        private static void MainThread()
        {
            if (!UnityEngine.Object.CurrentThreadIsMainThread()) throw new InvalidOperationException("Unity main thread required.");
        }
        private static void HostOnly()
        {
            MainThread();
            if (CoopSession.Role != CoopRole.Host) throw new InvalidOperationException("Host authority required.");
        }

        private static void Persist()
        {TryPersist();}

        private static bool TryPersist()
        {
            if (TestMode) { LastPersistenceError = ""; return true; }
            string temporary = null;
            try
            {
                string path = SidecarPath;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    var document = new SidecarDocument { Guest = WithCurrentStock(_guest) };
                    foreach (var pair in Catalog()) document.SharedStock.Add(pair.Key, pair.Value.totalAmountAvailable);
                    JsonSerializer.Serialize(stream, document, Json);
                    stream.Flush(true);
                }
                if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
                else File.Move(temporary, path);
                LastPersistenceError = "";
                _effectsDirty=false;
            }
            catch (Exception ex) { LastPersistenceError = "Sidecar write failed: " + ex.GetType().Name; }
            finally
            {
                if (temporary != null)
                    try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
            }
            return LastPersistenceError.Length==0;
        }
        internal static void AdvancePersonalClock(double delta,double lastDelta,bool hadTicks)
        {
            HostOnly();
            if(_guest==null || !double.IsFinite(delta) || delta<0 || delta>PersonalConsumableEffects.MaxTimerSeconds)
                throw new InvalidDataException("Personal clock advance unavailable or excessive.");
            // Tick identity distinguishes a paused heartbeat from an eligible
            // zero-time native update, which can reset alcohol passed time.
            if(!hadTicks)return;
            var effects=_guest.Effects;
            bool active=effects.Food>0 || effects.Drink>0 || effects.Drug>0 || effects.SpeedLeft>0 || effects.AlcoholLeft>0 || effects.AlcoholPassed>0;
            effects=effects.AdvanceTicks((float)delta,(float)lastDelta,true);
            _guest.Effects=effects;
            _effectsDirty|=active;
        }
        internal static void FlushPersonalEffects(float now,bool force=false)
        {
            if(CoopSession.Role!=CoopRole.Host || _guest==null || !_effectsDirty || !force && now<_effectPersistAt)return;
            _effectPersistAt=now+5;
            TryPersist();
        }
    }
}
