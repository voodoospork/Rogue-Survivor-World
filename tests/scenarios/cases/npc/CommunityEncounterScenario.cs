using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using djack.RogueSurvivor.Data;
using djack.RogueSurvivor.Engine;
using djack.RogueSurvivor.Engine.Actions;
using djack.RogueSurvivor.Gameplay.Personality;

static class CommunityEncounterScenario
{
    public static void Register()
    {
        ScenarioRunner.Add("npc/community-encounter", () => TownScenarioFactory.Arena(4912,
            "....................", "....................", "...................."), world =>
        {
            Session.Get.GamePreset = GamePreset.World();
            Session.Get.UniqueActors.TheSewersThing = new UniqueActor();
            RogueGame.KeyBindings.ResetToDefaults();
            Actor player = NpcIntentSupport.Player(world, 1, 1);
            Actor leader = NpcIntentSupport.Actor(world, "Mira", 2, 1, "loyal");
            Actor successor = NpcIntentSupport.Actor(world, "Sam", 3, 1, "loyal", "organized");
            Actor member = NpcIntentSupport.Actor(world, "Lee", 4, 1, "loyal");
            leader.AddFollower(successor); leader.AddFollower(member);
            SocialGroup group = leader.SocialGroup; group.CommunityName = "Mira's circle";
            world.Map.AddXpdBase(new XpdBase(leader, new[] { new Point(1,1), new Point(2,1), new Point(3,1), new Point(4,1) }));
            leader.FoodPoints = 0;
            Check.Equal(true, world.Try(new ActionPlayerTalk(player, world.Game, leader, null)), "outsider introduces self through a real talk action");
            KnownCommunity known = player.Personality.FindCommunity(group.Identity);
            Check.Equal(group.CommunityName, known.Name, "introduction records persistent name");
            Check.Equal(true, known.Shelter != null && known.Members.Count == 3, "speaker reports only locally known shelter and members");
            Check.Equal(true, NpcConversation.Pending(player, leader) != null, "actual hunger creates an answerable food request");
            Check.Equal(true, world.Try(new ActionPlayerTalk(player, world.Game, leader, true)), "player chooses a promise through the real reply action");
            var gift = NpcIntentSupport.Food(world, player, 1);
            world.Game.DoGiveItemTo(player, leader, gift);
            Check.Equal(true, NpcIntentSupport.HasEvent(leader, "promise_kept"), "actual food delivery fulfils the promise");
            Check.Equal(true, known.Notes.Exists(n => n.Kind == "helped"), "aid is recorded as an actual event");
            // Leave on foot. Nothing in the journal can see the other end of this map.
            for (int x = 2; x <= 17; x++)
            { player.ActionPoints = Rules.BASE_ACTION_COST; Check.Equal(true, world.Try(new ActionMoveStep(player, world.Game, new Point(x, 2))), "leave via legal movement"); }
            string journalBefore = String.Join("\n", (IList<string>)Check.Call(world.Game, "CommunityJournalLines", new[] { typeof(Actor) }, player));
            int foodBefore = leader.FoodPoints;
            Check.Equal(true, world.Try(new ActionUseItem(leader, world.Game, gift)), "recipient actually eats the delivered food while player is away");
            Check.Equal(true, leader.FoodPoints > foodBefore, "delivered food changes real hunger");
            world.Map.LocalTime.TurnCounter = 20;
            Actor attacker = world.Game.GameActors.Zombie.CreateAnonymous(world.Game.GameFactions.TheUndeads, 0);
            world.Map.PlaceActorAt(attacker, new Point(2, 0));
            leader.HitPoints = 1;
            for (int strike = 0; strike < 30 && !leader.IsDead; strike++)
            {
                attacker.ActionPoints = Rules.BASE_ACTION_COST;
                Check.Equal(true, world.Try(new ActionMeleeAttack(attacker, world.Game, leader)), "zombie attacks through production melee action");
            }
            Check.Equal(true, leader.IsDead, "actual combat kills the leader");
            world.Map.RemoveActor(attacker);
            Check.Same(successor, member.Leader, "real death triggers existing succession");
            Check.Equal("Mira's circle", successor.SocialGroup.CommunityName, "succession preserves community name");
            Check.Equal(journalBefore, String.Join("\n", (IList<string>)Check.Call(world.Game, "CommunityJournalLines", new[] { typeof(Actor) }, player)), "journal does not read an unseen death or succession");
            for (int x = 16; x >= 2; x--)
            { player.ActionPoints = Rules.BASE_ACTION_COST; Check.Equal(true, world.Try(new ActionMoveStep(player, world.Game, new Point(x, 2))), "return on foot"); }
            world.Map.LocalTime.TurnCounter = 30;
            Check.Equal(true, world.Try(new ActionPlayerTalk(player, world.Game, successor, null)), "return and ask surviving member for news");
            Check.Equal("Sam", known.Leader, "survivor reports leadership change");
            Check.Equal(true, known.Notes.Exists(n => n.Kind == "group_succession" && n.EventTurn == 20 && n.LearnedTurn == 30 && n.Reported), "report retains event date and later hearing date");
            Check.Equal(false, known.Shelter.StartsWith("Claim lost:"), "successful succession retains the reported community home");
            Check.Same(successor, world.Map.XpdBaseAt(successor.Location.Position).GroupLeader, "community claim follows successful succession");
            string path = Path.Combine(Environment.GetEnvironmentVariable("ROGUE_COMMUNITY_ARTIFACTS"), "encounter-format-5.dat");
            BinarySaveStore.Save(path, Session.Get);
            Session loaded = BinarySaveStore.Load<Session>(path);
            ScenarioWorld restored = NpcIntentSupport.Restore(world, loaded);
            Actor savedPlayer = NpcIntentSupport.Find(restored.Map, player.PersonalityIdentity);
            Actor savedSuccessor = NpcIntentSupport.Find(restored.Map, successor.PersonalityIdentity);
            Check.Equal(group.Identity, savedSuccessor.SocialGroup.Identity, "saved identity survives succession");
            Check.Equal("Mira's circle", savedSuccessor.SocialGroup.CommunityName, "saved name survives succession");
            KnownCommunity saved = savedPlayer.Personality.FindCommunity(group.Identity);
            Check.Equal(true, saved.Notes.Exists(n => n.Kind == "promise_kept"), "actual aid consequence persists");
            Check.Equal(true, saved.Notes.Exists(n => n.EventTurn == 20 && n.LearnedTurn == 30), "dated report persists");
            ScenarioUI ui = (ScenarioUI)restored.Game.UI;
            ui.QueueWaitKey(Keys.Tab); ui.QueueWaitKey(Keys.PageDown); ui.QueueWaitKey(Keys.PageUp); ui.QueueWaitKey(Keys.Escape);
            int ap = savedPlayer.ActionPoints;
            Check.Call(restored.Game, "HandleHeardJournal", Type.EmptyTypes);
            Check.Equal(ap, savedPlayer.ActionPoints, "journal switching spends no game action");
            Check.Equal(true, String.Join(" ", ui.DrawnStrings).Contains("Communities"), "J journal renders Communities through Tab");
            Console.WriteLine("Encounter playthrough: met Mira, promised and delivered food, left on foot; Mira ate, was killed in melee, Sam succeeded; returned, heard turn-20 change at turn 30, saved and reloaded.");
        });
    }
}
