using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using MetaRules.Core;

sealed class Subject : ISubject
{
    public long Id { get; set; } = 1;
    public readonly HashSet<string> Traits = new HashSet<string>();
    public readonly Dictionary<MetaKind, long> Groups = new Dictionary<MetaKind, long>();
    public readonly HashSet<string> Rejected = new HashSet<string>();
    public long MemberOf(MetaKind kind) => Groups.TryGetValue(kind, out long id) ? id : -1;
    public bool HasTrait(string id) => Traits.Contains(id);
    public bool HasEquipment(long id)
    {
        foreach (var slot in Slots.Values) if (slot.Id == id) return true;
        return false;
    }
    // Opposite pairs, both ways; Fixed traits cannot be removed.
    public readonly List<(string A, string B)> Exclusive = new List<(string, string)>();
    public readonly HashSet<string> Fixed = new HashSet<string>();
    bool Excludes(string a, string b) => Exclusive.Contains((a, b)) || Exclusive.Contains((b, a));
    public bool AddTrait(string id) => !Rejected.Contains(id) && Opposites(id).Count == 0 && Traits.Add(id);
    public void RemoveTrait(string id) => Traits.Remove(id);
    public List<string> Opposites(string id)
    {
        var held = new List<string>();
        foreach (string trait in Traits)
        {
            if (!Excludes(id, trait)) continue;
            if (Fixed.Contains(trait)) return null;
            held.Add(trait);
        }
        return held;
    }
    // Equipment: item id "slot:name", e.g. "weapon:sword".
    public readonly Dictionary<string, (string Item, long Id)> Slots = new Dictionary<string, (string, long)>();
    public readonly HashSet<string> Cursed = new HashSet<string>();
    public bool CanCarry = true;
    long next = 100;
    public string SlotOf(string item) => CanCarry ? item.Split(':')[0] : null;
    public long Equipped(string slot, out string item)
    {
        item = Slots.TryGetValue(slot, out var held) ? held.Item : null;
        return item == null ? -1 : held.Id;
    }
    public long Equip(string item)
    {
        string slot = SlotOf(item);
        if (Cursed.Contains(slot)) return -1;
        Slots[slot] = (item, ++next);
        return next;
    }
    public void Unequip(string slot) => Slots.Remove(slot);
    public string Held(string slot) => Slots.TryGetValue(slot, out var held) ? held.Item : null;
}

static class Program
{
    static int passed;
    static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void Test(string name, Action action) { action(); passed++; Console.WriteLine("PASS " + name); }
    static Evaluation Evaluate(params Declaration[] declarations) => RuleEngine.Evaluate(new[] { new RuleSet { Declarations = new List<Declaration>(declarations) } });
    static Declaration Trait(string id, Lifetime life = Lifetime.Membership) => new Declaration { Value = id, Lifetime = life };
    static Declaration Item(string id, Lifetime life = Lifetime.Membership) => new Declaration { Kind = EffectKind.Item, Value = id, Lifetime = life };
    static void Main()
    {
        Test("trait admission with AND, OR and negation", () =>
        {
            var actor = new Subject(); actor.Traits.Add("knight");
            var rule = new Restriction { Conditions = new List<Condition> { new Condition { Trait = "knight" }, new Condition { Trait = "veteran" } } };
            Assert(!rule.Allows(actor), "ALL must reject a missing trait");
            rule.Any = true; Assert(rule.Allows(actor), "ANY must accept a matching trait");
            rule.Any = false; rule.Conditions[1].Negated = true;
            Assert(rule.Allows(actor), "NOT veteran must accept a knight without veteran");
        });
        Test("membership relations use an individual ID in every supported meta type", () =>
        {
            var actor = new Subject();
            foreach (MetaKind kind in Enum.GetValues(typeof(MetaKind)))
            {
                if (kind == MetaKind.Equipment) continue; // Equipment is many simultaneous instances, not one membership.
                actor.Groups[kind] = 42;
                var c = new Condition { Kind = ConditionKind.Membership, Meta = kind, MetaId = 42 };
                Assert(c.Matches(actor), "matching instance rejected: " + kind);
                c.MetaId = 43; Assert(!c.Matches(actor), "different instance accepted: " + kind);
                c.MetaId = -1; actor.Groups.Remove(kind);
                Assert(!c.Matches(actor), "missing reference must not match missing membership");
            }
        });
        Test("all active restrictions must pass; disabled rules are ignored", () =>
        {
            var actor = new Subject(); actor.Traits.Add("knight");
            var set = new RuleSet { Restrictions = new List<Restriction> {
                new Restriction { Conditions = new List<Condition> { new Condition { Trait = "knight" } } },
                new Restriction { Conditions = new List<Condition> { new Condition { Trait = "veteran" } } }
            }};
            Assert(!set.Allows(actor), "second restriction was skipped");
            set.Restrictions[1].Enabled = false; Assert(set.Allows(actor), "disabled restriction still applied");
            set.Enabled = false; actor.Traits.Clear(); Assert(set.Allows(actor), "disabled group still applied");
        });
        Test("temporary grant is removed when its last source disappears", () =>
        {
            var actor = new Subject(); var ledger = new TraitLedger();
            RuleEngine.Reconcile(actor, Evaluate(Trait("brave"), Trait("brave")), ledger);
            Assert(actor.HasTrait("brave"), "grant missing");
            RuleEngine.Reconcile(actor, Evaluate(Trait("brave")), ledger);
            Assert(actor.HasTrait("brave"), "lost trait while another group still granted it");
            RuleEngine.Reconcile(actor, Evaluate(), ledger);
            Assert(!actor.HasTrait("brave") && ledger.Owned.Count == 0, "temporary grant leaked");
        });
        Test("pre-existing traits are preserved", () =>
        {
            var actor = new Subject(); actor.Traits.Add("brave"); var ledger = new TraitLedger();
            RuleEngine.Reconcile(actor, Evaluate(Trait("brave")), ledger);
            RuleEngine.Reconcile(actor, Evaluate(), ledger);
            Assert(actor.HasTrait("brave"), "native trait was deleted");
        });
        Test("permanent grant survives both leaving and rule deletion", () =>
        {
            var actor = new Subject(); var ledger = new TraitLedger();
            RuleEngine.Reconcile(actor, Evaluate(Trait("brave", Lifetime.Permanent)), ledger);
            RuleEngine.Reconcile(actor, Evaluate(), ledger);
            Assert(actor.HasTrait("brave"), "permanent grant was deleted");
        });
        Test("permanent declaration promotes a temporary trait", () =>
        {
            var actor = new Subject(); var ledger = new TraitLedger();
            RuleEngine.Reconcile(actor, Evaluate(Trait("brave")), ledger);
            RuleEngine.Reconcile(actor, Evaluate(Trait("brave", Lifetime.Permanent), Trait("brave")), ledger);
            RuleEngine.Reconcile(actor, Evaluate(), ledger);
            Assert(actor.HasTrait("brave"), "promoted grant was still treated as owned");
        });
        Test("a trait independently acquired during membership is preserved", () =>
        {
            var actor = new Subject(); var ledger = new TraitLedger();
            RuleEngine.Reconcile(actor, Evaluate(Trait("brave")), ledger);
            ledger.Owned["brave"] = false; // adapter records an external successful add request
            RuleEngine.Reconcile(actor, Evaluate(), ledger);
            Assert(actor.HasTrait("brave"), "externally earned trait was deleted");
        });
        Test("failed or conflicting grants are not claimed", () =>
        {
            var actor = new Subject(); actor.Rejected.Add("brave"); actor.Traits.Add("coward");
            var ledger = new TraitLedger();
            RuleEngine.Reconcile(actor, Evaluate(Trait("brave")), ledger);
            Assert(actor.HasTrait("coward") && !actor.HasTrait("brave") && !ledger.Owned.ContainsKey("brave"), "conflicting grant changed existing traits");
        });
        Test("a declared trait replaces opposites and hands them back afterwards", () =>
        {
            var actor = new Subject(); actor.Traits.Add("novice");
            actor.Exclusive.Add(("novice", "grandmaster")); actor.Exclusive.Add(("god", "grandmaster")); actor.Exclusive.Add(("novice", "god"));
            var ledger = new TraitLedger();
            RuleEngine.Reconcile(actor, Evaluate(Trait("grandmaster")), ledger);
            Assert(actor.HasTrait("grandmaster") && !actor.HasTrait("novice"), "opposite was not replaced");
            for (int i = 0; i < 5; i++) Assert(!RuleEngine.Reconcile(actor, Evaluate(Trait("grandmaster")), ledger), "replacement repeated");
            RuleEngine.Reconcile(actor, Evaluate(), ledger);
            Assert(actor.HasTrait("novice") && !actor.HasTrait("grandmaster") && ledger.IsEmpty(), "replaced trait not handed back");
        });
        Test("a served grant does not overrule a later native change", () =>
        {
            var actor = new Subject(); actor.Traits.Add("novice");
            actor.Exclusive.Add(("novice", "grandmaster")); actor.Exclusive.Add(("god", "grandmaster")); actor.Exclusive.Add(("novice", "god"));
            var ledger = new TraitLedger();
            RuleEngine.Reconcile(actor, Evaluate(Trait("grandmaster")), ledger);
            actor.Traits.Remove("grandmaster"); actor.Traits.Add("god"); // natural promotion
            RuleEngine.Reconcile(actor, Evaluate(Trait("grandmaster")), ledger);
            Assert(actor.HasTrait("god") && !actor.HasTrait("grandmaster"), "grant fought a promotion");
            RuleEngine.Reconcile(actor, Evaluate(), ledger);
            Assert(actor.HasTrait("god") && !actor.HasTrait("novice"), "handed back over a newer trait");
        });
        Test("a permanent grant keeps its replacement", () =>
        {
            var actor = new Subject(); actor.Traits.Add("novice"); actor.Exclusive.Add(("novice", "grandmaster"));
            var ledger = new TraitLedger();
            RuleEngine.Reconcile(actor, Evaluate(Trait("grandmaster", Lifetime.Permanent)), ledger);
            RuleEngine.Reconcile(actor, Evaluate(), ledger);
            Assert(actor.HasTrait("grandmaster") && !actor.HasTrait("novice") && ledger.IsEmpty(), "permanent replacement was undone");
        });
        Test("an unremovable opposite blocks the grant", () =>
        {
            var actor = new Subject(); actor.Traits.Add("cursed"); actor.Fixed.Add("cursed"); actor.Exclusive.Add(("cursed", "blessed"));
            var ledger = new TraitLedger();
            RuleEngine.Reconcile(actor, Evaluate(Trait("blessed")), ledger);
            Assert(actor.HasTrait("cursed") && !actor.HasTrait("blessed") && ledger.IsEmpty(), "unremovable trait was replaced");
        });
        Test("with replacement off, a conflicting grant is skipped", () =>
        {
            var actor = new Subject(); actor.Traits.Add("novice"); actor.Exclusive.Add(("novice", "grandmaster"));
            var ledger = new TraitLedger();
            RuleEngine.Reconcile(actor, Evaluate(Trait("grandmaster")), ledger, false);
            Assert(actor.HasTrait("novice") && !actor.HasTrait("grandmaster") && !ledger.Owned.ContainsKey("grandmaster"), "replaced while disabled");
            RuleEngine.Reconcile(actor, Evaluate(), ledger, false);
            Assert(actor.HasTrait("novice") && ledger.IsEmpty(), "skipped grant left state");
        });
        Test("conflicting declarations and requirements are detected", () =>
        {
            Func<string, string, bool> opposed = (a, b) => a != b && (a + b == "novicegrandmaster" || a + b == "grandmasternovice");
            Restriction Rule(bool any, params Condition[] conditions) => new Restriction { Any = any, Conditions = new List<Condition>(conditions) };
            Condition Has(string trait, bool negated = false) => new Condition { Trait = trait, Negated = negated };
            Condition City() => new Condition { Kind = ConditionKind.Membership, Meta = MetaKind.City, MetaId = 3 };
            int Count(RuleSet set) => RuleEngine.Clashes(set, opposed).Count();

            Assert(Count(new RuleSet { Declarations = { Trait("novice"), Trait("grandmaster") } }) == 1, "opposite declarations");
            Assert(Count(new RuleSet { Declarations = { Trait("novice"), Trait("brave") } }) == 0, "unrelated declarations");
            Assert(Count(new RuleSet { Restrictions = { Rule(false, Has("novice"), Has("grandmaster")) } }) == 1, "ALL needs opposites");
            Assert(Count(new RuleSet { Restrictions = { Rule(true, Has("novice"), Has("grandmaster")) } }) == 0, "ANY of opposites is fine");
            Assert(Count(new RuleSet { Restrictions = { Rule(false, Has("brave"), Has("brave", true)) } }) == 1, "trait and its negation");
            Assert(Count(new RuleSet { Restrictions = { Rule(false, Has("novice", true), Has("grandmaster", true)) } }) == 0, "negated opposites are fine");
            var breaks = RuleEngine.Clashes(new RuleSet { Declarations = { Trait("grandmaster") }, Restrictions = { Rule(false, Has("novice")) } }, opposed).Single();
            Assert(breaks.Declared && breaks.Rule != null && breaks.A == "grandmaster" && breaks.B == "novice", "declaration breaking its requirement");
            Assert(Count(new RuleSet { Declarations = { Trait("brave") }, Restrictions = { Rule(false, Has("brave", true)) } }) == 1, "declaring a forbidden trait");
            Assert(Count(new RuleSet { Declarations = { Trait("grandmaster") }, Restrictions = { Rule(true, Has("novice"), City()) } }) == 0, "ANY keeps another way in");
            Assert(Count(new RuleSet { Declarations = { Trait("grandmaster") }, Restrictions = { Rule(true, Has("novice")) } }) == 1, "ANY with every way broken");
        });
        Test("repeated reconciliation is idempotent", () =>
        {
            var actor = new Subject(); var ledger = new TraitLedger();
            Evaluation evaluation = Evaluate(Trait("brave"));
            Assert(RuleEngine.Reconcile(actor, evaluation, ledger), "initial grant missing");
            for (int i = 0; i < 100; i++) Assert(!RuleEngine.Reconcile(actor, evaluation, ledger), "repeated update changed the actor");
            Assert(ledger.Owned.Count == 1 && ledger.Owned["brave"], "ownership changed on reapplication");
        });
        Test("additive bonuses stack without carrying over between evaluations", () =>
        {
            var a = new Declaration { Kind = EffectKind.Stat, Value = "armor", Amount = 10 };
            var b = new Declaration { Kind = EffectKind.Stat, Value = "armor", Amount = -3 };
            Assert(Evaluate(a, b).Stats["armor"] == 7, "bonuses did not stack");
            Assert(Evaluate(a, b).Stats["armor"] == 7, "repeated evaluation accumulated bonuses");
            b.Enabled = false; Assert(Evaluate(a, b).Stats["armor"] == 10, "disabled bonus still applied");
            b.Enabled = true; b.Amount = float.NaN; Assert(Evaluate(a, b).Stats["armor"] == 10, "NaN poisoned stats");
        });
        Test("disabled group removes temporary effects", () =>
        {
            var actor = new Subject(); var ledger = new TraitLedger();
            var set = new RuleSet { Declarations = new List<Declaration> { Trait("brave") } };
            RuleEngine.Reconcile(actor, RuleEngine.Evaluate(new[] { set }), ledger);
            set.Enabled = false;
            RuleEngine.Reconcile(actor, RuleEngine.Evaluate(new[] { set }), ledger);
            Assert(!actor.HasTrait("brave"), "disabled source retained its grant");
        });
        Test("save/load round trip preserves conditions, IDs and trait provenance", () =>
        {
            var options = new JsonSerializerOptions { IncludeFields = true };
            var set = new RuleSet {
                Restrictions = new List<Restriction> { new Restriction { Conditions = new List<Condition> {
                    new Condition { Kind = ConditionKind.Membership, Meta = MetaKind.Clan, MetaId = 42, Negated = true }
                }}}, Declarations = new List<Declaration> { Trait("brave") }
            };
            RuleSet restored = JsonSerializer.Deserialize<RuleSet>(JsonSerializer.Serialize(set, options), options);
            Assert(restored.Restrictions[0].Conditions[0].MetaId == 42 && restored.Restrictions[0].Conditions[0].Negated, "relation lost on round trip");
            Assert(restored.Declarations[0].Id == set.Declarations[0].Id, "rule identity changed");
            var actor = new Subject(); var ledger = new TraitLedger();
            RuleEngine.Reconcile(actor, Evaluate(Trait("brave")), ledger);
            ledger = JsonSerializer.Deserialize<TraitLedger>(JsonSerializer.Serialize(ledger, options), options);
            RuleEngine.Reconcile(actor, Evaluate(), ledger);
            Assert(!actor.HasTrait("brave"), "loaded provenance did not clean up owned grant");
        });
        Test("invalid and future schemas are rejected without executing them", () =>
        {
            Assert(RuleEngine.Valid(new RuleSet()), "empty valid rules rejected");
            Assert(!RuleEngine.Valid(new RuleSet { Version = 2 }), "unknown future schema accepted");
            Assert(!RuleEngine.Valid(new RuleSet { Restrictions = null }), "null restriction list accepted");
            var set = new RuleSet { Restrictions = new List<Restriction> { new Restriction { Conditions = null } } };
            Assert(!RuleEngine.Valid(set), "null conditions accepted");
            set = new RuleSet { Restrictions = new List<Restriction> { new Restriction { Conditions = new List<Condition> {
                new Condition { Kind = ConditionKind.Membership, Meta = (MetaKind)999, MetaId = 1 }
            } } } };
            Assert(!RuleEngine.Valid(set), "unknown meta type accepted");
            set = new RuleSet { Declarations = new List<Declaration> { new Declaration { Kind = EffectKind.Stat, Value = "armor", Amount = float.PositiveInfinity } } };
            Assert(!RuleEngine.Valid(set), "infinite stat amount accepted");
        });
        Test("adding requirements marks incompatible existing members for departure and removes their grants", () =>
        {
            var actor = new Subject(); var ledger = new TraitLedger(); actor.Groups[MetaKind.Army] = 10;
            var set = new RuleSet {
                Declarations = new List<Declaration> { Trait("brave") }
            };
            RuleEngine.Reconcile(actor, RuleEngine.Evaluate(new[] { set }), ledger);
            Assert(actor.HasTrait("brave"), "initial declaration missing");
            set.Restrictions.Add(new Restriction { Conditions = new List<Condition> { new Condition { Trait = "knight" } } });
            Assert(RuleEngine.RequiresExit(actor, set), "existing member was grandfathered instead of marked for departure");
            actor.Groups.Remove(MetaKind.Army); // adapter invokes the native departure method
            RuleEngine.Reconcile(actor, Evaluate(), ledger);
            Assert(!actor.HasTrait("brave"), "departed member kept the temporary benefit");
        });
        Test("ongoing membership permits eligible members and ignores disabled requirements", () =>
        {
            var actor = new Subject(); actor.Traits.Add("knight");
            var set = new RuleSet { Restrictions = new List<Restriction> { new Restriction {
                Conditions = new List<Condition> { new Condition { Trait = "knight" } }
            } } };
            Assert(!RuleEngine.RequiresExit(actor, set), "eligible member was expelled");
            actor.Traits.Remove("knight"); Assert(RuleEngine.RequiresExit(actor, set), "loss of required trait was ignored");
            set.Restrictions[0].Enabled = false; Assert(!RuleEngine.RequiresExit(actor, set), "disabled requirement expelled a member");
            set.Restrictions[0].Enabled = true; set.Enabled = false; Assert(!RuleEngine.RequiresExit(actor, set), "disabled group expelled a member");
            Assert(!RuleEngine.RequiresExit(actor, null), "meta without rules expelled a member");
        });
        Test("losing a required relation invalidates existing membership", () =>
        {
            var actor = new Subject(); actor.Groups[MetaKind.Clan] = 42;
            var set = new RuleSet { Restrictions = new List<Restriction> { new Restriction { Conditions = new List<Condition> {
                new Condition { Kind = ConditionKind.Membership, Meta = MetaKind.Clan, MetaId = 42 }
            } } } };
            Assert(!RuleEngine.RequiresExit(actor, set), "matching clan was rejected");
            actor.Groups[MetaKind.Clan] = 43;
            Assert(RuleEngine.RequiresExit(actor, set), "changing clan did not invalidate membership");
        });
        Test("temporary equipment is given once and removed on leaving", () =>
        {
            var actor = new Subject(); var ledger = new TraitLedger();
            Assert(RuleEngine.Reconcile(actor, Evaluate(Item("weapon:sword")), ledger), "equipment not given");
            long id = actor.Equipped("weapon", out _);
            for (int i = 0; i < 20; i++) Assert(!RuleEngine.Reconcile(actor, Evaluate(Item("weapon:sword")), ledger), "equipment regenerated");
            Assert(actor.Equipped("weapon", out _) == id, "granted item replaced");
            RuleEngine.Reconcile(actor, Evaluate(), ledger);
            Assert(actor.Held("weapon") == null && ledger.IsEmpty(), "temporary equipment kept after leaving");
        });
        Test("replaced equipment is respected and never removed", () =>
        {
            var actor = new Subject(); var ledger = new TraitLedger();
            RuleEngine.Reconcile(actor, Evaluate(Item("weapon:sword")), ledger);
            actor.Slots["weapon"] = ("weapon:axe", 7); // city upgrade
            Assert(!RuleEngine.Reconcile(actor, Evaluate(Item("weapon:sword")), ledger), "fought a native replacement");
            RuleEngine.Reconcile(actor, Evaluate(), ledger);
            Assert(actor.Held("weapon") == "weapon:axe", "native item removed on leaving");
        });
        Test("pre-existing equipment is kept and not claimed", () =>
        {
            var actor = new Subject(); actor.Slots["weapon"] = ("weapon:sword", 5); var ledger = new TraitLedger();
            Assert(!RuleEngine.Reconcile(actor, Evaluate(Item("weapon:sword")), ledger), "regenerated an item already carried");
            RuleEngine.Reconcile(actor, Evaluate(), ledger);
            Assert(actor.Equipped("weapon", out _) == 5, "owned item removed");
        });
        Test("permanent equipment stays after leaving and is given again on rejoining", () =>
        {
            var actor = new Subject(); var ledger = new TraitLedger();
            RuleEngine.Reconcile(actor, Evaluate(Item("helmet:iron", Lifetime.Permanent)), ledger);
            RuleEngine.Reconcile(actor, Evaluate(), ledger);
            Assert(actor.Held("helmet") == "helmet:iron" && ledger.IsEmpty(), "permanent equipment removed");
            actor.Slots.Remove("helmet");
            RuleEngine.Reconcile(actor, Evaluate(Item("helmet:iron", Lifetime.Permanent)), ledger);
            Assert(actor.Held("helmet") == "helmet:iron", "rejoining did not give equipment again");
        });
        Test("first group wins a slot; cursed slots and unequippable creatures are skipped", () =>
        {
            var actor = new Subject(); var ledger = new TraitLedger();
            RuleEngine.Reconcile(actor, Evaluate(Item("weapon:sword"), Item("weapon:axe"), Item("ring:gold")), ledger);
            Assert(actor.Held("weapon") == "weapon:sword" && actor.Held("ring") == "ring:gold", "slot conflict resolved wrongly");
            var cursed = new Subject(); cursed.Cursed.Add("weapon"); var other = new TraitLedger();
            RuleEngine.Reconcile(cursed, Evaluate(Item("weapon:sword")), other);
            Assert(cursed.Held("weapon") == null && other.IsEmpty(), "cursed slot was claimed");
            var animal = new Subject { CanCarry = false }; other = new TraitLedger();
            RuleEngine.Reconcile(animal, Evaluate(Item("weapon:sword")), other);
            Assert(animal.Slots.Count == 0 && other.IsEmpty(), "creature without equipment received an item");
        });
        Test("changing the declared item swaps the temporary grant", () =>
        {
            var actor = new Subject(); var ledger = new TraitLedger();
            RuleEngine.Reconcile(actor, Evaluate(Item("weapon:sword")), ledger);
            RuleEngine.Reconcile(actor, Evaluate(Item("weapon:axe")), ledger);
            Assert(actor.Held("weapon") == "weapon:axe" && ledger.Items["weapon"].Item == "weapon:axe", "declaration change not applied");
        });
        Test("legacy ledgers without equipment load and round-trip", () =>
        {
            var options = new JsonSerializerOptions { IncludeFields = true };
            var ledger = JsonSerializer.Deserialize<TraitLedger>("{\"Owned\":{\"brave\":true}}", options);
            var actor = new Subject(); actor.Traits.Add("brave");
            RuleEngine.Reconcile(actor, Evaluate(Item("weapon:sword")), ledger);
            Assert(!actor.HasTrait("brave") && actor.Held("weapon") == "weapon:sword", "legacy ledger failed");
            ledger = JsonSerializer.Deserialize<TraitLedger>(JsonSerializer.Serialize(ledger, options), options);
            RuleEngine.Reconcile(actor, Evaluate(), ledger);
            Assert(actor.Held("weapon") == null, "equipment provenance lost on round trip");
        });
        Test("removed individuals may not belong while rules are enabled", () =>
        {
            var actor = new Subject { Id = 7 }; var other = new Subject { Id = 8 };
            var set = new RuleSet { Banned = new List<long> { 7 } };
            Assert(RuleEngine.RequiresExit(actor, set) && !RuleEngine.RequiresExit(other, set), "ban applied to the wrong individual");
            set.Enabled = false; Assert(!RuleEngine.RequiresExit(actor, set), "paused rules still enforced a ban");
            set.Enabled = true; set.Banned = null; Assert(!RuleEngine.RequiresExit(actor, set), "legacy rules without bans failed");
        });
        Test("meta declarations: first group per type wins and only grantable types are valid", () =>
        {
            Declaration Meta(MetaKind kind, long id) => new Declaration { Kind = EffectKind.Meta, Meta = kind, MetaId = id };
            Evaluation e = Evaluate(Meta(MetaKind.Language, 5), Meta(MetaKind.Language, 6), Meta(MetaKind.Religion, 9));
            Assert(e.Metas[MetaKind.Language] == 5 && e.Metas[MetaKind.Religion] == 9 && !e.Empty, "grant precedence wrong");
            Assert(RuleEngine.Valid(new RuleSet { Declarations = new List<Declaration> { Meta(MetaKind.Clan, 1) } }), "valid grant rejected");
            Assert(!RuleEngine.Valid(new RuleSet { Declarations = new List<Declaration> { Meta(MetaKind.Subspecies, 1) } }), "subspecies grant accepted");
            Assert(!RuleEngine.Valid(new RuleSet { Declarations = new List<Declaration> { Meta(MetaKind.Family, 1) } }), "family grant accepted");
            Assert(RuleEngine.Valid(new RuleSet { Declarations = new List<Declaration> { Meta(MetaKind.Army, 1), Meta(MetaKind.City, 2), Meta(MetaKind.Kingdom, 3) } }), "army/city/kingdom grants rejected");
            Assert(!RuleEngine.Valid(new RuleSet { Declarations = new List<Declaration> { Meta(MetaKind.Language, -1) } }), "grant without target accepted");
            var off = Meta(MetaKind.Culture, 3); off.Enabled = false;
            Assert(!Evaluate(off).Metas.ContainsKey(MetaKind.Culture), "disabled grant applied");
        });
        Test("a grant may not break the target's or any kept group's requirements", () =>
        {
            var actor = new Subject(); actor.Groups[MetaKind.Kingdom] = 1; actor.Groups[MetaKind.Language] = 2;
            var latinOnly = new RuleSet { Restrictions = new List<Restriction> { new Restriction { Conditions = new List<Condition> {
                new Condition { Kind = ConditionKind.Membership, Meta = MetaKind.Language, MetaId = 2 } } } } };
            Assert(!RuleEngine.CanJoin(actor, MetaKind.Language, 3, null, new[] { latinOnly }), "grant broke a kept group's requirement");
            Assert(RuleEngine.CanJoin(actor, MetaKind.Language, 2, null, new[] { latinOnly }), "compatible grant rejected");
            var knightsOnly = new RuleSet { Restrictions = new List<Restriction> { new Restriction { Conditions = new List<Condition> { new Condition { Trait = "knight" } } } } };
            Assert(!RuleEngine.CanJoin(actor, MetaKind.Religion, 4, knightsOnly, new RuleSet[0]), "target requirement ignored");
            actor.Traits.Add("knight");
            Assert(RuleEngine.CanJoin(actor, MetaKind.Religion, 4, knightsOnly, new RuleSet[0]), "eligible join rejected");
            Assert(RuleEngine.CanJoin(actor, MetaKind.Religion, 4, new RuleSet { Banned = new List<long> { 2 } }, new RuleSet[0]), "ban matched the wrong id");
            Assert(!RuleEngine.CanJoin(actor, MetaKind.Religion, 4, new RuleSet { Banned = new List<long> { 1 } }, new RuleSet[0]), "banned individual granted membership");
        });
        Test("a move that changes several memberships is checked as a whole", () =>
        {
            var actor = new Subject(); actor.Groups[MetaKind.City] = 1; actor.Groups[MetaKind.Kingdom] = 10; actor.Groups[MetaKind.Army] = 5;
            var clanNeedsKingdom10 = new RuleSet { Restrictions = new List<Restriction> { new Restriction { Conditions = new List<Condition> {
                new Condition { Kind = ConditionKind.Membership, Meta = MetaKind.Kingdom, MetaId = 10 } } } } };
            var toOtherKingdom = new Dictionary<MetaKind, long> { { MetaKind.City, 2 }, { MetaKind.Kingdom, 20 }, { MetaKind.Army, -1 } };
            Assert(!RuleEngine.CanMove(actor, toOtherKingdom, new RuleSet[0], new[] { clanNeedsKingdom10 }), "move broke the clan's kingdom requirement");
            var sameKingdom = new Dictionary<MetaKind, long> { { MetaKind.City, 3 }, { MetaKind.Kingdom, 10 }, { MetaKind.Army, -1 } };
            Assert(RuleEngine.CanMove(actor, sameKingdom, new RuleSet[0], new[] { clanNeedsKingdom10 }), "valid move rejected");
            var religionNeedsArmy = new RuleSet { Restrictions = new List<Restriction> { new Restriction { Conditions = new List<Condition> {
                new Condition { Kind = ConditionKind.Membership, Meta = MetaKind.Army, MetaId = 5 } } } } };
            Assert(!RuleEngine.CanMove(actor, sameKingdom, new RuleSet[0], new[] { religionNeedsArmy }), "leaving the army was not considered");
        });
        Test("a clan's granted genius satisfies a language requirement without bypassing bans", () =>
        {
            var actor = new Subject();
            actor.Groups[MetaKind.Clan] = 10; actor.Groups[MetaKind.Culture] = 5; actor.Groups[MetaKind.Language] = 6;
            var clan = new RuleSet {
                Restrictions = new List<Restriction> { new Restriction { Conditions = new List<Condition> {
                    new Condition { Kind = ConditionKind.Membership, Meta = MetaKind.Culture, MetaId = 5 } } } },
                Declarations = new List<Declaration> { Trait("genius") }
            };
            var language = new RuleSet { Restrictions = new List<Restriction> { new Restriction {
                Conditions = new List<Condition> { new Condition { Trait = "genius" } } } } };
            var ledger = new TraitLedger();
            Assert(!RuleEngine.CanJoin(actor, MetaKind.Language, 5, language, new[] { clan }), "missing trait accepted");
            RuleEngine.Reconcile(actor, RuleEngine.Evaluate(new[] { clan }), ledger);
            Assert(RuleEngine.CanJoin(actor, MetaKind.Language, 5, language, new[] { clan }), "clan's trait was ignored");
            language.Banned.Add(actor.Id);
            Assert(!RuleEngine.CanJoin(actor, MetaKind.Language, 5, language, new[] { clan }), "removal was ignored");
            language.Banned.Remove(actor.Id);
            actor.Groups[MetaKind.Culture] = 6;
            Assert(!RuleEngine.CanJoin(actor, MetaKind.Language, 5, language, new[] { clan }), "kept clan requirement was ignored");
        });
        Test("manual membership admission validates every meta type and readmission", () =>
        {
            foreach (MetaKind kind in Enum.GetValues(typeof(MetaKind)))
            {
                if (kind == MetaKind.Equipment) continue; // Equipment is many simultaneous instances, not one membership.
                var actor = new Subject(); actor.Groups[kind] = 1;
                var target = new RuleSet { Banned = new List<long> { actor.Id }, Restrictions = new List<Restriction> {
                    new Restriction { Conditions = new List<Condition> { new Condition { Trait = "genius" } } } } };
                actor.Traits.Add("genius");
                Assert(!RuleEngine.CanJoin(actor, kind, 2, target, new RuleSet[0]), "banned candidate accepted: " + kind);
                target.Banned.Remove(actor.Id);
                Assert(RuleEngine.CanJoin(actor, kind, 2, target, new RuleSet[0]), "eligible candidate rejected: " + kind);
                Assert(actor.MemberOf(kind) == 1, "admission preview mutated membership");
                actor.Traits.Clear();
                Assert(!RuleEngine.CanJoin(actor, kind, 2, target, new RuleSet[0]), "readmission bypassed requirements: " + kind);
            }
        });
        Test("equipment conditions match exact instances across simultaneous slots", () =>
        {
            var actor = new Subject();
            long sword = actor.Equip("weapon:sword"), ring = actor.Equip("ring:gold");
            var condition = new Condition { Kind = ConditionKind.Membership, Meta = MetaKind.Equipment, MetaId = sword };
            Assert(condition.Matches(actor), "equipped sword not found");
            condition.MetaId = ring; Assert(condition.Matches(actor), "second equipped item not found");
            actor.Unequip("ring"); Assert(!condition.Matches(actor), "removed item still matched");
            condition.MetaId = sword; actor.Equip("weapon:sword");
            Assert(!condition.Matches(actor), "another copy of the same asset matched");
            condition.Negated = true; Assert(condition.Matches(actor), "NOT equipment failed");
        });
        Test("equipment source grants traits stats and metas only to eligible wearers", () =>
        {
            var actor = new Subject(); var ledger = new TraitLedger();
            var sword = new RuleSet {
                Restrictions = new List<Restriction> { new Restriction { Conditions = new List<Condition> { new Condition { Trait = "knight" } } } },
                Declarations = new List<Declaration> { Trait("knight"), Trait("brave"),
                    new Declaration { Kind = EffectKind.Stat, Value = "damage", Amount = 10 },
                    new Declaration { Kind = EffectKind.Meta, Meta = MetaKind.Clan, MetaId = 42 } }
            };
            Evaluation desired = RuleEngine.Evaluate(actor, new RuleSet[0], new[] { sword });
            Assert(desired.Empty, "item bootstrapped its own admission");
            actor.Traits.Add("knight");
            desired = RuleEngine.Evaluate(actor, new RuleSet[0], new[] { sword });
            Assert(desired.Stats["damage"] == 10 && desired.Metas[MetaKind.Clan] == 42, "item effects missing");
            RuleEngine.Reconcile(actor, desired, ledger);
            Assert(actor.HasTrait("brave"), "item trait missing");
            actor.Traits.Remove("knight");
            desired = RuleEngine.Evaluate(actor, new RuleSet[0], new[] { sword });
            Assert(desired.Empty && RuleEngine.RequiresExit(actor, sword), "ineligible item still grants effects");
            RuleEngine.Reconcile(actor, desired, ledger);
            Assert(!actor.HasTrait("brave"), "temporary item trait leaked");
        });
        Test("equipment and meta sources share provenance without stacking repeatedly", () =>
        {
            var actor = new Subject(); var ledger = new TraitLedger();
            var clan = new RuleSet { Declarations = new List<Declaration> { Trait("brave"),
                new Declaration { Kind = EffectKind.Stat, Value = "armor", Amount = 3 } } };
            var ring = new RuleSet { Declarations = new List<Declaration> { Trait("brave"), Trait("wise", Lifetime.Permanent),
                new Declaration { Kind = EffectKind.Stat, Value = "armor", Amount = 5 } } };
            var helmet = new RuleSet { Declarations = new List<Declaration> {
                new Declaration { Kind = EffectKind.Stat, Value = "armor", Amount = 2 } } };
            for (int i = 0; i < 3; i++)
            {
                Evaluation desired = RuleEngine.Evaluate(actor, new[] { clan }, new[] { ring, helmet });
                Assert(desired.Stats["armor"] == 10, "equipment bonus accumulated or lost a source");
                RuleEngine.Reconcile(actor, desired, ledger);
            }
            RuleEngine.Reconcile(actor, RuleEngine.Evaluate(actor, new[] { clan }, new RuleSet[0]), ledger);
            Assert(actor.HasTrait("brave") && actor.HasTrait("wise"), "unequip lost a shared or permanent trait");
            RuleEngine.Reconcile(actor, RuleEngine.Evaluate(actor, new RuleSet[0], new RuleSet[0]), ledger);
            Assert(!actor.HasTrait("brave") && actor.HasTrait("wise"), "last source cleanup failed");
        });
        Test("equipment pause bans and save round trip preserve rule behavior", () =>
        {
            var actor = new Subject(); actor.Equip("weapon:sword");
            var item = new RuleSet { Declarations = new List<Declaration> { Trait("brave") }, Banned = new List<long> { actor.Id } };
            var options = new JsonSerializerOptions { IncludeFields = true };
            item = JsonSerializer.Deserialize<RuleSet>(JsonSerializer.Serialize(item, options), options);
            Assert(RuleEngine.Valid(item), "equipment rules did not round trip");
            Assert(RuleEngine.Evaluate(actor, new RuleSet[0], new[] { item }).Empty, "banned wearer received effects");
            item.Banned.Clear(); item.Enabled = false;
            Assert(RuleEngine.Evaluate(actor, new RuleSet[0], new[] { item }).Empty && !RuleEngine.RequiresExit(actor, item), "paused item still active");
            item.Enabled = true;
            Assert(RuleEngine.Evaluate(actor, new RuleSet[0], new[] { item }).Temporary.Contains("brave"), "resuming lost effects");
        });
        Test("membership previews respect equipped item requirements and equipment conditions", () =>
        {
            var actor = new Subject(); actor.Groups[MetaKind.Clan] = 10;
            long id = actor.Equip("weapon:sword");
            var item = new RuleSet { Restrictions = new List<Restriction> { new Restriction { Conditions = new List<Condition> {
                new Condition { Kind = ConditionKind.Membership, Meta = MetaKind.Clan, MetaId = 10 } } } } };
            Assert(!RuleEngine.CanJoin(actor, MetaKind.Clan, 20, null, new[] { item }), "move broke equipped item's requirement");
            var target = new RuleSet { Restrictions = new List<Restriction> { new Restriction { Conditions = new List<Condition> {
                new Condition { Kind = ConditionKind.Membership, Meta = MetaKind.Equipment, MetaId = id } } } } };
            Assert(RuleEngine.CanJoin(actor, MetaKind.Language, 7, target, new[] { item }), "move lost equipment snapshot");
            actor.Unequip("weapon");
            Assert(!RuleEngine.CanJoin(actor, MetaKind.Language, 7, target, new[] { item }), "missing item allowed admission");
            Assert(!RuleEngine.Grantable(MetaKind.Equipment), "item instances must not be granted as membership");
            Assert(!RuleEngine.Valid(new RuleSet { Declarations = new List<Declaration> {
                new Declaration { Kind = EffectKind.Meta, Meta = MetaKind.Equipment, MetaId = id } } }), "invalid equipment membership grant accepted");
            Assert((int)MetaKind.Subspecies == 8 && (int)MetaKind.Equipment == 9, "existing save enum IDs changed");
        });
        Test("a worn item's trait overrides a conflicting group trait only while worn", () =>
        {
            // Another mod's fighting schools: learning one removes the other.
            var actor = new Subject(); actor.Exclusive.Add(("iron_fist", "blades")); var ledger = new TraitLedger();
            Func<string, string, bool> opposed = (a, b) => actor.Exclusive.Contains((a, b)) || actor.Exclusive.Contains((b, a));
            var species = new RuleSet { Declarations = new List<Declaration> { Trait("iron_fist"), Trait("strong") } };
            var sword = new RuleSet { Declarations = new List<Declaration> { Trait("blades") } };
            RuleEngine.Reconcile(actor, RuleEngine.Evaluate(actor, new[] { species }, new RuleSet[0], opposed), ledger);
            Assert(actor.HasTrait("iron_fist"), "group trait missing");
            for (int i = 0; i < 3; i++)
            {
                Evaluation worn = RuleEngine.Evaluate(actor, new[] { species }, new[] { sword }, opposed);
                Assert(worn.Overridden["iron_fist"] == "blades" && !worn.Temporary.Contains("iron_fist"), "item did not take precedence");
                RuleEngine.Reconcile(actor, worn, ledger);
                Assert(actor.HasTrait("blades") && !actor.HasTrait("iron_fist") && actor.HasTrait("strong"), "conflicting grants alternated on scan " + i);
            }
            RuleEngine.Reconcile(actor, RuleEngine.Evaluate(actor, new[] { species }, new RuleSet[0], opposed), ledger);
            Assert(actor.HasTrait("iron_fist") && !actor.HasTrait("blades"), "group trait did not return after unequip");
        });
        Test("conflicting group grants settle on the first group instead of alternating", () =>
        {
            var actor = new Subject(); actor.Exclusive.Add(("calm", "angry")); var ledger = new TraitLedger();
            Func<string, string, bool> opposed = (a, b) => actor.Exclusive.Contains((a, b)) || actor.Exclusive.Contains((b, a));
            var first = new RuleSet { Declarations = new List<Declaration> { Trait("calm") } };
            var second = new RuleSet { Declarations = new List<Declaration> { Trait("angry", Lifetime.Permanent) } };
            for (int i = 0; i < 3; i++)
            {
                RuleEngine.Reconcile(actor, RuleEngine.Evaluate(actor, new[] { first, second }, new RuleSet[0], opposed), ledger);
                Assert(actor.HasTrait("calm") && !actor.HasTrait("angry"), "group precedence lost on scan " + i);
            }
            Assert(RuleEngine.Evaluate(actor, new[] { first, second }, new RuleSet[0]).Permanent.Contains("angry"), "unsettled evaluation changed");
        });
        Test("handing over an item checks the wearer's groups against the traits it settles on", () =>
        {
            var actor = new Subject(); actor.Exclusive.Add(("iron_fist", "blades")); var ledger = new TraitLedger();
            Func<string, string, bool> opposed = (a, b) => actor.Exclusive.Contains((a, b)) || actor.Exclusive.Contains((b, a));
            var species = new RuleSet { Declarations = new List<Declaration> { Trait("iron_fist") } };
            RuleEngine.Reconcile(actor, RuleEngine.Evaluate(actor, new[] { species }, new RuleSet[0], opposed), ledger);
            var sword = new RuleSet { Declarations = new List<Declaration> { Trait("blades") } };
            var handover = new Dictionary<MetaKind, long> { { MetaKind.Equipment, 7 } };
            Func<string, bool?> settled = RuleEngine.Settled(RuleEngine.Evaluate(actor, new[] { species }, new[] { sword }, opposed), ledger, opposed);
            RuleSet Requires(Condition c) => new RuleSet { Restrictions = new List<Restriction> { new Restriction { Conditions = new List<Condition> { c } } } };
            var monastery = Requires(new Condition { Trait = "iron_fist" });
            Assert(RuleEngine.CanMove(actor, handover, new[] { sword }, new[] { monastery }), "unsettled check should not see the override");
            Assert(!RuleEngine.CanMove(actor, handover, new[] { sword }, new[] { monastery }, settled), "handover broke a kept requirement");
            Assert(RuleEngine.CanMove(actor, handover, new[] { sword }, new[] { species }, settled), "handover without a broken requirement refused");
            var swordsmen = Requires(new Condition { Trait = "blades" });
            Assert(RuleEngine.CanMove(actor, handover, new[] { sword }, new[] { swordsmen }, settled), "granted trait not counted for kept groups");
            Assert(!RuleEngine.CanMove(actor, handover, new[] { swordsmen }, new RuleSet[0], settled), "item admitted on its own grant");
            var bearers = Requires(new Condition { Kind = ConditionKind.Membership, Meta = MetaKind.Equipment, MetaId = 7 });
            Assert(RuleEngine.CanMove(actor, handover, new RuleSet[0], new[] { bearers }), "handed item not counted as worn");
            actor.Traits.Add("native"); actor.Exclusive.Add(("native", "blades"));
            Assert(settled("native") == false && settled("unrelated") == null, "projection of replaced or untouched traits wrong");
        });
        Console.WriteLine(passed + " behavioral tests passed.");
    }
}
