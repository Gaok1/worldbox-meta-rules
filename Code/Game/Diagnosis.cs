using System;
using System.Collections.Generic;
using MetaRules.Core;
using UnityEngine;

namespace MetaRules.Game
{
    // How a declaration stands for one creature, from fine to worst.
    public enum Standing { Applied, Pending, Dormant, Overridden, Blocked }

    // Reason: the locale key suffix (why_*). Name/Kind: the group, item or creature it names;
    // Trait: the conflicting or winning trait; Other: what is granted instead; Rule: the
    // requirement that fails.
    public sealed class Finding
    {
        public Standing State = Standing.Applied;
        public string Reason = "";
        public string Name, Trait, Other;
        public MetaKind? Kind;
        public Restriction Rule;
        public string Key => State + "|" + Reason + "|" + Kind + "|" + Name + "|" + Trait + "|" + Other + "|" + Rule?.Id;
    }

    // One declaration across the members of its group: who it applies to and, grouped,
    // why it does not apply to the rest (most common first).
    public sealed class Diagnosis
    {
        public int Members, Checked, Applied;
        public bool Preview;
        public readonly List<KeyValuePair<Finding, int>> Issues = new List<KeyValuePair<Finding, int>>();
        public int Missing => Checked - Applied;
        public Standing Worst
        {
            get
            {
                Standing worst = Standing.Applied;
                foreach (var issue in Issues) if (issue.Key.State > worst) worst = issue.Key.State;
                return worst;
            }
        }
    }

    // Read-only diagnostics for the rules window. They reuse the scan's own evaluation and
    // planner, so what they report is what the next scan will do.
    public static partial class WorldRules
    {
        // Bounds the work of one window rebuild in very large groups.
        public const int DiagnosedMembers = 400;
        static string previewKey;
        static RuleSet previewSet;
        static int effectsFrame = -1;
        static readonly Dictionary<long, Evaluation> effectsCache = new Dictionary<long, Evaluation>();

        // A window rebuild diagnoses each declaration for every member: evaluate each creature once.
        static Evaluation CachedEffects(Actor actor)
        {
            if (effectsFrame != Time.frameCount) { effectsCache.Clear(); effectsFrame = Time.frameCount; }
            if (!effectsCache.TryGetValue(actor.id, out Evaluation effects)) effectsCache[actor.id] = effects = Effects(actor);
            return effects;
        }

        // 'preview': the source's rule set as it would be after saving an edit; the result
        // then says what the declaration would do rather than what it does.
        public static Diagnosis Diagnose(MetaHandle source, Declaration d, RuleSet preview = null)
        {
            var result = new Diagnosis { Preview = preview != null };
            if (!Ready || source == null || d == null || IsBroken(source)) return result;
            List<Actor> all = Members(source);
            result.Members = all.Count;
            if (preview != null) { previewKey = source.Key; previewSet = preview; effectsFrame = -1; }
            try
            {
                var counts = new Dictionary<string, int>();
                var findings = new Dictionary<string, Finding>();
                foreach (Actor actor in all)
                {
                    if (result.Checked >= DiagnosedMembers) break;
                    result.Checked++;
                    Finding finding = Diagnose(actor, source, d, preview != null);
                    if (finding.State == Standing.Applied) { result.Applied++; continue; }
                    counts.TryGetValue(finding.Key, out int count);
                    counts[finding.Key] = count + 1;
                    if (count == 0) findings[finding.Key] = finding;
                }
                foreach (var pair in counts) result.Issues.Add(new KeyValuePair<Finding, int>(findings[pair.Key], pair.Value));
                result.Issues.Sort((a, b) => b.Value != a.Value ? b.Value.CompareTo(a.Value) : b.Key.State.CompareTo(a.Key.State));
            }
            catch (Exception e) { Report(e); }
            finally
            {
                if (preview != null) { previewKey = null; previewSet = null; effectsFrame = -1; }
            }
            return result;
        }

        // The enabled declarations of this group or item that do not apply to this creature.
        public static List<KeyValuePair<Declaration, Finding>> Issues(MetaHandle source, Actor actor)
        {
            var result = new List<KeyValuePair<Declaration, Finding>>();
            RuleSet set = Get(source);
            if (!Ready || actor == null || !actor.isAlive() || set == null || !set.Enabled) return result;
            try
            {
                foreach (Declaration d in set.Declarations)
                {
                    if (!d.Enabled) continue;
                    Finding finding = Diagnose(actor, source, d, false);
                    if (finding.State != Standing.Applied) result.Add(new KeyValuePair<Declaration, Finding>(d, finding));
                }
            }
            catch (Exception e) { Report(e); }
            return result;
        }

        static Finding Diagnose(Actor actor, MetaHandle source, Declaration d, bool preview)
        {
            var f = new Finding();
            RuleSet set = Get(source);
            if (set == null || !set.Enabled || !d.Enabled) return Note(f, Standing.Dormant, "paused");
            var subject = new ActorSubject(actor);
            // An item only benefits a wearer that meets its own requirements.
            if (source.Kind == MetaKind.Equipment && !set.Allows(subject))
            {
                f.Rule = set.Failing(subject);
                return Note(f, Standing.Blocked, set.Bans(subject) ? "banned" : "item_requirement", source);
            }
            Evaluation desired = CachedEffects(actor);
            switch (d.Kind)
            {
                case EffectKind.Trait: TraitStanding(f, actor, d, desired); break;
                case EffectKind.Item: ItemStanding(f, actor, d, desired); break;
                case EffectKind.Meta: MetaStanding(f, actor, d, desired); break;
            }
            // A preview counts what the next scan would do as done.
            if (preview && f.State == Standing.Pending) f.State = Standing.Applied;
            return f;
        }

        static Finding Note(Finding f, Standing state, string reason, MetaHandle who = null)
        {
            f.State = state; f.Reason = reason;
            if (who != null) { f.Name = who.Name; f.Kind = who.Kind; }
            return f;
        }

        static void TraitStanding(Finding f, Actor actor, Declaration d, Evaluation desired)
        {
            if (desired.Overridden.TryGetValue(d.Value, out string winner))
            {
                f.Trait = winner;
                Note(f, Standing.Overridden, "overridden_trait", SourceOf(actor, x => x.Kind == EffectKind.Trait && x.Value == winner));
                return;
            }
            if (actor.hasTrait(d.Value)) return;
            ActorTrait trait = AssetManager.traits.get(d.Value);
            if (trait == null) { Note(f, Standing.Blocked, "missing"); return; }
            foreach (ActorTrait held in actor.getTraits())
            {
                if (!Opposed(d.Value, held.id)) continue;
                f.Trait = held.id;
                if (!held.can_be_removed) { Note(f, Standing.Blocked, "fixed_trait"); return; }
                // A grant replaces conflicting traits only the first time (and only when the
                // setting allows it); afterwards a change by the game or another mod stands.
                TraitLedger ledger = Ledger(actor);
                bool served = ledger?.Served != null && ledger.Served.ContainsKey(d.Value);
                if (served || !Main.ReplaceOpposedTraits) Note(f, Standing.Dormant, "conflict_trait");
                else Note(f, Standing.Pending, "replaces_trait");
                return;
            }
            if (trait.affects_mind && actor.hasTag("strong_mind")) { Note(f, Standing.Blocked, "strong_mind"); return; }
            Note(f, Standing.Pending, "pending");
        }

        static void ItemStanding(Finding f, Actor actor, Declaration d, Evaluation desired)
        {
            var subject = new ActorSubject(actor);
            string slot = subject.SlotOf(d.Value);
            if (slot == null) { Note(f, Standing.Blocked, "cannot_use"); return; }
            subject.Equipped(slot, out string held);
            if (held == d.Value) return;
            // The first source to declare an item for a slot wins.
            foreach (var item in desired.Items)
            {
                if (subject.SlotOf(item.Key) != slot) continue;
                if (item.Key == d.Value) break;
                f.Other = ItemLabel(item.Key);
                Note(f, Standing.Overridden, "overridden_item", SourceOf(actor, x => x.Kind == EffectKind.Item && x.Value == item.Key));
                return;
            }
            TraitLedger ledger = Ledger(actor);
            if (ledger?.Items != null && ledger.Items.TryGetValue(slot, out ItemGrant grant) && grant.Item == d.Value)
            {
                Note(f, Standing.Dormant, "item_replaced");
                return;
            }
            if (Enum.TryParse(slot, out EquipmentType type) && !actor.equipment.getSlot(type).canChangeSlot())
            {
                f.Name = actor.equipment.getSlot(type).getItem().getName();
                Note(f, Standing.Blocked, "cursed_slot");
                return;
            }
            Note(f, Standing.Pending, "pending");
        }

        static void MetaStanding(Finding f, Actor actor, Declaration d, Evaluation desired)
        {
            if (Member(actor, d.Meta)?.Id == d.MetaId) return;
            if (Find(d.Meta, d.MetaId) == null) { Note(f, Standing.Blocked, "missing"); return; }
            // A granted city already decides the kingdom.
            if (d.Meta == MetaKind.Kingdom && desired.Metas.ContainsKey(MetaKind.City))
            {
                Note(f, Standing.Overridden, "city_decides", SourceOf(actor, x => x.Kind == EffectKind.Meta && x.Meta == MetaKind.City));
                return;
            }
            if (desired.Metas.TryGetValue(d.Meta, out long chosen) && chosen != d.MetaId)
            {
                f.Other = Find(d.Meta, chosen)?.Name;
                Note(f, Standing.Overridden, "overridden_meta",
                    SourceOf(actor, x => x.Kind == EffectKind.Meta && x.Meta == d.Meta && x.MetaId == chosen));
                return;
            }
            if (Plan(actor, d.Meta, d.MetaId, false, f) == null) return;
            Note(f, Standing.Pending, "pending");
        }

        // The first source, in the scan's precedence order (worn items, then groups), with a
        // matching enabled declaration.
        static MetaHandle SourceOf(Actor actor, Predicate<Declaration> match)
        {
            var subject = new ActorSubject(actor);
            foreach (MetaHandle item in Worn(actor))
            {
                RuleSet set = Get(item);
                if (set != null && set.Enabled && set.Allows(subject) && set.Declarations.Exists(x => x.Enabled && match(x))) return item;
            }
            foreach (MetaKind kind in Enum.GetValues(typeof(MetaKind)))
            {
                MetaHandle group = Member(actor, kind);
                RuleSet set = Get(group);
                if (set != null && set.Enabled && set.Declarations.Exists(x => x.Enabled && match(x))) return group;
            }
            return null;
        }

        static string ItemLabel(string id)
        {
            EquipmentAsset asset = AssetManager.items.get(id);
            if (asset == null) return id;
            try { return asset.getTranslatedName(); } catch (Exception) { return id; }
        }
    }
}
