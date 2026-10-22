using System;
using System.Collections.Generic;

namespace MetaRules.Core
{
    // Append only: numeric values are persisted in existing saves.
    public enum MetaKind { Army, Clan, Culture, Religion, City, Kingdom, Language, Family, Subspecies, Equipment }
    public enum ConditionKind { Trait, Membership }
    public enum EffectKind { Trait, Stat, Item, Meta }
    public enum Lifetime { Membership, Permanent }

    [Serializable]
    public sealed class Condition
    {
        public ConditionKind Kind;
        public string Trait = "";
        public MetaKind Meta;
        public long MetaId = -1;
        public bool Negated;

        public bool Matches(ISubject subject)
        {
            bool result = Kind == ConditionKind.Trait
                ? !string.IsNullOrEmpty(Trait) && subject.HasTrait(Trait)
                : MetaId >= 0 && (Meta == MetaKind.Equipment ? subject.HasEquipment(MetaId) : subject.MemberOf(Meta) == MetaId);
            return Negated ? !result : result;
        }
    }

    [Serializable]
    public sealed class Restriction
    {
        public string Id = Guid.NewGuid().ToString("N");
        public bool Enabled = true;
        public bool Any;
        public List<Condition> Conditions = new List<Condition>();

        public bool Allows(ISubject subject)
        {
            if (!Enabled || Conditions.Count == 0) return true;
            foreach (Condition c in Conditions)
            {
                bool match = c.Matches(subject);
                if (Any && match) return true;
                if (!Any && !match) return false;
            }
            return !Any;
        }
    }

    [Serializable]
    public sealed class Declaration
    {
        public string Id = Guid.NewGuid().ToString("N");
        public bool Enabled = true;
        public EffectKind Kind;
        public string Value = "";
        public float Amount;
        public Lifetime Lifetime;
        // EffectKind.Meta: the specific group members are made to join.
        public MetaKind Meta;
        public long MetaId = -1;
    }

    [Serializable]
    public sealed class RuleSet
    {
        public int Version = 1;
        public bool Enabled = true;
        public List<Restriction> Restrictions = new List<Restriction>();
        public List<Declaration> Declarations = new List<Declaration>();
        // Individuals removed by hand; they may not belong while rules are enabled.
        public List<long> Banned = new List<long>();

        public bool Allows(ISubject subject) => !Bans(subject) && Failing(subject) == null;

        public bool Bans(ISubject subject) => Enabled && Banned != null && Banned.Contains(subject.Id);

        // The first active requirement the subject does not meet.
        public Restriction Failing(ISubject subject)
        {
            if (!Enabled) return null;
            foreach (Restriction rule in Restrictions) if (!rule.Allows(subject)) return rule;
            return null;
        }
    }

    public interface ISubject
    {
        long Id { get; }
        long MemberOf(MetaKind kind);
        bool HasTrait(string id);
        bool HasEquipment(long id);
        bool AddTrait(string id);
        void RemoveTrait(string id);
        // Held traits that exclude this one; null when one of them cannot be removed.
        List<string> Opposites(string id);
        // Equipment: null slot means this subject cannot carry the item.
        string SlotOf(string item);
        long Equipped(string slot, out string item);
        long Equip(string item);
        void Unequip(string slot);
    }

    // Per-actor save data. Owned=false means this trait also has a non-mod source.
    // Served: declared traits already handed out, with the opposites they replaced.
    [Serializable]
    public sealed class TraitLedger
    {
        public Dictionary<string, bool> Owned = new Dictionary<string, bool>();
        public Dictionary<string, ItemGrant> Items = new Dictionary<string, ItemGrant>();
        public Dictionary<string, List<string>> Served = new Dictionary<string, List<string>>();
        public bool IsEmpty() => Owned.Count == 0 && (Items == null || Items.Count == 0) && (Served == null || Served.Count == 0);
    }

    // One equipment declaration served during the current membership. Id=-1 means the
    // subject already carried that item, so the mod owns nothing in this slot.
    [Serializable]
    public sealed class ItemGrant
    {
        public string Item = "";
        public long Id = -1;
        public bool Permanent;
    }

    // Rule: the requirement involved, if any. Declared: A is a declared trait (and so is B
    // when there is no Rule); otherwise A and B are conditions of Rule.
    public sealed class TraitClash
    {
        public string A, B;
        public bool ANegated, BNegated, Declared;
        public Restriction Rule;
        public bool Involves(string trait) => A == trait || B == trait;
    }

    public sealed class Evaluation
    {
        public readonly HashSet<string> Temporary = new HashSet<string>(StringComparer.Ordinal);
        public readonly HashSet<string> Permanent = new HashSet<string>(StringComparer.Ordinal);
        public readonly Dictionary<string, float> Stats = new Dictionary<string, float>(StringComparer.Ordinal);
        // Ordered by group precedence; true means permanent.
        public readonly List<KeyValuePair<string, bool>> Items = new List<KeyValuePair<string, bool>>();
        // One granted group per meta type; the first group to declare one wins.
        public readonly Dictionary<MetaKind, long> Metas = new Dictionary<MetaKind, long>();
        // Declared traits left out because a conflicting one wins, with the winner.
        public readonly Dictionary<string, string> Overridden = new Dictionary<string, string>(StringComparer.Ordinal);
        public bool Empty => Temporary.Count == 0 && Permanent.Count == 0 && Stats.Count == 0 && Items.Count == 0 && Metas.Count == 0;
    }

    public static class RuleEngine
    {
        public static bool RequiresExit(ISubject subject, RuleSet set) => set != null && !set.Allows(subject);

        // A creature can be made to adopt or move into these. Family and subspecies are
        // biological and cannot be granted.
        public static bool Grantable(MetaKind kind) => kind >= MetaKind.Army && kind <= MetaKind.Language;

        // Joining must satisfy the target's requirements and every group the subject keeps.
        public static bool CanJoin(ISubject subject, MetaKind kind, long id, RuleSet target, IEnumerable<RuleSet> kept) =>
            CanMove(subject, new Dictionary<MetaKind, long> { { kind, id } }, new[] { target }, kept);

        // Some moves change several memberships at once (a new city also means its kingdom,
        // and leaving the old army). Id -1 means leaving that type; an Equipment change is
        // the item handed over. Targets admit the subject as it is now; kept groups must
        // still accept it once the move's trait grants settle ('traits', see Settled).
        public static bool CanMove(ISubject subject, IDictionary<MetaKind, long> changes, IEnumerable<RuleSet> targets, IEnumerable<RuleSet> kept,
            Func<string, bool?> traits = null) => Refusing(subject, changes, targets, kept, traits, out _) == null;

        // The first rule set that refuses the move and the requirement it fails (null when
        // the subject is banned from it); null when the move is allowed.
        public static RuleSet Refusing(ISubject subject, IDictionary<MetaKind, long> changes, IEnumerable<RuleSet> targets, IEnumerable<RuleSet> kept,
            Func<string, bool?> traits, out Restriction rule)
        {
            rule = null;
            var moved = new Moved(subject, changes, null);
            foreach (RuleSet set in targets)
                if (set != null && !set.Allows(moved)) { rule = set.Failing(moved); return set; }
            var settled = traits == null ? moved : new Moved(subject, changes, traits);
            foreach (RuleSet set in kept)
                if (set != null && !set.Allows(settled)) { rule = set.Failing(settled); return set; }
            return null;
        }

        // The traits a subject would hold once 'after' is reconciled: declared traits arrive
        // and replace their opposites, and the mod's own grants that are no longer declared
        // leave. Null means the trait does not change.
        public static Func<string, bool?> Settled(Evaluation after, TraitLedger ledger, Func<string, string, bool> opposed) => trait =>
        {
            if (after.Temporary.Contains(trait) || after.Permanent.Contains(trait)) return true;
            if (ledger?.Owned != null && ledger.Owned.TryGetValue(trait, out bool owned) && owned) return false;
            foreach (string granted in after.Permanent) if (opposed(granted, trait)) return false;
            foreach (string granted in after.Temporary) if (opposed(granted, trait)) return false;
            return null;
        };

        sealed class Moved : ISubject
        {
            readonly ISubject inner; readonly IDictionary<MetaKind, long> changes; readonly Func<string, bool?> traits;
            public Moved(ISubject subject, IDictionary<MetaKind, long> changes, Func<string, bool?> traits)
            { inner = subject; this.changes = changes; this.traits = traits; }
            public long Id => inner.Id;
            public long MemberOf(MetaKind value) => changes.TryGetValue(value, out long id) ? id : inner.MemberOf(value);
            public bool HasTrait(string trait) => traits?.Invoke(trait) ?? inner.HasTrait(trait);
            public bool HasEquipment(long id) =>
                changes.TryGetValue(MetaKind.Equipment, out long given) && given == id || inner.HasEquipment(id);
            public bool AddTrait(string trait) => false;
            public void RemoveTrait(string trait) { }
            public List<string> Opposites(string trait) => null;
            public string SlotOf(string item) => null;
            public long Equipped(string slot, out string item) { item = null; return -1; }
            public long Equip(string item) => -1;
            public void Unequip(string slot) { }
        }

        public static bool Valid(RuleSet set)
        {
            if (set == null || set.Version != 1 || set.Restrictions == null || set.Declarations == null) return false;
            foreach (Restriction restriction in set.Restrictions)
            {
                if (restriction?.Conditions == null) return false;
                foreach (Condition c in restriction.Conditions)
                {
                    if (c == null || !Enum.IsDefined(typeof(ConditionKind), c.Kind)) return false;
                    if (c.Kind == ConditionKind.Trait && string.IsNullOrWhiteSpace(c.Trait)) return false;
                    if (c.Kind == ConditionKind.Membership && (c.MetaId < 0 || !Enum.IsDefined(typeof(MetaKind), c.Meta))) return false;
                }
            }
            foreach (Declaration d in set.Declarations)
            {
                if (d == null || !Enum.IsDefined(typeof(EffectKind), d.Kind) || !Enum.IsDefined(typeof(Lifetime), d.Lifetime)
                    || float.IsNaN(d.Amount) || float.IsInfinity(d.Amount)) return false;
                if (d.Kind == EffectKind.Meta ? d.MetaId < 0 || !Enum.IsDefined(typeof(MetaKind), d.Meta) || !Grantable(d.Meta)
                    : string.IsNullOrWhiteSpace(d.Value)) return false;
            }
            return true;
        }

        // Evaluate a membership snapshot before applying effects: ordering of groups cannot
        // make a declaration qualify its own admission or recursively grant memberships.
        // 'opposed' settles conflicting declared traits (see Settle); without it every
        // declared trait is desired.
        public static Evaluation Evaluate(ISubject subject, IEnumerable<RuleSet> memberships, IEnumerable<RuleSet> equipment,
            Func<string, string, bool> opposed = null)
        {
            // What a creature wears is its most specific source: worn items go first, so
            // their grants (a language, an item for a slot) win over its groups' while worn.
            // Every equipped item is a separate source, and an ineligible item cannot grant
            // its own admission requirements.
            var ordered = new List<RuleSet>();
            foreach (RuleSet set in equipment) if (set != null && set.Allows(subject)) ordered.Add(set);
            foreach (RuleSet set in memberships) if (set != null) ordered.Add(set);
            Evaluation result = Evaluate(ordered);
            if (opposed != null) Settle(result, ordered, opposed);
            return result;
        }

        // Conflicting traits cannot be held together, and granting both would make them
        // replace each other on every scan: the first declaration in source order wins, so
        // an item's trait overrides its groups' (a sword's school over the one its species
        // teaches) only while it is worn.
        static void Settle(Evaluation result, List<RuleSet> ordered, Func<string, string, bool> opposed)
        {
            var kept = new List<string>();
            foreach (RuleSet set in ordered)
            {
                if (!set.Enabled) continue;
                foreach (Declaration d in set.Declarations)
                {
                    if (!d.Enabled || d.Kind != EffectKind.Trait || string.IsNullOrWhiteSpace(d.Value) || kept.Contains(d.Value)) continue;
                    string winner = kept.Find(k => opposed(k, d.Value));
                    if (winner == null)
                    {
                        kept.Add(d.Value);
                        continue;
                    }
                    result.Temporary.Remove(d.Value);
                    result.Permanent.Remove(d.Value);
                    result.Overridden[d.Value] = winner;
                }
            }
        }

        public static Evaluation Evaluate(IEnumerable<RuleSet> sets)
        {
            var result = new Evaluation();
            foreach (RuleSet set in sets)
            {
                if (!set.Enabled) continue;
                foreach (Declaration d in set.Declarations)
                {
                    if (!d.Enabled) continue;
                    if (d.Kind == EffectKind.Meta)
                    {
                        if (d.MetaId >= 0 && Grantable(d.Meta) && !result.Metas.ContainsKey(d.Meta)) result.Metas[d.Meta] = d.MetaId;
                        continue;
                    }
                    if (string.IsNullOrWhiteSpace(d.Value)) continue;
                    if (d.Kind == EffectKind.Trait)
                    {
                        (d.Lifetime == Lifetime.Permanent ? result.Permanent : result.Temporary).Add(d.Value);
                    }
                    else if (d.Kind == EffectKind.Item)
                    {
                        int known = result.Items.FindIndex(i => i.Key == d.Value);
                        bool permanent = d.Lifetime == Lifetime.Permanent;
                        if (known < 0) result.Items.Add(new KeyValuePair<string, bool>(d.Value, permanent));
                        else if (permanent) result.Items[known] = new KeyValuePair<string, bool>(d.Value, true);
                    }
                    else if (!float.IsNaN(d.Amount) && !float.IsInfinity(d.Amount))
                    {
                        result.Stats.TryGetValue(d.Value, out float prior);
                        result.Stats[d.Value] = prior + d.Amount;
                    }
                }
            }
            return result;
        }

        // Trait choices a rule set can never satisfy: declaring opposites, a requirement that
        // needs opposites (or a trait and its negation) at once, or declaring a trait that
        // breaks the set's own requirement. Disabled entries count, since they can be enabled.
        public static IEnumerable<TraitClash> Clashes(RuleSet set, Func<string, string, bool> opposed)
        {
            var declared = new List<string>();
            foreach (Declaration d in set.Declarations)
                if (d.Kind == EffectKind.Trait && !string.IsNullOrEmpty(d.Value)) declared.Add(d.Value);
            for (int i = 0; i < declared.Count; i++)
                for (int j = i + 1; j < declared.Count; j++)
                    if (opposed(declared[i], declared[j])) yield return new TraitClash { A = declared[i], B = declared[j], Declared = true };
            foreach (Restriction rule in set.Restrictions)
            {
                var traits = rule.Conditions.FindAll(c => c.Kind == ConditionKind.Trait && !string.IsNullOrEmpty(c.Trait));
                if (!rule.Any)
                    for (int i = 0; i < traits.Count; i++)
                        for (int j = i + 1; j < traits.Count; j++)
                        {
                            Condition a = traits[i], b = traits[j];
                            if (a.Negated != b.Negated ? a.Trait == b.Trait : !a.Negated && opposed(a.Trait, b.Trait))
                                yield return new TraitClash { A = a.Trait, ANegated = a.Negated, B = b.Trait, BNegated = b.Negated, Rule = rule };
                        }
                foreach (string trait in declared)
                {
                    // ALL fails when one condition breaks; ANY only when every one does.
                    Condition broken = null;
                    int count = 0;
                    foreach (Condition c in traits)
                        if (c.Negated ? c.Trait == trait : opposed(c.Trait, trait)) { broken = broken ?? c; count++; }
                    if (broken != null && (!rule.Any || count == rule.Conditions.Count))
                        yield return new TraitClash { A = trait, B = broken.Trait, BNegated = broken.Negated, Rule = rule, Declared = true };
                }
            }
        }

        public static bool Reconcile(ISubject subject, Evaluation desired, TraitLedger ledger, bool replaceOpposites = true)
        {
            if (ledger.Served == null) ledger.Served = new Dictionary<string, List<string>>();
            bool changed = false;
            // A permanent declaration promotes an existing temporary grant to a lasting one,
            // and whatever it replaced is gone for good.
            foreach (string trait in desired.Permanent)
            {
                if (!subject.HasTrait(trait)) changed |= Grant(subject, trait, ledger, replaceOpposites);
                ledger.Owned.Remove(trait);
                if (ledger.Served.TryGetValue(trait, out List<string> replaced)) replaced.Clear();
                else if (subject.HasTrait(trait)) ledger.Served[trait] = new List<string>();
            }
            foreach (string trait in new List<string>(ledger.Owned.Keys))
            {
                if (desired.Temporary.Contains(trait) || desired.Permanent.Contains(trait)) continue;
                if (ledger.Owned[trait] && subject.HasTrait(trait))
                {
                    subject.RemoveTrait(trait);
                    changed = true;
                }
                ledger.Owned.Remove(trait);
            }
            foreach (string trait in desired.Temporary)
            {
                if (desired.Permanent.Contains(trait)) continue;
                if (!ledger.Owned.ContainsKey(trait) && subject.HasTrait(trait)) ledger.Owned[trait] = false;
                if (!subject.HasTrait(trait) && Grant(subject, trait, ledger, replaceOpposites))
                {
                    ledger.Owned[trait] = true;
                    changed = true;
                }
                if (subject.HasTrait(trait) && !ledger.Served.ContainsKey(trait)) ledger.Served[trait] = new List<string>();
            }
            // A temporary grant hands back what it replaced, unless something else took the place.
            foreach (string trait in new List<string>(ledger.Served.Keys))
            {
                if (desired.Temporary.Contains(trait) || desired.Permanent.Contains(trait)) continue;
                foreach (string old in ledger.Served[trait])
                    if (!subject.HasTrait(old) && subject.AddTrait(old)) changed = true;
                ledger.Served.Remove(trait);
            }
            return ReconcileItems(subject, desired, ledger) | changed;
        }

        // Like the game's trait editor, a declared trait replaces held opposites (another tier
        // of the same ladder, for instance). Only the first time: once served, a later change
        // made by the game or another mod is not overruled.
        static bool Grant(ISubject subject, string trait, TraitLedger ledger, bool replaceOpposites)
        {
            if (!replaceOpposites || ledger.Served.ContainsKey(trait)) return subject.AddTrait(trait);
            List<string> held = subject.Opposites(trait);
            if (held == null) return false;
            foreach (string old in held) subject.RemoveTrait(old);
            if (!subject.AddTrait(trait))
            {
                foreach (string old in held) subject.AddTrait(old);
                return false;
            }
            ledger.Served[trait] = held;
            return true;
        }

        // Equipment is served once per membership. Later replacements (city upgrades,
        // breakage, looting) are respected instead of regenerating items every scan.
        static bool ReconcileItems(ISubject subject, Evaluation desired, TraitLedger ledger)
        {
            if (ledger.Items == null) ledger.Items = new Dictionary<string, ItemGrant>();
            if (desired.Items.Count == 0 && ledger.Items.Count == 0) return false;
            bool changed = false;
            // The first group to declare an item for a slot wins.
            var chosen = new Dictionary<string, KeyValuePair<string, bool>>(StringComparer.Ordinal);
            foreach (var item in desired.Items)
            {
                string slot = subject.SlotOf(item.Key);
                if (slot != null && !chosen.ContainsKey(slot)) chosen[slot] = item;
            }
            foreach (string slot in new List<string>(ledger.Items.Keys))
            {
                ItemGrant grant = ledger.Items[slot];
                if (chosen.TryGetValue(slot, out var wanted) && wanted.Key == grant.Item)
                {
                    grant.Permanent |= wanted.Value;
                    continue;
                }
                if (!grant.Permanent && grant.Id >= 0 && subject.Equipped(slot, out _) == grant.Id)
                {
                    subject.Unequip(slot);
                    changed = true;
                }
                ledger.Items.Remove(slot);
            }
            foreach (var pair in chosen)
            {
                if (ledger.Items.ContainsKey(pair.Key)) continue;
                subject.Equipped(pair.Key, out string held);
                long id = held == pair.Value.Key ? -1 : subject.Equip(pair.Value.Key);
                if (held != pair.Value.Key && id < 0) continue; // e.g. a cursed item blocks the slot
                ledger.Items[pair.Key] = new ItemGrant { Item = pair.Value.Key, Id = id, Permanent = pair.Value.Value };
                changed |= id >= 0;
            }
            return changed;
        }
    }
}
