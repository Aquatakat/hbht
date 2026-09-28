using System.Collections;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace Aidyn.Rom.Inspection;

/// <summary>
/// One field of a decoded record, for the asset inspector. Children are read only when asked
/// for, so a tree over a large Borg costs nothing until it's expanded.
/// </summary>
public sealed class FieldNode
{
    private readonly Lazy<IReadOnlyList<FieldNode>>? _children;

    internal FieldNode(string name, string value, Func<IReadOnlyList<FieldNode>>? children)
    {
        Name = name;
        Value = value;
        _children = children is null ? null : new Lazy<IReadOnlyList<FieldNode>>(children);
    }

    public string Name { get; }
    public string Value { get; }
    public bool HasChildren => _children is not null;
    public IReadOnlyList<FieldNode> Children => _children?.Value ?? [];

    public override string ToString() => $"{Name} = {Value}";
}

/// <summary>
/// Turns any decoded record into a <see cref="FieldNode"/> tree by reflection, so each new decoder
/// shows up in the inspector without inspector code. Records and classes expand into their public
/// properties, collections into their items (in pages of <see cref="PageSize"/>), and matrices into
/// rows. Numbers, enums, vectors and types with their own <c>ToString</c> are leaves.
/// </summary>
public static class FieldTree
{
    public const int PageSize = 256;

    public static FieldNode Of(string name, object? value)
    {
        switch (value)
        {
            case null:
                return Leaf(name, "null");
            case string s:
                return Leaf(name, Quote(s));
            case Enum e:
                return Leaf(name, EnumText(e));
            case bool b:
                return Leaf(name, b ? "true" : "false");
            case float f:
                return Leaf(name, f.ToString("R", CultureInfo.InvariantCulture));
            case double d:
                return Leaf(name, d.ToString("R", CultureInfo.InvariantCulture));
            case sbyte or byte or short or ushort or int or uint or long or ulong:
                return Leaf(name, IntegerText(Convert.ToInt64(value, CultureInfo.InvariantCulture)));
            case Vector2 v:
                return Leaf(name, Vector(v.X, v.Y));
            case Vector3 v:
                return Leaf(name, Vector(v.X, v.Y, v.Z));
            case Vector4 v:
                return Leaf(name, Vector(v.X, v.Y, v.Z, v.W));
            case Quaternion q:
                return Leaf(name, Vector(q.X, q.Y, q.Z, q.W));
            case Matrix4x4 m:
                return new FieldNode(name, "Matrix4x4", () =>
                [
                    Leaf("row 1", Vector(m.M11, m.M12, m.M13, m.M14)),
                    Leaf("row 2", Vector(m.M21, m.M22, m.M23, m.M24)),
                    Leaf("row 3", Vector(m.M31, m.M32, m.M33, m.M34)),
                    Leaf("row 4", Vector(m.M41, m.M42, m.M43, m.M44)),
                ]);
            case byte[] bytes:
                return Leaf(name, $"byte[{bytes.Length}]");
            case ITuple tuple when value.GetType().IsValueType:
                return Leaf(name, "(" + string.Join(", ", Enumerable.Range(0, tuple.Length).Select(i => Of("", tuple[i]).Value)) + ")");
            case IEnumerable items:
                return Collection(name, value.GetType(), items);
        }

        var type = value.GetType();
        if (!IsRecord(type) && DeclaresToString(type))
            return Leaf(name, value.ToString() ?? "");
        return new FieldNode(name, TypeName(type), () => Members(value));
    }

    private static FieldNode Leaf(string name, string value) => new(name, value, null);

    private static IReadOnlyList<FieldNode> Members(object value)
    {
        var nodes = new List<FieldNode>();
        foreach (var member in Readable(value.GetType()))
        {
            object? v;
            try
            {
                v = member is PropertyInfo p ? p.GetValue(value) : ((FieldInfo)member).GetValue(value);
            }
            catch (TargetInvocationException e)
            {
                nodes.Add(Leaf(member.Name, $"‹{e.InnerException?.Message ?? e.Message}›"));
                continue;
            }
            nodes.Add(Of(member.Name, v));
        }
        return nodes;
    }

    /// <summary>Public instance properties and fields, base types' first, each in declaration order.</summary>
    private static IEnumerable<MemberInfo> Readable(Type type)
    {
        var chain = new List<Type>();
        for (var t = type; t is not null && t != typeof(object) && t != typeof(ValueType); t = t.BaseType)
            chain.Insert(0, t);

        const BindingFlags Declared = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        return chain.SelectMany(t => t.GetProperties(Declared)
            .Where(p => p.GetIndexParameters().Length == 0 && p.GetMethod is not null)
            .Cast<MemberInfo>()
            .Concat(t.GetFields(Declared))
            .OrderBy(m => m.MetadataToken));
    }

    private static FieldNode Collection(string name, Type type, IEnumerable items)
    {
        var list = items as IList ?? items.Cast<object?>().ToList();
        var label = $"{TypeName(ElementType(type))}[{list.Count}]";
        return list.Count == 0 ? Leaf(name, label) : new FieldNode(name, label, () => Page(list, 0, list.Count));
    }

    /// <summary>Items <paramref name="start"/> to <paramref name="start"/> + <paramref name="count"/>, grouped into pages if there are too many.</summary>
    private static IReadOnlyList<FieldNode> Page(IList list, int start, int count)
    {
        if (count <= PageSize)
            return [.. Enumerable.Range(start, count).Select(i => Of($"[{i}]", list[i]))];

        var step = PageSize;
        while (count > step * PageSize)
            step *= PageSize;
        var pages = new List<FieldNode>();
        for (var s = start; s < start + count; s += step)
        {
            var (first, n) = (s, Math.Min(step, start + count - s));
            pages.Add(new FieldNode($"[{first}…{first + n - 1}]", "", () => Page(list, first, n)));
        }
        return pages;
    }

    private static Type ElementType(Type type)
    {
        if (type.IsArray)
            return type.GetElementType()!;
        var enumerable = type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>)
            ? type
            : type.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
        return enumerable?.GetGenericArguments()[0] ?? typeof(object);
    }

    private static string TypeName(Type type)
    {
        if (Nullable.GetUnderlyingType(type) is { } inner)
            return TypeName(inner) + "?";
        if (type.IsGenericType && type.FullName?.StartsWith("System.ValueTuple", StringComparison.Ordinal) == true)
            return "(" + string.Join(", ", type.GetGenericArguments().Select(TypeName)) + ")";
        if (!type.IsGenericType)
            return type.Name;
        var name = type.Name[..type.Name.IndexOf('`', StringComparison.Ordinal)];
        return $"{name}<{string.Join(", ", type.GetGenericArguments().Select(TypeName))}>";
    }

    /// <summary>Records get a compiler-written <c>PrintMembers</c>; their <c>ToString</c> is better expanded.</summary>
    private static bool IsRecord(Type type) =>
        type.GetMethod("PrintMembers", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance, [typeof(StringBuilder)]) is not null;

    private static bool DeclaresToString(Type type) =>
        type.GetMethod(nameof(ToString), Type.EmptyTypes)?.DeclaringType is { } d && d != typeof(object) && d != typeof(ValueType);

    private static string EnumText(Enum e)
    {
        var raw = Convert.ToInt64(e, CultureInfo.InvariantCulture);
        var text = e.ToString();
        if (text == raw.ToString(CultureInfo.InvariantCulture))
            return IntegerText(raw);
        return raw is >= 0 and < 10 or < 0 ? $"{text} ({raw})" : $"{text} (0x{raw:X})";
    }

    private static string IntegerText(long v) =>
        v is >= 0 and < 10 ? v.ToString(CultureInfo.InvariantCulture) : v < 0 ? v.ToString(CultureInfo.InvariantCulture) : $"{v} (0x{v:X})";

    private static string Vector(params float[] values) =>
        "(" + string.Join(", ", values.Select(v => v.ToString("R", CultureInfo.InvariantCulture))) + ")";

    private static string Quote(string s) =>
        "\"" + s.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal) + "\"";
}
