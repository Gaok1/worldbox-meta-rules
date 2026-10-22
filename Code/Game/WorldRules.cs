using System;
using System.Collections.Generic;
using MetaRules.Core;
using Newtonsoft.Json;
using UnityEngine;

namespace MetaRules.Game
{
    public sealed class MetaHandle
    {
        public MetaKind Kind;
        public BaseSystemData Data;
        public object Target;
        public long Id => Data.id;
        public string Key => Kind + ":" + Id;
        public string Name => Target is Item item ? item.getName() : string.IsNullOrEmpty(Data.name) ? "#" + Id : Data.name;
    }

    public sealed class ActorSubject : ISubject
    {
        readonly Actor actor;
        public ActorSubject(Actor value) { actor = value; }
        public long Id => actor.id;
        public long MemberOf(MetaKind kind) => WorldRules.Member(actor, kind)?.Id ?? -1;
        public bool HasTrait(string id) => actor.hasTrait(id);
        public bool HasEquipment(long id)
        {
            if (actor.equipment != null)
                foreach (Item item in actor.equipment.getItems()) if (item.data.id == id) return true;
            return false;
        }
        public bool AddTrait(string id)
        {
            // Do not remove conflicting traits (addTrait silently drops traits_to_remove)
            // or execute a partial conflicting addition.
            ActorTrait trait = AssetManager.traits.get(id);
            return trait != null && Opposites(id)?.Count == 0 && !actor.hasOppositeTrait(trait) && actor.addTrait(trait, false);
        }
        public void RemoveTrait(string id) => actor.removeTrait(id);
        public List<string> Opposites(string id)
        {
            var held = new List<string>();
            if (AssetManager.traits.get(id) == null) return held;
            foreach (ActorTrait other in new List<ActorTrait>(actor.getTraits()))
            {
                if (!WorldRules.Opposed(id, other.id)) continue;
                if (!other.can_be_removed) return null;
                held.Add(other.id);
            }
            return held;
        }

        public string SlotOf(string id)
        {
            EquipmentAsset asset = AssetManager.items.get(id);
            if (asset == null || !asset.can_be_given || actor.equipment == null || !actor.canUseItems() || !actor.asset.canEditItem(asset)) return null;
            return asset.equipment_type.ToString();
        }

        static bool TryType(string slot, out EquipmentType type) => Enum.TryParse(slot, out type);

        public long Equipped(string slot, out string item)
        {
            item = null;
            if (actor.equipment == null || !TryType(slot, out EquipmentType type)) return -1;
            ActorEquipmentSlot place = actor.equipment.getSlot(type);
            if (place.isEmpty()) return -1;
            item = place.getItem().getAsset()?.id;
            return place.getItem().data.id;
        }

        public long Equip(string id)
        {
            EquipmentAsset asset = AssetManager.items.get(id);
            if (asset == null || actor.equipment == null) return -1;
            ActorEquipmentSlot place = actor.equipment.getSlot(asset.equipment_type);
            if (!place.canChangeSlot()) return -1;
            Item displaced = place.isEmpty() ? null : place.getItem();
            Item item = World.world.items.generateItem(asset, actor.kingdom, World.world.map_stats.player_name, 1, actor, 0, true);
            if (displaced != null)
            {
                place.takeAwayItem();
                // Like a native upgrade, the previous item returns to the city instead of vanishing.
                if (actor.city != null && actor.city.isAlive())
                {
                    actor.city.data.equipment.addItem(actor.city, displaced);
                    actor.city._storage_version++;
                }
            }
            actor.equipment.setItem(item, actor);
            actor.setStatsDirty();
            return item.data.id;
        }

        public void Unequip(string slot)
        {
            if (actor.equipment == null || !TryType(slot, out EquipmentType type)) return;
            actor.equipment.getSlot(type).takeAwayItem();
            actor.setStatsDirty();
        }
    }

    public sealed class ActorState
    {
        public Actor Actor;
        public TraitLedger Ledger;
        public Dictionary<string, float> Stats = new Dictionary<string, float>();
    }

    // Rules live on their target meta; trait provenance lives on each affected actor.
    // Both use vanilla custom save data, with no external files or cross-world IDs.
    public static class WorldRules
    {
        public const string SaveKey = "gaok1_meta_rules_v1";
        const string LedgerKey = "gaok1_meta_traits_v1";
        static readonly Dictionary<string, RuleSet> rules = new Dictionary<string, RuleSet>();
        static readonly Dictionary<long, ActorState> actors = new Dictionary<long, ActorState>();
        static readonly HashSet<string> broken = new HashSet<string>();
        static MapStats world;
        static List<Actor> pending = new List<Actor>();
        static int cursor;
        static float nextScan;
        public static bool Mutating { get; private set; }
        public static bool Ready { get; private set; }
        public static int BlockedAdmissions { get; private set; }
        public static readonly List<MetaHandle> Metas = new List<MetaHandle>();
        public static string LastError { get; private set; }

        public static MetaHandle Handle(MetaKind kind, object target, BaseSystemData data) =>
            data == null ? null : new MetaHandle { Kind = kind, Target = target, Data = data };

        public static MetaHandle Member(Actor a, MetaKind kind)
        {
            if (a == null) return null;
            switch (kind)
            {
                case MetaKind.Army: return Handle(kind, a.army, a.army?.data);
                case MetaKind.Clan: return Handle(kind, a.clan, a.clan?.data);
                case MetaKind.Culture: return Handle(kind, a.culture, a.culture?.data);
                case MetaKind.Religion: return Handle(kind, a.religion, a.religion?.data);
                case MetaKind.City: return Handle(kind, a.city, a.city?.data);
                case MetaKind.Kingdom: return Handle(kind, a.kingdom, a.kingdom?.data);
                case MetaKind.Language: return Handle(kind, a.language, a.language?.data);
                case MetaKind.Family: return Handle(kind, a.family, a.family?.data);
                case MetaKind.Subspecies: return Handle(kind, a.subspecies, a.subspecies?.data);
                default: return null;
            }
        }

        public static RuleSet Get(MetaHandle meta)
        {
            if (meta == null) return null;
            if (broken.Contains(meta.Key)) return null;
            if (rules.TryGetValue(meta.Key, out RuleSet known)) return known;
            meta.Data.get(SaveKey, out string json, null);
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                var value = JsonConvert.DeserializeObject<RuleSet>(json);
                if (value != null && value.Banned == null) value.Banned = new List<long>();
                if (!RuleEngine.Valid(value))
                    throw new InvalidOperationException("Unsupported rule data: " + meta.Key);
                rules[meta.Key] = value;
                return value;
            }
            catch (Exception e)
            {
                if (broken.Add(meta.Key)) Report(e);
                return null;
            }
        }

        public static bool IsBroken(MetaHandle meta) => meta != null && broken.Contains(meta.Key);

        public static void Save(MetaHandle meta, RuleSet value)
        {
            if (meta == null || IsBroken(meta) || !RuleEngine.Valid(value)) return;
            if (!ReferenceEquals(world, World.world?.map_stats) || !Metas.Exists(m => m.Target == meta.Target && m.Id == meta.Id)) return;
            meta.Data.set(SaveKey, JsonConvert.SerializeObject(value));
            rules[meta.Key] = value;
            RescanSoon();
        }

        public static void Reset()
        {
            Ready = false;
            rules.Clear(); actors.Clear(); broken.Clear(); Metas.Clear(); pending.Clear();
            world = null; cursor = 0; nextScan = 0; BlockedAdmissions = 0; LastError = null;
        }

        public static void RescanSoon() { nextScan = 0; }

        public static void Tick()
        {
            if (World.world == null || World.world.units == null || !Config.game_loaded) return;
            if (SmoothLoader.isLoading()) { Ready = false; return; }
            if (!ReferenceEquals(world, World.world.map_stats))
            {
                Reset();
                world = World.world.map_stats;
                nextScan = Time.realtimeSinceStartup + 1f;
            }
            if (Time.realtimeSinceStartup >= nextScan && cursor >= pending.Count)
            {
                RefreshMetas();
                pending = new List<Actor>(World.world.units.getSimpleList());
                cursor = 0;
                Ready = true;
                nextScan = Time.realtimeSinceStartup + 2f;
                var live = new HashSet<long>();
                foreach (Actor a in pending) if (a != null && a.isAlive()) live.Add(a.id);
                foreach (long id in new List<long>(actors.Keys)) if (!live.Contains(id)) actors.Remove(id);
            }
            // Keep work bounded per frame, including large worlds and high simulation speed.
            int budget = 96;
            while (cursor < pending.Count && budget-- > 0)
            {
                Actor actor = pending[cursor++];
                if (actor != null && actor.isAlive()) Reconcile(actor, true);
            }
        }

        public static void RefreshMetas()
        {
            Metas.Clear();
            foreach (Army m in World.world.armies) Add(Handle(MetaKind.Army, m, m.data));
            foreach (Clan m in World.world.clans) Add(Handle(MetaKind.Clan, m, m.data));
            foreach (Culture m in World.world.cultures) Add(Handle(MetaKind.Culture, m, m.data));
            foreach (Religion m in World.world.religions) Add(Handle(MetaKind.Religion, m, m.data));
            foreach (City m in World.world.cities) Add(Handle(MetaKind.City, m, m.data));
            foreach (Kingdom m in World.world.kingdoms) Add(Handle(MetaKind.Kingdom, m, m.data));
            foreach (Language m in World.world.languages) Add(Handle(MetaKind.Language, m, m.data));
            foreach (Family m in World.world.families) Add(Handle(MetaKind.Family, m, m.data));
            foreach (Subspecies m in World.world.subspecies) Add(Handle(MetaKind.Subspecies, m, m.data));
            foreach (Item m in World.world.items)
                if (!m.shouldbe_removed) Add(Handle(MetaKind.Equipment, m, m.data));
            Metas.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        }

        static void Add(MetaHandle meta) { if (meta != null) Metas.Add(meta); }

        public static List<RuleSet> Applicable(Actor actor)
        {
            var result = new List<RuleSet>();
            foreach (MetaKind kind in Enum.GetValues(typeof(MetaKind)))
            {
                RuleSet set = Get(Member(actor, kind));
                if (set != null && set.Enabled) result.Add(set);
            }
            return result;
        }

        static Evaluation Effects(Actor actor) => RuleEngine.Evaluate(new ActorSubject(actor), Applicable(actor), EquipmentRules(actor), Opposed);

        // 'freed': the slot of an item about to be handed over, whose current item leaves.
        static IEnumerable<RuleSet> EquipmentRules(Actor actor, Item freed = null)
        {
            if (actor.equipment == null) yield break;
            EquipmentAsset replaced = freed?.getAsset();
            foreach (Item item in actor.equipment.getItems())
            {
                if (replaced != null && item.getAsset()?.equipment_type == replaced.equipment_type) continue;
                RuleSet set = Get(Handle(MetaKind.Equipment, item, item.data));
                if (set != null) yield return set;
            }
        }

        // Inspect actual slots: a creature can wear several different rule sources.
        public static bool Belongs(Actor actor, MetaHandle target) => target != null &&
            (target.Kind == MetaKind.Equipment ? new ActorSubject(actor).HasEquipment(target.Id) : Member(actor, target.Kind)?.Id == target.Id);

        static void EnforceEquipment(Actor actor)
        {
            if (actor.equipment == null) return;
            var subject = new ActorSubject(actor);
            foreach (ActorEquipmentSlot slot in actor.equipment)
            {
                Item item = slot.getItem();
                if (!RuleEngine.RequiresExit(subject, Get(Handle(MetaKind.Equipment, item, item.data)))) continue;
                UnequipToStorage(actor, slot);
            }
        }

        static void UnequipToStorage(Actor actor, ActorEquipmentSlot slot)
        {
            Item item = slot.getItem();
            slot.takeAwayItem();
            if (actor.city != null && actor.city.isAlive())
            {
                actor.city.data.equipment.addItem(actor.city, item);
                actor.city._storage_version++;
            }
            actor.setStatsDirty();
        }

        static ActorState State(Actor actor)
        {
            if (actors.TryGetValue(actor.id, out ActorState state) && state.Actor == actor) return state;
            actor.data.get(LedgerKey, out string json, null);
            TraitLedger ledger = string.IsNullOrEmpty(json) ? new TraitLedger() : JsonConvert.DeserializeObject<TraitLedger>(json);
            if (ledger?.Owned == null) throw new InvalidOperationException("Invalid trait provenance for actor " + actor.id);
            if (ledger.Items == null) ledger.Items = new Dictionary<string, ItemGrant>();
            if (ledger.Served == null) ledger.Served = new Dictionary<string, List<string>>();
            state = new ActorState { Actor = actor, Ledger = ledger };
            actors[actor.id] = state;
            return state;
        }

        public static void Reconcile(Actor actor, bool enforceMembership = false)
        {
            if (!Ready || Mutating || actor?.data == null || !actor.isAlive()) return;
            try
            {
                if (enforceMembership)
                {
                    Mutating = true;
                    EnforceEquipment(actor);
                    EnforceMembership(actor);
                    GrantMemberships(actor);
                    EnforceEquipment(actor);
                    Mutating = false;
                }
                Evaluation desired = Effects(actor);
                if (desired.Empty && !actors.ContainsKey(actor.id))
                {
                    actor.data.get(LedgerKey, out string savedLedger, null);
                    if (string.IsNullOrEmpty(savedLedger)) return;
                }
                ActorState state = State(actor);
                Mutating = true;
                RuleEngine.Reconcile(new ActorSubject(actor), desired, state.Ledger, Main.ReplaceOpposedTraits);
                if (!state.Ledger.IsEmpty()) actor.data.set(LedgerKey, JsonConvert.SerializeObject(state.Ledger));
                else actor.data.removeString(LedgerKey);
                if (!SameStats(state.Stats, desired.Stats))
                {
                    state.Stats = desired.Stats;
                    actor.setStatsDirty();
                }
            }
            catch (Exception e) { if (broken.Add("actor:" + actor.id)) Report(e); }
            finally { Mutating = false; }
        }

        // The scan runs after construction/loading, rather than in the middle of a
        // setter. Use native departure methods so captains, chiefs and rulers update.
        static void EnforceMembership(Actor actor)
        {
            if (actor.asset == null || actor.subspecies == null) return;
            var subject = new ActorSubject(actor);
            foreach (MetaKind kind in Enum.GetValues(typeof(MetaKind)))
            {
                MetaHandle meta = Member(actor, kind);
                if (meta == null || !RuleEngine.RequiresExit(subject, Get(meta))) continue;
                switch (kind)
                {
                    case MetaKind.Army:
                        Army oldArmy = actor.army;
                        actor.removeFromArmy();
                        // The native manager rebuilds this cache later. Update the
                        // departed member now so the army window is correct even paused.
                        oldArmy.units.Remove(actor);
                        break;
                    case MetaKind.Clan:
                        actor.clan.tryForgetChief(actor);
                        actor.setClan(null);
                        break;
                    case MetaKind.Culture: actor.setCulture(null); break;
                    case MetaKind.Religion: actor.setReligion(null); break;
                    case MetaKind.Language: actor.joinLanguage(null); break;
                    case MetaKind.Family: actor.setFamily(null); break;
                    case MetaKind.City:
                    case MetaKind.Kingdom:
                        actor.removeFromPreviousFaction();
                        actor.setDefaultKingdom();
                        break;
                    case MetaKind.Subspecies:
                        // A creature cannot have a null subspecies. Transfer only to a
                        // compatible alternative of its existing species, if one exists.
                        foreach (Subspecies alternative in World.world.subspecies)
                        {
                            if (alternative == actor.subspecies || alternative.data.species_id != actor.subspecies.data.species_id) continue;
                            if (RuleEngine.RequiresExit(subject, Get(Handle(kind, alternative, alternative.data)))) continue;
                            actor.setSubspecies(alternative);
                            break;
                        }
                        break;
                }
            }
        }

        public static MetaHandle Find(MetaKind kind, long id)
        {
            MetaHandle meta;
            switch (kind)
            {
                case MetaKind.Army: { Army m = World.world.armies.get(id); meta = Handle(kind, m, m?.data); break; }
                case MetaKind.Clan: { Clan m = World.world.clans.get(id); meta = Handle(kind, m, m?.data); break; }
                case MetaKind.Culture: { Culture m = World.world.cultures.get(id); meta = Handle(kind, m, m?.data); break; }
                case MetaKind.Religion: { Religion m = World.world.religions.get(id); meta = Handle(kind, m, m?.data); break; }
                case MetaKind.City: { City m = World.world.cities.get(id); meta = Handle(kind, m, m?.data); break; }
                case MetaKind.Kingdom: { Kingdom m = World.world.kingdoms.get(id); meta = Handle(kind, m, m?.data); break; }
                case MetaKind.Language: { Language m = World.world.languages.get(id); meta = Handle(kind, m, m?.data); break; }
                case MetaKind.Family: { Family m = World.world.families.get(id); meta = Handle(kind, m, m?.data); break; }
                case MetaKind.Subspecies: { Subspecies m = World.world.subspecies.get(id); meta = Handle(kind, m, m?.data); break; }
                case MetaKind.Equipment: { Item m = World.world.items.get(id); meta = m == null || m.shouldbe_removed ? null : Handle(kind, m, m.data); break; }
                default: return null;
            }
            return meta != null && meta.Target is NanoObject nano && !nano.isRekt() ? meta : null;
        }

        // Declared memberships ("everyone in this kingdom speaks Latin", "every clan member serves
        // in this army and lives in that city"). A grant only happens when the game itself would
        // allow it and no requirement — of the target or of any group the creature stays in —
        // would be broken by it. Cities and kingdoms go first, so an army in the granted city works.
        static readonly MetaKind[] GrantOrder =
        { MetaKind.City, MetaKind.Kingdom, MetaKind.Army, MetaKind.Clan, MetaKind.Culture, MetaKind.Religion, MetaKind.Language };

        static void GrantMemberships(Actor actor)
        {
            Evaluation desired = Effects(actor);
            if (desired.Metas.Count == 0) return;
            foreach (MetaKind kind in GrantOrder)
            {
                if (!desired.Metas.TryGetValue(kind, out long id)) continue;
                // A granted city already decides the kingdom; never let the two pull apart.
                if (kind == MetaKind.Kingdom && desired.Metas.ContainsKey(MetaKind.City)) continue;
                Plan(actor, kind, id)?.Invoke();
            }
        }

        // What joining would take for this creature, or null when it is not allowed.
        static Action Plan(Actor actor, MetaKind kind, long id, bool manual = false)
        {
            if (actor?.data == null || !actor.isAlive() || Member(actor, kind)?.Id == id || actor.subspecies == null || actor.asset == null || actor.asset.is_boat) return null;
            MetaHandle target = Find(kind, id);
            if (target == null) return null;
            var subject = new ActorSubject(actor);
            var changes = new Dictionary<MetaKind, long> { { kind, id } };
            var targets = new List<RuleSet> { Get(target) };
            Action apply;
            switch (target.Target)
            {
                case Family family when manual:
                    if (family.data.species_id != actor.subspecies.data.species_id) return null;
                    apply = () => actor.setFamily(family);
                    break;
                case Subspecies subspecies when manual:
                    if (subspecies.data.species_id != actor.subspecies.data.species_id) return null;
                    apply = () => actor.setSubspecies(subspecies);
                    break;
                case Item item when manual:
                    if (!CanWear(actor, item) || item.getActor() == actor) return null;
                    // A cursed item does not let go of its current wearer.
                    if (item.getActor() != null && item.isCursed()) return null;
                    if (!actor.equipment.getSlot(item.getAsset().equipment_type).canChangeSlot()) return null;
                    apply = () => HandOver(actor, item);
                    break;
                case Language language:
                    if (!Convertible(actor) || !actor.subspecies.has_advanced_communication) return null;
                    apply = () => actor.joinLanguage(language);
                    break;
                case Culture culture:
                    if (!Convertible(actor) || !actor.subspecies.has_advanced_memory) return null;
                    apply = () =>
                    {
                        bool converted = actor.hasCulture();
                        actor.setCulture(culture);
                        if (converted) culture.countConversion();
                    };
                    break;
                case Religion religion:
                    if (!Convertible(actor) || !actor.subspecies.has_advanced_memory) return null;
                    apply = () => { actor.setReligion(religion); religion.countConversion(); };
                    break;
                case Clan clan:
                    if (!Convertible(actor) || !actor.subspecies.has_advanced_memory || clan.isFull() || actor.isKing() ||
                        !string.IsNullOrEmpty(clan.data.original_actor_asset) && clan.data.original_actor_asset != actor.asset.id) return null;
                    apply = () => { actor.clan?.tryForgetChief(actor); actor.setClan(clan); };
                    break;
                case City city:
                    if (!CanMoveTo(actor, city)) return null;
                    AddMove(actor, city, changes, targets);
                    apply = () => MoveTo(actor, city);
                    break;
                case Kingdom kingdom:
                    // Live in the capital, or any city of the kingdom that accepts the creature.
                    City home = null;
                    foreach (City candidate in Candidates(kingdom))
                        if (CanMoveTo(actor, candidate)) { home = candidate; break; }
                    if (home == null) return null;
                    changes.Clear();
                    AddMove(actor, home, changes, targets);
                    apply = () => MoveTo(actor, home);
                    break;
                case Army army:
                    // Armies belong to one city: only its residents can serve, as warriors.
                    City barracks = army.getCity();
                    if (barracks == null || actor.city != barracks || actor.isKing() || actor.isCityLeader() || actor.isBaby()) return null;
                    bool warrior = actor.isWarrior();
                    if (!warrior && !barracks.checkCanMakeWarrior(actor)) return null;
                    apply = () =>
                    {
                        if (!actor.isWarrior()) barracks.makeWarrior(actor);
                        actor.setArmy(army);
                    };
                    break;
                default: return null;
            }
            Item given = target.Target as Item;
            return RuleEngine.CanMove(subject, changes, targets, Kept(actor, changes, given), Settled(actor, changes, given)) ? apply : null;
        }

        // The groups and worn items a move leaves in place.
        static List<RuleSet> Kept(Actor actor, IDictionary<MetaKind, long> changes, Item given)
        {
            var kept = new List<RuleSet>();
            foreach (MetaKind other in Enum.GetValues(typeof(MetaKind)))
                if (!changes.ContainsKey(other)) kept.Add(Get(Member(actor, other)));
            kept.AddRange(EquipmentRules(actor, given));
            return kept;
        }

        // The creature's traits once everything it would belong to and wear is reconciled:
        // a new group's or item's traits may replace or override ones another group
        // requires, which would only get it expelled on the next scan.
        static Func<string, bool?> Settled(Actor actor, IDictionary<MetaKind, long> changes, Item given)
        {
            var groups = new List<RuleSet>();
            foreach (MetaKind kind in Enum.GetValues(typeof(MetaKind)))
            {
                if (kind == MetaKind.Equipment) continue;
                if (!changes.TryGetValue(kind, out long id)) groups.Add(Get(Member(actor, kind)));
                else if (id >= 0) groups.Add(Get(Find(kind, id)));
            }
            var worn = new List<RuleSet>(EquipmentRules(actor, given));
            if (given != null) worn.Add(Get(Handle(MetaKind.Equipment, given, given.data)));
            Evaluation after = RuleEngine.Evaluate(new ActorSubject(actor), groups, worn, Opposed);
            return RuleEngine.Settled(after, Ledger(actor), Opposed);
        }

        // Read-only: previews must not create or cache provenance.
        static TraitLedger Ledger(Actor actor)
        {
            if (actors.TryGetValue(actor.id, out ActorState state) && state.Actor == actor) return state.Ledger;
            actor.data.get(LedgerKey, out string json, null);
            if (string.IsNullOrEmpty(json)) return null;
            try { return JsonConvert.DeserializeObject<TraitLedger>(json); }
            catch (Exception) { return null; }
        }

        // The group or worn item whose requirements this creature meets today but would
        // break once the item's traits settle.
        static MetaHandle BrokenBy(Actor actor, Item item)
        {
            var changes = new Dictionary<MetaKind, long> { { MetaKind.Equipment, item.data.id } };
            Func<string, bool?> traits = Settled(actor, changes, item);
            var subject = new ActorSubject(actor);
            var kept = new List<MetaHandle>();
            foreach (MetaKind kind in Enum.GetValues(typeof(MetaKind))) kept.Add(Member(actor, kind));
            EquipmentAsset replaced = item.getAsset();
            if (actor.equipment != null)
                foreach (Item worn in actor.equipment.getItems())
                    if (worn.getAsset()?.equipment_type != replaced?.equipment_type) kept.Add(Handle(MetaKind.Equipment, worn, worn.data));
            foreach (MetaHandle handle in kept)
            {
                RuleSet set = Get(handle);
                if (set != null && set.Allows(subject) && !RuleEngine.CanMove(subject, changes, new RuleSet[0], new[] { set }, traits)) return handle;
            }
            return null;
        }

        // The worn item's traits that override ones this creature's groups declare.
        public static List<KeyValuePair<string, string>> Overrides(MetaHandle item, Actor actor)
        {
            var result = new List<KeyValuePair<string, string>>();
            if (!Ready || actor == null || !(item?.Target is Item) || !Belongs(actor, item)) return result;
            RuleSet set = Get(item);
            if (set == null || !set.Enabled) return result;
            foreach (var pair in Effects(actor).Overridden)
                if (set.Declarations.Exists(d => d.Enabled && d.Kind == EffectKind.Trait && d.Value == pair.Value)) result.Add(pair);
            return result;
        }

        static bool CanWear(Actor actor, Item item)
        {
            EquipmentAsset asset = item.getAsset();
            return asset != null && actor.equipment != null && actor.canUseItems() && actor.asset.canEditItem(asset);
        }

        // Hand over this exact item, as a city hands out its storage: it leaves its wearer or
        // storage, and whatever the new wearer held in that slot goes to their own city.
        static void HandOver(Actor actor, Item item)
        {
            EquipmentType type = item.getAsset().equipment_type;
            Actor wearer = item.getActor();
            if (wearer?.equipment != null && wearer.equipment.getSlot(type).getItem() == item)
            {
                wearer.equipment.getSlot(type).takeAwayItem();
                wearer.setStatsDirty();
            }
            City storage = item.getCity();
            if (storage != null)
            {
                storage.data.equipment.getEquipmentList(type).Remove(item.id);
                item.clearCity();
                storage._storage_version++;
            }
            ActorEquipmentSlot slot = actor.equipment.getSlot(type);
            if (!slot.isEmpty()) UnequipToStorage(actor, slot);
            actor.equipment.setItem(item, actor);
            actor.setStatsDirty();
        }

        // Either side may list the other: mods do not always declare opposites both ways.
        // Traits that remove one another on addition (another tier, school or desire of the
        // same kind) cannot be held together either.
        public static bool Opposed(string a, string b)
        {
            ActorTrait first = AssetManager.traits.get(a), second = AssetManager.traits.get(b);
            return first != null && second != null && first != second &&
                (first.opposite_traits?.Contains(second) == true || second.opposite_traits?.Contains(first) == true ||
                 Removes(first, second) || Removes(second, first));
        }

        static bool Removes(ActorTrait trait, ActorTrait other) => trait.traits_to_remove != null && Array.IndexOf(trait.traits_to_remove, other) >= 0;

        // Only explicit mod actions use this setting; native conversion remains untouched.
        static bool Convertible(Actor actor) => Main.OverrideCulturalBlock || !actor.hasCulture() || actor.culture.isPossibleToConvertToOtherMeta();

        static IEnumerable<City> Candidates(Kingdom kingdom)
        {
            if (kingdom.capital != null && kingdom.capital.isAlive()) yield return kingdom.capital;
            foreach (City city in kingdom.getCities()) if (city != kingdom.capital) yield return city;
        }

        // The game's hard rules for changing city (City.isPossibleToJoin without its
        // "only move to smaller cities" migration heuristic).
        static bool CanMoveTo(Actor actor, City city)
        {
            if (city == null || !city.isAlive() || city == actor.city || !actor.isSapient()) return false;
            if (actor.city != null && (actor.isKing() || actor.isCityLeader())) return false;
            return city.isNeutral() || city.isWelcomedToJoin(actor);
        }

        static void AddMove(Actor actor, City city, Dictionary<MetaKind, long> changes, List<RuleSet> targets)
        {
            changes[MetaKind.City] = city.id;
            if (city.kingdom != null)
            {
                changes[MetaKind.Kingdom] = city.kingdom.id;
                targets.Add(Get(Handle(MetaKind.Kingdom, city.kingdom, city.kingdom.data)));
            }
            targets.Add(Get(Handle(MetaKind.City, city, city.data)));
            if (actor.army != null) changes[MetaKind.Army] = -1;
        }

        static void MoveTo(Actor actor, City city)
        {
            actor.stopBeingWarrior();
            actor.joinCity(city);
        }

        // Previews for the editor: how many current members a change would affect.
        public static int CountFailing(MetaHandle target, RuleSet set, out int members)
        {
            int failing = 0;
            List<Actor> all = Members(target);
            members = all.Count;
            foreach (Actor actor in all) if (set != null && !set.Allows(new ActorSubject(actor))) failing++;
            return failing;
        }

        public static int CountJoining(MetaHandle declarer, MetaKind kind, long id, out int members)
        {
            int joining = 0;
            List<Actor> all = Members(declarer);
            members = all.Count;
            RuleSet source = declarer?.Kind == MetaKind.Equipment ? Get(declarer) : null;
            foreach (Actor actor in all)
                if (!RuleEngine.RequiresExit(new ActorSubject(actor), source) && Plan(actor, kind, id) != null) joining++;
            return joining;
        }

        public static Kingdom KingdomOf(MetaHandle meta)
        {
            switch (meta?.Target)
            {
                case Kingdom kingdom: return kingdom;
                case City city: return city.kingdom;
                case Army army: return army.getKingdom();
                default: return null;
            }
        }

        public static List<Actor> Members(MetaHandle target)
        {
            var result = new List<Actor>();
            if (target == null || World.world?.units == null) return result;
            if (target.Target is Item item)
            {
                Actor wearer = item.getActor();
                if (wearer != null && wearer.isAlive() && Belongs(wearer, target)) result.Add(wearer);
                return result;
            }
            foreach (Actor actor in World.world.units.getSimpleList())
                if (actor != null && actor.isAlive() && Belongs(actor, target)) result.Add(actor);
            return result;
        }

        public static bool CanAdd(MetaHandle meta, Actor actor) => Ready && !Mutating && meta != null &&
            ReferenceEquals(world, World.world?.map_stats) && Find(meta.Kind, meta.Id)?.Target == meta.Target &&
            Plan(actor, meta.Kind, meta.Id, true) != null && !IsBroken(meta);

        // 'blocker': the group or item named by the problem, if any.
        public static string AdditionProblem(MetaHandle meta, Actor actor, out string blocker)
        {
            blocker = null;
            if (!Ready || Mutating || meta == null || actor == null || !actor.isAlive() || IsBroken(meta) ||
                !ReferenceEquals(world, World.world?.map_stats) || Find(meta.Kind, meta.Id)?.Target != meta.Target)
                return "member_add_unavailable";
            RuleSet set = Get(meta);
            if (set != null && set.Enabled && set.Banned.Contains(actor.id)) return "member_add_removed";
            if (!RuleEngine.CanJoin(new ActorSubject(actor), meta.Kind, meta.Id, set, new RuleSet[0]))
                return "member_add_requirements";
            if (meta.Target is Item item)
            {
                if (!CanWear(actor, item)) return "wearer_add_cannot_use";
                blocker = BrokenBy(actor, item)?.Name;
                return blocker == null ? "wearer_add_blocked" : "wearer_add_breaks";
            }
            return "member_add_blocked";
        }

        // Reuse the declaration planner: manual additions obey requirements, bans and
        // native structural limits too. Revalidate on click rather than trusting the UI.
        public static bool TryAdd(MetaHandle meta, Actor actor)
        {
            if (!CanAdd(meta, actor)) return false;
            Actor previous = (meta.Target as Item)?.getActor();
            try
            {
                Action apply = Plan(actor, meta.Kind, meta.Id, true);
                if (apply == null) return false;
                Mutating = true;
                apply();
            }
            catch (Exception e) { Report(e); return false; }
            finally { Mutating = false; }
            Reconcile(actor);
            if (previous != null) Reconcile(previous);
            RescanSoon();
            return Belongs(actor, meta);
        }

        // Removing an individual bans it from this meta and expels it now.
        public static void Remove(MetaHandle meta, Actor actor)
        {
            if (!Ready || Mutating || actor == null || !actor.isAlive() || meta == null || IsBroken(meta) ||
                !ReferenceEquals(world, World.world?.map_stats) || Find(meta.Kind, meta.Id)?.Target != meta.Target || !Belongs(actor, meta)) return;
            RuleSet set = Get(meta) ?? new RuleSet();
            if (!set.Banned.Contains(actor.id))
            {
                set.Banned.Add(actor.id);
                Save(meta, set);
            }
            // A direct Unequip action works even while automatic rules are paused.
            if (meta.Target is Item item)
            {
                try
                {
                    Mutating = true;
                    UnequipToStorage(actor, actor.equipment.getSlot(item.getAsset().equipment_type));
                }
                catch (Exception error) { Report(error); }
                finally { Mutating = false; }
            }
            Reconcile(actor, true);
        }

        public static void Readmit(MetaHandle meta, long id)
        {
            RuleSet set = Get(meta);
            if (set == null || !set.Banned.Remove(id)) return;
            Save(meta, set);
        }

        static bool SameStats(Dictionary<string, float> a, Dictionary<string, float> b)
        {
            if (a.Count != b.Count) return false;
            foreach (var pair in a) if (!b.TryGetValue(pair.Key, out float value) || value != pair.Value) return false;
            return true;
        }

        public static void AddStats(Actor actor)
        {
            if (!Ready || actor == null || !actor.isAlive()) return;
            // This runs only after a real rebuild, never after an early-return updateStats.
            Evaluation effects = Effects(actor);
            foreach (var pair in effects.Stats)
                if (AllowedStats.Contains(pair.Key)) actor.stats[pair.Key] += pair.Value;
        }

        // Ordered for display.
        public static readonly string[] Stats =
        { "health", "damage", "armor", "speed", "attack_speed", "intelligence", "warfare", "diplomacy", "stewardship" };
        public static readonly HashSet<string> AllowedStats = new HashSet<string>(Stats);

        public static bool Allows(Actor actor, MetaHandle target)
        {
            if (!Ready || Mutating || actor == null || target == null || actor.data == null) return true;
            // Same-target setter calls are harmless; the scan separately enforces
            // ongoing eligibility. Removing membership is always permitted.
            if (Member(actor, target.Kind)?.Id == target.Id) return true;
            RuleSet set = Get(target);
            if (set == null || set.Allows(new ActorSubject(actor))) return true;
            BlockedAdmissions++;
            return false;
        }

        public static void ExternalTraitAdded(Actor actor, ActorTrait trait)
        {
            if (!Ready || Mutating || actor?.data == null || trait == null || !actor.hasTrait(trait)) return;
            try
            {
                ActorState state = State(actor);
                if (!state.Ledger.Owned.ContainsKey(trait.id)) return;
                state.Ledger.Owned[trait.id] = false;
                actor.data.set(LedgerKey, JsonConvert.SerializeObject(state.Ledger));
            }
            catch (Exception e) { if (broken.Add("actor:" + actor.id)) Report(e); }
        }

        public static int MemberCount(MetaHandle target, out int violations)
        {
            int count = 0; violations = 0;
            if (target == null || World.world?.units == null) return 0;
            RuleSet set = Get(target);
            foreach (Actor actor in Members(target))
            {
                count++;
                if (set != null && !set.Allows(new ActorSubject(actor))) violations++;
            }
            return count;
        }

        public static void Report(Exception error)
        {
            LastError = error.Message;
            Debug.LogError("[Meta Rules] " + error);
        }
    }
}
