using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using djack.RogueSurvivor.Data;
using djack.RogueSurvivor.Engine;
using djack.RogueSurvivor.Engine.Actions;
using djack.RogueSurvivor.Engine.Items;
using djack.RogueSurvivor.Gameplay.AI;

static class PerformanceRedesignScenario
{
    static ScenarioWorld Arena(int seed)
    {
        var w = TownScenarioFactory.Arena(seed, new string('.',24), new string('.',24), new string('.',24), new string('.',24), new string('.',24), new string('.',24), new string('.',24), new string('.',24));
        Session.Get.GamePreset = GamePreset.World(); Session.Get.UniqueActors.TheSewersThing = new UniqueActor();
        NpcIntentSupport.Player(w,1,7); w.Map.Lighting = Lighting.LIT;
        return w;
    }
    static Actor Guard(ScenarioWorld w)
    {
        Session.Get.GamePreset.Simulation = SimulationMode.ResponsiveNpcs;
        var chief = NpcIntentSupport.Actor(w,"chief",20,2);
        var guard = NpcIntentSupport.Actor(w,"guard",17,2); chief.AddFollower(guard);
        ((BaseAI)guard.Controller).SetOrder(new ActorOrder(ActorTasks.GUARD,guard.Location));
        guard.HitPoints = w.Game.Rules.ActorMaxHPs(guard);
        var action = guard.Controller.GetAction(w.Game);
        Check.Equal(true, action is ActionWait,"safe guard selects a real wait");
        return guard;
    }
    static object Routine(Actor actor) { return typeof(BaseAI).GetField("routine",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(actor.Controller); }
    public static void Register()
    {
        ScenarioRunner.Add("npc/redesign-default-and-save-mode",()=>Arena(5600),w=>{
            Check.Equal(SimulationMode.Classic,Session.Get.GamePreset.Simulation,"default stays classic");
            Session.Get.GamePreset.Simulation=SimulationMode.ResponsiveNpcs;
            string file=Path.Combine(Environment.GetEnvironmentVariable("ROGUE_COMMUNITY_ARTIFACTS"),"redesign-mode.dat");
            BinarySaveStore.Save(file,Session.Get);
            Check.Equal(SimulationMode.ResponsiveNpcs,BinarySaveStore.Load<Session>(file).GamePreset.Simulation,"mode survives format-5 save");
        });
        ScenarioRunner.Add("npc/redesign-guard-order-and-damage",()=>Arena(5601),w=>{
            var guard=Guard(w); var previous=Routine(guard); Check.Equal(true,previous!=null,"guard retains routine");
            guard.ActionPoints=Rules.BASE_ACTION_COST; var action=guard.Controller.GetAction(w.Game);
            Check.Same(previous,Routine(guard),"safe guard continues without selecting another activity");
            action.Perform(); Check.Equal(0,guard.ActionPoints,"continuation pays ordinary action cost");
            ((BaseAI)guard.Controller).SetOrder(new ActorOrder(ActorTasks.GUARD,new Location(w.Map,new Point(21,2))));
            Check.Equal(null,Routine(guard),"orders interrupt immediately");
            guard.HitPoints--; guard.Controller.GetAction(w.Game);
            Check.Equal(null,Routine(guard),"injury requires full processing");
        });
        ScenarioRunner.Add("npc/redesign-needs-and-player-contact",()=>Arena(5602),w=>{
            var guard=Guard(w); guard.FoodPoints=Session.Get.GamePreset.HungerPoints;
            guard.Controller.GetAction(w.Game); Check.Equal(null,Routine(guard),"hunger ends continuation");
            guard.FoodPoints=w.Game.Rules.ActorMaxFood(guard);guard.Controller.GetAction(w.Game);
            w.Map.PlaceActorAt(w.Game.Player,new Point(16,2)); guard.Controller.GetAction(w.Game);
            Check.Equal(null,Routine(guard),"nearby player interaction gets a full decision");
        });
        ScenarioRunner.Add("npc/redesign-routine-save-reload",()=>Arena(5603),w=>{
            var guard=Guard(w); Guid id=guard.PersonalityIdentity;
            string file=Path.Combine(Environment.GetEnvironmentVariable("ROGUE_COMMUNITY_ARTIFACTS"),"redesign-routine.dat");
            BinarySaveStore.Save(file,Session.Get); var restored=NpcIntentSupport.Restore(w,BinarySaveStore.Load<Session>(file));
            guard=NpcIntentSupport.Find(restored.Map,id); Check.Equal(true,Routine(guard)!=null,"routine is authoritative saved state");
            var action=guard.Controller.GetAction(restored.Game); Check.Equal(true,action is ActionWait && action.IsLegal(),"restored routine continues legally");
        });
        ScenarioRunner.Add("npc/redesign-goal-identity-mutations",()=>Arena(5604),w=>{
            var a=new NpcGeneratedGoal {Value=NpcGoalValue.Nutrition,SubjectId=Guid.NewGuid(),IdentitySuffix=""};
            var b=new NpcGeneratedGoal {Value=a.Value,SubjectId=a.SubjectId,IdentitySuffix=""};
            Action compare=()=>Check.Equal(a.Key==b.Key,a.SameIdentity(b),"structural matching equals the saved key contract");
            compare();b.Resource="";compare();b.Resource="food";compare();a.Resource="food";compare();b.ObligationId=9;compare();a.ObligationId=9;compare();b.IdentitySuffix=":extra";compare();
            a.ObligationId=1;a.IdentitySuffix="";b.Resource="food:1";b.ObligationId=0;b.IdentitySuffix="";compare();
            b.Resource="food";b.IdentitySuffix=":1";compare();
        });
        ScenarioRunner.Add("npc/redesign-coordinate-equality",()=>Arena(5605),w=>{
            var comparer=PointComparer.Instance;
            for(int x=-2;x<105;x++) for(int y=-2;y<105;y++) {
                var p=new Point(x,y);Check.Equal(true,comparer.Equals(p,new Point(x,y)),"typed equality preserves coordinates");
                Check.Equal(false,comparer.Equals(p,new Point(x+1,y)),"different coordinates stay distinct");
            }
            Check.Equal(true,new Location(w.Map,new Point(2,3)).Equals(new Location(w.Map,new Point(2,3))),"typed location identity stays local");
        });
        ScenarioRunner.Add("npc/redesign-noise-and-directives",()=>Arena(5606),w=>{
            var guard=Guard(w);Check.Equal(true,Routine(guard)!=null,"guard begins a safe activity");
            Check.Call(w.Game,"OnLoudNoise",w.Map,guard.Location.Position,"controlled alarm");
            Check.Equal(null,Routine(guard),"an audible alarm interrupts before the next legal action");
            guard.Controller.GetAction(w.Game);((BaseAI)guard.Controller).Directives.CanTrade=false;
            var previous=Routine(guard);guard.Controller.GetAction(w.Game);
            Check.Equal(false,Object.ReferenceEquals(previous,Routine(guard)),"changed standing instructions force reconsideration");
        });
        ScenarioRunner.Add("npc/redesign-event-recipient-order",()=>Arena(5607),w=>{
            var near=NpcIntentSupport.Actor(w,"near",10,2);var direct=NpcIntentSupport.Actor(w,"direct",21,2);
            var list=w.Map.NearbyEventActors(new Point(9,2),2,direct,null);
            Check.Equal(true,list.Contains(near)&&list.Contains(direct),"nearby witnesses and distant direct participants are retained");
            Check.Equal(false,list.Contains(w.Game.Player),"unrelated distant actors are excluded");
            Check.Equal(true,list.IndexOf(near)<list.IndexOf(direct),"indexed recipients retain actor delivery order");
            w.Map.MoveActorToFirstPosition(direct);list=w.Map.NearbyEventActors(new Point(9,2),2,direct,null);
            Check.Equal(true,list.IndexOf(direct)<list.IndexOf(near),"order changes invalidate the index");
        });
        ScenarioRunner.Add("npc/redesign-hostile-and-explosive-interruption",()=>Arena(5608),w=>{
            var guard=Guard(w);
            var enemy=w.Game.GameActors.Zombie.CreateAnonymous(w.Game.GameFactions.TheUndeads,0);w.Map.PlaceActorAt(enemy,new Point(16,2));
            var action=guard.Controller.GetAction(w.Game);
            Check.Equal(null,Routine(guard),"hostile contact interrupts a safe routine immediately");
            Check.Equal(false,action is ActionWait,"hostile contact reaches ordinary combat or escape logic");
            w.Map.RemoveActor(enemy);guard.Controller.GetAction(w.Game);
            w.Map.DropItemAt(new ItemGrenadePrimed(w.Game.GameItems.GRENADE_PRIMED){FuseTimeLeft=1},guard.Location.Position);
            action=guard.Controller.GetAction(w.Game);
            Check.Equal(null,Routine(guard),"a new live explosive forces reconsideration");
            Check.Equal(false,action is ActionWait,"explosive escape is not replaced with waiting");
        });
        ScenarioRunner.Add("npc/redesign-expiring-supplies",()=>Arena(5609),w=>{
            var guard=Guard(w);var state=Routine(guard);
            int next=(int)state.GetType().GetField("NextDecision").GetValue(state);
            w.Map.LocalTime.TurnCounter=next;guard.Controller.GetAction(w.Game);
            var food=new ItemFood(w.Game.GameItems.GROCERIES,next+1);guard.Inventory.AddIntact(food);
            guard.Controller.GetAction(w.Game);state=Routine(guard);
            w.Map.LocalTime.TurnCounter=next+1;guard.Controller.GetAction(w.Game);
            Check.Equal(false,Object.ReferenceEquals(state,Routine(guard)),"food changing freshness interrupts before the next scheduled decision");
        });
    }
}
