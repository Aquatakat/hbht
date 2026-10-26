#:project ../../src/HBHT.Core/HBHT.Core.csproj
#:property PublishAot=false
using System.Globalization;
using HBHT.Core;
using HBHT.Plugins;
using HBHT.Plugins.Records;
using HBHT.Plugins.Saves;

// Changes a save's party by the scripts' own ops, so the party's flags follow:
//   dotnet run tools/saves/party.cs -- <in.json> <out.json> [--remove <entity hex,...>] [--add <entity hex,...>]
var plugins = PluginSet.LoadInstalled(DataPaths.Plugins);
var game = GameState.Load(plugins, SaveFile.Read(args[0]));
RecordId Entity(string hex) => RecordId.Hex(RecordTypes.Entity, int.Parse(hex.Replace("0x", ""), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
for (var i = 2; i < args.Length; i += 2)
{
    foreach (var e in args[i + 1].Split(','))
    {
        if (args[i] == "--remove")
            game.RemoveMember(Entity(e));
        else if (args[i] == "--add" && !game.AddMember(Entity(e)))
            throw new InvalidOperationException($"{e} didn't join.");
    }
}
foreach (var problem in game.Problems)
    Console.WriteLine($"problem: {problem}");
SaveFile.Write(args[1], game.Save());
Console.WriteLine($"Party: {string.Join(", ", game.Party.Members.Select(m => m.Name))}");
