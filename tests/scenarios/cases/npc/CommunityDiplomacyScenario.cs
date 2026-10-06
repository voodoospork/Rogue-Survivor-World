using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using djack.RogueSurvivor.Data;
using djack.RogueSurvivor.Engine;
using djack.RogueSurvivor.Engine.Actions;
using djack.RogueSurvivor.Gameplay;
using djack.RogueSurvivor.Gameplay.Personality;

static class CommunityDiplomacyScenario
{
    internal static Actor Group(ScenarioWorld w, string name, int x, bool police = false)
    {
        Actor a = NpcIntentSupport.Actor(w, name, x, 2), b = NpcIntentSupport.Actor(w, name + " member", x - 1, 2);
        if (police) { a.Faction = w.Game.GameFactions.ThePolice; b.Faction = w.Game.GameFactions.ThePolice; }
        a.Sheet.SkillTable.AddOrIncreaseSkill((int)Skills.IDs.LEADERSHIP); a.AddFollower(b);
        a.SocialGroup.CommunityName = name; a.SocialGroup.Diplomacy = new CommunityDiplomacyState();
        return a;
    }
    internal static void Introduce(ScenarioWorld w, params Actor[] leaders)
    {
        CommunityMembershipScenario.Fov(w);
        foreach (Actor a in leaders) foreach (Actor speaker in a.PartyTree()) CommunityDiplomacySystem.Introduce(w.Game, speaker);
    }
    internal static CommunityRelation Relation(Actor a, Actor b) { return a.SocialGroup.Diplomacy.Find(PoliticalIdentity.Of(b.SocialGroup)); }
    internal static void Injury(ScenarioWorld w, Actor attacker, Actor victim, int turn)
    {
        CommunityMembershipScenario.Time(w, turn);
        Adjacent(w, attacker, victim); int before = victim.HitPoints;
        for (int i = 0; i < 40 && victim.HitPoints == before; i++)
        { attacker.ActionPoints = Rules.BASE_ACTION_COST; Check.Equal(true, w.Try(new ActionMeleeAttack(attacker,w.Game,victim)), "production melee action"); }
        Check.Equal(true, victim.HitPoints < before, "actual melee inflicts an injury");
    }
    internal static void Kill(ScenarioWorld w, Actor attacker, Actor victim)
    {
        Adjacent(w, attacker, victim);
        int attempts = 0;
        while (!victim.IsDead && attempts++ < 500)
        { attacker.ActionPoints = Rules.BASE_ACTION_COST; attacker.StaminaPoints = w.Game.Rules.ActorMaxSTA(attacker); w.Try(new ActionMeleeAttack(attacker,w.Game,victim)); }
        Check.Equal(true, victim.IsDead, "production melee resolves the killing");
    }
    static void Adjacent(ScenarioWorld w, Actor attacker, Actor victim)
    {
        if (w.Game.Rules.GridDistance(attacker.Location.Position, victim.Location.Position) <= 1) return;
        Point point = Direction.COMPASS.Select(d => victim.Location.Position+d).First(p => w.Map.IsInBounds(p) && w.Map.GetActorAt(p)==null);
        w.Map.PlaceActorAt(attacker,point); CommunityMembershipScenario.Fov(w);
    }
    internal static void Declare(ScenarioWorld w, Actor leader)
    {
        ActorAction action = null;
        for (int i = 0; i < 20; i++)
        {
            leader.ActionPoints = Rules.BASE_ACTION_COST; action = leader.Controller.GetAction(w.Game);
            Check.Equal(true, w.Try(action), "NPC controller selects a legal action");
            if (action is ActionCommunityDiplomacy && leader.SocialGroup.Diplomacy.Relations.Any(r => r.Feud)) break;
        }
        Check.Equal(true, leader.SocialGroup.Diplomacy.Relations.Any(r => r.Feud), "real NPC turns declare the qualifying feud");
        Check.Equal(0, leader.ActionPoints, "declaration spends one normal action");
    }
    internal static void TalkKey(ScenarioWorld w, Actor player, Actor leader, params Keys[] keys)
    {
        Point destination = new Point(leader.Location.Position.X, leader.Location.Position.Y + 1);
        if (player.Location.Position != destination) w.Map.PlaceActorAt(player, destination);
        CommunityMembershipScenario.Fov(w); NpcConversation.Pending(player, leader);
        // No request is outstanding in these fixtures. Use the actual V and relations menus.
        var ui = (ScenarioUI)w.Game.UI;
        int neighbors = w.Map.Actors.Count(a => a != player && !a.IsDead && !a.IsSleeping && a.Model.Abilities.IsIntelligent &&
            !w.Game.Rules.AreEnemies(player,a) && w.Game.Rules.GridDistance(player.Location.Position,a.Location.Position) <= 1);
        if (neighbors > 1) ui.QueueKey(CommunityClarityScenario.DirectionKey(player.Location.Position, leader.Location.Position));
        foreach (Keys key in keys) ui.QueueWaitKey(key);
        player.ActionPoints = Rules.BASE_ACTION_COST;
        Check.Equal(true, Check.Call(w.Game, "HandlePlayerTalk", player), "V completes a real diplomatic exchange");
        Check.Equal(0, player.ActionPoints, "V exchange charges once");
    }
    public static void Register()
    {
        ScenarioRunner.Add("npc/community-diplomacy-injuries", () => CommunityMembershipScenario.Arena(4970), w => {
            Actor player = NpcIntentSupport.Player(w, 5, 3), a = Group(w, "Oak circle", 2), b = Group(w, "River patrol", 7, true);
            Introduce(w, a, b);
            Actor victim = a.Followers.First(), attacker = b.Followers.First();
            Injury(w, attacker, victim, 0);
            Check.Equal(25, Relation(a,b).Grievance, "one injurious engagement has weight 25");
            Injury(w, attacker, victim, 1);
            Check.Equal(25, Relation(a,b).Grievance, "repeated blows do not create separate assaults");
            Check.Equal(false, Relation(a,b).Feud, "one assault is not an automatic declaration");
            Check.Equal(false, w.Game.Rules.AreEnemies(a,b), "personal enemies do not implicate their leaders");
            NpcFact reported = a.Personality.Knowledge.Facts.Last(f => f.Kind == "community_injury");
            int grievance = Relation(a,b).Grievance;
            NpcKnowledgeSystem.Hear(w.Game, a, victim, reported); NpcKnowledgeSystem.Hear(w.Game, a, victim, reported);
            Check.Equal(grievance, Relation(a,b).Grievance, "repeated reports do not add grievance");
            Kill(w, attacker, victim);
            Check.Equal(60, Relation(a,b).Grievance, "known unprovoked killing replaces its assault weight");
            Check.Equal(1, Relation(a,b).Incidents.Count, "death, murder and killing are one political incident");
            w.Map.PlaceActorAt(attacker, new Point(20,6)); Declare(w,a);
            Check.Equal(true, player.Personality.KnownDiplomacy.Any(d => d.Message.Feud), "nearby awake player hears the declaration");
            Check.Equal(true, w.Game.Rules.IsKnownEnemy(a,b), "leader recognizes a rival member after declaring");
            Actor sleeper = NpcIntentSupport.Actor(w, "Sleeper", 3,4); sleeper.IsSleeping = true;
            CommunityDiplomacySystem.Publish(w.Game, a, CommunityDiplomacySystem.Message(a.SocialGroup, Relation(a,b), 1), "community_feud");
            Check.Equal(0, sleeper.Personality.KnownDiplomacy.Count, "sleepers acquire no automatic feud knowledge");
        });
        ScenarioRunner.Add("npc/community-diplomacy-attribution", () => CommunityMembershipScenario.Arena(4971), w => {
            Actor player = NpcIntentSupport.Player(w,5,3), a = Group(w,"Oak",2), b=Group(w,"River",7);
            Actor victim=a.Followers.First(), attacker=b.Followers.First();
            Injury(w,attacker,victim,0);
            Check.Equal(null, Relation(a,b), "unintroduced attackers cannot be blamed on their hidden group");
            var unidentified = a.Personality.Knowledge.Facts.Last(f=>f.Kind=="community_injury");
            Introduce(w,a,b); NpcKnowledgeSystem.Hear(w.Game,a,victim,unidentified);
            Check.Equal(null, Relation(a,b), "old reports never resolve newly learned or live affiliations");
            Injury(w,attacker,victim,31);
            Check.Equal(25,Relation(a,b).Grievance,"a subsequent witnessed incident has its own known attribution");
            Actor outsider=NpcIntentSupport.Actor(w,"Bystander",20,6);
            Check.Equal(0,outsider.Personality.KnownDiplomacy.Count,"distant actors know nothing");
            var report=a.Personality.Knowledge.Facts.Last(f=>f.Kind=="community_injury");
            var identity=report.DiplomacyEvidence.Other.Group;
            b.RemoveFollower(attacker);
            Check.Equal(identity,report.Retell(victim.PersonalityIdentity,32,70).DiplomacyEvidence.Other.Group,"reported incident preserves the original affiliation");
            string before=String.Join("\n",(IList<string>)Check.Call(w.Game,"CommunityJournalLines",new[]{typeof(Actor)},player));
            b.SocialGroup.CommunityName="Changed unseen";
            Check.Equal(before,String.Join("\n",(IList<string>)Check.Call(w.Game,"CommunityJournalLines",new[]{typeof(Actor)},player)),"journal reads snapshots only");
        });
        ScenarioRunner.Add("npc/community-diplomacy-police-local", () => CommunityMembershipScenario.Arena(4972), w => {
            Actor player=NpcIntentSupport.Player(w,5,3), a=Group(w,"Station",2,true), b=Group(w,"Other patrol",7,true);
            Introduce(w,a,b); Actor attacked=a.Followers.First();
            w.Game.DoMakeAggression(player,attacked);
            Check.Equal(true,w.Game.Rules.AreEnemies(player,attacked,false),"attacked officer has immediate self-defense");
            Check.Equal(false,w.Game.Rules.AreEnemies(player,b,false),"separate police group receives no automatic personal enemies");
            Check.Equal(false,w.Game.Rules.IsKnownEnemy(b,player),"separate police unit has no automatic feud");
            var actors=w.Map.Actors.ToArray(); var positions=actors.Select(x=>x.Location.Position).ToArray();
            Check.Equal(0,w.Game.EstablishLocalPoliceUnits(),"generation does not reorganize already grouped officers");
            for(int i=0;i<actors.Length;i++) Check.Equal(positions[i],actors[i].Location.Position,"organization preserves positions");
        });
        ScenarioRunner.Add("npc/community-diplomacy-peace-input-save", () => CommunityMembershipScenario.Arena(4973), w => {
            Actor player=NpcIntentSupport.Player(w,5,3), a=Group(w,"Oak",2), b=Group(w,"River",7);
            Introduce(w,a,b); Kill(w,b.Followers.First(),a.Followers.First());
            w.Map.PlaceActorAt(b.Followers.First(),new Point(20,6)); Declare(w,a);
            Check.Equal(true,w.Game.Rules.IsMurder(a,b),"a declaration does not legalize a new killing");
            CommunityMembershipScenario.Time(w,180);
            TalkKey(w,player,a,Keys.D4,Keys.D2,Keys.Y);
            var offer=player.Personality.PeaceOffers.Single();
            string file=Path.Combine(Environment.GetEnvironmentVariable("ROGUE_COMMUNITY_ARTIFACTS"),"diplomacy-offer-format-5.dat");
            BinarySaveStore.Save(file,Session.Get);
            Session loaded=BinarySaveStore.Load<Session>(file); w=NpcIntentSupport.Restore(w,loaded);
            player=NpcIntentSupport.Find(w.Map,player.PersonalityIdentity); a=NpcIntentSupport.Find(w.Map,a.PersonalityIdentity); b=NpcIntentSupport.Find(w.Map,b.PersonalityIdentity);
            offer=player.Personality.PeaceOffers.Single();
            Check.Equal(true,Relation(a,b).Feud,"format 5 preserves feud and pending terms");
            TalkKey(w,player,b,Keys.D4,Keys.D3);
            Check.Equal(true,offer.Accepted,"recipient leader accepts real delivered terms");
            TalkKey(w,player,a,Keys.D4,Keys.D4);
            Check.Equal(true,offer.Ratified,"origin leader ratifies the returned reply");
            Check.Equal(false,Relation(a,b).Feud,"ratification ends the originating declaration");
            Check.Equal(false,Relation(b,a).Feud,"ratification ends the recipient declaration");
            Check.Equal(null,CommunityDiplomacySystem.Declaration(a),"old killing does not immediately redeclare the ended feud");
            Check.Equal(true,player.Personality.KnownDiplomacy.Any(d=>!d.Message.Feud),"participant learns dated peace");
            Check.Equal(true,String.Join("\n",(IList<string>)Check.Call(w.Game,"CommunityJournalLines",new[]{typeof(Actor)},player)).Contains("ratified"),"J describes the carried agreement");
            BinarySaveStore.Save(file,Session.Get);
            Check.Equal(true,BinarySaveStore.Load<Session>(file).CurrentMap.Actors.First(x=>x.IsPlayer).Personality.PeaceOffers[0].Ratified,"ratified agreement persists");
        });
    }
}
