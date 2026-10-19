using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using djack.RogueSurvivor.Data;
using djack.RogueSurvivor.Engine;
using djack.RogueSurvivor.Engine.Items;
using djack.RogueSurvivor.Engine.MapObjects;
using djack.RogueSurvivor.Gameplay.AI.Sensors;
using djack.RogueSurvivor.Gameplay.Personality;

static class PerformanceCacheScenario
{
    static ScenarioWorld Arena(int seed)
    {
        var world=TownScenarioFactory.Arena(seed,new string('.',12),new string('.',12),new string('.',12),new string('.',12),new string('.',12),new string('.',12),new string('.',12),new string('.',12));
        Session.Get.GamePreset=GamePreset.World();Session.Get.UniqueActors.TheSewersThing=new UniqueActor();
        NpcIntentSupport.Player(world,2,3);world.Map.Lighting=Lighting.LIT;RogueGame.KeyBindings.ResetToDefaults();return world;
    }
    static LOSSensor Sensor() { return new LOSSensor(LOSSensor.SensingFilter.ACTORS|LOSSensor.SensingFilter.ITEMS|LOSSensor.SensingFilter.CORPSES); }
    static int Count(LOSSensor sensor,string name) { return (int)typeof(LOSSensor).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(sensor); }
    static List<djack.RogueSurvivor.Engine.AI.Percept> Compare(ScenarioWorld world,LOSSensor sensor)
    {
        var result=sensor.Sense(world.Game,world.Game.Player);var fresh=Sensor();var reference=fresh.Sense(world.Game,world.Game.Player);
        Check.Equal(true,sensor.FOV.SetEquals(fresh.FOV),"cached visible tiles equal fresh computation");
        Check.Equal(reference.Count,result.Count,"same current percept count");
        for(int i=0;i<result.Count;i++) {
            Check.Same(reference[i].Percepted,result[i].Percepted,"same current percept identity/order");
            Check.Equal(reference[i].Turn,result[i].Turn,"percept keeps current date");
            Check.Equal(reference[i].Location,result[i].Location,"percept keeps current location");
        }
        return result;
    }
    static void Miss(ScenarioWorld world,LOSSensor sensor,Action change)
    { int before=Count(sensor,"CacheMisses");change();Compare(world,sensor);Check.Equal(before+1,Count(sensor,"CacheMisses"),"changed geometry/range invalidates cached view"); }
    static bool Departure(ScenarioWorld world,Actor visitor,XpdBase claim,string method)
    { return (bool)typeof(CommunityCultureSystem).GetMethod(method,BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{world.Game,visitor,claim}); }
    public static void Register()
    {
        ScenarioRunner.Add("npc/performance-view-current-occupants",()=>Arena(5450),w=>{
            var sensor=Sensor();Compare(w,sensor);
            var other=NpcIntentSupport.Actor(w,"new arrival",5,3);var food=new ItemFood(w.Game.GameItems.CANNED_FOOD);w.Map.DropItemAt(food,new Point(3,3));
            w.Map.LocalTime.TurnCounter++;var percepts=Compare(w,sensor);
            Check.Equal(true,percepts.Exists(p=>p.Percepted==other),"reused geometry sees a newly arrived actor");
            Check.Equal(true,percepts.Exists(p=>p.Percepted==w.Map.GetItemsAt(new Point(3,3))),"reused geometry sees newly dropped supplies");
            Check.Equal(1,Count(sensor,"CacheHits"),"occupant/stock changes preserve geometry cache");
            w.Map.RemoveActor(other);w.Map.PlaceActorAt(other,new Point(10,7));Compare(w,sensor);
            Check.Equal(null,w.Game.Player.Personality.FindCommunity(other.PersonalityIdentity),"sensing alone grants no affiliation");
        });
        ScenarioRunner.Add("npc/performance-view-door-fire-tile",()=>Arena(5451),w=>{
            var door=new DoorWindow("door","closed","open","broken",40);w.Map.PlaceMapObjectAt(door,new Point(4,3));var sensor=Sensor();Compare(w,sensor);Compare(w,sensor);
            Miss(w,sensor,()=>door.SetState(DoorWindow.STATE_OPEN));
            Miss(w,sensor,()=>door.BarricadePoints=10);Miss(w,sensor,()=>door.BarricadePoints=0);
            Miss(w,sensor,()=>door.FireState=MapObject.Fire.ONFIRE);Miss(w,sensor,()=>door.FireState=MapObject.Fire.ASHES);
            Miss(w,sensor,()=>door.BreakState=MapObject.Break.BROKEN);
            Miss(w,sensor,()=>w.Map.GetTileAt(3,3).Model=w.Game.GameTiles.WALL_BRICK);
            Miss(w,sensor,()=>w.Map.SetTileModelAt(3,3,w.Game.GameTiles.FLOOR_ASPHALT));
            Miss(w,sensor,()=>w.Map.PlaceMapObjectAt(door,new Point(5,3)));
            Miss(w,sensor,()=>w.Map.RemoveMapObjectAt(5,3));
        });
        ScenarioRunner.Add("npc/performance-view-map-range-reload",()=>Arena(5452),w=>{
            var sensor=Sensor();Compare(w,sensor);
            Miss(w,sensor,()=>w.Map.PlaceActorAt(w.Game.Player,new Point(3,3)));
            Miss(w,sensor,()=>w.Game.Player.IsSleeping=true);Miss(w,sensor,()=>w.Game.Player.IsSleeping=false);
            Miss(w,sensor,()=>w.Map.Lighting=Lighting.DARKNESS);Miss(w,sensor,()=>w.Map.Lighting=Lighting.LIT);
            string path=Path.Combine(Environment.GetEnvironmentVariable("ROGUE_COMMUNITY_ARTIFACTS"),"performance-sensor.dat");
            BinarySaveStore.Save(path,sensor);var restored=BinarySaveStore.Load<LOSSensor>(path);Compare(w,restored);
            Check.Equal(1,Count(restored,"CacheMisses"),"reload clears transient geometry metadata");
            var alternate=new Map(w.Map.Seed,w.Map.Name,12,8);
            for(int x=0;x<12;x++)for(int y=0;y<8;y++)alternate.SetTileModelAt(x,y,w.Game.GameTiles.FLOOR_ASPHALT);
            alternate.Lighting=Lighting.LIT;w.Map.RemoveActor(w.Game.Player);alternate.PlaceActorAt(w.Game.Player,new Point(3,3));
            var moved=new ScenarioWorld(w.Seed,alternate,w.Game);Compare(moved,sensor);
            Check.Equal(7,Count(sensor,"CacheMisses"),"map reference invalidates despite matching seed/name/coordinates");
        });
        ScenarioRunner.Add("npc/performance-territorial-route-mutations",()=>Arena(5454),w=>{
            var chief=NpcIntentSupport.Actor(w,"chief",4,2);chief.EnsurePersonalGroup();
            var cells=new List<Point>();for(int x=2;x<=5;x++)for(int y=1;y<=5;y++)cells.Add(new Point(x,y));
            var claim=new XpdBase(chief,cells);w.Map.AddXpdBase(claim);var visitor=w.Game.Player;
            for(int x=1;x<=6;x++)for(int y=0;y<=6;y++)if(x==1||x==6||y==0||y==6)w.Map.SetTileModelAt(x,y,w.Game.GameTiles.WALL_BRICK);
            w.Map.SetTileModelAt(6,3,w.Game.GameTiles.FLOOR_ASPHALT);
            Action compare=()=>Check.Equal(Departure(w,visitor,claim,"DeparturePossibleCore"),Departure(w,visitor,claim,"DeparturePossible"),"cached route equals actual bounded legal flood");
            compare();compare();var blocker=NpcIntentSupport.Actor(w,"blocker",6,3);compare();
            Check.Equal(false,Departure(w,visitor,claim,"DeparturePossible"),"occupied exit pauses grace");
            w.Map.PlaceActorAt(blocker,new Point(9,7));compare();Check.Equal(true,Departure(w,visitor,claim,"DeparturePossible"),"moving blocker restores legal exit immediately");
            var door=new DoorWindow("door","closed","open","broken",40);w.Map.PlaceMapObjectAt(door,new Point(6,3));door.BarricadePoints=10;compare();
            Check.Equal(false,Departure(w,visitor,claim,"DeparturePossible"),"barricade blocks exit");door.BarricadePoints=0;compare();
            w.Map.GetTileAt(6,3).Model=w.Game.GameTiles.WALL_BRICK;compare();
            Check.Equal(0,visitor.Personality.CaseReports.Count,"cached geometry grants no witness knowledge or warning");
        });
        ScenarioRunner.Add("npc/performance-live-input-profiler",()=>Arena(5455),w=>{
            var ui=(ScenarioUI)typeof(RogueGame).GetField("m_UI",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(w.Game);
            var profile=typeof(RogueGame).Assembly.GetType("djack.RogueSurvivor.Engine.SimulationPerformance");
            profile.GetMethod("Reset",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
            bool enabled=(bool)profile.GetField("Enabled",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
            int inputs=enabled?31:3;
            for(int i=0;i<inputs;i++) { w.Game.Player.ActionPoints=Rules.BASE_ACTION_COST;ui.QueueKey(i<3||i%2==0?Keys.NumPad6:Keys.NumPad4);Check.Call(w.Game,"HandlePlayerActor",w.Game.Player); }
            Check.Equal(new Point(5,3),w.Game.Player.Location.Position,"real input moves at normal costs");
            string report=(string)profile.GetMethod("Report",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
            Check.Equal(enabled,report.Contains("command-to-next-input wall: samples=30"),"opt-in records real input; disabled profiling records none");
            string log=Environment.GetEnvironmentVariable("ROGUE_PROFILE_LOG");
            if(enabled&&!String.IsNullOrEmpty(log))Check.Equal(true,File.ReadAllText(log).Contains("command-to-next-input wall: samples=30"),"bounded report flushes after thirty charged inputs");
        });
    }
}
