using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using djack.RogueSurvivor.Data;
using djack.RogueSurvivor.Engine;
using djack.RogueSurvivor.Gameplay.Generators;

static class PopulationScalingScenario
{
    // Reflection keeps the measurement runner compatible with the preserved executable.
    static int Target(GameOptions options, int width, int height)
    {
        return (int)Check.Call(options, "CivilianPopulationForMap", new[] { typeof(int), typeof(int) }, width, height);
    }

    internal static void SetOptions(GameOptions options)
    {
        typeof(RogueGame).GetField("s_Options", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, options);
    }

    internal static ScenarioWorld Generate(ScenarioWorld setup, int width, int height, int policeChance, bool populate)
    {
        var parameters = BaseTownGenerator.DEFAULT_PARAMS;
        parameters.MapWidth = width; parameters.MapHeight = height;
        parameters.PolicemanChance = policeChance;
        parameters.GeneratePoliceStation = false; parameters.GenerateHospital = false;
        parameters.District = new District(new Point(0, 0), DistrictKind.RESIDENTIAL);
        BaseTownGenerator generator = populate ? new StdTownGenerator(setup.Game, parameters) : new BaseTownGenerator(setup.Game, parameters);
        var map = generator.Generate(setup.Seed);
        parameters.District.EntryMap = map;
        Session.Get.World[0, 0] = parameters.District;
        Session.Get.CurrentMap = map;
        return new ScenarioWorld(setup.Seed, map, setup.Game);
    }

    static int Residents(ScenarioWorld w)
    {
        return w.Map.Actors.Count(a => a.Faction == w.Game.GameFactions.TheCivilians || a.Faction == w.Game.GameFactions.ThePolice);
    }

    static ScenarioWorld Refugees(ScenarioWorld setup, int size)
    {
        var options = RogueGame.Options; options.MaxCivilians = 25;
        options.DistrictSize = 30; // Existing map size, rather than the new-game selector, determines capacity.
        SetOptions(options);
        var w = Generate(setup, size, size, 15, false);
        // Special interiors may already contain guards; this fixture measures arrivals into an empty district.
        foreach (var map in w.Map.District.Maps) foreach (var actor in map.Actors.ToArray()) map.RemoveActor(actor);
        var point = w.Map.FindFirstInMap(p => w.Map.IsWalkable(p.X, p.Y) && w.Map.GetActorAt(p) == null).Value;
        var player = NpcIntentSupport.Player(w, point.X, point.Y);
        w.Map.RemoveActor(player);
        new Map(w.Seed + 1, "player elsewhere", 2, 2).PlaceActorAt(player, Point.Empty);
        var sewer = new Map(w.Seed + 2, "refugee sewer", size, size);
        for (int x = 0; x < size; x++) for (int y = 0; y < size; y++) sewer.SetTileModelAt(x, y, w.Game.GameTiles.FLOOR_ASPHALT);
        w.Map.District.SewersMap = sewer;
        foreach (var property in typeof(UniqueActors).GetProperties())
            if (property.PropertyType == typeof(UniqueActor)) property.SetValue(Session.Get.UniqueActors, new UniqueActor(), null);
        return w;
    }

    static void Wave(ScenarioWorld w)
    {
        Check.Call(w.Game, "FireEvent_RefugeesWave", new[] { typeof(District) }, w.Map.District);
    }

    public static void Register()
    {
        ScenarioRunner.Add("world/population-area-targets", () => TownScenarioFactory.Create(5910, false), w => {
            var options = RogueGame.Options; options.ResetToDefaultValues();
            Check.Equal(25, Target(options, 50, 50), "standard district retains its population");
            Check.Equal(49, Target(options, 70, 70), "70x70 retains standard density");
            Check.Equal(100, Target(options, 100, 100), "100x100 has four standard areas");
            Check.Equal(9, Target(options, 30, 30), "smaller districts scale down");
            Check.Equal(16, Target(options, 40, 40), "40x40 target");
            Check.Equal(50, Target(options, 50, 100), "rectangular map uses both dimensions");
            Check.Equal(26, Target(options, 50, 51), "half resident rounds up");
            Check.Equal(1, Target(options, 1, 1), "tiny test maps retain a positive target");
            options.MaxCivilians = 75;
            Check.Equal(300, Target(options, 100, 100), "reference-density limit does not cap a large map at 75");
            Check.Throws<ArgumentOutOfRangeException>(() => Target(options, 0, 50), "invalid width");
            Check.Throws<ArgumentOutOfRangeException>(() => Target(options, 50, -1), "invalid height");
            SetOptions(options);
            var maximum = Generate(w, 100, 100, 15, true);
            Check.Equal(300, Residents(maximum), "maximum reference density places 300 actual residents on a large map");
        });
        foreach (int size in new[] { 50, 70, 100 }) foreach (int police in new[] { 0, 15, 100 }) {
            int mapSize = size, chance = police;
            ScenarioRunner.Add("world/population-generated-" + size + "-police-" + police,
                () => TownScenarioFactory.Create(5911 + mapSize + chance, false), setup => {
                    var options = RogueGame.Options; options.ResetToDefaultValues(); SetOptions(options);
                    var clock = Stopwatch.StartNew(); var w = Generate(setup, mapSize, mapSize, chance, true); clock.Stop();
                    int expected = Target(options, mapSize, mapSize);
                    Check.Equal(expected, Residents(w), "real generation places the entire area-scaled resident population");
                    int cops = w.Map.Actors.Count(a => a.Faction == w.Game.GameFactions.ThePolice);
                    if (chance == 0) Check.Equal(0, cops, "zero police chance remains zero");
                    if (chance == 100) Check.Equal(expected, cops, "police share the scaled population budget");
                    Check.Equal(expected, w.Map.Actors.Where(a => a.Faction == w.Game.GameFactions.TheCivilians || a.Faction == w.Game.GameFactions.ThePolice)
                        .Select(a => a.Location.Position).Distinct().Count(), "every resident occupies a distinct tile");
                    Console.WriteLine("POPULATION generation " + mapSize + "x" + mapSize + ": residents=" + expected + "; police=" + cops + "; ms=" + clock.Elapsed.TotalMilliseconds.ToString("F3"));
                });
        }
        foreach (int size in new[] { 50, 70, 100 }) {
            int mapSize = size;
            ScenarioRunner.Add("world/population-refugee-wave-" + size, () => TownScenarioFactory.Create(6020 + mapSize, false), setup => {
                var w = Refugees(setup, mapSize);
                Wave(w);
                int expected = 1 + (int)(0.20f * 2 * Target(RogueGame.Options, mapSize, mapSize));
                Check.Equal(expected, w.Map.District.Maps.Sum(m => m.CountActors), "wave size uses actual district area, including underground arrivals");
            });
        }
        ScenarioRunner.Add("world/population-refugee-cap-and-reload", () => TownScenarioFactory.Create(6130, false), setup => {
            var w = Refugees(setup, 100);
            var generator = new BaseTownGenerator(w.Game, BaseTownGenerator.DEFAULT_PARAMS);
            for (int i = 0; i < 99; i++) {
                var free = w.Map.FindFirstInMap(p => w.Map.IsWalkable(p.X, p.Y) && w.Map.GetActorAt(p) == null).Value;
                w.Map.PlaceActorAt(i % 2 == 0 ? generator.CreateNewCivilian(0, 0, 1) : generator.CreateNewPoliceman(0), free);
            }
            Wave(w);
            Check.Equal(100, w.Map.District.Maps.Sum(m => m.CountActors), "police and civilians both count toward the cap");
            string path = Path.Combine(Environment.GetEnvironmentVariable("ROGUE_COMMUNITY_ARTIFACTS"), "population-scaled-format-5.dat");
            BinarySaveStore.Save(path, Session.Get);
            var loaded = BinarySaveStore.Load<Session>(path);
            Check.Equal(100, loaded.World[0, 0].Maps.Sum(m => m.CountActors), "loading does not add or remove residents");
            var identities = w.Map.District.Maps.SelectMany(m => m.Actors).Select(a => a.PersonalityIdentity).OrderBy(id => id).ToArray();
            Check.Equal(String.Join(",", identities), String.Join(",", loaded.World[0, 0].Maps.SelectMany(m => m.Actors).Select(a => a.PersonalityIdentity).OrderBy(id => id)), "saved residents retain individual identities");
            var options = RogueGame.Options; options.MaxCivilians = 10; SetOptions(options);
            Wave(w);
            Check.Equal(100, w.Map.District.Maps.Sum(m => m.CountActors), "lowering density preserves an existing over-target population");
        });
        ScenarioRunner.Add("world/population-option-compatibility", () => TownScenarioFactory.Create(6131, false), w => {
            var options = RogueGame.Options; options.ResetToDefaultValues(); options.DistrictSize = 100;
            Check.Equal(true, options.DescribeValue(GameOptions.IDs.GAME_MAX_CIVILIANS).Contains("100/map"), "options show the resulting population");
            string path = Path.Combine(Environment.GetEnvironmentVariable("ROGUE_COMMUNITY_ARTIFACTS"), "population-options.dat");
            GameOptions.Save(options, path); var loaded = GameOptions.Load(path);
            Check.Equal(25, loaded.MaxCivilians, "stored reference density retains its existing field and default");
            Check.Equal(100, Target(loaded, 100, 100), "target rebuilds from existing options without new saved fields");
            options.DistrictSize = 50;
            Check.Equal(1f, Scoring.ComputeDifficultyRating(options, DifficultySide.FOR_SURVIVOR, 0), "default difficulty remains unchanged");
        });
    }
}
