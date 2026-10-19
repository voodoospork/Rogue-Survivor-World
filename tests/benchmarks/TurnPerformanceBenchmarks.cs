using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;
using djack.RogueSurvivor.Data;
using djack.RogueSurvivor.Engine;
using djack.RogueSurvivor.Gameplay;
using djack.RogueSurvivor.Gameplay.AI;
using djack.RogueSurvivor.Engine.Items;
using djack.RogueSurvivor.Engine.MapObjects;

// Controlled moving-world workloads. No claim of native graphics or human play.
static class TurnPerformanceBenchmarks
{
    public static void WriteFireCatchup(string directory)
    {
        WriteCatchup(directory);
        var setup=TownScenarioFactory.Create(5890,false);
        var w=NpcIntentSupport.Restore(setup,BinarySaveStore.Load<Session>(Path.Combine(directory,"catchup-quiet.dat")));
        var map=Session.Get.World[1,0].EntryMap;map.ReconstructAuxiliaryFields();
        var fire=new DoorWindow("burning door","closed","open","broken",40);
        map.PlaceMapObjectAt(fire,new System.Drawing.Point(21,21));fire.FireState=MapObject.Fire.ONFIRE;
        Session.Get.WorldTime.TurnCounter=w.Map.LocalTime.TurnCounter=368;
        BinarySaveStore.Save(Path.Combine(directory,"catchup-fire.dat"),Session.Get);
        Console.WriteLine("WROTE active-fire catch-up: 100 guards, ordinary 40-HP burning door, eight full elapsed turns; no protected actors or altered actor health");
    }
    public static void WriteCatchup(string directory)
    {
        if (Directory.Exists(directory) && Directory.GetFiles(directory).Length > 0) throw new InvalidOperationException("Preserve catch-up fixtures.");
        Directory.CreateDirectory(directory);
        foreach (string kind in new[] { "quiet", "injured" }) {
            var w = TownScenarioFactory.Arena(5890, Enumerable.Repeat(new string('.', 24), 8).ToArray());
            Session.Get.GamePreset = GamePreset.World(); Session.Get.GamePreset.OrganicCommunities = false;
            Session.Get.UniqueActors.TheSewersThing = new UniqueActor(); NpcIntentSupport.Player(w, 1, 7);
            var chief = NpcIntentSupport.Actor(w, "remote guards' leader", 20, 2);
            var district = new District(new Point(1, 0), DistrictKind.GENERAL);
            var map = new Map(5891, "Quiet guard post", 50, 50); map.Lighting = Lighting.LIT;
            for (int y = 0; y < 50; y++) for (int x = 0; x < 50; x++) map.SetTileModelAt(x, y, w.Game.GameTiles.FLOOR_ASPHALT);
            district.EntryMap = map;
            var world = new World(2); world[0, 0] = w.Map.District; world[1, 0] = district;
            // Empty neighbouring districts keep normal world reconstruction valid.
            for (int x = 0; x < 2; x++) { var empty = new District(new Point(x, 1), DistrictKind.GENERAL);
                var floor = new Map(5892 + x, "Empty post", 5, 5); for (int y = 0; y < 5; y++) for (int xx = 0; xx < 5; xx++) floor.SetTileModelAt(xx,y,w.Game.GameTiles.FLOOR_ASPHALT);
                empty.EntryMap = floor; world[x, 1] = empty; }
            Session.Get.World = world;
            var remote = new ScenarioWorld(5891, map, w.Game);
            for (int i = 0; i < 100; i++) {
                var guard = NpcIntentSupport.Actor(remote, "guard-" + i, 3 + i % 10 * 4, 3 + i / 10 * 4);
                chief.AddFollower(guard);
                guard.HitPoints = w.Game.Rules.ActorMaxHPs(guard); guard.FoodPoints = w.Game.Rules.ActorMaxFood(guard);
                guard.SleepPoints = w.Game.Rules.ActorMaxSleep(guard); guard.Sanity = w.Game.Rules.ActorMaxSanity(guard);
                ((BaseAI)guard.Controller).SetOrder(new ActorOrder(ActorTasks.GUARD, guard.Location));
            }
            if (kind == "injured") map.GetActor(0).HitPoints--;
            w.Map.LocalTime.TurnCounter = 480; map.LocalTime.TurnCounter = 360; Session.Get.WorldTime.TurnCounter = 480;
            BinarySaveStore.Save(Path.Combine(directory, "catchup-" + kind + ".dat"), Session.Get);
            Console.WriteLine("WROTE fixed catch-up " + kind + ": 100 real guards; 120 full elapsed turns; no survival skips");
        }
    }
    public static void RunCatchup(string path)
    {
        if (Environment.GetEnvironmentVariable("ROGUE_CATCHUP_WARMUP") == "1") {
            var output = Console.Out;
            try {
                Environment.SetEnvironmentVariable("ROGUE_CATCHUP_WARMUP", "0");
                Console.SetOut(TextWriter.Null);
                RunCatchup(path);
            } finally {
                Console.SetOut(output);
                Environment.SetEnvironmentVariable("ROGUE_CATCHUP_WARMUP", "1");
            }
            Console.WriteLine("CATCHUP METHOD warmed process; one untimed run, then reload the identical original save; all timed state starts from that save");
        }
        bool diagnostic = Environment.GetEnvironmentVariable("ROGUE_PROFILE_TURNS") == "1";
        if (diagnostic) AppDomain.MonitoringIsEnabled = true;
        var setup = TownScenarioFactory.Create(5890, false);
        var w = NpcIntentSupport.Restore(setup, BinarySaveStore.Load<Session>(path));
        for (int x = 0; x < Session.Get.World.Size; x++) for (int y = 0; y < Session.Get.World.Size; y++)
            foreach (Map floor in Session.Get.World[x,y].Maps) floor.ReconstructAuxiliaryFields();
        int mode = 0; Int32.TryParse(Environment.GetEnvironmentVariable("ROGUE_PERF_MODE"), out mode);
        var field = typeof(GamePreset).GetField("Simulation"); if (field != null) field.SetValue(Session.Get.GamePreset, Enum.ToObject(field.FieldType, mode));
        var district = Session.Get.World[1,0]; var map = district.EntryMap;
        var adaptive = typeof(RogueGame).GetMethod("AdvanceAdaptiveDistrict", BindingFlags.Instance | BindingFlags.NonPublic);
        var flags = typeof(RogueGame).GetNestedType("SimFlags", BindingFlags.NonPublic);
        var detailed = typeof(RogueGame).GetMethod("AdvancePlay", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(Map), flags }, null);
        object highDetail = Enum.Parse(flags, "HIDETAIL_TURN");
        int start = map.LocalTime.TurnCounter, target = Session.Get.WorldTime.TurnCounter;
        var profile = typeof(RogueGame).Assembly.GetType("djack.RogueSurvivor.Engine.SimulationPerformance");
        if (diagnostic && profile != null) profile.GetMethod("Reset",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
        long retained = GC.GetTotalMemory(true), peak = retained;
        var times = new List<double>(); var total = Stopwatch.StartNew();
        while (map.LocalTime.TurnCounter < target) {
            var watch = Stopwatch.StartNew();
            if (mode == 2) adaptive.Invoke(w.Game, new object[] { district, target, 1 });
            else { int before = map.LocalTime.TurnCounter;
                do { detailed.Invoke(w.Game, new object[] { map, highDetail }); } while (map.LocalTime.TurnCounter == before); }
            watch.Stop(); times.Add(watch.Elapsed.TotalMilliseconds); peak = Math.Max(peak, GC.GetTotalMemory(false));
        }
        total.Stop(); var sorted = times.OrderBy(t => t).ToArray();
        Console.WriteLine(String.Format(CultureInfo.InvariantCulture,
            "CATCHUP BENCH {0}: mode={1}; actors={2}; turns={3}; total={4:F3} ms; median={5:F3}; p95={6:F3}; p99={7:F3}; managed-peak-delta={8}; world={9}; local={10}",
            Path.GetFileNameWithoutExtension(path), mode, map.CountActors, map.LocalTime.TurnCounter-start, total.Elapsed.TotalMilliseconds,
            sorted[(sorted.Length-1)/2], sorted[(int)Math.Ceiling(sorted.Length*.95)-1], sorted[(int)Math.Ceiling(sorted.Length*.99)-1],peak-retained,
            Session.Get.WorldTime.TurnCounter,map.LocalTime.TurnCounter));
        var adaptiveField = typeof(District).GetField("Adaptive",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public);
        var state = adaptiveField?.GetValue(district);
        if (state != null) foreach (var record in (System.Collections.IEnumerable)state.GetType().GetField("Maps").GetValue(state))
            Console.WriteLine("CATCHUP ELIGIBILITY quiet="+record.GetType().GetField("QuietTurns").GetValue(record)+"; detailed="+record.GetType().GetField("DetailedTurns").GetValue(record));
        Console.WriteLine("CATCHUP CONSERVATION people="+map.Actors.Count()+"; food="+map.Actors.Sum(a=>a.FoodPoints)+"; item-units="+map.Actors.Sum(a=>a.Inventory.Items.Sum(i=>i.Quantity)));
        Console.WriteLine("CATCHUP HAZARDS fires="+map.MapObjects.Count(obj=>obj.IsOnFire)+"; corpses="+map.CountCorpses);
        if (diagnostic) {
            Console.WriteLine("CATCHUP DIAGNOSTIC per-round ms: " + String.Join(",", times.Select(t => t.ToString("F3", CultureInfo.InvariantCulture))));
            if (profile != null) Console.WriteLine(profile.GetMethod("Report",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null));
        }
    }
    public static void WriteMatrix(string directory)
    {
        if (Directory.Exists(directory) && Directory.GetFiles(directory).Length > 0) throw new InvalidOperationException("Preserve fixed matrix fixtures.");
        Directory.CreateDirectory(directory);
        foreach (int size in new[] { 50, 70, 100 }) foreach (string density in new[] { "ordinary", "matched", "increased" }) {
            int population = density == "ordinary" ? GameOptions.DEFAULT_MAX_CIVILIANS : GameOptions.DEFAULT_MAX_CIVILIANS * size * size / 2500 * (density == "increased" ? 2 : 1);
            var w = TownScenarioFactory.Arena(5700 + size + population, Enumerable.Repeat(new string('.', size), size).ToArray());
            Session.Get.GamePreset = GamePreset.World(); Session.Get.GamePreset.OrganicCommunities = false;
            Session.Get.UniqueActors.TheSewersThing = new UniqueActor();
            NpcIntentSupport.Player(w, 1, 1);
            int columns = (int)Math.Ceiling(Math.Sqrt(population)), rows = (population + columns - 1) / columns;
            for (int i = 0; i < population; i++) {
                var actor = NpcIntentSupport.Actor(w, "resident-" + i, 3 + i % columns * (size - 6) / (columns - 1),
                    3 + i / columns * (size - 6) / Math.Max(1, rows - 1), i % 2 == 0 ? "homebody" : "brave");
                actor.EnsurePersonalGroup(); NpcIntentSupport.Food(w, actor, 2);
                if (i % 3 == 0) ((OrderableAI)actor.Controller).SetOrder(new ActorOrder(ActorTasks.GUARD, actor.Location));
            }
            Session.Get.WorldTime.TurnCounter = w.Map.LocalTime.TurnCounter = 360;
            CommunityMembershipScenario.Fov(w);
            string path = Path.Combine(directory, "matrix-" + size + "-" + density + ".dat");
            BinarySaveStore.Save(path, Session.Get);
            Console.WriteLine("WROTE " + path + "; residents=" + population + "; fixture populations do not alter game defaults or limits");
        }
    }
    public static void Write(string directory)
    {
        Directory.CreateDirectory(directory);
        foreach (int population in new[] { 32, 96, 192 }) {
            var world = TownScenarioFactory.Arena(5400 + population, Enumerable.Repeat(new string('.', 60), 48).ToArray());
            Session.Get.GamePreset = GamePreset.World();
            Session.Get.GamePreset.OrganicCommunities = false; Session.Get.GamePreset.InitialCommunities = true;
            Session.Get.UniqueActors.TheSewersThing = new UniqueActor();
            RogueGame.KeyBindings.ResetToDefaults();
            var player = NpcIntentSupport.Player(world, 1, 7);
            player.EnsurePersonalGroup();
            for (int i = 0; i < 4; i++) {
                var follower = NpcIntentSupport.Actor(world, "companion-" + i, 1+i, 8, "loyal");
                player.AddFollower(follower);
            }
            CommunityMembershipScenario.Group(world);
            for (int i = 0; i < population; i++) {
                var npc = NpcIntentSupport.Actor(world, "resident-" + i, 12+i%40, 9+i/40,
                    i%2 == 0 ? "homebody" : "brave");
                npc.EnsurePersonalGroup();
                // Real assignments, visible/survival checks and actions still run.
                if (i%3 == 0) ((OrderableAI)npc.Controller).SetOrder(new ActorOrder(ActorTasks.GUARD,npc.Location));
                NpcIntentSupport.Food(world,npc,2);
            }
            Session.Get.WorldTime.TurnCounter = world.Map.LocalTime.TurnCounter = 360;
            CommunityMembershipScenario.Fov(world);
            string path = Path.Combine(directory,"turns-"+population+".dat");
            BinarySaveStore.Save(path,Session.Get);
            Console.WriteLine("WROTE moving fixture {0}: actors={1}; fixed saved identities; ordinary needs/actions; controlled population",path,world.Map.CountActors);
        }
    }
    static string Fingerprint(ScenarioWorld world)
    {
        var text = new StringBuilder();
        text.Append(world.Map.LocalTime.TurnCounter).Append('|');
        var state = typeof(DiceRoller).GetField("m_State",BindingFlags.Instance|BindingFlags.NonPublic);
        text.Append(state.GetValue(world.Game.Rules.DiceRoller)).Append('|');
        foreach (var actor in world.Map.Actors) {
            text.Append(actor.PersonalityIdentity).Append(':').Append(actor.Location.Position.X).Append(',').Append(actor.Location.Position.Y)
                .Append(',').Append(actor.HitPoints).Append(',').Append(actor.FoodPoints).Append(',').Append(actor.SleepPoints)
                .Append(',').Append(actor.StaminaPoints).Append(',').Append(actor.ActionPoints).Append(',').Append(actor.IsSleeping)
                .Append(',').Append(actor.Leader == null ? Guid.Empty : actor.Leader.PersonalityIdentity);
            if(actor.Inventory != null)foreach(var item in actor.Inventory.Items)text.Append('/').Append(item.Model.ID).Append('=').Append(item.Quantity);
            if(actor.Personality!=null) {
                foreach(var fact in actor.Personality.Events) text.Append('!').Append(fact.Kind).Append('@').Append(fact.Turn);
                foreach(var community in actor.Personality.Communities) {
                    text.Append('~').Append(community.Identity);
                    foreach(var note in community.Notes)text.Append(';').Append(note.Kind).Append('@').Append(note.EventTurn);
                }
            }
            text.Append('|');
        }
        using(var sha = SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()))).Replace("-","");
    }
    public static void Run(string path)
    {
        bool diagnostic = Environment.GetEnvironmentVariable("ROGUE_PROFILE_TURNS") == "1";
        if (diagnostic) AppDomain.MonitoringIsEnabled = true;
        var setup = TownScenarioFactory.Create(5400,false);
        var world = NpcIntentSupport.Restore(setup,BinarySaveStore.Load<Session>(path));
        // Generated fixtures include reachable basements and other floors.
        for (int x = 0; x < Session.Get.World.Size; x++) for (int y = 0; y < Session.Get.World.Size; y++)
            foreach (Map floor in Session.Get.World[x, y].Maps) floor.ReconstructAuxiliaryFields();
        // Generated-map movement can open an ordinary trade prompt; the scripted player declines.
        if (Environment.GetEnvironmentVariable("ROGUE_PERF_DECLINE_OFFERS") == "1")
            for (int i = 0; i < 4096; i++) ((ScenarioUI)world.Game.UI).QueueWaitKey(Keys.N);
        int simulation;
        var mode = typeof(GamePreset).GetField("Simulation");
        if (mode != null && Int32.TryParse(Environment.GetEnvironmentVariable("ROGUE_PERF_MODE"), out simulation))
            mode.SetValue(Session.Get.GamePreset, Enum.ToObject(mode.FieldType, simulation));
        RogueGame.KeyBindings.ResetToDefaults();
        var profile = typeof(RogueGame).Assembly.GetType("djack.RogueSurvivor.Engine.SimulationPerformance");
        if(profile!=null)profile.GetMethod("Reset",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
        var times = new List<double>(); var trace = new StringBuilder();
        int startActors=world.Map.CountActors, gc0=GC.CollectionCount(0), gc1=GC.CollectionCount(1),gc2=GC.CollectionCount(2);
        int startTurn=world.Map.LocalTime.TurnCounter;
        var originalActors=world.Map.Actors.ToArray();
        var timedGc=new int[3];
        long timedAllocated = 0, memoryBaseline = GC.GetTotalMemory(false), memoryPeak = memoryBaseline;
        int rounds;
        if (!Int32.TryParse(Environment.GetEnvironmentVariable("ROGUE_PERF_ROUNDS"), out rounds) || rounds < 1) rounds = 160;
        for(int round=0;round<rounds;round++) {
            var beforeGc=new[]{GC.CollectionCount(0),GC.CollectionCount(1),GC.CollectionCount(2)};
            long allocatedBefore = diagnostic ? AppDomain.CurrentDomain.MonitoringTotalAllocatedMemorySize : 0;
            var watch = Stopwatch.StartNew();
            var player = world.Game.Player;
            if(!player.IsSleeping) {
                player.ActionPoints=Rules.BASE_ACTION_COST;
                // Legal movement with normal costs, or a normal wait when occupied.
                Direction direction=round%24<12?Direction.E:Direction.W;
                var action=new djack.RogueSurvivor.Engine.Actions.ActionBump(player,world.Game,direction);
                if(action.IsLegal())action.Perform();else world.Game.DoWait(player);
            }
            CommunityClarityScenario.AmbientTurn(world);
            watch.Stop(); times.Add(watch.Elapsed.TotalMilliseconds);
            if (diagnostic) timedAllocated += AppDomain.CurrentDomain.MonitoringTotalAllocatedMemorySize - allocatedBefore;
            memoryPeak = Math.Max(memoryPeak, GC.GetTotalMemory(false));
            for(int g=0;g<3;g++)timedGc[g]+=GC.CollectionCount(g)-beforeGc[g];
            trace.Append(round).Append('\t').Append(Fingerprint(world)).AppendLine();
            var ui=(ScenarioUI)typeof(RogueGame).GetField("m_UI",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(world.Game);
            ui.DrawnStrings.Clear();ui.DrawnImages.Clear();ui.DrawnBold.Clear();
            if(player.IsDead)break;
        }
        var sorted=times.OrderBy(t=>t).ToArray();
        string digest;
        using(var sha=SHA256.Create())digest=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(trace.ToString()))).Replace("-","");
        string label=Environment.GetEnvironmentVariable("ROGUE_PERF_LABEL")??"unlabelled";
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(path),Path.GetFileNameWithoutExtension(path)+"-"+label+"-trace.tsv"),trace.ToString());
        Console.WriteLine(String.Format(CultureInfo.InvariantCulture,
            "TURN BENCH {0}: rounds={1}; actors={2}->{3}; local={4}->{5}; median={6:F3}; p95={7:F3}; p99={8:F3} ms; GC={9}/{10}/{11}; digest={12}",
            Path.GetFileNameWithoutExtension(path),times.Count,startActors,world.Map.CountActors,startTurn,world.Map.LocalTime.TurnCounter,
            sorted[(sorted.Length-1)/2],sorted[(int)Math.Ceiling(sorted.Length*.95)-1],sorted[(int)Math.Ceiling(sorted.Length*.99)-1],
            GC.CollectionCount(0)-gc0,GC.CollectionCount(1)-gc1,GC.CollectionCount(2)-gc2,digest));
        Console.WriteLine("TURN BENCH controlled fixture; real changing-world scheduler/needs/actions; fingerprint outside timing; headless UI, no native input/display latency");
        Console.WriteLine("TURN BENCH collections inside timed rounds="+String.Join("/",timedGc)+"; overall counts also include verification fingerprints");
        Console.WriteLine("TURN BENCH managed baseline="+memoryBaseline+"; sampled peak="+memoryPeak+" bytes");
        Console.WriteLine("TURN POPULATION initial="+originalActors.Length+"; living="+originalActors.Count(a=>!a.IsDead)+
            "; dead="+originalActors.Count(a=>a.IsDead)+"; moved="+originalActors.Count(a=>!a.IsDead&&a.Location.Map!=world.Map)+"; corpses="+world.Map.CountCorpses);
        if(diagnostic)Console.WriteLine("TURN DIAGNOSTIC allocated during round work="+timedAllocated+" bytes; profiling enabled; timing results are diagnostic only");
        if(profile!=null)Console.WriteLine(profile.GetMethod("Report",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null));
    }
}
