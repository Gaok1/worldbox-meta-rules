using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

// Exercise actual game data and lifecycle adapters without starting Unity.
// Uninitialized native objects isolate cleanup from rendering/world generation.
static class Program
{
    const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    static void Field(object target, string name, object value) =>
        target.GetType().GetField(name, InstanceFields).SetValue(target, value);

    static void Main(string[] args)
    {
        if (args.Length != 2)
            throw new ArgumentException("Expected game Managed directory and built MetaRules.dll path");
        string managed = Path.GetFullPath(args[0]);
        string nml = Path.Combine(Path.GetDirectoryName(managed), "StreamingAssets/mods");
        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            foreach (string directory in new[] { managed, nml, Path.Combine(nml, "NML/Assemblies") })
            {
                string path = Path.Combine(directory, name.Name + ".dll");
                if (File.Exists(path)) return context.LoadFromAssemblyPath(path);
            }
            return null;
        };

        // Load the original runtime assembly, not the publicized compilation reference:
        // the native JSON converter reflects private custom-data container fields.
        Assembly game = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(managed, "Assembly-CSharp.dll"));
        Assembly mod = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(args[1]));
        Type creation = mod.GetType("MetaRules.Game.MetaCreation", true);
        var factories = (IEnumerable)creation.GetField("Factories").GetValue(null);
        string key = (string)creation.GetField("KeepEmptyKey").GetRawConstantValue();
        MethodInfo keep = creation.GetMethod("KeepEmpty");
        MethodInfo set = game.GetType("BaseSystemData").GetMethod("set", new[] { typeof(string), typeof(bool) });
        Type json = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(managed, "Newtonsoft.Json.dll"))
            .GetType("Newtonsoft.Json.JsonConvert");
        MethodInfo serialize = json.GetMethod("SerializeObject", new[] { typeof(object) });
        MethodInfo deserialize = json.GetMethod("DeserializeObject", new[] { typeof(string), typeof(Type) });
        int count = 0;

        foreach (object factory in factories)
        {
            Type type = (Type)factory.GetType().GetField("ObjectType").GetValue(factory);
            object instance = RuntimeHelpers.GetUninitializedObject(type);
            FieldInfo dataField = type.GetField("data");
            object data = Activator.CreateInstance(dataField.FieldType);
            dataField.SetValue(instance, data);
            Field(instance, "_alive", true);
            void Assert(bool wanted, string message)
            {
                if ((bool)keep.Invoke(null, new[] { instance }) != wanted)
                    throw new Exception(type.Name + ": " + message);
            }

            Assert(false, "unmarked native object retained");
            set.Invoke(data, new object[] { key, true });
            if (type.Name == "Army")
            {
                Assert(false, "detached army retained");
                object city = RuntimeHelpers.GetUninitializedObject(game.GetType("City"));
                object kingdom = RuntimeHelpers.GetUninitializedObject(game.GetType("Kingdom"));
                Field(city, "_alive", true);
                Field(kingdom, "_alive", true);
                Field(city, "kingdom", kingdom);
                Field(instance, "_city", city);
                Field(instance, "_kingdom", kingdom);
                Assert(true, "army with living owners removed");
                Field(city, "_alive", false);
                Assert(false, "army retained after city destruction");
                Field(city, "_alive", true);
                Field(kingdom, "_alive", false);
                Assert(false, "army retained after kingdom destruction");
                Field(kingdom, "_alive", true);
            }
            Assert(true, "marked empty object removed");

            string saved = (string)serialize.Invoke(null, new[] { data });
            data = deserialize.Invoke(null, new object[] { saved, data.GetType() });
            dataField.SetValue(instance, data);
            Assert(true, "empty marker lost in native-data JSON round trip");
            Field(instance, "_alive", false);
            Assert(false, "dead object retained");
            Field(instance, "_alive", true);
            set.Invoke(data, new object[] { key, false });
            Assert(false, "cleared marker ignored");
            Console.WriteLine("PASS lifecycle/save data " + type.Name);
            count++;
        }
        if (count != 7) throw new Exception("Update lifecycle coverage for the registered factories");

        var methods = (IEnumerable)mod.GetType("MetaRules.Game.KeepCreatedMetas")
            .GetMethod("TargetMethods", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
        int targets = 0;
        foreach (MethodBase method in methods)
        {
            if (method.GetMethodBody() == null) throw new Exception("Missing native cleanup method body");
            targets++;
        }
        if (targets != 7) throw new Exception("Missing native cleanup target");
        Console.WriteLine($"PASS {count} native lifecycle contracts; {targets} cleanup targets");
    }
}
