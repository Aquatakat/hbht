#:project ../../src/HBHT.Core/HBHT.Core.csproj
#:property PublishAot=false
using System.Globalization;
using HBHT.Core;
using HBHT.Plugins;
using HBHT.Plugins.Saves;

// Prints flags as a save loads them: dotnet run tools/saves/flags.cs -- <save.json> <n,...>
var game = GameState.Load(PluginSet.LoadInstalled(DataPaths.Plugins), SaveFile.Read(args[0]));
foreach (var n in args[1].Split(',').Select(s => int.Parse(s, CultureInfo.InvariantCulture)))
    Console.WriteLine($"flag {n}: {game.Flags.Get(n)}");
foreach (var problem in game.Problems)
    Console.WriteLine($"problem: {problem}");
