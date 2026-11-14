using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Godot;

/// <summary>
/// Whether GodotSharp binds to the template it runs on (docs/systems/releases.md#the-engine). GodotSharp looks up every
/// method of a class in the class's static constructor, the first time the class is used, so a template missing one
/// method of a class fails every use of it. This runs the static constructor of each of GodotSharp's classes whose
/// engine class the template registers, and quits with 1 if any fails. It lists GodotSharp's classes the template
/// doesn't register (the build profile's and the disabled modules'), by their C# names, on a line of their own:
/// tools/package.ps1 fails an export whose assemblies use any of them.
/// </summary>
public partial class BindCheck : Node
{
    public override void _Ready()
    {
        var registered = ClassDB.GetClassList().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var failed = new List<string>();
        var unregistered = new List<string>();
        var checkedCount = 0;
        foreach (var type in typeof(GodotObject).Assembly.GetTypes().OrderBy(t => t.FullName))
        {
            if (type.Namespace != "Godot" || type.IsGenericTypeDefinition || type.IsNested) continue;
            // A class of the engine's: an object, or one of the static classes over the singletons (Input, OS, ...),
            // which hold their methods' binds the same way.
            var engineClass = typeof(GodotObject).IsAssignableFrom(type)
                || (type.IsAbstract && type.IsSealed && type.GetField("NativeName", BindingFlags.Static | BindingFlags.NonPublic) is not null);
            if (!engineClass) continue;
            var name = type.GetCustomAttributesData().FirstOrDefault(a => a.AttributeType.Name == "GodotClassNameAttribute")?.ConstructorArguments[0].Value as string ?? type.Name;
            if (type == typeof(GodotObject)) name = "Object";
            if (!registered.Contains(name))
            {
                unregistered.Add(type.Name);
                continue;
            }
            checkedCount++;
            try
            {
                RuntimeHelpers.RunClassConstructor(type.TypeHandle);
            }
            catch (TypeInitializationException e)
            {
                failed.Add($"{type.Name}: {e.InnerException?.Message}");
            }
        }
        GD.Print($"BindCheck: {checkedCount} classes bound, {failed.Count} failed, {unregistered.Count} not in this template");
        GD.Print($"BindCheck missing: {string.Join(' ', unregistered)}");
        foreach (var line in failed) GD.PrintErr($"BindCheck failed: {line}");
        GetTree().Quit(failed.Count == 0 ? 0 : 1);
    }
}
