using HarmonyLib;
using MetaRules.Core;

namespace MetaRules.Game
{
    [HarmonyPatch(typeof(Army), nameof(Army.isReadyForRemoval))]
    static class KeepConfiguredArmy
    {
        static bool Prefix(Army __instance, ref bool __result)
        {
            if (__instance.data == null || __instance.getKingdom() == null || __instance.getKingdom().isRekt()) return true;
            RuleSet set = WorldRules.Get(WorldRules.Handle(MetaKind.Army, __instance, __instance.data));
            if (set == null || set.Restrictions.Count == 0 && set.Declarations.Count == 0) return true;
            __result = false;
            return false;
        }
    }

    // Setter guards cover native and mod callers. The city/kingdom pair is checked
    // together before joining, so a blocked move never splits political membership.
    [HarmonyPatch(typeof(Actor), nameof(Actor.setArmy))]
    static class ArmyAdmission
    {
        static bool Prefix(Actor __instance, Army pObject) => WorldRules.Allows(__instance, WorldRules.Handle(MetaKind.Army, pObject, pObject?.data));
        static void Postfix(Actor __instance) => WorldRules.Reconcile(__instance);
    }
    [HarmonyPatch(typeof(Actor), nameof(Actor.setClan))]
    static class ClanAdmission
    {
        static bool Prefix(Actor __instance, Clan pObject) => WorldRules.Allows(__instance, WorldRules.Handle(MetaKind.Clan, pObject, pObject?.data));
        static void Postfix(Actor __instance) => WorldRules.Reconcile(__instance);
    }
    [HarmonyPatch(typeof(Actor), nameof(Actor.setCulture))]
    static class CultureAdmission
    {
        static bool Prefix(Actor __instance, Culture pCulture) => WorldRules.Allows(__instance, WorldRules.Handle(MetaKind.Culture, pCulture, pCulture?.data));
        static void Postfix(Actor __instance) => WorldRules.Reconcile(__instance);
    }
    [HarmonyPatch(typeof(Actor), nameof(Actor.setReligion))]
    static class ReligionAdmission
    {
        static bool Prefix(Actor __instance, Religion pObject) => WorldRules.Allows(__instance, WorldRules.Handle(MetaKind.Religion, pObject, pObject?.data));
        static void Postfix(Actor __instance) => WorldRules.Reconcile(__instance);
    }
    [HarmonyPatch(typeof(Actor), nameof(Actor.setLanguage))]
    static class LanguageAdmission
    {
        static bool Prefix(Actor __instance, Language pObject) => WorldRules.Allows(__instance, WorldRules.Handle(MetaKind.Language, pObject, pObject?.data));
        static void Postfix(Actor __instance) => WorldRules.Reconcile(__instance);
    }
    [HarmonyPatch(typeof(Actor), nameof(Actor.joinLanguage))]
    static class LanguageMove
    {
        static bool Prefix(Actor __instance, Language pLanguage) => WorldRules.Allows(__instance, WorldRules.Handle(MetaKind.Language, pLanguage, pLanguage?.data));
    }
    [HarmonyPatch(typeof(Actor), nameof(Actor.tryToConvertToCulture))]
    static class CultureConversion
    {
        static bool Prefix(Actor __instance, Culture pCulture, ref bool __result)
        {
            if (WorldRules.Allows(__instance, WorldRules.Handle(MetaKind.Culture, pCulture, pCulture?.data))) return true;
            __result = false;
            return false;
        }
    }
    [HarmonyPatch(typeof(Actor), nameof(Actor.tryToConvertToReligion))]
    static class ReligionConversion
    {
        static bool Prefix(Actor __instance, Religion pReligion, ref bool __result)
        {
            if (WorldRules.Allows(__instance, WorldRules.Handle(MetaKind.Religion, pReligion, pReligion?.data))) return true;
            __result = false;
            return false;
        }
    }
    [HarmonyPatch(typeof(Actor), nameof(Actor.tryToConvertToLanguage))]
    static class LanguageConversion
    {
        static bool Prefix(Actor __instance, Language pLanguage, ref bool __result)
        {
            if (WorldRules.Allows(__instance, WorldRules.Handle(MetaKind.Language, pLanguage, pLanguage?.data))) return true;
            __result = false;
            return false;
        }
    }
    [HarmonyPatch(typeof(Actor), nameof(Actor.setFamily))]
    static class FamilyAdmission
    {
        static bool Prefix(Actor __instance, Family pObject) => WorldRules.Allows(__instance, WorldRules.Handle(MetaKind.Family, pObject, pObject?.data));
        static void Postfix(Actor __instance) => WorldRules.Reconcile(__instance);
    }
    [HarmonyPatch(typeof(Actor), nameof(Actor.setSubspecies))]
    static class SubspeciesAdmission
    {
        // Initial species assignment is necessary to construct a valid creature.
        static bool Prefix(Actor __instance, Subspecies pObject) => __instance.subspecies == null || WorldRules.Allows(__instance, WorldRules.Handle(MetaKind.Subspecies, pObject, pObject?.data));
        static void Postfix(Actor __instance) => WorldRules.Reconcile(__instance);
    }
    static class PoliticalAdmission
    {
        internal static bool City(Actor actor, City target) => target == null ||
            WorldRules.Allows(actor, WorldRules.Handle(MetaKind.City, target, target.data)) &&
            WorldRules.Allows(actor, WorldRules.Handle(MetaKind.Kingdom, target.kingdom, target.kingdom?.data));
    }
    [HarmonyPatch(typeof(Actor), nameof(Actor.joinCity))]
    static class CityMove
    {
        static bool Prefix(Actor __instance, City pCity) => PoliticalAdmission.City(__instance, pCity);
    }
    [HarmonyPatch(typeof(Actor), "setCity")]
    static class CityAdmission
    {
        static bool Prefix(Actor __instance, City pCity) => PoliticalAdmission.City(__instance, pCity);
        static void Postfix(Actor __instance) => WorldRules.Reconcile(__instance);
    }
    [HarmonyPatch(typeof(Actor), nameof(Actor.joinKingdom))]
    static class KingdomMove
    {
        static bool Prefix(Actor __instance, Kingdom pKingdom) => WorldRules.Allows(__instance, WorldRules.Handle(MetaKind.Kingdom, pKingdom, pKingdom?.data));
    }
    [HarmonyPatch(typeof(Actor), "setKingdom")]
    static class KingdomAdmission
    {
        static bool Prefix(Actor __instance, Kingdom pKingdomToSet)
        {
            // A city's conquest changes its ownership. Never leave residents pointing
            // to a different kingdom from their city; these cases are audited in UI.
            if (__instance.city != null && __instance.city.kingdom == pKingdomToSet) return true;
            return WorldRules.Allows(__instance, WorldRules.Handle(MetaKind.Kingdom, pKingdomToSet, pKingdomToSet?.data));
        }
        static void Postfix(Actor __instance) => WorldRules.Reconcile(__instance);
    }
    [HarmonyPatch(typeof(Actor), "updateStats")]
    static class DeclaredStats
    {
        static void Prefix(Actor __instance, out bool __state) { __state = __instance.isStatsDirty(); }
        static void Postfix(Actor __instance, bool __state) { if (__state) WorldRules.AddStats(__instance); }
    }
    [HarmonyPatch(typeof(Actor), nameof(Actor.addTrait), new[] { typeof(ActorTrait), typeof(bool) })]
    static class ExternalTraitSource
    {
        static void Postfix(Actor __instance, ActorTrait pTrait) => WorldRules.ExternalTraitAdded(__instance, pTrait);
    }
    [HarmonyPatch(typeof(MapBox), nameof(MapBox.clearWorld))]
    static class ClearWorld
    {
        static void Prefix() => WorldRules.Reset();
    }
}
