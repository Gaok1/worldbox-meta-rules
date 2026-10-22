using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using MetaRules.Core;

namespace MetaRules.Game
{
    // Only the creation adapter varies by type. Identity, storage, customization and
    // serialization remain owned by the native managers and meta objects.
    public static class MetaCreation
    {
        public const string KeepEmptyKey = "gaok1_meta_created_empty_v1";

        public sealed class Factory
        {
            public MetaKind Kind;
            public Type ObjectType;
            internal Func<Context, MetaHandle> Create;
            internal Func<object, BaseSystemData> Data;
        }

        internal sealed class Context
        {
            public ActorAsset Species;
            public WorldTile Tile;
            public City City;
            public Subspecies Subspecies;
        }

        public static readonly IReadOnlyList<Factory> Factories = new[]
        {
            Register<Language, LanguageData>(MetaKind.Language, () => World.world.languages, (m, c) =>
            {
                m.data.creator_species_id = c.Species.id;
                m.generateNewMetaObject(true);
                m.books.setMeta(null, m);
                World.world.game_stats.data.languagesCreated++;
                World.world.map_stats.languagesCreated++;
            }),
            Register<Culture, CultureData>(MetaKind.Culture, () => World.world.cultures, (m, c) =>
            {
                m.data.original_actor_asset = c.Species.id;
                m.data.name_template_set = c.Species.name_template_sets.GetRandom();
                m.generateNewMetaObject(true);
                m.books.setMeta(m);
                World.world.game_stats.data.culturesCreated++;
                World.world.map_stats.culturesCreated++;
            }),
            Register<Religion, ReligionData>(MetaKind.Religion, () => World.world.religions, (m, c) =>
            {
                m.data.creator_species_id = c.Species.id;
                m.generateNewMetaObject(true);
                m.books.setMeta(null, null, m);
                World.world.game_stats.data.religionsCreated++;
                World.world.map_stats.religionsCreated++;
            }),
            Register<Clan, ClanData>(MetaKind.Clan, () => World.world.clans, (m, c) =>
            {
                m.data.original_actor_asset = c.Species.id;
                m.generateNewMetaObject(true);
                World.world.game_stats.data.clansCreated++;
                World.world.map_stats.clansCreated++;
            }),
            Register<Family, FamilyData>(MetaKind.Family, () => World.world.families, (m, c) =>
            {
                m.data.species_id = c.Species.id;
                m.data.subspecies_id = c.Subspecies?.id ?? -1;
                m.data.subspecies_name = c.Subspecies?.name;
                m.generateNewMetaObject();
                World.world.game_stats.data.familiesCreated++;
                World.world.map_stats.familiesCreated++;
            }),
            Register<Subspecies, SubspeciesData>(MetaKind.Subspecies, () => World.world.subspecies, (m, c) =>
            {
                m.newSpecies(c.Species, c.Tile);
                World.world.game_stats.data.subspeciesCreated++;
                World.world.map_stats.subspeciesCreated++;
            }),
            Register<Army, ArmyData>(MetaKind.Army, () => World.world.armies, (m, c) =>
            {
                m.createArmy(null, c.City);
                c.City.setArmy(m);
                World.world.game_stats.data.armiesCreated++;
                World.world.map_stats.armiesCreated++;
            })
        };

        static Factory Register<TObject, TData>(MetaKind kind, Func<MetaSystemManager<TObject, TData>> manager,
            Action<TObject, Context> initialize)
            where TObject : MetaObject<TData>, new() where TData : MetaObjectData, new()
        {
            return new Factory
            {
                Kind = kind, ObjectType = typeof(TObject), Data = value => ((TObject)value).data,
                Create = context =>
                {
                    var owner = manager();
                    TObject made = owner.newObject();
                    try
                    {
                        initialize(made, context);
                        // There is deliberately no actor founder and no membership setter.
                        if (string.IsNullOrEmpty(made.name)) made.setName(UI.RulesWindow.KindName(kind) + " #" + made.id);
                        made.data.set(KeepEmptyKey, true);
                        made.preserveAlive();
                        owner.checkLists(); // Include it even if the player saves while paused.
                        return WorldRules.Handle(kind, made, made.data);
                    }
                    catch
                    {
                        owner.removeObject(made);
                        owner.checkLists();
                        throw;
                    }
                }
            };
        }

        public static Factory For(MetaKind kind)
        {
            foreach (Factory factory in Factories) if (factory.Kind == kind) return factory;
            return null;
        }

        static Context GetContext(MetaHandle source, MetaKind kind, City city)
        {
            Actor wearer = (source?.Target as Item)?.getActor();
            var meta = source?.Target as IMetaObject;
            ActorAsset species = meta?.getActorAsset() ?? wearer?.asset ?? AssetManager.actor_library.get("human");
            // Cultures need a naming template; animals can still seed families/subspecies.
            if (kind != MetaKind.Family && kind != MetaKind.Subspecies &&
                (species?.name_template_sets == null || species.name_template_sets.Length == 0))
                species = AssetManager.actor_library.get("human");
            Subspecies subspecies = null;
            if (kind == MetaKind.Family)
            {
                subspecies = source?.Target as Subspecies ?? wearer?.subspecies;
                if (subspecies == null && source?.Target is Family family)
                    subspecies = World.world.subspecies.get(family.data.subspecies_id);
                if (subspecies == null && meta != null)
                    foreach (Actor unit in meta.getUnits())
                        if (unit != null && unit.isAlive() && unit.asset == species && unit.subspecies != null)
                        { subspecies = unit.subspecies; break; }
                if (subspecies == null)
                    foreach (Subspecies candidate in World.world.subspecies)
                        if (candidate.isAlive() && candidate.data.species_id == species?.id)
                        { subspecies = candidate; break; }
            }
            return new Context
            {
                Species = species,
                Tile = wearer?.current_tile ?? World.world.GetTileSimple(0, 0),
                City = city,
                Subspecies = subspecies
            };
        }

        public static string Problem(MetaKind kind, MetaHandle source, City city = null)
        {
            if (For(kind) == null) return "creation_territory";
            if (World.world?.map_stats == null || World.world.units == null || SmoothLoader.isLoading() ||
                source == null || !WorldRules.Metas.Exists(m => ReferenceEquals(m.Target, source.Target) && m.Id == source.Id))
                return "creation_unavailable";
            if (kind == MetaKind.Army && (city == null || !city.isAlive() || city.kingdom == null ||
                !city.kingdom.isAlive() || city.hasArmy())) return "creation_army_city";
            Context context = GetContext(source, kind, city);
            if (context.Species == null || context.Tile == null) return "creation_unavailable";
            return null;
        }

        public static MetaHandle Create(MetaKind kind, MetaHandle source, City city = null)
        {
            WorldRules.RefreshMetas();
            if (Problem(kind, source, city) != null) return null;
            MetaHandle made = For(kind).Create(GetContext(source, kind, city));
            WorldRules.RefreshMetas();
            WorldRules.RescanSoon();
            return made;
        }

        public static bool KeepEmpty(object value)
        {
            if (!(value is NanoObject native) || !native.isAlive()) return false;
            foreach (Factory factory in Factories)
            {
                if (!factory.ObjectType.IsInstanceOfType(value)) continue;
                BaseSystemData data = factory.Data(value);
                if (data == null) return false;
                data.get(KeepEmptyKey, out bool keep, false);
                // Detached armies still follow native destruction when their city disappears.
                return keep && (!(value is Army army) || army.getCity() != null && army.getCity().isAlive() &&
                    army.getKingdom() != null && army.getKingdom().isAlive());
            }
            return false;
        }
    }

    [HarmonyPatch]
    static class KeepCreatedMetas
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            var seen = new HashSet<MethodBase>();
            foreach (MetaCreation.Factory factory in MetaCreation.Factories)
            {
                MethodInfo method = AccessTools.Method(factory.ObjectType, "isReadyForRemoval");
                if (method != null && seen.Add(method)) yield return method;
            }
        }

        static bool Prefix(object __instance, ref bool __result)
        {
            if (!MetaCreation.KeepEmpty(__instance)) return true;
            __result = false;
            return false;
        }
    }
}
