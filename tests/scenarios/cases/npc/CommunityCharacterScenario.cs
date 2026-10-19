using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using djack.RogueSurvivor.Data;
using djack.RogueSurvivor.Engine;
using djack.RogueSurvivor.Engine.Actions;
using djack.RogueSurvivor.Engine.Items;
using djack.RogueSurvivor.Gameplay;
using djack.RogueSurvivor.Gameplay.Personality;

static class CommunityCharacterScenario
{
    static Actor chief, member;
    static SocialGroup Setup(ScenarioWorld w,int territory=20,int force=20,int grudge=20)
    {
        var group=CommunityAdaptationScenario.Found(w,out chief,out member);
        group.Culture.Territoriality=territory;group.Culture.Enforcement=force;group.Culture.Grudge=grudge;
        CommunityCultureSystem.SayPolicy(w.Game,chief,null,true);
        w.Map.PlaceActorAt(w.Game.Player,new Point(7,6));CommunityMembershipScenario.Fov(w);return group;
    }
    static void Npc(ScenarioWorld w,Actor a)
    { a.ActionPoints=Rules.BASE_ACTION_COST;Check.Equal(true,w.NpcTurn(a),"production NPC action is legal"); }
    static CommunityCase Case(Actor observer,Actor offender)
    { return observer.Personality.CaseReports.Last(r=>r.Community==observer.SocialGroup.Identity&&r.Case.Person==offender.PersonalityIdentity).Case; }
    static ItemFood Steal(ScenarioWorld w,int quantity=1)
    {
        var food=new ItemFood(w.Game.GameItems.CANNED_FOOD) { Quantity=quantity };
        w.Map.DropItemAt(food,w.Game.Player.Location.Position);w.Game.Player.ActionPoints=Rules.BASE_ACTION_COST;
        Check.Equal(true,w.Try(new ActionTakeItem(w.Game.Player,w.Game,w.Game.Player.Location.Position,food)),"actual claimed pickup");return food;
    }
    static void GiveKey(ScenarioWorld w,Item item,Actor recipient)
    {
        var p=w.Game.Player;var destination=new Point(recipient.Location.Position.X,recipient.Location.Position.Y+1);
        if(p.Location.Position!=destination)w.Map.PlaceActorAt(p,destination);
        CommunityMembershipScenario.Fov(w);int slot=0;while(p.Inventory[slot]!=item)slot++;
        string reason;Check.Equal(true,w.Game.Rules.CanActorGiveItemTo(p,recipient,item,out reason),"G precondition: "+reason);
        ((ScenarioUI)w.Game.UI).QueueKey(CommunityClarityScenario.DirectionKey(p.Location.Position,recipient.Location.Position));
        p.ActionPoints=Rules.BASE_ACTION_COST;Check.Equal(true,Check.Call(w.Game,"HandlePlayerGiveItem",p,CommunityClarityScenario.InventorySlot(w,slot)),"actual G input");
        Check.Equal(0,p.ActionPoints,"G costs one player action");
    }
    static void TalkKey(ScenarioWorld w,params Keys[] keys)
    {
        var p=w.Game.Player;w.Map.PlaceActorAt(p,new Point(chief.Location.Position.X,chief.Location.Position.Y+1));CommunityMembershipScenario.Fov(w);
        var ui=(ScenarioUI)w.Game.UI;
        int neighbors=w.Map.Actors.Count(a=>a!=p&&!a.IsDead&&!a.IsSleeping&&w.Game.Rules.GridDistance(p.Location.Position,a.Location.Position)<=1);
        if(neighbors>1)ui.QueueKey(CommunityClarityScenario.DirectionKey(p.Location.Position,chief.Location.Position));
        foreach(var key in keys)ui.QueueWaitKey(key);p.ActionPoints=Rules.BASE_ACTION_COST;
        Check.Equal(true,Check.Call(w.Game,"HandlePlayerTalk",p),"actual V input");Check.Equal(0,p.ActionPoints,"V charged once");
    }
    public static void Register()
    {
        ScenarioRunner.Add("npc/community-character-founders",()=>CommunityAdaptationScenario.Arena(5300),w=>{
            var group=CommunityAdaptationScenario.Found(w,out chief,out member);var p=CommunityCultureSystem.Personal(chief);var q=CommunityCultureSystem.Personal(member);
            Check.Equal((p.Territoriality+q.Territoriality)/2,group.Culture.Territoriality,"founder average");
            Check.Equal(true,group.Culture.Territoriality<50,"sociable kind founders are more open");
            var harsh=NpcIntentSupport.Actor(w,"Severe",20,4,"cruel","vindictive","homebody");var mild=NpcIntentSupport.Actor(w,"Tolerant",21,4,"kind","forgiving","peacemaker");
            var h=CommunityCultureSystem.Personal(harsh);var m=CommunityCultureSystem.Personal(mild);
            Check.Equal(true,h.Territoriality>m.Territoriality&&h.Enforcement>m.Enforcement&&h.Grudge>m.Grudge,"existing traits produce contrasting tendencies");
            Check.Equal(0,harsh.Sheet.SkillTable.GetSkillLevel((int)Skills.IDs.LEADERSHIP),"no skills granted");
            Check.Equal(3,harsh.Personality.Traits.Count,"culture does not change actor traits");
        });
        ScenarioRunner.Add("npc/community-character-tolerant-giving",()=>CommunityAdaptationScenario.Arena(5301),w=>{
            Setup(w);var p=w.Game.Player;var food=Steal(w);
            Check.Equal(false,w.Game.Rules.AreEnemies(chief,p),"first tolerant theft does not create aggression");
            Npc(w,chief);Check.Equal(true,Case(chief,p).DemandClock>=0,"NPC actually demands restitution");
            Check.Equal(true,NpcConversation.Pending(p,chief)!=null,"demand provides the real outstanding Y/N input");
            TalkKey(w,Keys.Y);Check.Equal(true,Case(chief,p).HasLoss,"a promise does not repay");
            chief.FoodPoints=100;GiveKey(w,food,chief);Check.Equal(true,Case(chief,p).Resolved,"actual G replaces stolen units");
            Check.Equal(false,chief.Personality.Knowledge.Facts.Any(f=>f.Kind=="helped"&&f.OtherId==p.PersonalityIdentity),"replacement is not rewarded as useful aid");
            Check.Equal(false,w.Game.Rules.AreEnemies(chief,p),"amends remain peaceful");
            var ui=(ScenarioUI)w.Game.UI;ui.QueueWaitKey(Keys.Tab);ui.QueueWaitKey(Keys.Escape);Check.Call(w.Game,"HandleHeardJournal");
            Check.Equal(true,p.Personality.FindCommunity(chief.SocialGroup.Identity).Notes.Any(n=>n.Text.Contains("recorded supplies")),"dated journal records actual repayment");
        });
        ScenarioRunner.Add("npc/community-character-partial-and-unusable",()=>CommunityAdaptationScenario.Arena(5302),w=>{
            Setup(w);var p=w.Game.Player;Steal(w,3);Npc(w,chief);
            var stolen=p.Inventory.Items.OfType<ItemFood>().First();p.Inventory.Consume(stolen);p.Inventory.Consume(stolen);
            GiveKey(w,stolen,chief);Check.Equal(2,Case(chief,p).Losses.Single().Remaining,"only the received unit is repaid");
            var rotten=new ItemFood(w.Game.GameItems.CANNED_FOOD,0);p.Inventory.AddAll(rotten);
            Check.Equal(null,CommunityCultureSystem.Repayment(p,chief,rotten,w.Game),"spoiled substitutes do not qualify");
            var weapon=new ItemMeleeWeapon(w.Game.GameItems.BASEBALLBAT);p.Inventory.AddAll(weapon);
            Check.Equal(null,CommunityCultureSystem.Repayment(p,chief,weapon,w.Game),"unrelated items do not silently settle food debt");
        });
        ScenarioRunner.Add("npc/community-character-territory-leave",()=>CommunityAdaptationScenario.Arena(5303),w=>{
            Setup(w,80,80,50);var p=w.Game.Player;Npc(w,chief);Check.Equal(true,Case(chief,p).WarnedClock>=0,"real NPC turn warns");
            for(int i=1;i<=31;i++){CommunityAdaptationScenario.Time(w,360+i);Npc(w,chief);}
            Check.Equal(true,Case(chief,p).Defied,"30 observed awake turns qualify defiance");
            Check.Equal(true,w.Game.Rules.IsKnownEnemy(chief,p),"local enforcement recognizes the current intruder");
            var path=CommunityClarityScenario.TryWalkingPath(w,new Point(14,6),0);Check.Equal(true,path!=null,"legal exit route exists");
            int steps=0;while(p.Location.Position!=new Point(14,6)&&steps++<40)
            { path=CommunityClarityScenario.TryWalkingPath(w,new Point(14,6),0);Point next=path[0];p.ActionPoints=Rules.BASE_ACTION_COST;
                Check.Equal(true,w.Bump(p,Direction.FromVector(next.X-p.Location.Position.X,next.Y-p.Location.Position.Y)),"leaves through ordinary door movement"); }
            Check.Equal(new Point(14,6),p.Location.Position,"completed ordinary departure");
            Check.Equal(false,w.Game.Rules.IsKnownEnemy(chief,p),"leaving ends local pursuit");
            Check.Equal(false,w.Game.Rules.ArePersonalEnemies(chief,p),"warning enforcement creates no permanent personal aggression");
            Check.Equal(true,chief.Personality.Knowledge.Facts.Any(f=>f.Kind=="community_trespass_defied"),"offence remains recorded");
        });
        ScenarioRunner.Add("npc/community-character-blocked-and-sleeping",()=>CommunityAdaptationScenario.Arena(5304),w=>{
            Setup(w,80);var p=w.Game.Player;Npc(w,chief);w.SetTile(10,6,'#');
            for(int n=1;n<=40;n++){CommunityAdaptationScenario.Time(w,360+n);CommunityCultureSystem.Observe(w.Game,chief,new[]{p});}
            Check.Equal(0,Case(chief,p).ObservedGrace,"blocked legal route pauses grace");w.SetTile(10,6,'.');p.IsSleeping=true;
            for(int n=41;n<=80;n++){CommunityAdaptationScenario.Time(w,360+n);CommunityCultureSystem.Observe(w.Game,chief,new[]{p});}
            Check.Equal(0,Case(chief,p).ObservedGrace,"sleeping target does not spend awake grace");p.IsSleeping=false;
            for(int n=81;n<=111;n++){CommunityAdaptationScenario.Time(w,360+n);Npc(w,chief);}
            Check.Equal(true,Case(chief,p).Defied,"reopened route and awake observations resume normal enforcement");
        });
        ScenarioRunner.Add("npc/community-character-permission-household",()=>CommunityAdaptationScenario.Arena(5305),w=>{
            var group=Setup(w,50);var p=w.Game.Player;var follower=NpcIntentSupport.Actor(w,"Personal companion",6,7);p.AddFollower(follower);
            TalkKey(w,Keys.D1);var permitted=Case(chief,p);Check.Equal(540,permitted.PermissionUntil,"actual V grants 180 map-local turns");
            Check.Equal(true,permitted.Guests.Contains(follower.PersonalityIdentity),"accompanying household is named in permission");
            CommunityCultureSystem.Observe(w.Game,chief,new[]{p,follower});Check.Equal(false,CommunityCultureSystem.Enforcing(chief,follower),"permission covers follower");
            Check.Equal(false,w.Map.XpdBaseAt(follower.Location.Position).Owns(p),"visitor is not an owner");
            Check.Same(p,follower.Leader,"permission preserves command link");
        });
        ScenarioRunner.Add("npc/community-character-harsh-minor-feud",()=>CommunityAdaptationScenario.Arena(5306),w=>{
            Setup(w,20,80,100);var p=w.Game.Player;p.EnsurePersonalGroup();CommunityDiplomacySystem.Introduce(w.Game,p);
            Steal(w);Check.Equal(true,w.Game.Rules.IsKnownEnemy(chief,p),"forceful witnessed theft triggers local defense");
            w.Map.PlaceActorAt(p,new Point(14,6));CommunityMembershipScenario.Fov(w);
            Check.Equal(true,CommunityDiplomacySystem.Declaration(chief)!=null,"one attributed theft qualifies an unforgiving community");
            CommunityDiplomacyScenario.Declare(w,chief);
            var r=chief.SocialGroup.Diplomacy.Relations.Single();Check.Equal("base_theft",r.FoundingCause.Kind,"feud retains its nonviolent founding cause");
            Check.Equal(false,r.FoundingCause.Violence,"declaration does not invent bloodshed");
        });
        ScenarioRunner.Add("npc/community-character-repeated-minor-feud",()=>CommunityAdaptationScenario.Arena(5307),w=>{
            Setup(w,20,20,50);var p=w.Game.Player;p.EnsurePersonalGroup();CommunityDiplomacySystem.Introduce(w.Game,p);
            Steal(w);Steal(w);var relation=chief.SocialGroup.Diplomacy.Relations.Single();Check.Equal(1,relation.Incidents.Count,"stack operations are one episode");
            Check.Equal(null,CommunityDiplomacySystem.Declaration(chief),"one episode cannot qualify a middle community");
            for(int n=1;n<=2;n++){CommunityAdaptationScenario.Time(w,360+31*n);Steal(w);}
            Check.Equal(3,relation.Incidents.Count,"separate encounters count separately");Check.Equal(true,CommunityDiplomacySystem.Declaration(chief)!=null,"repeated property offences can qualify without injury");
            var fact=chief.Personality.Knowledge.Facts.Last(f=>f.Kind=="base_theft");int grievance=relation.Grievance;
            NpcKnowledgeSystem.Hear(w.Game,chief,member,fact);NpcKnowledgeSystem.Hear(w.Game,chief,member,fact);Check.Equal(grievance,relation.Grievance,"reports do not multiply offences");
        });
        ScenarioRunner.Add("npc/community-character-uninformed-attribution",()=>CommunityAdaptationScenario.Arena(5308),w=>{
            Setup(w);var p=w.Game.Player;chief.IsSleeping=true;Steal(w);Check.Equal(null,chief.SocialGroup.Diplomacy,"sleeping chief receives no automatic political blame");
            chief.IsSleeping=false;var fact=member.Personality.Knowledge.Facts.Last(f=>f.Kind=="base_theft");
            NpcKnowledgeSystem.Hear(w.Game,chief,member,fact);Check.Equal(true,chief.Personality.CaseReports.Any(r=>r.Case.Person==p.PersonalityIdentity),"attributed report teaches loss");
            p.EnsurePersonalGroup();CommunityDiplomacySystem.Introduce(w.Game,p);
            Check.Equal(null,CommunityDiplomacySystem.Declaration(chief),"later affiliation does not retroactively blame a group");
        });
        ScenarioRunner.Add("npc/community-character-save-and-stale-policy",()=>CommunityAdaptationScenario.Arena(5309),w=>{
            Setup(w,80);var p=w.Game.Player;Npc(w,chief);long episode=Case(chief,p).Episode;int warned=Case(chief,p).WarnedClock;
            string file=Path.Combine(Environment.GetEnvironmentVariable("ROGUE_COMMUNITY_ARTIFACTS"),"character-warning.dat");Guid cid=chief.PersonalityIdentity;
            BinarySaveStore.Save(file,Session.Get);w=NpcIntentSupport.Restore(w,BinarySaveStore.Load<Session>(file));chief=NpcIntentSupport.Find(w.Map,cid);p=w.Game.Player;
            Check.Equal(episode,Case(chief,p).Episode,"warning identity survives format 5");Check.Equal(warned,Case(chief,p).WarnedClock,"local deadline persists");
            var old=p.Personality.FindCommunity(chief.SocialGroup.Identity).Policy.Copy();chief.SocialGroup.Culture.Territoriality=10;chief.SocialGroup.Culture.Revision++;
            Check.Equal(old.Rules,p.Personality.FindCommunity(chief.SocialGroup.Identity).Policy.Rules,"journal does not refresh hidden culture");
        });
        ScenarioRunner.Add("npc/community-character-revision-and-priorities",()=>CommunityAdaptationScenario.Arena(5310),w=>{
            Setup(w);member.IsSleeping=true;long revision=member.Personality.FindCommunity(chief.SocialGroup.Identity).Policy.Revision;
            var donor=w.Game.Player;w.Map.PlaceActorAt(donor,new Point(4,5));chief.FoodPoints=100;var gift=NpcIntentSupport.Food(w,donor,1);w.Game.DoGiveItemTo(donor,chief,gift);chief.FoodPoints=w.Game.Rules.ActorMaxFood(chief);
            int previous=chief.SocialGroup.Culture.Territoriality;CommunityAdaptationScenario.Time(w,360+WorldTime.TURNS_PER_DAY);
            Npc(w,chief);Check.Equal(true,chief.SocialGroup.Culture.Revision>1,"chief revises after known experience and a day");
            Check.Equal(true,Math.Abs(previous-chief.SocialGroup.Culture.Territoriality)<=5,"daily drift is bounded");
            Check.Equal(revision,member.Personality.FindCommunity(chief.SocialGroup.Identity).Policy.Revision,"sleeping resident retains old rules");
            chief.FoodPoints=0;Check.Equal(null,CommunityCultureSystem.Select(w.Game,chief,new[]{donor}),"urgent hunger outranks cultural work");
        });
        ScenarioRunner.Add("npc/community-character-police-culture",()=>CommunityAdaptationScenario.Arena(5311),w=>{
            var a=CommunityDiplomacyScenario.Group(w,"First patrol",20,true);var b=CommunityDiplomacyScenario.Group(w,"Second patrol",25,true);
            a.Personality.AddTrait(new TraitInstance("vindictive"));b.Personality.AddTrait(new TraitInstance("forgiving"));int count=w.Map.Actors.Count();
            CommunityCultureSystem.Initialize(w.Game,a.SocialGroup,a.PartyTree());CommunityCultureSystem.Initialize(w.Game,b.SocialGroup,b.PartyTree());
            Check.Equal(true,a.SocialGroup.Culture.Grudge>b.SocialGroup.Culture.Grudge,"local units have distinct norms");
            Check.Equal(count,w.Map.Actors.Count(),"police culture conserves actors");Check.Equal(null,a.SocialGroup.Home,"police gain no home or restricted station");
            Check.Equal(false,w.Game.Rules.AreEnemies(a,b),"different norms are not starting hostility");
        });
        ScenarioRunner.Add("npc/community-character-refusal-and-expiry",()=>CommunityAdaptationScenario.Arena(5312),w=>{
            Setup(w,20,20,100);var p=w.Game.Player;p.EnsurePersonalGroup();CommunityDiplomacySystem.Introduce(w.Game,p);
            var first=Steal(w);Check.Equal(null,CommunityDiplomacySystem.Declaration(chief),"peaceful first offence has its response opportunity");
            Npc(w,chief);TalkKey(w,Keys.N);var relation=chief.SocialGroup.Diplomacy.Relations.Single();
            Check.Equal(true,Case(chief,p).Refused,"actual V/N refuses debt");Check.Equal(25,relation.Grievance,"refusal adds ten to original loss");
            Check.Equal(1,relation.Incidents.Count,"refusal upgrades the same dispute");
            var fact=chief.Personality.Knowledge.Facts.Last(f=>f.Kind=="community_restitution_refused");
            NpcKnowledgeSystem.Hear(w.Game,chief,member,fact);Check.Equal(25,relation.Grievance,"retelling refusal does not add another penalty");
            GiveKey(w,first,chief);
            w.Map.PlaceActorAt(p,new Point(7,6));CommunityAdaptationScenario.Time(w,391);Steal(w);Npc(w,chief);
            int due=Case(chief,p).DemandClock+180;w.Map.PlaceActorAt(p,new Point(24,8));CommunityAdaptationScenario.Time(w,due);Npc(w,chief);
            Check.Equal(true,Case(chief,p).Expired,"actual chief action expires communicated debt while offender is absent");
            var expiry=chief.Personality.Knowledge.Facts.Last(f=>f.Kind=="community_restitution_failed");
            Check.Equal(true,expiry.NoSubjectLocation,"expiry does not locate an absent offender");
            Check.Equal(p.SocialGroup.Identity,expiry.DiplomacyEvidence.Subject.Group,"expiry retains incident-time attribution");
        });
        ScenarioRunner.Add("npc/community-character-repayment-reload-and-no-farming",()=>CommunityAdaptationScenario.Arena(5313),w=>{
            Setup(w);var p=w.Game.Player;p.EnsurePersonalGroup();CommunityDiplomacySystem.Introduce(w.Game,p);
            var food=Steal(w,3);Npc(w,chief);long episode=Case(chief,p).Episode;Guid cid=chief.PersonalityIdentity;
            p.Inventory.Consume(food);p.Inventory.Consume(food);GiveKey(w,food,chief);
            string file=Path.Combine(Environment.GetEnvironmentVariable("ROGUE_COMMUNITY_ARTIFACTS"),"character-partial-debt.dat");
            BinarySaveStore.Save(file,Session.Get);w=NpcIntentSupport.Restore(w,BinarySaveStore.Load<Session>(file));chief=NpcIntentSupport.Find(w.Map,cid);p=w.Game.Player;
            Check.Equal(2,Case(chief,p).Losses.Single().Remaining,"partial replacement survives reload");Check.Equal(episode,Case(chief,p).Episode,"same dispute survives reload");
            var replacement=new ItemFood(w.Game.GameItems.CANNED_FOOD) { Quantity=2 };p.Inventory.AddAll(replacement);GiveKey(w,replacement,chief);
            var relation=chief.SocialGroup.Diplomacy.Relations.Single();Check.Equal(0,relation.Trust,"actual repayment restores only original trust penalty");
            relation.Trust=-100;w.Map.PlaceActorAt(p,new Point(7,6));CommunityAdaptationScenario.Time(w,400);
            food=Steal(w);GiveKey(w,food,chief);Check.Equal(-100,relation.Trust,"clamped distrust cannot be farmed through theft and return");
            var stale=chief.Personality.Knowledge.Facts.Last(f=>f.Kind=="base_theft");
            NpcKnowledgeSystem.Hear(w.Game,chief,chief,stale);Check.Equal(true,chief.Personality.CaseReports.Last().Case.Resolved,"same-date old report cannot resurrect a repaid debt");
            Check.Equal(false,CommunityDiplomacySystem.Declaration(chief)!=null,"fully repaired offences cannot qualify a new declaration");
        });
        ScenarioRunner.Add("npc/community-character-defense-repair",()=>CommunityAdaptationScenario.Arena(5314),w=>{
            Setup(w);var p=w.Game.Player;Point position=new Point(7,5);var claim=w.Map.XpdBaseAt(position);
            var trap=new ItemTrap(w.Game.GameItems.BARBED_WIRE);trap.Activate(chief);trap.SetBaseOwner(claim);w.Map.DropItemAt(trap,position);
            p.ActionPoints=100;Check.Equal(true,w.Game.DoDisarmTrap(p,position,trap),"production disarm records witnessed defense tampering");
            Check.Equal(false,w.Game.Rules.AreEnemies(chief,p),"tolerant owner first permits actual repair");Npc(w,chief);
            p.ActionPoints=100;Check.Equal(true,w.Try(new ActionTakeItem(p,w.Game,position,trap)),"take disabled defense for repair");
            p.ActionPoints=100;w.Map.PlaceActorAt(p,position);w.Game.DoUseItem(p,trap);p.ActionPoints=100;w.Game.DoDropItem(p,trap);
            Check.Equal(true,chief.Personality.CaseReports.Any(r=>r.Case.Kind=="defense"&&r.Case.Resolved),"actual activation and drop at original position settle defense loss");
            Check.Same(claim,((ItemTrap)w.Map.GetItemsAt(position).TopItem).BaseOwner,"actual replacement binds existing claim");
        });
        ScenarioRunner.Add("npc/community-character-unwitnessed-and-bounds",()=>CommunityAdaptationScenario.Arena(5315),w=>{
            var group=Setup(w);chief.IsSleeping=member.IsSleeping=true;Steal(w);Check.Equal(0,group.Culture.Cases.Count,"unwitnessed theft invents no case or witness");
            chief.IsSleeping=member.IsSleeping=false;
            for(int n=0;n<40;n++) { CommunityAdaptationScenario.Time(w,400+n*31);Steal(w);
                w.Game.Player.Inventory.Consume(w.Game.Player.Inventory.Items.OfType<ItemFood>().First()); }
            Check.Equal(32,group.Culture.Cases.Count,"community active records are bounded");Check.Equal(32,chief.Personality.CaseReports.Count,"resident knowledge is bounded");
        });
        ScenarioRunner.Add("npc/community-character-minority-and-succession",()=>CommunityAdaptationScenario.Arena(5316),w=>{
            var group=Setup(w,80,80,100);member.Personality.AddTrait(new TraitInstance("peacemaker"));
            w.Map.PlaceActorAt(w.Game.Player,new Point(14,6));
            for(int n=0;n<8&&!group.Culture.Views.Any(v=>v.Person==member.PersonalityIdentity);n++)Npc(w,member);
            Check.Equal(true,group.Culture.Views.Any(v=>v.Person==member.PersonalityIdentity),"resident actually communicates independent view");
            var p=w.Game.Player;p.EnsurePersonalGroup();w.Map.PlaceActorAt(p,new Point(7,6));CommunityDiplomacySystem.Introduce(w.Game,p);Steal(w);w.Map.PlaceActorAt(p,new Point(14,6));
            CommunityDiplomacyScenario.Declare(w,chief);long revision=group.Culture.Revision;Guid identity=group.Identity;
            CommunityDiplomacyScenario.Kill(w,p,chief);
            Check.Same(member,member.CommunityChief,"independent resident succeeds chief");Check.Equal(identity,member.Community.Identity,"stable community identity");
            Check.Equal(revision,group.Culture.Revision,"succession does not replace founding culture");Check.Equal(true,group.Diplomacy.Relations.Single().Feud,"property feud survives succession");
        });
        ScenarioRunner.Add("npc/community-character-property-peace-and-renewal",()=>CommunityAdaptationScenario.Arena(5317),w=>{
            var group=Setup(w,20,80,100);var p=w.Game.Player;w.Map.PlaceActorAt(p,new Point(14,5));
            var rival=CommunityDiplomacyScenario.Group(w,"River neighbours",24);w.Map.PlaceActorAt(rival,new Point(8,5));
            CommunityDiplomacyScenario.Introduce(w,chief,rival);w.Map.PlaceActorAt(rival,new Point(7,6));
            var food=new ItemFood(w.Game.GameItems.CANNED_FOOD);w.Map.DropItemAt(food,rival.Location.Position);
            rival.ActionPoints=100;Check.Equal(true,w.Try(new ActionTakeItem(rival,w.Game,rival.Location.Position,food)),"rival actually takes claimed stock");
            w.Map.PlaceActorAt(rival,new Point(12,6));CommunityDiplomacyScenario.Declare(w,chief);
            Check.Equal(true,p.Personality.FindCommunity(group.Identity).Notes.Any(n=>n.Text.Contains(rival.UnmodifiedName+": Stop")),"overheard demands identify their actual addressee");
            var relation=CommunityDiplomacyScenario.Relation(chief,rival);Check.Equal(false,relation.FoundingCause.Violence,"property founding cause survives declaration");
            w.Map.PlaceActorAt(rival,new Point(4,5));string reason;
            Check.Equal(true,w.Game.Rules.CanActorGiveItemTo(rival,chief,food,out reason),"real restitution possible before personal violence");
            rival.ActionPoints=100;w.Game.DoGiveItemTo(rival,chief,food);Check.Equal(true,relation.Feud,"replacement cannot end a declared feud");
            w.Map.PlaceActorAt(rival,new Point(25,3));CommunityAdaptationScenario.Time(w,600);
            CommunityDiplomacyScenario.TalkKey(w,p,chief,Keys.D4,Keys.D2,Keys.Y);
            var offer=p.Personality.PeaceOffers.Single();CommunityDiplomacyScenario.TalkKey(w,p,rival,Keys.D4,Keys.D3);
            Check.Equal(true,offer.Accepted,"neutral courier delivers property peace terms");
            CommunityDiplomacyScenario.TalkKey(w,p,chief,Keys.D4,Keys.D4);Check.Equal(true,offer.Ratified,"real V ratifies the returned response");
            Check.Equal(false,relation.Feud,"peace ends the collective property feud");Check.Equal(null,CommunityDiplomacySystem.Declaration(chief),"old property incident cannot redeclare after peace");
            w.Map.PlaceActorAt(p,new Point(14,5));w.Map.PlaceActorAt(rival,new Point(7,6));CommunityAdaptationScenario.Time(w,631);
            food=new ItemFood(w.Game.GameItems.CANNED_FOOD);w.Map.DropItemAt(food,rival.Location.Position);rival.ActionPoints=100;
            Check.Equal(true,w.Try(new ActionTakeItem(rival,w.Game,rival.Location.Position,food)),"fresh post-peace violation uses production pickup");
            Check.Equal(true,CommunityDiplomacySystem.Declaration(chief)!=null,"new unresolved property offence can provoke a new declaration");
        });
        ScenarioRunner.Add("npc/community-character-missed-theft-and-local-clock",()=>CommunityAdaptationScenario.Arena(5318),w=>{
            Setup(w,50);var p=w.Game.Player;chief.IsSleeping=true;Steal(w);Npc(w,member);chief.IsSleeping=false;
            chief.Personality.CaseReports.Clear();chief.Personality.CommunityCases.Clear();
            CommunityCultureSystem.Observe(w.Game,chief,new[]{p});Check.Equal(false,Case(chief,p).HasLoss,"seeing a visitor cannot reveal a missed theft from shared state");
            var fact=member.Personality.Knowledge.Facts.Last(f=>f.Kind=="base_theft");NpcKnowledgeSystem.Hear(w.Game,chief,member,fact);
            Check.Equal(true,chief.Personality.CaseReports.Any(r=>r.Case.HasLoss),"an actual witness report teaches the property loss");
            Session.Get.WorldTime.TurnCounter=9000;CommunityCultureSystem.Talk(w.Game,chief,p);
            Check.Equal(true,NpcConversation.Pending(p,chief)!=null,"Y/N demand uses the map clock rather than distant world date");
            Check.Equal(360,chief.Personality.CaseReports.Last(r=>r.Case.HasLoss).Case.DemandClock,"response deadline begins on the actual local clock");
            w.Map.LocalTime.TurnCounter=540;Session.Get.WorldTime.TurnCounter=9180;CommunityMembershipScenario.Fov(w);Npc(w,chief);
            Check.Equal(true,chief.Personality.CaseReports.Any(r=>r.Case.HasLoss&&r.Case.Expired),"correct local deadline expires despite unrelated world date");
        });
        ScenarioRunner.Add("npc/community-character-boundary-self-defense",()=>CommunityAdaptationScenario.Arena(5319),w=>{
            Setup(w,80,80,20);var p=w.Game.Player;p.EnsurePersonalGroup();CommunityDiplomacySystem.Introduce(w.Game,p);Npc(w,chief);
            for(int n=1;n<=30;n++){CommunityAdaptationScenario.Time(w,360+n);Npc(w,chief);}
            Check.Equal(true,Case(chief,p).Defied,"actual heard warning expires before enforcement");
            w.Map.PlaceActorAt(chief,new Point(7,5));chief.ActionPoints=100;Check.Equal(true,w.Try(new ActionMeleeAttack(chief,w.Game,p)),"actual resident boundary attack");
            Check.Equal(true,w.Game.Rules.IsMurder(chief,p),"ownership cannot legalize killing a peaceful intruder");
            Check.Equal(false,w.Game.Rules.IsMurder(p,chief),"actual attack permits existing self-defense");
            CommunityDiplomacyScenario.Kill(w,p,chief);
            var fact=p.Personality.Knowledge.Facts.Last(f=>f.Kind=="community_killing");Check.Equal(15,fact.DiplomacyEvidence.Severity,"boundary self-defense retains the existing fifteen-point weight");
            Check.Equal(0,p.MurdersCounter,"actual self-defense does not invent a murder conviction");
        });
    }
}
