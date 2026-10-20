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
        public long MemberOf(MetaKind kind) => WorldRules.Member(actor, kind)?.Id ?? -1;
        public bool HasTrait(string id) => actor.hasTrait(id);
        public bool AddTrait(string id)
        {
            // Do not remove opposite traits or execute a partial conflicting addition.
            ActorTrait trait = AssetManager.traits.get(id);
            return trait != null && !actor.hasOppositeTrait(trait) && actor.addTrait(trait, false);
        }
        public void RemoveTrait(string id) => actor.removeTrait(id);
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
                if (actor != null && actor.isAlive()) Reconcile(actor);
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
            state = new ActorState { Actor = actor, Ledger = ledger };
            actors[actor.id] = state;
            return state;
        }

        public static void Reconcile(Actor actor)
        {
            if (!Ready || Mutating || actor?.data == null || !actor.isAlive()) return;
            try
            {
                Evaluation desired = RuleEngine.Evaluate(Applicable(actor));
                if (desired.Temporary.Count == 0 && desired.Permanent.Count == 0 && desired.Stats.Count == 0 && !actors.ContainsKey(actor.id))
                {
                    actor.data.get(LedgerKey, out string savedLedger, null);
                    if (string.IsNullOrEmpty(savedLedger)) return;
                }
                ActorState state = State(actor);
                Mutating = true;
                RuleEngine.Reconcile(new ActorSubject(actor), desired, state.Ledger);
                if (state.Ledger.Owned.Count > 0) actor.data.set(LedgerKey, JsonConvert.SerializeObject(state.Ledger));
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

        public static readonly HashSet<string> AllowedStats = new HashSet<string>
        { "health", "damage", "armor", "speed", "attack_speed", "intelligence", "warfare", "diplomacy", "stewardship" };

        public static bool Allows(Actor actor, MetaHandle target)
        {
            if (!Ready || Mutating || actor == null || target == null || actor.data == null) return true;
            // Existing members are grandfathered; removing membership is always permitted.
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
