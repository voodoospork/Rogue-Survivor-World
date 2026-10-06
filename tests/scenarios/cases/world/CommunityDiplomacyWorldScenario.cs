using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.IO;
using System.Windows.Forms;
using djack.RogueSurvivor.Data;
using djack.RogueSurvivor.Engine;
using djack.RogueSurvivor.Engine.Actions;
using djack.RogueSurvivor.Engine.Items;
using djack.RogueSurvivor.Gameplay;
using djack.RogueSurvivor.Gameplay.Personality;
using djack.RogueSurvivor.Gameplay.AI;

static class CommunityDiplomacyWorldScenario
{
    // Test-only actor pilot. It replaces one NPC's decisions during the explicitly scripted provocation,
    // while production scheduling supplies its AP, metabolism, attacks, defense, noise and movement.
    sealed class ProvocationPilot : CivilianAI
    {
        public readonly AIController Original; public readonly ScenarioWorld World; public readonly Actor Victim, Origin, HomeLeader;
        public readonly Point ReturnPoint;
        bool introduced;
        public ProvocationPilot(ScenarioWorld w,Actor actor,Actor victim,Actor origin,Actor home) { World=w; Original=(AIController)actor.Controller; Victim=victim; Origin=origin; HomeLeader=home; ReturnPoint=home.Location.Position; }
        public override ActorOrder Order { get { return Original.Order; } }
        public override ActorDirective Directives { get { return Original.Directives; } set { Original.Directives=value; } }
        public override void SetOrder(ActorOrder order) { Original.SetOrder(order); }
        public override ActorAction GetAction(RogueGame game)
        {
            if(m_Actor.IsSleeping) return new ActionWait(m_Actor,game);
            var food=m_Actor.Inventory.Items.OfType<ItemFood>().FirstOrDefault(f=>!game.Rules.IsFoodSpoiled(f,World.Map.LocalTime.TurnCounter));
            if(game.Rules.IsActorHungry(m_Actor) && food!=null) return new ActionUseItem(m_Actor,game,food);
            if(game.Rules.IsActorSleepy(m_Actor) && game.Rules.CanActorSleep(m_Actor)) return new ActionSleep(m_Actor,game);
            if(game.Rules.IsActorTired(m_Actor)) return new ActionWait(m_Actor,game);
            bool retreat=Victim.IsDead || m_Actor.HitPoints<game.Rules.ActorMaxHPs(m_Actor)/2;
            var gun=m_Actor.Inventory.Items.OfType<ItemRangedWeapon>().FirstOrDefault(i=>!i.IsEquipped && i.Ammo>0 && game.Rules.CanActorEquipItem(m_Actor,i));
            if(!retreat && gun!=null) return new ActionEquipItem(m_Actor,game,gun);
            if(!introduced && !retreat && game.Rules.GridDistance(m_Actor.Location.Position,Origin.Location.Position)<=1 &&
                NpcKnowledgeSystem.Visible(game,Origin,m_Actor.Location)) { introduced=true; return new ActionCommunityDiplomacy(m_Actor,game); }
            bool witnessed=NpcKnowledgeSystem.Visible(game,Origin,Victim.Location);
            if(!retreat && introduced && witnessed && game.Rules.CanActorFireAt(m_Actor,Victim)) return new ActionRangedAttack(m_Actor,game,Victim);
            if(!retreat && introduced && witnessed && game.Rules.CanActorMeleeAttack(m_Actor,Victim)) return new ActionMeleeAttack(m_Actor,game,Victim);
            var path=Route(World,m_Actor,retreat ? ReturnPoint : !introduced || !witnessed ? Origin.Location.Position : Victim.Location.Position);
            if(path==null || path.Count==0) return new ActionWait(m_Actor,game);
            return new ActionBump(m_Actor,game,Direction.FromVector(path[0].X-m_Actor.Location.Position.X,path[0].Y-m_Actor.Location.Position.Y));
        }
    }
    static IEnumerable<Map> Maps() { for(int x=0;x<3;x++) for(int y=0;y<3;y++) foreach(Map map in Session.Get.World[x,y].Maps) yield return map; }
    static int rounds;
    static int TrialSeed { get { int seed; return int.TryParse(Environment.GetEnvironmentVariable("ROGUE_DIPLOMACY_WORLD_SEED"), out seed) ? seed : 4923; } }
    static void Round(ScenarioWorld w)
    {
        Check.Equal(false,w.Game.Player.IsDead,"generated courier survives without added protection");
        CommunityClarityScenario.AmbientTurn(w); rounds++;
    }
    internal static ActorAction ThreatResponse(ScenarioWorld w)
    {
        Actor player=w.Game.Player;
        Actor danger=w.Map.Actors.Where(a=>a!=player && !a.IsDead && w.Game.Rules.IsKnownEnemy(player,a) &&
            w.Game.IdentifiesDiplomacySpeaker(player,a)).OrderBy(a=>w.Game.Rules.GridDistance(player.Location.Position,a.Location.Position)).FirstOrDefault();
        if(danger!=null && w.Game.Rules.GridDistance(player.Location.Position,danger.Location.Position)<=4) {
            var escapes=Direction.COMPASS.Select(d=>player.Location.Position+d).Where(p=>w.Map.IsInBounds(p) && w.Map.GetTileAt(p).Model.IsWalkable &&
                w.Map.GetActorAt(p)==null && (w.Map.GetMapObjectAt(p)==null || w.Map.GetMapObjectAt(p).IsWalkable))
                .OrderByDescending(p=>w.Game.Rules.GridDistance(p,danger.Location.Position)).ToArray();
            if(escapes.Length>0 && w.Game.Rules.GridDistance(escapes[0],danger.Location.Position)>w.Game.Rules.GridDistance(player.Location.Position,danger.Location.Position)) {
                return new ActionBump(player,w.Game,Direction.FromVector(escapes[0].X-player.Location.Position.X,escapes[0].Y-player.Location.Position.Y));
            }
            if(w.Game.Rules.CanActorMeleeAttack(player,danger)) return new ActionMeleeAttack(player,w.Game,danger);
        }
        return null;
    }
    static bool Survive(ScenarioWorld w)
    {
        Actor player=w.Game.Player;
        if(player.IsSleeping) { Round(w); return true; }
        var defense=ThreatResponse(w); if(defense!=null) { Check.Equal(true,w.Try(defense),"courier responds to a visible threat through a production action"); Round(w); return true; }
        var food=player.Inventory.Items.OfType<ItemFood>().FirstOrDefault(f=>!w.Game.Rules.IsFoodSpoiled(f,w.Map.LocalTime.TurnCounter));
        if(w.Game.Rules.IsActorHungry(player) && food!=null) { w.Try(new ActionUseItem(player,w.Game,food)); Round(w); return true; }
        if(w.Game.Rules.IsActorSleepy(player) && w.Game.Rules.CanActorSleep(player)) { w.Try(new ActionSleep(player,w.Game)); Round(w); return true; }
        return false;
    }
    static bool Step(ScenarioWorld w,Point goal,int range)
    {
        if(Survive(w)) return true;
        var path=CommunityClarityScenario.TryWalkingPath(w,goal,range);
        if(path==null) { w.Try(new ActionWait(w.Game.Player,w.Game)); Round(w); return false; }
        if(path.Count==0) return true;
        Check.Equal(true,w.Bump(w.Game.Player,Direction.FromVector(path[0].X-w.Game.Player.Location.Position.X,path[0].Y-w.Game.Player.Location.Position.Y)),"ordinary courier walking/doors"); Round(w); return true;
    }
    static void Walk(ScenarioWorld w,Point goal,int range)
    {
        for(int i=0;i<400 && w.Game.Rules.GridDistance(w.Game.Player.Location.Position,goal)>range;i++) Step(w,goal,range);
        Check.Equal(true,w.Game.Rules.GridDistance(w.Game.Player.Location.Position,goal)<=range,"courier reaches reachable location by real travel");
    }
    static void Talk(ScenarioWorld w,Actor leader,params Keys[] keys)
    {
        for(int i=0;i<500 && (w.Game.Player.IsSleeping || leader.IsSleeping || w.Game.Rules.GridDistance(w.Game.Player.Location.Position,leader.Location.Position)>1);i++) {
            Check.Equal(false,leader.IsDead,"leader survives until actual conversation"); Step(w,leader.Location.Position,1);
            if(leader.IsSleeping && w.Game.Rules.GridDistance(w.Game.Player.Location.Position,leader.Location.Position)<=1) { w.Try(new ActionWait(w.Game.Player,w.Game)); Round(w); }
        }
        var player=w.Game.Player; var ui=(ScenarioUI)w.Game.UI;
        Console.WriteLine("Courier V: "+leader.UnmodifiedName+" at "+leader.Location.Position+"; player="+player.Location.Position+"; sleep="+leader.IsSleeping+"; dead="+leader.IsDead+"; player sleeping="+player.IsSleeping+"; hostility="+w.Game.Rules.AreEnemies(player,leader)+"; time="+w.Map.LocalTime.TurnCounter+"; keys="+String.Join(",",keys));
        if(w.Game.Rules.AreEnemies(player,leader)) Console.WriteLine("Courier dispute: player aggressor="+player.IsAggressorOf(leader)+"; leader aggressor="+leader.IsAggressorOf(player)+"; known facts="+String.Join(",",leader.Personality.Knowledge.Facts.Where(f=>f.SubjectId==player.PersonalityIdentity || f.OtherId==player.PersonalityIdentity).Select(f=>f.Kind+"@"+f.EventTurn)));
        if(NpcConversation.Pending(player,leader)!=null) {
            int requestNeighbors=w.Map.Actors.Count(a=>a!=player && !a.IsDead && !a.IsSleeping && a.Model.Abilities.IsIntelligent && !w.Game.Rules.AreEnemies(player,a) && w.Game.Rules.GridDistance(player.Location.Position,a.Location.Position)<=1);
            if(requestNeighbors>1) ui.QueueKey(CommunityClarityScenario.DirectionKey(player.Location.Position,leader.Location.Position));
            ui.QueueWaitKey(Keys.N); Check.Equal(true,Check.Call(w.Game,"HandlePlayerTalk",player),"courier explicitly declines outstanding request before negotiation menu"); Round(w);
            Talk(w,leader,keys); return;
        }
        int nearby=w.Map.Actors.Count(a=>a!=player && !a.IsDead && !a.IsSleeping && a.Model.Abilities.IsIntelligent && !w.Game.Rules.AreEnemies(player,a) && w.Game.Rules.GridDistance(player.Location.Position,a.Location.Position)<=1);
        if(nearby>1) ui.QueueKey(CommunityClarityScenario.DirectionKey(player.Location.Position,leader.Location.Position));
        foreach(Keys key in keys) ui.QueueWaitKey(key);
        Check.Equal(true,Check.Call(w.Game,"HandlePlayerTalk",player),"generated encounter uses real V menu"); Round(w);
    }
    static void Enter(ref ScenarioWorld w,Map target)
    {
        var queue=new Queue<Map>(); var parent=new Dictionary<Map,KeyValuePair<Map,Point>>(); queue.Enqueue(w.Map); parent[w.Map]=default(KeyValuePair<Map,Point>);
        while(queue.Count>0 && !parent.ContainsKey(target)) { Map map=queue.Dequeue(); foreach(var exit in map.ExitEntries) if(!parent.ContainsKey(exit.Value.ToMap)) { parent[exit.Value.ToMap]=new KeyValuePair<Map,Point>(map,exit.Key); queue.Enqueue(exit.Value.ToMap); } }
        Check.Equal(true,parent.ContainsKey(target),"generated target has connected exits");
        var route=new List<KeyValuePair<Map,Point>>(); for(Map at=target;at!=w.Map;at=parent[at].Key) route.Add(parent[at]); route.Reverse();
        foreach(var leg in route) {
            Walk(w,leg.Value,0); Actor player=w.Game.Player;
            Map destination=w.Map.GetExitAt(leg.Value).ToMap;
            // Catch up an inactive map before placing the player there, using the engine's full NPC scheduler.
            Type flags=typeof(RogueGame).GetNestedType("SimFlags",System.Reflection.BindingFlags.NonPublic);
            object simulation=Enum.Parse(flags,"HIDETAIL_TURN");
            while(destination.LocalTime.TurnCounter<Session.Get.WorldTime.TurnCounter)
                Check.Call(w.Game,"AdvancePlay",new[]{typeof(Map),flags},destination,simulation);
            Check.Equal(true,w.Try(new ActionUseExit(player,leg.Value,w.Game)),"ordinary generated exit transition");
            w=new ScenarioWorld(w.Seed,player.Location.Map,w.Game); Round(w);
        }
    }
    static void RelationTalk(ScenarioWorld w,Actor leader,Actor other,Keys operation,bool confirm=false)
    {
        int index=leader.SocialGroup.Diplomacy.Relations.FindIndex(r=>r.Target.Group==other.SocialGroup.Identity);
        Check.Equal(true,index>=0,"leader menu has actual known counterpart");
        var keys=new List<Keys>{Keys.D4}; for(int i=0;i<index;i++) keys.Add(Keys.Right); keys.Add(operation); if(confirm) keys.Add(Keys.Y);
        Talk(w,leader,keys.ToArray());
    }
    static void Donate(ScenarioWorld w,Actor leader)
    {
        if(NpcIntentSupport.FoodUnits(w.Game.Player)<3) return;
        var player=w.Game.Player; ItemFood gift=player.Inventory.Items.OfType<ItemFood>().FirstOrDefault(f=>!w.Game.Rules.IsFoodSpoiled(f,w.Map.LocalTime.TurnCounter));
        string reason; if(gift==null || !w.Game.Rules.CanActorGiveItemTo(player,leader,gift,out reason)) return;
        for(int i=0;i<200 && w.Game.Rules.GridDistance(player.Location.Position,leader.Location.Position)>1;i++) Step(w,leader.Location.Position,1);
        int slot=0; while(player.Inventory[slot]!=gift) slot++;
        ((ScenarioUI)w.Game.UI).QueueKey(CommunityClarityScenario.DirectionKey(player.Location.Position,leader.Location.Position));
        Check.Equal(true,Check.Call(w.Game,"HandlePlayerGiveItem",player,CommunityClarityScenario.InventorySlot(w,slot)),"courier voluntarily shares actual gathered food through G"); Round(w);
        Console.WriteLine("Courier donation: "+leader.UnmodifiedName+"; food="+leader.FoodPoints+"; HP="+leader.HitPoints+"; turn="+w.Map.LocalTime.TurnCounter);
    }
    static void Camp(ScenarioWorld w,Actor awayFrom)
    {
        var camps=new List<Point>();
        for(int x=0;x<w.Map.Width;x++) for(int y=0;y<w.Map.Height;y++) { Point p=new Point(x,y);
            if(w.Map.GetTileAt(p).Model.IsWalkable && w.Map.GetTileAt(p).IsInside && w.Map.XpdBaseAt(p)==null && w.Map.GetActorAt(p)==null &&
                w.Game.Rules.GridDistance(p,awayFrom.Location.Position)>=12) camps.Add(p); }
        Point camp=camps.OrderBy(p=>w.Game.Rules.GridDistance(w.Game.Player.Location.Position,p)).First(p=>CommunityClarityScenario.TryWalkingPath(w,p,0)!=null);
        Walk(w,camp,0);
    }
    public static void Register()
    {
        ScenarioRunner.Add("world/community-diplomacy-police-conservation",()=>TownScenarioFactory.Create(4921,false),initial=>{
            CommunityNewWorldScenario.Generate(initial,false);
            var actors=Maps().SelectMany(m=>m.Actors).ToArray(); var locations=actors.Select(a=>a.Location).ToArray();
            var inventories=actors.Select(a=>a.Inventory==null ? new Item[0] : a.Inventory.Items.ToArray()).ToArray(); var quantities=inventories.Select(items=>items.Select(i=>i.Quantity).ToArray()).ToArray();
            var capacity=actors.Select(a=>initial.Game.Rules.ActorMaxFollowers(a)).ToArray(); var skills=actors.Select(a=>a.Sheet.SkillTable).ToArray();
            Session.Get.GamePreset.InitialCommunities=true; int units=initial.Game.EstablishLocalPoliceUnits();
            Check.Equal(true,units>0,"real generated officers form local units");
            Check.Equal(actors.Length,Maps().Sum(m=>m.Actors.Count()),"all generated survivors conserved");
            var groups=new HashSet<Guid>(); var names=new HashSet<string>();
            for(int i=0;i<actors.Length;i++) {
                Actor a=actors[i]; Check.Equal(locations[i],a.Location,"organization preserves actor position/map"); Check.Same(skills[i],a.Sheet.SkillTable,"organization retains skill table");
                Check.Equal(capacity[i],initial.Game.Rules.ActorMaxFollowers(a),"organization grants no Leadership");
                var current=a.Inventory==null ? new Item[0] : a.Inventory.Items.ToArray();
                Check.Equal(true,inventories[i].SequenceEqual(current),"organization conserves equipment/inventory objects");
                Check.Equal(true,quantities[i].SequenceEqual(current.Select(item=>item.Quantity)),"organization conserves quantities");
                if(a.Faction!=initial.Game.GameFactions.ThePolice || a.SocialGroup==null || a.HasLeader) continue;
                Check.Equal(true,groups.Add(a.SocialGroup.Identity),"distinct police identity"); Check.Equal(true,names.Add(a.SocialGroup.CommunityName),"distinct police name");
                Check.Equal(true,a.CountFollowers<=capacity[i],"legal existing command capacity");
                if(a.Location.Map.Name.IndexOf("Police Station",StringComparison.OrdinalIgnoreCase)<0) Check.Equal(true,a.CountFollowers<=2,"outdoor patrol capped at three total");
                Check.Equal(0,a.SocialGroup.Diplomacy.Relations.Count,"fresh police relationships neutral");
            }
            Console.WriteLine("Generated police conservation: "+actors.Length+" existing actors, "+units+" distinct local units; inventories, quantities, skill capacity and positions unchanged.");
        });
        ScenarioRunner.Add("world/community-diplomacy-round-trip",()=>TownScenarioFactory.Create(TrialSeed,false),initial=>{
            CommunityNewWorldScenario.Generate(initial); var w=new ScenarioWorld(TrialSeed,initial.Game.Player.Location.Map,initial.Game); rounds=0;
            foreach(var group in w.Map.Actors.Where(a=>a.SocialGroup!=null && !a.HasLeader)) Console.WriteLine("Generated candidate "+group.SocialGroup.CommunityName+" at "+group.Location.Position+"; members="+group.SocialGroup.Members.Count+"; declaration bias="+(PersonalitySystem.Bias(group,DecisionKind.Group)+PersonalitySystem.Bias(group,DecisionKind.Courage)/2-PersonalitySystem.Bias(group,DecisionKind.Compassion)/2));
            // Gather only generated unclaimed food. Map-aware routing is a test driver, not player knowledge.
            for(int trip=0;trip<6 && NpcIntentSupport.FoodUnits(w.Game.Player)<6;trip++) {
                var supplies=new List<KeyValuePair<Point,ItemFood>>();
                for(int x=0;x<w.Map.Width;x++) for(int y=0;y<w.Map.Height;y++) { Point p=new Point(x,y); var stock=w.Map.GetItemsAt(p); if(stock==null || w.Map.XpdBaseAt(p)!=null) continue; foreach(var f in stock.Items.OfType<ItemFood>()) if(!w.Game.Rules.IsFoodSpoiled(f,w.Map.LocalTime.TurnCounter)) supplies.Add(new KeyValuePair<Point,ItemFood>(p,f)); }
                var source=supplies.OrderBy(p=>w.Game.Rules.GridDistance(w.Game.Player.Location.Position,p.Key)).First(p=>CommunityClarityScenario.TryWalkingPath(w,p.Key,1)!=null);
                Walk(w,source.Key,1); if(w.Map.GetItemsAt(source.Key)==null || !w.Map.GetItemsAt(source.Key).Contains(source.Value)) continue;
                Check.Equal(true,w.Try(new ActionTakeItem(w.Game.Player,w.Game,source.Key,source.Value)),"courier gathers existing unclaimed provisions"); Round(w);
            }
            var roots=w.Map.Actors.Where(a=>!a.IsDead && CommunityDiplomacySystem.IsLeader(a)).ToArray();
            Actor origin=roots.FirstOrDefault(a=>a.Faction==w.Game.GameFactions.TheCivilians && PersonalitySystem.Bias(a,DecisionKind.Group)+PersonalitySystem.Bias(a,DecisionKind.Courage)/2-PersonalitySystem.Bias(a,DecisionKind.Compassion)/2>=0 &&
                CommunityDiplomacySystem.PeaceScore(a,new CommunityRelation { Grievance=60 })>=0);
            Check.Equal(true,origin!=null,"natural leader can qualify from attributed killing");
            Actor other=roots.Where(a=>a.Faction==w.Game.GameFactions.ThePolice && CommunityDiplomacySystem.PeaceScore(a,new CommunityRelation())>=0).OrderByDescending(a=>w.Game.Rules.GridDistance(a.Location.Position,origin.Location.Position)).FirstOrDefault();
            Check.Equal(true,other!=null,"another surviving generated group exists on encounter map");
            Talk(w,origin,Keys.D1); Donate(w,origin); Talk(w,other,Keys.D1);
            // Scripted provocation: pilot one existing rival member with legal movement and melee. Every travel/combat round still runs the normal scheduler/metabolism.
            Actor aggressor=other,victim=origin.Followers.Where(a=>!a.IsDead).OrderBy(a=>w.Game.Rules.GridDistance(a.Location.Position,origin.Location.Position)).First();
            Console.WriteLine("Provoker equipment: "+String.Join(",",aggressor.Inventory.Items.Select(i=>i.AName))+"; victim: "+String.Join(",",victim.Inventory.Items.Select(i=>i.AName))+"; turn="+w.Map.LocalTime.TurnCounter);
            Guid originId=origin.SocialGroup.Identity, otherId=other.SocialGroup.Identity;
            Actor originalOrigin=origin,originalOther=other;
            var pilot=new ProvocationPilot(w,aggressor,victim,origin,other); aggressor.Controller=pilot;
            Console.WriteLine("Scripted provocation uses existing "+aggressor.UnmodifiedName+" of "+other.SocialGroup.CommunityName+" against "+victim.UnmodifiedName+"; no spawned actors/items, altered health, skills or protection.");
            Camp(w,origin);
            for(int attempt=0;attempt<500 && !victim.IsDead;attempt++) {
                Check.Equal(false,aggressor.IsDead,"scripted rival survives ordinary combat");
                // Route computation can be reused for any actor by selecting its start, without moving or replacing the player.
                if(!Survive(w)) { w.Try(new ActionWait(w.Game.Player,w.Game)); Round(w); }
            }
            Check.Equal(true,victim.IsDead,"scripted real engagement resolves");
            for(int turn=0;turn<400;turn++) {
                origin=w.Map.Actors.FirstOrDefault(a=>!a.IsDead && a.SocialGroup!=null && a.SocialGroup.Identity==originId && !a.HasLeader);
                other=w.Map.Actors.FirstOrDefault(a=>!a.IsDead && a.SocialGroup!=null && a.SocialGroup.Identity==otherId && !a.HasLeader);
                if(origin==null || other==null) foreach(var actor in Maps().SelectMany(map=>map.Actors).Where(a=>a.SocialGroup!=null && (a.SocialGroup.Identity==originId || a.SocialGroup.Identity==otherId)))
                    Console.WriteLine("Remaining group actor "+actor.UnmodifiedName+"; dead="+actor.IsDead+"; HP="+actor.HitPoints+"; map="+actor.Location.Map.Name+"; leader="+actor.HasLeader+"; turn="+w.Map.LocalTime.TurnCounter);
                if(origin==null || other==null) Console.WriteLine("Original roots: "+originalOrigin.UnmodifiedName+" dead="+originalOrigin.IsDead+"; "+originalOther.UnmodifiedName+" dead="+originalOther.IsDead+"; other leader="+(originalOther.Leader==null ? "none" : originalOther.Leader.UnmodifiedName)+"; group="+(originalOther.SocialGroup==null ? "none" : originalOther.SocialGroup.CommunityName)+"; map="+originalOther.Location.Map.Name);
                Check.Equal(true,origin!=null && other!=null,"naturally surviving groups retain leaders or actual successors");
                if(origin.SocialGroup.Diplomacy!=null && origin.SocialGroup.Diplomacy.Relations.Any(r=>r.Feud)) break;
                if(!Survive(w)) { w.Try(new ActionWait(w.Game.Player,w.Game)); Round(w); }
            }
            for(int attempt=0;attempt<400 && !aggressor.IsDead && w.Game.Rules.GridDistance(aggressor.Location.Position,pilot.ReturnPoint)>1;attempt++)
                if(!Survive(w)) { w.Try(new ActionWait(w.Game.Player,w.Game)); Round(w); }
            aggressor.Controller=pilot.Original;
            Check.Equal(false,origin.IsDead,"origin leader survives autonomous aftermath");
            foreach(var relation in origin.SocialGroup.Diplomacy.Relations) Console.WriteLine("Aftermath relation "+origin.UnmodifiedName+" -> "+relation.Target.Name+": grievance="+relation.Grievance+"; feud="+relation.Feud+"; causes="+String.Join(",",relation.Incidents.Select(i=>i.Explanation+"/"+i.Severity)));
            Check.Equal(true,origin.SocialGroup.Diplomacy.Relations.Any(r=>r.Feud),"actual NPC scheduler declares from real evidence");
            RelationTalk(w,origin,other,Keys.D1);
            Donate(w,origin);
            // Spend the cooling-off period away from the grieving group's home and visible residents.
            Camp(w,origin);
            for(int attempt=0;attempt<800 && CommunityDiplomacySystem.PeaceReason(w.Game,origin,CommunityDiplomacyScenario.Relation(origin,other))!=null;attempt++) { if(!Survive(w)) { w.Try(new ActionWait(w.Game.Player,w.Game)); Round(w); } }
            RelationTalk(w,origin,other,Keys.D2,true); var offer=w.Game.Player.Personality.PeaceOffers.Last();
            Guid originPerson=origin.PersonalityIdentity, otherPerson=other.PersonalityIdentity;
            string save=Path.Combine(Environment.GetEnvironmentVariable("ROGUE_COMMUNITY_ARTIFACTS"),"diplomacy-world-carried-offer-format-5.dat");
            BinarySaveStore.Save(save,Session.Get);
            Check.Equal(true,Check.Call(w.Game,"LoadGame",save),"real game reload while generated courier carries authorized terms");
            w=new ScenarioWorld(TrialSeed,w.Game.Player.Location.Map,w.Game);
            origin=Maps().SelectMany(map=>map.Actors).First(a=>a.PersonalityIdentity==originPerson);
            other=Maps().SelectMany(map=>map.Actors).First(a=>a.PersonalityIdentity==otherPerson);
            offer=w.Game.Player.Personality.PeaceOffers.Last();
            if(other.Location.Map!=w.Map) Enter(ref w,other.Location.Map);
            Talk(w,other,Keys.D4,Keys.D3); Check.Equal(true,offer.Accepted,"generated counterpart accepts actual delivered offer");
            if(origin.Location.Map!=w.Map) Enter(ref w,origin.Location.Map);
            Talk(w,origin,Keys.D4,Keys.D4); Check.Equal(true,offer.Ratified,"courier returns to actual ratification");
            Check.Equal(false,CommunityDiplomacyScenario.Relation(origin,other).Feud,"collective feud ends after return");
            BinarySaveStore.Save(Path.Combine(Environment.GetEnvironmentVariable("ROGUE_COMMUNITY_ARTIFACTS"),"diplomacy-world-ratified-format-5.dat"),Session.Get);
            Console.WriteLine("Fresh generated diplomacy trial: "+rounds+" normal scheduler/survival rounds; player recognized groups, learned declaration, carried terms, delivered and returned to ratification. Provocation was scripted; declaration and survival were autonomous. No human visual review claimed.");
        });
    }
    static List<Point> Route(ScenarioWorld w,Actor actor,Point goal)
    {
        Point start=actor.Location.Position; var queue=new Queue<Point>(); var parent=new Dictionary<Point,Point>(); queue.Enqueue(start); parent[start]=start;
        while(queue.Count>0) { Point at=queue.Dequeue(); if(w.Game.Rules.GridDistance(at,goal)<=1) { var result=new List<Point>(); while(at!=start) { result.Add(at); at=parent[at]; } result.Reverse(); return result; }
            foreach(var direction in Direction.COMPASS) { Point p=at+direction; if(!w.Map.IsInBounds(p) || parent.ContainsKey(p) || !w.Map.GetTileAt(p).Model.IsWalkable || w.Map.GetActorAt(p)!=null) continue;
                MapObject obj=w.Map.GetMapObjectAt(p); var door=obj as djack.RogueSurvivor.Engine.MapObjects.DoorWindow;
                if(obj!=null && !obj.IsWalkable && (door==null || door.BarricadePoints>0)) continue;
                parent[p]=at; queue.Enqueue(p); }
        } return null;
    }
}
