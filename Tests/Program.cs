using System;
using System.Collections.Generic;
using System.Text.Json;
using MetaRules.Core;

sealed class Subject : ISubject
{
    public readonly HashSet<string> Traits = new HashSet<string>();
    public readonly Dictionary<MetaKind, long> Groups = new Dictionary<MetaKind, long>();
    public readonly HashSet<string> Rejected = new HashSet<string>();
    public long MemberOf(MetaKind kind) => Groups.TryGetValue(kind, out long id) ? id : -1;
    public bool HasTrait(string id) => Traits.Contains(id);
    public bool AddTrait(string id) => !Rejected.Contains(id) && Traits.Add(id);
    public void RemoveTrait(string id) => Traits.Remove(id);
}

static class Program
{
    static int passed;
    static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void Test(string name, Action action) { action(); passed++; Console.WriteLine("PASS " + name); }
    static Evaluation Evaluate(params Declaration[] declarations) => RuleEngine.Evaluate(new[] { new RuleSet { Declarations = new List<Declaration>(declarations) } });
    static Declaration Trait(string id, Lifetime life = Lifetime.Membership) => new Declaration { Value = id, Lifetime = life };
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
        Console.WriteLine(passed + " behavioral tests passed.");
    }
}
