using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using djack.RogueSurvivor.Data;
using djack.RogueSurvivor.Engine;
using djack.RogueSurvivor.Engine.Actions;
using djack.RogueSurvivor.Engine.Items;
using djack.RogueSurvivor.Gameplay.Personality;
using djack.RogueSurvivor.Gameplay.AI;
using djack.RogueSurvivor.Gameplay;

static class CommunityDiplomacyEdgesScenario
{
    static Actor Group(ScenarioWorld w,string name,int x,bool cop=false) { return CommunityDiplomacyScenario.Group(w,name,x,cop); }
    static CommunityRelation Relation(Actor a,Actor b) { return CommunityDiplomacyScenario.Relation(a,b); }
    static void Introduce(ScenarioWorld w,params Actor[] groups) { CommunityDiplomacyScenario.Introduce(w,groups); }
    static void Reply(ScenarioWorld w,Actor player,Actor leader,bool yes)
    {
        NpcEvents.Publish(w.Game,"requested_food",leader,player);
        player.ActionPoints=100;
        Check.Equal(true,w.Try(new ActionPlayerTalk(player,w.Game,leader,yes)),"real request reply");
    }
    public static void Register()
    {
        ScenarioRunner.Add("npc/community-diplomacy-peace-news-boundaries",()=>CommunityMembershipScenario.Arena(4987),w=>{
            Actor player=NpcIntentSupport.Player(w,5,3),a=Group(w,"Oak",2),b=Group(w,"River",7);
            Introduce(w,a,b); CommunityDiplomacyScenario.Kill(w,b.Followers.First(),a.Followers.First());
            w.Map.PlaceActorAt(b.Followers.First(),new Point(20,6)); CommunityDiplomacyScenario.Declare(w,a);
            CommunityMembershipScenario.Time(w,180);
            CommunityDiplomacyScenario.TalkKey(w,player,a,Keys.D4,Keys.D2,Keys.Y);
            CommunityDiplomacyScenario.TalkKey(w,player,b,Keys.D4,Keys.D3);
            w.Map.PlaceActorAt(b,new Point(23,2));
            Check.Equal(true,w.Game.Rules.StdDistance(a.Location.Position,b.Location.Position)>b.AudioRange,"ratification is genuinely beyond hearing range");
            CommunityDiplomacyScenario.TalkKey(w,player,a,Keys.D4,Keys.D4);
            var relation=Relation(b,a); int peaceTurn=relation.LastPeaceTurn;
            Check.Equal(180,peaceTurn,"formal agreement ends the remote signed declaration");
            Check.Equal(0,CommunityDiplomacySystem.ReportedMessage(b,relation,180).PeaceTurn,"remote leader does not know unheard ratification");
            Check.Equal(true,b.Personality.FindDiplomacy(a.SocialGroup.Identity,PoliticalIdentity.Of(b.SocialGroup)).Message.Feud,"uninformed leader retains old incoming feud belief");
            string file=System.IO.Path.Combine(Environment.GetEnvironmentVariable("ROGUE_COMMUNITY_ARTIFACTS"),"diplomacy-peace-news-format-5.dat");
            BinarySaveStore.Save(file,Session.Get); w=NpcIntentSupport.Restore(w,BinarySaveStore.Load<Session>(file));
            player=NpcIntentSupport.Find(w.Map,player.PersonalityIdentity); a=NpcIntentSupport.Find(w.Map,a.PersonalityIdentity); b=NpcIntentSupport.Find(w.Map,b.PersonalityIdentity); relation=Relation(b,a);
            Check.Equal(peaceTurn,relation.LastPeaceTurn,"formal agreement persists in format 5");
            Check.Equal(0,CommunityDiplomacySystem.ReportedMessage(b,relation,180).PeaceTurn,"reload preserves local ignorance separately from the agreement");
            CommunityDiplomacyScenario.TalkKey(w,player,b,Keys.D4,Keys.D1);
            Check.Equal(peaceTurn,player.Personality.FindDiplomacy(b.SocialGroup.Identity,PoliticalIdentity.Of(a.SocialGroup)).Message.PeaceTurn,"actual stale V report cannot overwrite the courier's witnessed peace");
            w.Map.PlaceActorAt(a,new Point(22,2)); b.FoodPoints=100;
            var food=NpcIntentSupport.Food(w,a,1); a.ActionPoints=100; w.Game.DoGiveItemTo(a,b,food);
            Check.Equal(0,CommunityDiplomacySystem.ReportedMessage(b,relation,180).PeaceTurn,"unrelated useful aid does not disclose the remote agreement");
            var reportedPeace=a.Personality.Knowledge.Facts.Last(f=>f.DiplomacyMessage!=null && f.DiplomacyMessage.Community==b.SocialGroup.Identity && f.DiplomacyMessage.PeaceTurn==peaceTurn);
            a.ActionPoints=100; Check.Equal(true,w.Try(new ActionCommunityDiplomacy(a,w.Game,b,reportedPeace)),"real speech carries the remote group's signed peace");
            Check.Equal(0,a.ActionPoints,"news report charges one action");
            Check.Equal(peaceTurn,CommunityDiplomacySystem.ReportedMessage(b,relation,180).PeaceTurn,"leader learns peace only through heard news despite intervening relation revisions");
            CommunityDiplomacyScenario.TalkKey(w,player,b,Keys.D4,Keys.D1);
            Check.Equal(peaceTurn,player.Personality.FindDiplomacy(b.SocialGroup.Identity,PoliticalIdentity.Of(a.SocialGroup)).Message.PeaceTurn,"informed V report retains signed peace date");
        });
        ScenarioRunner.Add("npc/community-diplomacy-unclear-killing",()=>CommunityMembershipScenario.Arena(4986),w=>{
            NpcIntentSupport.Player(w,20,7); Actor a=Group(w,"Oak",2),b=Group(w,"River",7);
            Introduce(w,a,b); Actor victim=a.Followers.First(),attacker=b.Followers.First();
            w.Map.PlaceActorAt(a,new Point(20,6)); victim.HitPoints=50;
            CommunityDiplomacyScenario.Injury(w,attacker,victim,0);
            Check.Equal(null,Relation(a,b),"absent leader does not automatically learn the first attack");
            victim.HitPoints=1; w.Map.PlaceActorAt(a,new Point(2,3));
            CommunityDiplomacyScenario.Kill(w,attacker,victim);
            Check.Equal(40,Relation(a,b).Grievance,"leader witnessing only the killing records unclear circumstances");
            Check.Equal(null,CommunityDiplomacySystem.Declaration(a),"unclear circumstances alone do not reach neutral leader threshold");
        });
        ScenarioRunner.Add("npc/community-diplomacy-single-officer-identity",()=>CommunityMembershipScenario.Arena(4984),w=>{
            NpcIntentSupport.Player(w,20,7); Actor a=Group(w,"Police Station detail 1",2,true),b=Group(w,"District police patrol 1",7,true);
            b.RemoveFollower(b.Followers.First()); var identity=b.SocialGroup.Identity; string reason;
            Check.Equal(false,w.Game.Rules.CanActorTakeLead(a,b,out reason),"ordinary recruitment cannot dissolve a named singleton unit");
            Check.Equal("is a leader",reason,"single officer remains its unit's leader");
            Check.Equal(identity,b.SocialGroup.Identity,"failed recruitment preserves diplomatic identity");
        });
        ScenarioRunner.Add("npc/community-diplomacy-membership-continuity",()=>CommunityMembershipScenario.Arena(4982),w=>{
            Actor player=NpcIntentSupport.Player(w,3,2),personal=NpcIntentSupport.Actor(w,"Personal follower",2,2),a=CommunityMembershipScenario.Group(w),b=Group(w,"River",16);
            player.AddFollower(personal); player.Sheet.SkillTable.AddOrIncreaseSkill((int)Skills.IDs.LEADERSHIP); player.Sheet.SkillTable.AddOrIncreaseSkill((int)Skills.IDs.LEADERSHIP);
            var order=new ActorOrder(ActorTasks.GUARD,personal.Location); ((OrderableAI)personal.Controller).SetOrder(order);
            var original=player.SocialGroup; original.Diplomacy=new CommunityDiplomacyState();
            var history=original.Diplomacy.Get(PoliticalIdentity.Of(b.SocialGroup)); history.Grievance=60; history.Feud=true; history.Revision=1;
            Introduce(w,player,a,b); a.FoodPoints=100; var food=NpcIntentSupport.Food(w,player,1);
            var ui=(ScenarioUI)w.Game.UI; ui.QueueKey(Keys.NumPad6); player.ActionPoints=100;
            Check.Equal(true,Check.Call(w.Game,"HandlePlayerGiveItem",player,CommunityClarityScenario.InventorySlot(w,0)),"membership eligibility uses real giving input");
            player.ActionPoints=100; Check.Equal(true,w.Try(new ActionJoinCommunity(player,w.Game,a)),"actual membership action admits the personal party");
            Check.Equal(a.SocialGroup.Identity,CommunityDiplomacySystem.Recognizes(a,player).Group,"witnessed joining updates recognized affiliation");
            CommunityMembershipScenario.Time(w,200);
            var dispute=a.SocialGroup.Diplomacy.Get(PoliticalIdentity.Of(b.SocialGroup)); dispute.Feud=true; string reason;
            Check.Equal(null,CommunityDiplomacySystem.Authorize(w.Game,player,a,dispute,out reason),"a community member cannot authorize a supposedly neutral courier packet");
            Check.Equal(true,reason.Contains("outside both"),"neutrality restriction explains membership conflict");
            Actor recruit=NpcIntentSupport.Actor(w,"New follower",3,1); player.ActionPoints=100;
            Check.Equal(true,w.Try(new ActionTakeLead(player,w.Game,recruit)),"real recruitment retains personal control while joined");
            Check.Same(player,recruit.Leader,"recruit is commanded by player");
            Check.Equal(a.SocialGroup.Identity,CommunityDiplomacySystem.Recognizes(player,recruit).Group,"actual recruitment updates known community affiliation");
            player.ActionPoints=100; w.Game.DoCancelLead(player,recruit);
            Check.Equal(false,a.SocialGroup.Members.Contains(recruit.PersonalityIdentity),"dismissal clears community roster");
            player.ActionPoints=100; Check.Equal(true,w.Try(new ActionLeaveCommunity(player,w.Game)),"actual departure restores original group");
            Check.Same(original,player.SocialGroup,"original political identity restored");
            Check.Same(history,original.Diplomacy.Find(PoliticalIdentity.Of(b.SocialGroup)),"original diplomatic history survives admission and departure");
            Check.Equal(true,history.Feud,"joining and leaving never erase original feud");
            Check.Equal(60,history.Grievance,"no join/leave relationship reward"); Check.Same(order,((OrderableAI)personal.Controller).Order,"personal order conserved");
        });
        ScenarioRunner.Add("npc/community-diplomacy-call-news",()=>CommunityMembershipScenario.Arena(4983),w=>{
            Actor player=NpcIntentSupport.Player(w,5,3),a=Group(w,"Oak",2),b=Group(w,"River",7);
            Introduce(w,a,b); CommunityDiplomacyScenario.Kill(w,b.Followers.First(),a.Followers.First());
            w.Map.PlaceActorAt(b.Followers.First(),new Point(20,6)); CommunityDiplomacyScenario.Declare(w,a);
            player.Personality=new PersonalityState(); a.SocialGroup.CommunityContact=null;
            CommunityEncounterSystem.ObserveContact(w.Game,a,new[]{player}); a.ActionPoints=100;
            Check.Equal(true,w.Try(new ActionCommunityCall(a,w.Game,player)),"real greeting includes learned diplomatic news");
            Check.Equal(0,a.ActionPoints,"call and its news cost one action together");
            var learned=player.Personality.KnownDiplomacy.Single(d=>d.Message.Target.Group==b.SocialGroup.Identity);
            Check.Equal(true,learned.Message.Feud,"player hears the stated feud");
            Check.Equal(null,learned.Message.Cause,"short call supplies no unspoken founding incident");
            Check.Equal(false,player.Personality.Communities.Any(c=>c.Identity==b.SocialGroup.Identity && c.Members.Count>0),"call reveals no rival roster");
        });
        ScenarioRunner.Add("npc/community-diplomacy-aid-and-quiet",()=>CommunityMembershipScenario.Arena(4981),w=>{
            Actor player=NpcIntentSupport.Player(w,5,3),a=Group(w,"Oak",2),b=Group(w,"River",7);
            w.Map.PlaceActorAt(b,new Point(2,4)); Introduce(w,a,b);
            CommunityDiplomacyScenario.Kill(w,b.Followers.First(),a.Followers.First());
            Check.Equal(0,Relation(b,a).Grievance,"knowing our member's violence does not blame its victim");
            w.Map.PlaceActorAt(b.Followers.First(),new Point(20,6)); CommunityDiplomacyScenario.Declare(w,a);
            CommunityMembershipScenario.Time(w,180);
            var relation=Relation(a,b); int before=CommunityDiplomacySystem.PeaceScore(a,relation);
            a.FoodPoints=100; w.Map.PlaceActorAt(b,new Point(2,3));
            var food=NpcIntentSupport.Food(w,b,1); string reason;
            Check.Equal(true,w.Game.Rules.CanActorGiveItemTo(b,a,food,out reason),"collective feud permits a voluntary useful gift without parley protection");
            b.ActionPoints=100; w.Game.DoGiveItemTo(b,a,food);
            Check.Equal(0,b.ActionPoints,"real donation spends exactly one action");
            Check.Equal(50,relation.Grievance,"actual rival aid reduces attributed grievance");
            Check.Equal(before+5,CommunityDiplomacySystem.PeaceScore(a,relation),"useful aid improves prescribed willingness score");
            Check.Equal(true,relation.Feud,"giving does not end collective hostility");
            w.Game.DoMakeAggression(a,b);
            food=NpcIntentSupport.Food(w,b,1);
            Check.Equal(false,w.Game.Rules.CanActorGiveItemTo(b,a,food,out reason),"personal hostility still bars giving");
            w.Map.PlaceActorAt(b,new Point(2,4));
            // A neutral courier can obtain terms; freshly known own-side violence invalidates them.
            CommunityDiplomacyScenario.TalkKey(w,player,a,Keys.D4,Keys.D2,Keys.Y);
            var offer=player.Personality.PeaceOffers.Last();
            CommunityMembershipScenario.Time(w,200);
            CommunityDiplomacyScenario.Injury(w,a,b,200);
            Check.Equal(200,relation.LastViolenceLearnedTurn,"leader's own real violence restarts quiet period");
            Check.Equal(50,relation.Grievance,"our own attack adds no grievance against the victim");
            Check.Equal(true,CommunityDiplomacySystem.PeaceReason(w.Game,a,relation).Contains("180"),"fresh own-side violence blocks peace");
            offer.Answered=true; offer.Accepted=true; // Isolate origin ratification's freshness check.
            Check.Equal(false,CommunityDiplomacySystem.Ratify(w.Game,player,a,offer,out reason),"previous offer cannot ratify after newly known violence");
        });
        ScenarioRunner.Add("npc/community-diplomacy-contributions",()=>CommunityMembershipScenario.Arena(4974),w=>{
            Actor player=NpcIntentSupport.Player(w,2,3),a=Group(w,"Oak",2),b=Group(w,"Courier party",7);
            player.AddFollower(b); player.EnsurePersonalGroup().CommunityName="Courier party";
            Introduce(w,a,player); a.FoodPoints=100;
            var food=NpcIntentSupport.Food(w,player,1); w.Game.DoGiveItemTo(player,a,food);
            Check.Equal(10,Relation(a,player).Trust,"useful aid improves collective trust once");
            Reply(w,player,a,true);
            food=NpcIntentSupport.Food(w,player,1); w.Game.DoGiveItemTo(player,a,food);
            Check.Equal(true,Relation(a,player).Incidents.Any(i=>i.Kind=="promise_kept"),"fulfilled delivery is known independently of useful aid");
            int trust=Relation(a,player).Trust;
            var traded=NpcIntentSupport.Food(w,player,1); var asked=NpcIntentSupport.Food(w,a,1);
            Check.Call(w.Game,"SwapActorItems",player,traded,a,asked);
            Check.Equal(trust+2,Relation(a,player).Trust,"actual inventory exchange has smaller trade credit");
            Reply(w,player,a,false);
            Check.Equal(2,Relation(a,player).Grievance,"refusal harms relations but cannot qualify for a feud");
            Reply(w,player,a,true);
            int due=a.Personality.Commitments.Last(c=>c.Status==NpcCommitmentStatus.Active).DueTurn;
            // Departure changes current affiliation; the overdue promise retains its original attribution.
            var original=player.SocialGroup; player.RemoveFollower(b);
            CommunityMembershipScenario.Time(w,due); NpcPromises.Expire(a,w.Game.NpcContent);
            Check.Equal(7,original==player.SocialGroup ? Relation(a,player).Grievance : a.SocialGroup.Diplomacy.Find(PoliticalIdentity.Of(original)).Grievance,"broken promise adds five using saved attribution");
            Check.Equal(null,CommunityDiplomacySystem.Declaration(a),"aid/refusal/broken promises never qualify without violence");
        });
        ScenarioRunner.Add("npc/community-diplomacy-theft-and-reports",()=>CommunityMembershipScenario.Arena(4975),w=>{
            Actor player=NpcIntentSupport.Player(w,20,7),a=CommunityMembershipScenario.Group(w),b=Group(w,"River",12);
            Introduce(w,a,b); a.SocialGroup.Diplomacy=new CommunityDiplomacyState();
            Actor thief=b.Followers.First(); w.Map.PlaceActorAt(thief,new Point(6,4));
            // The leader initially cannot witness. The owner who sees the theft carries its attributed report.
            Actor witness=a.Followers.First(); w.Map.PlaceActorAt(witness,new Point(6,3));
            CommunityDiplomacySystem.Introduce(w.Game,thief);
            w.Map.PlaceActorAt(a,new Point(22,6));
            var stock=NpcIntentSupport.Food(w,player,1); player.Inventory.RemoveAllQuantity(stock); w.Map.DropItemAt(stock,new Point(6,4));
            thief.ActionPoints=100; Check.Equal(true,w.Try(new ActionTakeItem(thief,w.Game,new Point(6,4),stock)),"real witnessed theft takes existing stock");
            Check.Equal(null,Relation(a,b),"absent leader gets no automatic collective grievance");
            var fact=witness.Personality.Knowledge.Facts.Last(f=>f.Kind=="base_theft");
            w.Map.PlaceActorAt(a,new Point(4,2)); w.Map.PlaceActorAt(thief,new Point(20,6));
            witness.ActionPoints=100; var selected=CommunityDiplomacySystem.Select(w.Game,witness,new[]{a});
            Check.Equal(true,selected is ActionCommunityDiplomacy,"witness chooses an actual attributed report");
            Check.Equal(true,w.Try(selected),"report action executes");
            Check.Equal(15,Relation(a,b).Grievance,"heard theft adds fifteen once");
            NpcKnowledgeSystem.Hear(w.Game,a,witness,fact);
            Check.Equal(15,Relation(a,b).Grievance,"duplicate report is idempotent");
            Check.Equal(null,CommunityDiplomacySystem.Declaration(a),"theft alone cannot declare");
        });
        ScenarioRunner.Add("npc/community-diplomacy-assaults-and-priorities",()=>CommunityMembershipScenario.Arena(4976),w=>{
            Actor player=NpcIntentSupport.Player(w,5,3),a=Group(w,"Oak",2),b=Group(w,"River",7);
            Introduce(w,a,b); Actor victim=a.Followers.First(),attacker=b.Followers.First();
            // Fixture health supports independent engagements, not a fatal event.
            victim.HitPoints=1000;
            CommunityDiplomacyScenario.Injury(w,attacker,victim,0);
            CommunityDiplomacyScenario.Injury(w,attacker,victim,31);
            Check.Equal(50,Relation(a,b).Grievance,"separate attacks count independently");
            Check.Equal(null,CommunityDiplomacySystem.Declaration(a),"neutral leader does not reach sixty from two assaults");
            a.Personality.AddTrait(new TraitInstance("loyal")); a.Personality.AddTrait(new TraitInstance("brave"));
            Check.Equal(true,CommunityDiplomacySystem.Declaration(a)!=null,"group/courage bias can qualify the same evidence");
            a.IsSleeping=true; Check.Equal(null,CommunityDiplomacySystem.Select(w.Game,a,new Actor[0]),"sleeping leader does not declare"); a.IsSleeping=false;
            a.FoodPoints=100; Check.Equal(null,CommunityDiplomacySystem.Select(w.Game,a,new Actor[0]),"hunger outranks diplomatic actions"); a.FoodPoints=w.Game.Rules.ActorMaxFood(a);
            ((OrderableAI)a.Controller).SetOrder(new ActorOrder(ActorTasks.GUARD,a.Location));
            Check.Equal(null,CommunityDiplomacySystem.Select(w.Game,a,new Actor[0]),"active orders are preserved"); ((OrderableAI)a.Controller).SetOrder(null);
            w.Game.DoMakeAggression(attacker,a);
            Check.Equal(null,CommunityDiplomacySystem.Select(w.Game,a,new[]{attacker}),"visible combat danger outranks declaration");
            w.Map.PlaceActorAt(attacker,new Point(20,6)); CommunityDiplomacyScenario.Declare(w,a);
        });
        ScenarioRunner.Add("npc/community-diplomacy-self-defense",()=>CommunityMembershipScenario.Arena(4977),w=>{
            Actor player=NpcIntentSupport.Player(w,5,3),a=Group(w,"Oak",2),b=Group(w,"River",7);
            Introduce(w,a,b); Actor victim=a.Followers.First(),defender=b.Followers.First();
            w.Map.PlaceActorAt(defender,new Point(1,3));
            CommunityDiplomacyScenario.Injury(w,victim,defender,0);
            Check.Equal(false,w.Game.Rules.IsMurder(defender,victim),"ordinary self-defense rule is preserved");
            CommunityDiplomacyScenario.Kill(w,defender,victim);
            Check.Equal(15,Relation(a,b).Grievance,"witnessed self-defense killing has reduced weight");
            Check.Equal(null,CommunityDiplomacySystem.Declaration(a),"one justified killing does not reach threshold");
        });
        ScenarioRunner.Add("npc/community-diplomacy-succession-and-peace",()=>CommunityMembershipScenario.Arena(4978),w=>{
            Actor player=NpcIntentSupport.Player(w,5,3),a=Group(w,"Oak",2),b=Group(w,"River",7);
            Actor successor=NpcIntentSupport.Actor(w,"Next Oak leader",3,2,"loyal","kind"); a.AddFollower(successor);
            Introduce(w,a,b); CommunityDiplomacyScenario.Kill(w,b.Followers.First(),a.Followers.First());
            w.Map.PlaceActorAt(b.Followers.First(),new Point(20,6)); CommunityDiplomacyScenario.Declare(w,a);
            CommunityMembershipScenario.Time(w,180); CommunityDiplomacyScenario.TalkKey(w,player,a,Keys.D4,Keys.D2,Keys.Y);
            var offer=player.Personality.PeaceOffers.Single(); CommunityDiplomacyScenario.TalkKey(w,player,b,Keys.D4,Keys.D3);
            var group=a.SocialGroup; var founding=Relation(a,b).FoundingCause;
            w.Map.PlaceActorAt(player,new Point(22,7)); CommunityMembershipScenario.Fov(w);
            string dated=String.Join("\n",(System.Collections.Generic.IList<string>)Check.Call(w.Game,"CommunityJournalLines",new[]{typeof(Actor)},player));
            w.Game.KillActor(null,a,"ordinary environmental death",false); a=successor;
            Check.Same(group,a.SocialGroup,"stable identity and diplomatic record survive original leader death");
            Check.Same(founding,Relation(a,b).FoundingCause,"founding cause survives original participants' deaths");
            Check.Equal(true,Relation(a,b).Feud,"succession does not erase feud");
            Check.Equal(dated,String.Join("\n",(System.Collections.Generic.IList<string>)Check.Call(w.Game,"CommunityJournalLines",new[]{typeof(Actor)},player)),"unwitnessed succession does not refresh J");
            Check.Equal(false,offer.Ratified,"successor must personally ratify outstanding terms");
            w.Game.DoMakeAggression(a,b);
            CommunityDiplomacyScenario.TalkKey(w,player,a,Keys.D4,Keys.D4);
            Check.Equal(true,offer.Ratified,"actual successor conversation ratifies");
            Check.Equal(false,Relation(a,b).Feud,"feud ends");
            Check.Equal(true,w.Game.Rules.AreEnemies(a,b,false),"ratification preserves individual revenge");
            a.HitPoints=1000; // Unit fixture permits two independent nonfatal engagements.
            CommunityMembershipScenario.Time(w,400);
            CommunityDiplomacyScenario.Injury(w,b.Followers.First(),a,400);
            CommunityDiplomacyScenario.Injury(w,b.Followers.First(),a,431);
            w.Map.PlaceActorAt(b.Followers.First(),new Point(20,6));
            Check.Equal(true,CommunityDiplomacySystem.Declaration(a)!=null,"fresh known violence can reopen feud with preserved grievance");
        });
        ScenarioRunner.Add("npc/community-diplomacy-peace-refusal-expiry-cancel",()=>CommunityMembershipScenario.Arena(4979),w=>{
            Actor player=NpcIntentSupport.Player(w,5,3),a=Group(w,"Oak",2),b=Group(w,"River",7);
            Introduce(w,a,b); CommunityDiplomacyScenario.Kill(w,b.Followers.First(),a.Followers.First());
            w.Map.PlaceActorAt(b.Followers.First(),new Point(20,6)); CommunityDiplomacyScenario.Declare(w,a);
            // Actual cancelled V menu is free and produces no packet.
            w.Map.PlaceActorAt(player,new Point(2,3)); CommunityMembershipScenario.Fov(w);
            var ui=(ScenarioUI)w.Game.UI; ui.QueueWaitKey(Keys.D4); ui.QueueWaitKey(Keys.Escape);
            player.ActionPoints=100; Check.Equal(false,Check.Call(w.Game,"HandlePlayerTalk",player),"menu cancellation is free"); Check.Equal(100,player.ActionPoints,"cancelled menu costs no time");
            string reason; Check.Equal(null,CommunityDiplomacySystem.Authorize(w.Game,player,a,Relation(a,b),out reason),"quiet period required");
            CommunityMembershipScenario.Time(w,180); CommunityDiplomacyScenario.TalkKey(w,player,a,Keys.D4,Keys.D2,Keys.Y);
            var offer=player.Personality.PeaceOffers.Single();
            b.SocialGroup.Diplomacy.Get(PoliticalIdentity.Of(a.SocialGroup)).Grievance=100;
            CommunityDiplomacyScenario.TalkKey(w,player,b,Keys.D4,Keys.D3);
            Check.Equal(false,offer.Accepted,"recipient actually refuses unacceptable grievance");
            CommunityDiplomacyScenario.TalkKey(w,player,a,Keys.D4,Keys.D4); Check.Equal(false,offer.Ratified,"refusal cannot ratify");
            CommunityDiplomacyScenario.TalkKey(w,player,a,Keys.D4,Keys.D2,Keys.Y); offer=player.Personality.PeaceOffers.Last();
            CommunityMembershipScenario.Time(w,offer.ExpiresTurn+1);
            w.Map.PlaceActorAt(player,new Point(7,3));
            Check.Equal(false,CommunityDiplomacySystem.Deliver(w.Game,player,b,offer,out reason),"expired offer cannot be delivered");
            Check.Equal(true,reason.Contains("expired"),"expiration has an explicit explanation");
        });
        ScenarioRunner.Add("npc/community-diplomacy-bounds-and-unseen",()=>CommunityMembershipScenario.Arena(4980),w=>{
            Actor player=NpcIntentSupport.Player(w,3,3),a=Group(w,"Oak",2),b=Group(w,"River",7);
            Introduce(w,a,b); CommunityDiplomacyScenario.Kill(w,b.Followers.First(),a.Followers.First());
            w.Map.PlaceActorAt(b.Followers.First(),new Point(20,6)); CommunityDiplomacyScenario.Declare(w,a);
            var relation=Relation(a,b); var old=a.Personality.Knowledge.Facts.Last(f=>f.Kind=="community_killing");
            w.Map.PlaceActorAt(b,new Point(3,2)); Introduce(w,b);
            for(int i=0;i<20;i++) { CommunityMembershipScenario.Time(w,40+i); NpcEvents.Publish(w.Game,"request_refused",b,a); }
            Check.Equal(16,relation.Incidents.Count,"history stays bounded"); Check.Equal(true,relation.FoundingCause!=null,"active founding cause remains pinned");
            int grievance=relation.Grievance; CommunityDiplomacySystem.Hear(a,a,old);
            Check.Equal(grievance,relation.Grievance,"trimmed incident cannot be charged a second time");
            Actor unknown=NpcIntentSupport.Actor(w,"Unseen listener",2,4); unknown.Personality=new PersonalityState(); player.Personality=new PersonalityState();
            // Player's actual visibility is authoritative, independent of NPC geometric sight.
            var view=(System.Collections.Generic.HashSet<Point>)typeof(RogueGame).GetField("m_PlayerFOV",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(w.Game); view.Clear();
            w.Map.SetViewAndMarkVisited(view);
            CommunityDiplomacySystem.Publish(w.Game,a,CommunityDiplomacySystem.Message(a.SocialGroup,relation,60),"community_feud");
            Check.Equal(true,player.Personality.KnownDiplomacy.Count>0,"audible unseen declaration teaches only named groups");
            Check.Equal(0,player.Personality.Communities.Sum(c=>c.Members.Count),"unseen political speech reveals no roster or affiliation");
            Check.Equal(false,player.Personality.KnownDiplomacy.Any(d=>d.Source.Contains(a.UnmodifiedName)),"unseen source name is not leaked");
            Actor uninformed=b.Followers.First(); uninformed.Personality=new PersonalityState();
            Check.Equal(false,w.Game.Rules.IsKnownEnemy(uninformed,a),"uninformed rival does not act on hidden declaration");
            Check.Equal(false,w.Game.Rules.IsKnownEnemy(player,b),"player cannot recognize an unintroduced rival from hidden affiliation");
        });
    }
}
