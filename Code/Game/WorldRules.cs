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
        public string Name => string.IsNullOrEmpty(Data.name) ? "#" + Id : Data.name;
    }

    public sealed class ActorSubject : ISubject
    {
        readonly Actor actor;
        public ActorSubject(Actor value) { actor = value; }
        public long Id => actor.id;
        public long MemberOf(MetaKind kind) => WorldRules.Member(actor, kind)?.Id ?? -1;
        public bool HasTrait(string id) => actor.hasTrait(id);
        public bool AddTrait(string id)
        {
            // Do not remove opposite traits or execute a partial conflicting addition.
            ActorTrait trait = AssetManager.traits.get(id);
            return trait != null && !actor.hasOppositeTrait(trait) && actor.addTrait(trait, false);
        }
        public void RemoveTrait(string id) => actor.removeTrait(id);

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

        static ActorState State(Actor actor)
        {
            if (actors.TryGetValue(actor.id, out ActorState state) && state.Actor == actor) return state;
            actor.data.get(LedgerKey, out string json, null);
            TraitLedger ledger = string.IsNullOrEmpty(json) ? new TraitLedger() : JsonConvert.DeserializeObject<TraitLedger>(json);
            if (ledger?.Owned == null) throw new InvalidOperationException("Invalid trait provenance for actor " + actor.id);
            if (ledger.Items == null) ledger.Items = new Dictionary<string, ItemGrant>();
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
                    EnforceMembership(actor);
                    GrantMemberships(actor);
                    Mutating = false;
                }
                Evaluation desired = RuleEngine.Evaluate(Applicable(actor));
                if (desired.Empty && !actors.ContainsKey(actor.id))
                {
                    actor.data.get(LedgerKey, out string savedLedger, null);
                    if (string.IsNullOrEmpty(savedLedger)) return;
                }
                ActorState state = State(actor);
                Mutating = true;
                RuleEngine.Reconcile(new ActorSubject(actor), desired, state.Ledger);
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
            Evaluation desired = RuleEngine.Evaluate(Applicable(actor));
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
            var kept = new List<RuleSet>();
            foreach (MetaKind other in Enum.GetValues(typeof(MetaKind)))
                if (!changes.ContainsKey(other)) kept.Add(Get(Member(actor, other)));
            return RuleEngine.CanMove(subject, changes, targets, kept) ? apply : null;
        }

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
            foreach (Actor actor in all) if (Plan(actor, kind, id) != null) joining++;
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
            foreach (Actor actor in World.world.units.getSimpleList())
                if (actor != null && actor.isAlive() && Member(actor, target.Kind)?.Id == target.Id) result.Add(actor);
            return result;
        }

        public static bool CanAdd(MetaHandle meta, Actor actor) => Ready && !Mutating && meta != null &&
            ReferenceEquals(world, World.world?.map_stats) && Find(meta.Kind, meta.Id)?.Target == meta.Target &&
            Plan(actor, meta.Kind, meta.Id, true) != null && !IsBroken(meta);

        // Reuse the declaration planner: manual additions obey requirements, bans and
        // native structural limits too. Revalidate on click rather than trusting the UI.
        public static bool TryAdd(MetaHandle meta, Actor actor)
        {
            if (!CanAdd(meta, actor)) return false;
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
            RescanSoon();
            return Member(actor, meta.Kind)?.Id == meta.Id;
        }

        // Removing an individual bans it from this meta and expels it now.
        public static void Remove(MetaHandle meta, Actor actor)
        {
            RuleSet set = Get(meta) ?? new RuleSet();
            if (actor == null || set.Banned.Contains(actor.id)) return;
            set.Banned.Add(actor.id);
            Save(meta, set);
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
            Evaluation effects = RuleEngine.Evaluate(Applicable(actor));
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
            foreach (Actor actor in World.world.units.getSimpleList())
            {
                if (actor == null || !actor.isAlive() || Member(actor, target.Kind)?.Id != target.Id) continue;
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
