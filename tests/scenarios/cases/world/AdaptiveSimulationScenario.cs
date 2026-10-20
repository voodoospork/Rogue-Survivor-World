using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using djack.RogueSurvivor.Data;
using djack.RogueSurvivor.Engine;
using djack.RogueSurvivor.Engine.Items;
using djack.RogueSurvivor.Engine.MapObjects;
using djack.RogueSurvivor.Engine.Tasks;
using djack.RogueSurvivor.Gameplay.AI;
using System.Windows.Forms;

static class AdaptiveSimulationScenario
{
    static ScenarioWorld Arena(int seed)
    {
        var w=TownScenarioFactory.Arena(seed,new string('.',24),new string('.',24),new string('.',24),new string('.',24),new string('.',24),new string('.',24),new string('.',24),new string('.',24));
        Session.Get.GamePreset=GamePreset.World();Session.Get.GamePreset.Simulation=SimulationMode.AdaptiveWorld;
        Session.Get.UniqueActors.TheSewersThing=new UniqueActor();NpcIntentSupport.Player(w,1,7);
        Session.Get.WorldTime.TurnCounter=w.Map.LocalTime.TurnCounter=360;
        var world=new World(2);world[0,0]=w.Map.District;
        // Keep the complete world valid for the ordinary Session.Load/Save APIs,
        // including copied-save budget workers that reconstruct every district.
        for(int x=0;x<2;x++) {
            var empty=new District(new Point(x,1),DistrictKind.GENERAL);
            var floor=new Map(seed+200+x,"Empty neighbouring floor",5,5);floor.Lighting=Lighting.LIT;floor.LocalTime.TurnCounter=360;
            for(int xx=0;xx<5;xx++)for(int y=0;y<5;y++)floor.SetTileModelAt(xx,y,w.Game.GameTiles.FLOOR_ASPHALT);
            empty.EntryMap=floor;world[x,1]=empty;
        }
        var district=new District(new Point(1,0),DistrictKind.GENERAL);
        var away=new Map(seed+100,"Quiet away",24,8);away.Lighting=Lighting.LIT;away.LocalTime.TurnCounter=360;
        for(int x=0;x<24;x++)for(int y=0;y<8;y++)away.SetTileModelAt(x,y,w.Game.GameTiles.FLOOR_ASPHALT);
        district.EntryMap=away;world[1,0]=district;Session.Get.World=world;
        var remote=new ScenarioWorld(seed,away,w.Game);
        for(int i=0;i<12;i++) {
            var a=NpcIntentSupport.Actor(remote,"resident-"+i,3+i,3);
            a.HitPoints=w.Game.Rules.ActorMaxHPs(a);a.Sanity=w.Game.Rules.ActorMaxSanity(a);
            a.SleepPoints=w.Game.Rules.ActorMaxSleep(a)/4;a.IsSleeping=true;
        }
        return w;
    }
    static District Away() { return Session.Get.World[1,0]; }
    static string FilePath(string name) { return Path.Combine(Environment.GetEnvironmentVariable("ROGUE_COMMUNITY_ARTIFACTS"),name+".dat"); }
    static string Snapshot()
    {
        var d=Away();return d.EntryMap.LocalTime.TurnCounter+"|"+String.Join("|",d.EntryMap.Actors.Select(a=>a.PersonalityIdentity+":"+a.Location.Position.X+","+a.Location.Position.Y+","+a.HitPoints+","+a.FoodPoints+","+a.SleepPoints+","+a.StaminaPoints+","+a.Sanity+","+a.ActionPoints+","+a.IsSleeping))+
            "|"+typeof(DiceRoller).GetField("m_State",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(d.Adaptive.Maps[0].Random);
    }
    static ScenarioWorld Reload(ScenarioWorld w,string file)
    {
        var restored=NpcIntentSupport.Restore(w,BinarySaveStore.Load<Session>(file));
        for(int x=0;x<Session.Get.World.Size;x++)for(int y=0;y<Session.Get.World.Size;y++)
            if(Session.Get.World[x,y]!=null)foreach(var map in Session.Get.World[x,y].Maps)map.ReconstructAuxiliaryFields();
        return restored;
    }
    static string PopulationSnapshot()
    {
        var dice=typeof(DiceRoller).GetField("m_State",BindingFlags.Instance|BindingFlags.NonPublic);
        var maps=new System.Collections.Generic.List<string>();
        foreach(var map in Away().Maps) {
            var actors=new System.Collections.Generic.List<string>();
            foreach(var a in map.Actors) {
                string items=a.Inventory==null?"none":String.Join("/",a.Inventory.Items.Select(i=>i.StoryIdentity+":"+i.Model.ID+":"+i.Quantity));
                string traits=a.Personality==null?"none":String.Join("/",a.Personality.Traits.Select(t=>t.Id+":"+t.ItemModelId));
                actors.Add(a.PersonalityIdentity+","+a.UnmodifiedName+","+a.Model.ID+","+a.Location.Position+","+a.HitPoints+","+a.FoodPoints+","+a.SleepPoints+","+items+","+traits);
            }
            var state=Away().Adaptive.Maps.Find(s=>s.Map==map);
            maps.Add(map.Seed+":"+map.LocalTime.TurnCounter+":"+dice.GetValue(state.Random)+":"+state.Identities.Sequence+":"+String.Join(";",actors));
        }
        return String.Join("|",maps)+"|events="+Away().Adaptive.EventsThrough+":"+dice.GetValue(Away().Adaptive.EventRandom)+":"+Away().Adaptive.Identities.Sequence;
    }
    public static void Register()
    {
        ScenarioRunner.Add("world/adaptive-community-predators-stay-detailed",()=>Arena(5813),w=>{
            var district=Away();var map=district.EntryMap;var actor=map.GetActor(0);
            actor.IsSleeping=false;actor.SleepPoints=w.Game.Rules.ActorMaxSleep(actor);
            var resident=map.GetActor(1);actor.AddFollower(resident);
            actor.SocialGroup.Home=new CommunityHomeState();
            actor.SocialGroup.Culture=new CommunityCulture { Aggression=CommunityAggression.AttackOutsiders, AggressionInitialized=true };
            var known=actor.Personality.LearnCommunity(actor.SocialGroup.Identity,"Predators",360,"own policy");
            known.Policy=new CommunityPolicy { Aggression=CommunityAggression.AttackOutsiders,AggressionKnown=true };
            var state=(AdaptiveMapState)Check.Call(w.Game,"AdaptiveState",new[]{typeof(Map)},map);
            Check.Equal(0,Check.Call(w.Game,"QuietHorizon",map,state,390),"predators cannot bypass possible encounters through quiet batching");
        });
        ScenarioRunner.Add("world/adaptive-arrival-waits-for-district-boundary",()=>Arena(5673),w=>{
            var district=Away();var map=district.EntryMap;var actor=map.GetActor(0);
            actor.IsSleeping=false;actor.SleepPoints=w.Game.Rules.ActorMaxSleep(actor);actor.ActionPoints=Rules.BASE_ACTION_COST;
            var chief=NpcIntentSupport.Actor(w,"arrival leader",20,2);chief.AddFollower(actor);
            ((BaseAI)actor.Controller).SetOrder(new ActorOrder(ActorTasks.GUARD,actor.Location));
            var sewer=new Map(5773,"lagging arrival floor",24,8);sewer.Lighting=Lighting.LIT;sewer.LocalTime.TurnCounter=359;
            for(int x=0;x<24;x++)for(int y=0;y<8;y++)sewer.SetTileModelAt(x,y,w.Game.GameTiles.FLOOR_ASPHALT);
            district.SewersMap=sewer;
            var scheduler=(AdaptiveDistrictState)Check.Call(w.Game,"AdaptiveState",new[]{typeof(District)},district);
            var state=scheduler.Maps.Find(s=>s.Map==map);var lagging=scheduler.Maps.Find(s=>s.Map==sewer);
            scheduler.Arrivals.Add(new AdaptiveArrival{Actor=actor,From=actor.Location,DueTurn=360});
            state.DetailedInProgress=true;
            Check.Equal(false,Check.Call(w.Game,"ResolveAdaptiveArrivals",district,360),"a slice cannot insert queued decisions into a partial actor round");
            state.DetailedInProgress=false;
            Check.Equal(false,Check.Call(w.Game,"ResolveAdaptiveArrivals",district,360),"all receiving district floors must reach the arrival boundary first");
            lagging.WorldTurn=360;sewer.LocalTime.TurnCounter=360;scheduler.EventsThrough=359;
            Check.Equal(false,Check.Call(w.Game,"ResolveAdaptiveArrivals",district,360),"district events precede queued decisions in both sliced and continuous work");
            Check.Equal(Rules.BASE_ACTION_COST,actor.ActionPoints,"deferred work has not spent an extra action");
            scheduler.EventsThrough=360;
            Check.Equal(true,Check.Call(w.Game,"ResolveAdaptiveArrivals",district,360),"a complete boundary permits normal reconsideration");
            Check.Equal(0,actor.ActionPoints,"the ordinary guard action pays its normal cost");
            Check.Equal(0,scheduler.Arrivals.Count,"reconsideration consumes the saved event once");
        });
        ScenarioRunner.Add("world/adaptive-quiet-review-renewal-and-danger",()=>Arena(5672),w=>{
            var map=Away().EntryMap;var chief=NpcIntentSupport.Actor(w,"watch leader",20,2);
            foreach(var actor in map.Actors) {
                actor.IsSleeping=false;actor.SleepPoints=w.Game.Rules.ActorMaxSleep(actor);
                actor.FoodPoints=w.Game.Rules.ActorMaxFood(actor);chief.AddFollower(actor);
                ((BaseAI)actor.Controller).SetOrder(new ActorOrder(ActorTasks.GUARD,actor.Location));
            }
            int food=map.GetActor(0).FoodPoints;
            Session.Get.WorldTime.TurnCounter=w.Map.LocalTime.TurnCounter=480;
            Check.Equal(true,w.Game.AdvanceAdaptiveDistrict(Away(),480,120),"quiet watch reaches its full elapsed frontier");
            var state=Away().Adaptive.Maps[0];
            Check.Equal(1L,state.DetailedTurns,"unchanged hourly safety reviews do not rebuild every guard decision");
            Check.Equal(119L,state.QuietTurns,"a validated watch continues through quiet review boundaries");
            Check.Equal(food-120,map.GetActor(0).FoodPoints,"renewing a safety proof preserves every survival tick");
            string file=FilePath("adaptive-review-renewal");BinarySaveStore.Save(file,Session.Get);w=Reload(w,file);map=Away().EntryMap;
            var door=new DoorWindow("door","closed","open","broken",40);map.PlaceMapObjectAt(door,new Point(20,3));door.FireState=MapObject.Fire.ONFIRE;
            w.Game.AdvanceAdaptiveDistrict(Away(),481,1);
            Check.Equal(2L,Away().Adaptive.Maps[0].DetailedTurns,"new fire after reload immediately ends the renewed quiet watch");
        });
        ScenarioRunner.Add("world/adaptive-quiet-metabolism-and-clock",()=>Arena(5650),w=>{
            var actor=Away().EntryMap.GetActor(0);int food=actor.FoodPoints,sleep=actor.SleepPoints,world=Session.Get.WorldTime.TurnCounter;
            Check.Equal(true,w.Game.AdvanceAdaptiveDistrict(Away(),390,30),"district reaches requested frontier");
            Check.Equal(food-30,actor.FoodPoints,"quiet catch-up accounts for every hunger tick");
            Check.Equal(true,actor.SleepPoints>sleep,"rest restores actual sleep");
            Check.Equal(world,Session.Get.WorldTime.TurnCounter,"historical catch-up never changes live world date");
            Check.Equal(true,Away().Adaptive.Maps[0].QuietTurns>0,"eligible quiet periods bypass full decisions");
            Check.Equal(12,Away().EntryMap.CountActors,"quiet simulation retains individual residents");
        });
        ScenarioRunner.Add("world/adaptive-slice-and-reload-determinism",()=>Arena(5651),w=>{
            string initial=FilePath("adaptive-initial");BinarySaveStore.Save(initial,Session.Get);
            w.Game.AdvanceAdaptiveDistrict(Away(),390,30);string continuous=Snapshot();
            w=Reload(w,initial);
            for(int i=0;i<12;i++)w.Game.AdvanceAdaptiveDistrict(Away(),390,1);
            string halfway=FilePath("adaptive-halfway");BinarySaveStore.Save(halfway,Session.Get);w=Reload(w,halfway);
            for(int i=0;i<18;i++)w.Game.AdvanceAdaptiveDistrict(Away(),390,1);
            Check.Equal(continuous,Snapshot(),"one batch, slices and reload preserve the same people, needs and random stream");
        });
        ScenarioRunner.Add("world/adaptive-hunger-and-injury-fallback",()=>Arena(5652),w=>{
            var map=Away().EntryMap;var actor=map.GetActor(0);
            actor.FoodPoints=Session.Get.GamePreset.HungerPoints+1;actor.HitPoints--;
            w.Game.AdvanceAdaptiveDistrict(Away(),364,4);
            Check.Equal(0L,Away().Adaptive.Maps[0].QuietTurns,"risk requires detailed processing");
            Check.Equal(true,actor.FoodPoints<=Session.Get.GamePreset.HungerPoints,"elapsed hunger is not skipped");
        });
        ScenarioRunner.Add("world/adaptive-timer-and-fire-fallback",()=>Arena(5653),w=>{
            var map=Away().EntryMap;var door=new DoorWindow("door","closed","open","broken",40);
            map.PlaceMapObjectAt(door,new Point(20,3));door.FireState=MapObject.Fire.ONFIRE;
            w.Game.AdvanceAdaptiveDistrict(Away(),363,3);
            Check.Equal(0L,Away().Adaptive.Maps[0].QuietTurns,"fire promotes the map to detailed turns");
        });
        ScenarioRunner.Add("world/adaptive-follower-maps-stay-detailed",()=>Arena(5654),w=>{
            var actor=Away().EntryMap.GetActor(0);w.Game.Player.AddFollower(actor);
            w.Game.AdvanceAdaptiveDistrict(Away(),363,3);
            Check.Equal(0L,Away().Adaptive.Maps[0].QuietTurns,"personal followers pin their map to detailed simulation");
        });
        ScenarioRunner.Add("world/adaptive-mode-switch-preserves-state",()=>Arena(5655),w=>{
            w.Game.AdvanceAdaptiveDistrict(Away(),370,10);string before=Snapshot();
            w.Game.ChangeSimulationMode(SimulationMode.Classic);w.Game.ChangeSimulationMode(SimulationMode.AdaptiveWorld);
            Check.Equal(before,Snapshot(),"mode changes retain already simulated consequences and frontier");
            Check.Equal(true,w.Game.AdvanceAdaptiveDistrict(Away(),375,5),"reactivation resumes saved frontier");
        });
        ScenarioRunner.Add("world/adaptive-clock-scope-and-random",()=>Arena(5656),w=>{
            var dice=new DiceRoller(712);var reference=new DiceRoller(712);int live=Session.Get.WorldTime.TurnCounter;
            using(new SimulationClock(Session.Get,123,dice)) {
                Check.Equal(123,Session.Get.WorldTime.TurnCounter,"historical records receive event date");
                Check.Equal(reference.Roll(0,1000),w.Game.Rules.Roll(0,1000),"rules use saved simulation random stream");
                using(new SimulationClock(Session.Get,124,dice)) Check.Equal(124,Session.Get.WorldTime.TurnCounter,"nested arrival has its own clock");
                Check.Equal(123,Session.Get.WorldTime.TurnCounter,"nested arrival restores previous clock");
            }
            Check.Equal(live,Session.Get.WorldTime.TurnCounter,"scope restores live clock");
        });
        ScenarioRunner.Add("world/adaptive-action-slice-reload",()=>Arena(5657),w=>{
            var map=Away().EntryMap;var actor=map.GetActor(0);actor.IsSleeping=false;actor.HitPoints--;
            string initial=FilePath("adaptive-actions-initial");BinarySaveStore.Save(initial,Session.Get);
            w.Game.AdvanceAdaptiveDistrict(Away(),365,5);string expected=Snapshot();w=Reload(w,initial);
            Check.Call(w.Game,"AdvanceAdaptiveStep",Away().EntryMap,365);
            Check.Equal(360,Away().EntryMap.LocalTime.TurnCounter,"one action slice retains unfinished round");
            Check.Equal(true,Away().Adaptive.Maps[0].DetailedInProgress,"unfinished processing is authoritative state");
            string partial=FilePath("adaptive-actions-partial");BinarySaveStore.Save(partial,Session.Get);w=Reload(w,partial);
            w.Game.AdvanceAdaptiveDistrict(Away(),365,5);
            Check.Equal(expected,Snapshot(),"reloading between individual actions preserves the completed history");
        });
        ScenarioRunner.Add("world/adaptive-timer-is-detailed",()=>Arena(5658),w=>{
            var map=Away().EntryMap;var timer=new TaskRemoveDecoration(2,1,1,"controlled-decoration");map.AddTimer(timer);
            w.Game.AdvanceAdaptiveDistrict(Away(),362,2);
            Check.Equal(true,timer.IsCompleted,"real timed task reaches its deadline");
            Check.Equal(0,map.CountTimers,"completed timed task is removed once");
            Check.Equal(0L,Away().Adaptive.Maps[0].QuietTurns,"unsupported timers retain detailed processing");
        });
        ScenarioRunner.Add("world/adaptive-historical-weather",()=>Arena(5659),w=>{
            var world=Session.Get.World;world.EnsureWeatherHistory();world.Weather=Weather.RAIN;world.RecordWeather(365);
            using(new SimulationClock(Session.Get,360,new DiceRoller(42)))
                Check.Equal(Weather.CLEAR,world.Weather,"historical perception and scent use historical weather");
            Check.Equal(Weather.RAIN,world.Weather,"live weather is retained");
            string file=FilePath("adaptive-weather");BinarySaveStore.Save(file,Session.Get);w=Reload(w,file);
            using(new SimulationClock(Session.Get,365,new DiceRoller(42)))
                Check.Equal(Weather.RAIN,Session.Get.World.Weather,"saved weather transition applies at its date");
        });
        ScenarioRunner.Add("world/adaptive-arrival-conserves-cargo",()=>Arena(5660),w=>{
            var courier=NpcIntentSupport.Actor(w,"courier",3,6);NpcIntentSupport.Food(w,courier,2);
            var cargo=courier.Inventory.Items.First();int units=cargo.Quantity;Guid id=courier.PersonalityIdentity;
            w.Map.LocalTime.TurnCounter=Session.Get.WorldTime.TurnCounter=365;
            w.Map.SetExitAt(courier.Location.Position,new Exit(Away().EntryMap,new Point(20,6)){IsAnAIExit=true});
            Check.Equal(true,w.Game.DoLeaveMap(courier,courier.Location.Position,false),"courier uses ordinary physical transfer");
            Check.Same(Away().EntryMap,courier.Location.Map,"arrival follows receiving-map synchronization");
            Check.Equal(365,Away().EntryMap.LocalTime.TurnCounter,"receiving map reaches arrival time");
            Check.Same(cargo,courier.Inventory.Items.First(),"the actual cargo object travels with its owner");
            Check.Equal(units,cargo.Quantity,"arrival creates no replacement provisions");
            Check.Equal(1,Away().EntryMap.Actors.Count(a=>a.PersonalityIdentity==id),"arrival resolves once");
            Check.Equal(false,w.Map.Actors.Any(a=>a.PersonalityIdentity==id),"source no longer holds the courier");
        });
        ScenarioRunner.Add("world/adaptive-cancelled-entry-resumes",()=>Arena(5661),w=>{
            var player=w.Game.Player;w.Map.LocalTime.TurnCounter=Session.Get.WorldTime.TurnCounter=365;
            var exitPoint=player.Location.Position;w.Map.SetExitAt(exitPoint,new Exit(Away().EntryMap,new Point(20,6)){IsAnAIExit=true});
            ((ScenarioUI)w.Game.UI).QueueKey(Keys.Escape);
            Check.Equal(false,w.Game.DoLeaveMap(player,exitPoint,false),"Escape cancels entry before exposing an unfinished map");
            Check.Same(w.Map,player.Location.Map,"cancelled entry preserves player location");
            Check.Equal(true,Away().EntryMap.LocalTime.TurnCounter<365,"cancelled work retains its real frontier");
            Check.Equal(365,Away().Adaptive.Maps[0].TargetTurn,"cancelled work remains pending");
            Check.Equal(true,w.Game.DoLeaveMap(player,exitPoint,false),"another entry resumes required work");
            Check.Same(Away().EntryMap,player.Location.Map,"finished destination is exposed");
            Check.Equal(365,player.Location.Map.LocalTime.TurnCounter,"entry never skips unfinished elapsed time");
        });
        ScenarioRunner.Add("world/adaptive-threshold-and-order-invalidation",()=>Arena(5662),w=>{
            var actor=Away().EntryMap.GetActor(0);actor.IsSleeping=false;actor.SleepPoints=w.Game.Rules.ActorMaxSleep(actor);
            var leader=NpcIntentSupport.Actor(w,"leader",20,2);leader.AddFollower(actor);
            ((BaseAI)actor.Controller).SetOrder(new ActorOrder(ActorTasks.GUARD,actor.Location));
            actor.Sanity=w.Game.Rules.ActorDisturbedLevel(actor)+2;actor.TrustInLeader=Rules.TRUST_BOND_THRESHOLD-1;
            w.Game.AdvanceAdaptiveDistrict(Away(),363,3);
            Check.Equal(0L,Away().Adaptive.Maps[0].QuietTurns,"sanity and bond thresholds require detailed processing");
            ((BaseAI)actor.Controller).SetOrder(null);
            Check.Equal(false,((BaseAI)actor.Controller).QuietWait(w.Game),"changed order cannot reuse a quiet guard activity");
        });
        ScenarioRunner.Add("world/adaptive-validated-travel-and-obstacle",()=>Arena(5663),w=>{
            var map=Away().EntryMap;var actor=map.GetActor(0);
            foreach(var other in map.Actors.ToArray())if(other!=actor)map.RemoveActor(other);
            actor.IsSleeping=false;actor.SleepPoints=w.Game.Rules.ActorMaxSleep(actor);
            var leader=NpcIntentSupport.Actor(w,"leader",20,2);leader.AddFollower(actor);
            ((BaseAI)actor.Controller).SetOrder(new ActorOrder(ActorTasks.GUARD,new Location(map,new Point(20,3))));
            w.Game.AdvanceAdaptiveDistrict(Away(),361,1);var before=actor.Location.Position;
            int through=361;
            do { w.Game.AdvanceAdaptiveDistrict(Away(),++through,1); } while(Away().Adaptive.Maps[0].QuietTurns==0 && through<366);
            Check.Equal(true,actor.Location.Position!=before,"validated travel continues ordinary movement between decisions");
            Check.Equal(true,Away().Adaptive.Maps[0].QuietTurns>0,"isolated committed travel can use the quiet scheduler");
            before=actor.Location.Position;
            for(int dx=-1;dx<=1;dx++)for(int dy=-1;dy<=1;dy++)if(dx!=0||dy!=0)map.SetTileModelAt(before.X+dx,before.Y+dy,w.Game.GameTiles.WALL_BRICK);
            long detailed=Away().Adaptive.Maps[0].DetailedTurns;
            w.Game.AdvanceAdaptiveDistrict(Away(),through+1,1);
            Check.Equal(before,actor.Location.Position,"blocked travel does not teleport");
            Check.Equal(true,Away().Adaptive.Maps[0].DetailedTurns>detailed,"an obstacle immediately restores full decision processing");
            Check.Equal(false,((BaseAI)actor.Controller).QuietWait(w.Game),"blocked movement is not replaced by a persistent wait at the wrong post");
        });
        ScenarioRunner.Add("world/adaptive-mode-switch-during-action",()=>Arena(5664),w=>{
            var actor=Away().EntryMap.GetActor(0);actor.IsSleeping=false;actor.HitPoints--;
            string file=FilePath("adaptive-switch-partial");BinarySaveStore.Save(file,Session.Get);
            w.Game.AdvanceAdaptiveDistrict(Away(),365,5);string expected=Snapshot();w=Reload(w,file);
            Check.Call(w.Game,"AdvanceAdaptiveStep",Away().EntryMap,365);
            w.Game.ChangeSimulationMode(SimulationMode.Classic);w.Game.ChangeSimulationMode(SimulationMode.AdaptiveWorld);
            Check.Equal(true,Away().Adaptive.Maps[0].DetailedInProgress,"mode change retains the pending action round without advancing its clock");
            w.Game.AdvanceAdaptiveDistrict(Away(),365,5);
            Check.Equal(expected,Snapshot(),"mode changes retain actual progress, people and random state");
        });
        ScenarioRunner.Add("world/adaptive-saved-preset-controls",()=>Arena(5665),w=>{
            var preset=Session.Get.GamePreset.Copy();preset.Name="CATCHUP TEST";
            preset.AdaptivePauseBackground=true;preset.AdaptiveWorkBudget=2;
            var presets=new GamePresetCollection();presets.AddOrReplace(preset);
            string path=FilePath("adaptive-preset");presets.Save(path);var restored=GamePresetCollection.Load(path).Presets.Single();
            Check.Equal(SimulationMode.AdaptiveWorld,restored.Simulation,"preset retains explicit simulation choice");
            Check.Equal(true,restored.AdaptivePauseBackground,"preset retains its saved background policy");
            Check.Equal(2,restored.AdaptiveWorkBudget,"preset retains effort without changing global options");
            Check.Equal(SimulationMode.Classic,GamePreset.World().Simulation,"new built-in games still default to Classic");
        });
        ScenarioRunner.Add("world/adaptive-clock-event-boundaries",()=>Arena(5666),w=>{
            using(new SimulationClock(Session.Get,360,new DiceRoller(1),Away()))
                Check.Equal(true,Away().EntryMap.LocalTime.IsStrikeOfMidday,"historical event clock preserves the actual midday strike");
            using(new SimulationClock(Session.Get,361,new DiceRoller(1),Away()))
                Check.Equal(false,Away().EntryMap.LocalTime.IsStrikeOfMidday,"a following turn does not replay the strike");
            using(new SimulationClock(Session.Get,720,new DiceRoller(1),Away()))
                Check.Equal(true,Away().EntryMap.LocalTime.IsStrikeOfMidnight,"midnight events retain their threshold");
        });
        ScenarioRunner.Add("world/adaptive-spawn-and-reload-identities",()=>Arena(5667),w=>{
            var map=Away().EntryMap;foreach(var actor in map.Actors.ToArray())map.RemoveActor(actor);
            var sewer=new Map(5767,"event sewer",24,8);sewer.Lighting=Lighting.LIT;
            for(int x=0;x<24;x++)for(int y=0;y<8;y++)sewer.SetTileModelAt(x,y,w.Game.GameTiles.FLOOR_ASPHALT);
            Away().SewersMap=sewer;map.LocalTime.TurnCounter=sewer.LocalTime.TurnCounter=359;
            var options=RogueGame.Options;options.MaxCivilians=5;
            typeof(RogueGame).GetField("s_Options",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,options);
            foreach(var property in typeof(UniqueActors).GetProperties())if(property.PropertyType==typeof(UniqueActor))property.SetValue(Session.Get.UniqueActors,new UniqueActor(),null);
            string initial=FilePath("adaptive-spawn-initial");BinarySaveStore.Save(initial,Session.Get);
            Console.WriteLine("Adaptive spawning: continuous history");
            w.Game.AdvanceAdaptiveDistrict(Away(),361,2);
            Check.Equal(true,Away().Maps.Sum(m=>m.CountActors)>0,"the real scheduled refugee wave creates actual residents");
            string expected=PopulationSnapshot();w=Reload(w,initial);
            Console.WriteLine("Adaptive spawning: first slice after restoring initial history");
            w.Game.AdvanceAdaptiveDistrict(Away(),361,1);
            string partial=FilePath("adaptive-spawn-partial");BinarySaveStore.Save(partial,Session.Get);w=Reload(w,partial);
            Console.WriteLine("Adaptive spawning: second slice after restoring newborn residents");
            w.Game.AdvanceAdaptiveDistrict(Away(),361,1);
            Check.Equal(expected,PopulationSnapshot(),"spawning, names, traits, items and identities survive sliced catch-up and reload");
        });
        ScenarioRunner.Add("world/adaptive-world-work-queue-reload",()=>Arena(5668),w=>{
            Session.Get.WorldTime.TurnCounter=w.Map.LocalTime.TurnCounter=365;
            string initial=FilePath("adaptive-work-initial");BinarySaveStore.Save(initial,Session.Get);
            for(int i=0;i<20;i++)w.Game.AdvanceAdaptiveWork(365);
            string expected=Snapshot();int cursor=Session.Get.AdaptiveWorkCursor;w=Reload(w,initial);
            for(int i=0;i<7;i++)w.Game.AdvanceAdaptiveWork(365);
            string partial=FilePath("adaptive-work-partial");BinarySaveStore.Save(partial,Session.Get);w=Reload(w,partial);
            for(int i=0;i<13;i++)w.Game.AdvanceAdaptiveWork(365);
            Check.Equal(expected,Snapshot(),"shorter slices resume the same ordered world work");
            Check.Equal(cursor,Session.Get.AdaptiveWorkCursor,"the next queue position is saved");
        });
        ScenarioRunner.Add("world/adaptive-queued-arrival-reload-and-orders",()=>Arena(5670),w=>{
            var source=w.Map;var player=w.Game.Player;source.PlaceActorAt(player,new Point(2,3));
            var follower=NpcIntentSupport.Actor(w,"courier",7,3,"loyal");player.AddFollower(follower);follower.TrustInLeader=Rules.TRUST_TRUSTING_THRESHOLD;
            var cargo=NpcIntentSupport.Food(w,follower,2);Guid cargoId=cargo.StoryIdentity,id=follower.PersonalityIdentity;
            source.SetExitAt(player.Location.Position,new Exit(Away().EntryMap,new Point(2,3)){IsAnAIExit=true});
            foreach(var property in typeof(UniqueActors).GetProperties())if(property.PropertyType==typeof(UniqueActor))property.SetValue(Session.Get.UniqueActors,new UniqueActor(),null);
            CommunityMembershipScenario.Fov(w);((ScenarioUI)w.Game.UI).QueueWaitKey(Keys.Y);
            Check.Equal(true,w.Game.DoUseExit(player,player.Location.Position),"leader makes an ordinary heard departure");
            source.PlaceActorAt(follower,new Point(2,3));source.LocalTime.TurnCounter=359;follower.ActionPoints=Rules.BASE_ACTION_COST;
            var sewer=new Map(5770,"source sewer",24,8);sewer.Lighting=Lighting.LIT;sewer.LocalTime.TurnCounter=359;
            for(int x=0;x<24;x++)for(int y=0;y<8;y++)sewer.SetTileModelAt(x,y,w.Game.GameTiles.FLOOR_ASPHALT);
            source.District.SewersMap=sewer;
            int food=follower.FoodPoints;
            Check.Equal(false,w.Game.DoUseExit(follower,follower.Location.Position),"an arrival into a later frontier is scheduled");
            Check.Equal(1,source.District.Adaptive.Arrivals.Count,"one authoritative arrival is pending");
            string file=FilePath("adaptive-queued-arrival");BinarySaveStore.Save(file,Session.Get);
            w=Reload(w,file);source=Session.Get.World[0,0].EntryMap;source.ReconstructAuxiliaryFields();follower=NpcIntentSupport.Find(source,id);
            w.Game.AdvanceAdaptiveDistrict(source.District,360,1);
            Check.Same(Away().EntryMap,follower.Location.Map,"the arrival resolves when the source reaches its date");
            Check.Equal(1,Away().EntryMap.Actors.Count(a=>a.PersonalityIdentity==id),"queued arrival transfers the individual once");
            Check.Equal(cargoId,follower.Inventory.Items.First().StoryIdentity,"queued travel preserves real cargo identity");
            Check.Equal(food-1,follower.FoodPoints,"waiting for a frontier still advances hunger");
            Check.Equal(0,source.District.Adaptive.Arrivals.Count,"completed arrival leaves no duplicate event");
            w=Reload(w,file);source=Session.Get.World[0,0].EntryMap;source.ReconstructAuxiliaryFields();follower=NpcIntentSupport.Find(source,id);
            ((BaseAI)follower.Controller).SetOrder(new ActorOrder(ActorTasks.GUARD,follower.Location));
            w.Game.AdvanceAdaptiveDistrict(source.District,360,1);
            Check.Same(source,follower.Location.Map,"new orders are reconsidered before a queued crossing");
            Check.Equal(0,source.District.Adaptive.Arrivals.Count,"reconsideration cancels the obsolete arrival");
        });
        ScenarioRunner.Add("world/adaptive-world-and-observation-clocks",()=>Arena(5671),w=>{
            w.Map.LocalTime.TurnCounter=900;
            var state=(AdaptiveMapState)Check.Call(w.Game,"AdaptiveState",new[]{typeof(Map)},w.Map);
            Check.Equal(360,state.WorldTurn,"processing frontier uses world time for the current district");
            using(new SimulationClock(Session.Get,state.WorldTurn,state.Random,w.Map.District,state.Identities)) {
                Check.Equal(360,Session.Get.WorldTime.TurnCounter,"world deadlines retain their clock");
                Check.Equal(900,w.Map.LocalTime.TurnCounter,"observation dates retain their local clock");
            }
            Check.Equal(900,w.Map.LocalTime.TurnCounter,"clock projection does not rewrite observed local dates");
        });
    }
}
