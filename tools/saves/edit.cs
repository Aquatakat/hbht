#:project ../../src/HBHT.Core/HBHT.Core.csproj
#:property PublishAot=false
using System.Globalization;
using System.Numerics;
using HBHT.Core;
using HBHT.Core.Chunks;
using HBHT.Core.Physics;
using HBHT.Plugins;
using HBHT.Plugins.Records;
using HBHT.Plugins.Saves;

// Edits a save for a user check:
//   dotnet run tools/saves/edit.cs -- <in.json> <out.json> [--label <text>] [--at <mapset> <chunk> <x> <z> [<y>]] [--face <x> <z>]
//                         [--set <n,...>] [--clear <n,...>] [--give <item ID> <n>]
// --at stands the player on the floor under (x, y, z) of the chunk (y defaults to high above, sweeping down to the first floor),
// facing the point --face gives (in the same chunk), or as he faced. --give adds n of an item (aidyn:item/0x1000) to its stack,
// or a new stack after the others.
var (input, output) = (args[0], args[1]);
var plugins = PluginSet.LoadInstalled(DataPaths.Plugins);
var save = SaveFile.Read(input);
const float Radius = 0.5f;
float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);
var flags = save.Flags.ToHashSet();
(int MapSet, string Chunk, Vector3 Local, bool HasY)? at = null;
Vector2? face = null;
for (var i = 2; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--label": save = save with { Label = args[++i] }; break;
        case "--at":
            var (set, chunk, x, z) = (int.Parse(args[i + 1], CultureInfo.InvariantCulture), args[i + 2], F(args[i + 3]), F(args[i + 4]));
            i += 4;
            var hasY = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal);
            at = (set, chunk, new Vector3(x, hasY ? F(args[++i]) : 500, z), hasY);
            break;
        case "--face": face = new Vector2(F(args[++i]), F(args[++i])); break;
        case "--set": foreach (var n in args[++i].Split(',')) flags.Add(RecordId.Decimal(RecordTypes.Flag, int.Parse(n, CultureInfo.InvariantCulture))); break;
        case "--clear": foreach (var n in args[++i].Split(',')) flags.Remove(RecordId.Decimal(RecordTypes.Flag, int.Parse(n, CultureInfo.InvariantCulture))); break;
        case "--give":
            var (item, count) = (RecordId.Parse(args[i + 1]), int.Parse(args[i + 2], CultureInfo.InvariantCulture));
            i += 2;
            var stacks = save.Party.Inventory.ToList();
            var held = stacks.FindIndex(s => s.Item == item);
            if (held < 0) stacks.Add(new SaveStack(item, count));
            else stacks[held] = stacks[held] with { Quantity = stacks[held].Quantity + count };
            save = save with { Party = save.Party with { Inventory = stacks } };
            break;
        default: throw new ArgumentException($"What's {args[i]}?");
    }
}
save = save with { Flags = [.. flags.OrderBy(f => int.Parse(f.Key, CultureInfo.InvariantCulture))] };

if (at is { } a)
{
    var set = plugins.Get<MapSetRecord>(RecordId.Decimal(RecordTypes.MapSet, a.MapSet));
    var streamer = new ChunkStreamer(plugins, GameState.NewGame(plugins), new CollisionWorld(), set);
    var chunk = streamer.Find(a.Chunk) ?? throw new ArgumentException($"{set.Label} has no chunk {a.Chunk}.");
    var origin = streamer.Origin(chunk);
    var from = origin + a.Local + (a.HasY ? new Vector3(0, 2, 0) : Vector3.Zero);
    streamer.Update(from);
    var depth = a.HasY ? 20 : 2000;
    if (!SpherePhysics.Sweep(streamer.World, from, from - new Vector3(0, depth, 0), Radius, false, out var ground, out var normal))
        throw new InvalidOperationException($"No floor under {a.Chunk} {a.Local}.");
    var centre = ground + normal * Radius - origin;
    var heading = face is { } f ? Vector2.Normalize(new Vector2(f.X - centre.X, f.Y - centre.Z)) : save.Place?.Heading ?? new Vector2(0, -1);
    save = save with { Place = new SavePlace(set.Id, a.Chunk, centre, heading) };
    Console.WriteLine($"{set.Label} {a.Chunk}: standing at {centre} (floor normal {normal}), facing {heading}");
}
SaveFile.Write(output, save);
Console.WriteLine($"Wrote {output}");
