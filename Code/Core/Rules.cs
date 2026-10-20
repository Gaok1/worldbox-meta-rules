using System;
using System.Collections.Generic;

namespace MetaRules.Core
{
    public enum MetaKind { Army, Clan, Culture, Religion, City, Kingdom, Language, Family, Subspecies }
    public enum ConditionKind { Trait, Membership }
    public enum EffectKind { Trait, Stat }
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
                : MetaId >= 0 && subject.MemberOf(Meta) == MetaId;
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
    }

    [Serializable]
    public sealed class RuleSet
    {
        public int Version = 1;
        public bool Enabled = true;
        public List<Restriction> Restrictions = new List<Restriction>();
        public List<Declaration> Declarations = new List<Declaration>();

        public bool Allows(ISubject subject)
        {
            if (!Enabled) return true;
            foreach (Restriction rule in Restrictions) if (!rule.Allows(subject)) return false;
            return true;
        }
    }

    public interface ISubject
    {
        long MemberOf(MetaKind kind);
        bool HasTrait(string id);
        bool AddTrait(string id);
        void RemoveTrait(string id);
    }

    // Per-actor save data. Owned=false means this trait also has a non-mod source.
    [Serializable]
    public sealed class TraitLedger
    {
        public Dictionary<string, bool> Owned = new Dictionary<string, bool>();
    }

    public sealed class Evaluation
    {
        public readonly HashSet<string> Temporary = new HashSet<string>(StringComparer.Ordinal);
        public readonly HashSet<string> Permanent = new HashSet<string>(StringComparer.Ordinal);
        public readonly Dictionary<string, float> Stats = new Dictionary<string, float>(StringComparer.Ordinal);
    }

    public static class RuleEngine
    {
        public static bool RequiresExit(ISubject subject, RuleSet set) => set != null && !set.Allows(subject);

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
                if (d == null || string.IsNullOrWhiteSpace(d.Value) || !Enum.IsDefined(typeof(EffectKind), d.Kind)
                    || !Enum.IsDefined(typeof(Lifetime), d.Lifetime) || float.IsNaN(d.Amount) || float.IsInfinity(d.Amount)) return false;
            return true;
        }

        // Evaluate a membership snapshot before applying effects: ordering of groups cannot
        // make a declaration qualify its own admission or recursively grant memberships.
        public static Evaluation Evaluate(IEnumerable<RuleSet> sets)
        {
            var result = new Evaluation();
            foreach (RuleSet set in sets)
            {
                if (!set.Enabled) continue;
                foreach (Declaration d in set.Declarations)
                {
                    if (!d.Enabled || string.IsNullOrWhiteSpace(d.Value)) continue;
                    if (d.Kind == EffectKind.Trait)
                    {
                        (d.Lifetime == Lifetime.Permanent ? result.Permanent : result.Temporary).Add(d.Value);
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

        public static bool Reconcile(ISubject subject, Evaluation desired, TraitLedger ledger)
        {
            bool changed = false;
            // A permanent declaration promotes an existing temporary grant to a lasting one.
            foreach (string trait in desired.Permanent)
            {
                if (!subject.HasTrait(trait)) changed |= subject.AddTrait(trait);
                ledger.Owned.Remove(trait);
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
                if (!subject.HasTrait(trait) && subject.AddTrait(trait))
                {
                    ledger.Owned[trait] = true;
                    changed = true;
                }
            }
            return changed;
        }
    }
}
