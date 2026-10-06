using System;
using System.Drawing;
using djack.RogueSurvivor.Data;
using djack.RogueSurvivor.Engine;
using djack.RogueSurvivor.Engine.Actions;
using djack.RogueSurvivor.Engine.Items;
using djack.RogueSurvivor.Gameplay.Personality;

static class CommunityBoundariesScenario
{
    public static void Register()
    {
        ScenarioRunner.Add("npc/community-boundaries", () => TownScenarioFactory.Arena(4913,
            "................................", "................................", "................................"), world =>
        {
            Session.Get.GamePreset = GamePreset.World();
            RogueGame.KeyBindings.ResetToDefaults();
            Actor player = NpcIntentSupport.Player(world, 1, 1);
            Actor leader = NpcIntentSupport.Actor(world, "Asha", 2, 1);
            Actor local = NpcIntentSupport.Actor(world, "Noah", 3, 1);
            Actor distant = NpcIntentSupport.Actor(world, "Hidden member", 30, 1);
            leader.AddFollower(local); leader.AddFollower(distant);
            SocialGroup group = leader.SocialGroup;
            // An introduction knows neither the distant member nor an unseen base.
            Check.Equal(true, world.Try(new ActionPlayerTalk(player, world.Game, leader, null)), "talk introduces real group");
            KnownCommunity known = player.Personality.FindCommunity(group.Identity);
            Check.Equal(2, known.Members.Count, "unknown distant member is not revealed from group membership list");
            Check.Equal(null, known.Shelter, "no global search reveals an unseen shelter");
            XpdBase remote = new XpdBase(leader, new[] { new Point(30, 1) });
            world.Map.AddXpdBase(remote);
            CommunityKnowledgeSystem.Talk(world.Game, leader, player);
            Check.Equal(null, known.Shelter, "even an existing remote claim remains unknown to speaker");
            world.Map.LocalTime.TurnCounter = 10;
            leader.FoodPoints = 0;
            Check.Equal(true, world.Try(new ActionPlayerTalk(player, world.Game, leader, null)), "actual food shortage is reported");
            Check.Equal(true, world.Try(new ActionPlayerTalk(player, world.Game, leader, false)), "player refuses through actual action");
            Check.Equal(true, NpcIntentSupport.HasEvent(leader, "request_refused"), "refusal changes NPC history");
            Check.Equal(true, known.Notes.Exists(n => n.Kind == "request_refused" && n.EventTurn == 10), "refusal is a dated community observation");
            Check.Equal(null, NpcConversation.Pending(player, leader), "refusal clears actual request");
            Check.Equal(true, world.Try(new ActionPlayerTalk(player, world.Game, leader, null)), "immediate follow-up talk remains possible");
            Check.Equal(null, NpcConversation.Pending(player, leader), "cooldown prevents repeated immediate requests");
            world.Map.RemoveXpdBase(remote);
            XpdBase home = new XpdBase(leader, new[] { new Point(1, 1), new Point(2, 1), new Point(3, 1) });
            world.Map.AddXpdBase(home);
            ItemFood stock = new ItemFood(world.Game.GameItems.CANNED_FOOD);
            world.Map.DropItemAt(stock, new Point(1, 1));
            Check.Equal(true, world.Try(new ActionTakeItem(player, world.Game, new Point(1, 1), stock)), "outsider can actually steal stock");
            Check.Equal(true, world.Game.Rules.AreEnemies(player, leader), "witnessed theft creates existing hostility");
            Check.Equal(true, known.Notes.Exists(n => n.Kind == "base_theft"), "theft consequence is recorded");
            Check.Equal(false, new ActionPlayerTalk(player, world.Game, leader, null).IsLegal(), "hostile owner will no longer have a friendly conversation");
            Check.Equal(false, known.Members.Exists(m => m.Id == distant.PersonalityIdentity), "theft does not expose a distant member");
            // Overheard generic speech can be retained, but cannot create community records
            // containing information that has never been reported.
            Actor sleeper = NpcIntentSupport.Actor(world, "sleeper", 0, 1);
            sleeper.IsSleeping = true;
            Check.Equal(false, CommunityKnowledgeSystem.Talk(world.Game, local, sleeper), "sleeping listeners cannot acquire an introduction");
            Check.Equal(0, sleeper.Personality.Communities.Count, "no knowledge leaks through sleeping conversation");
            for (int i = 0; i < 80; i++)
            {
                KnownCommunity bounded = player.Personality.LearnCommunity(Guid.NewGuid(), "record " + i);
                for (int n = 0; n < 70; n++) bounded.Note(new CommunityNote { EventId = n + 1, Kind = "test", Text = "bounded" });
                Check.Equal(32, bounded.Notes.Count, "observation history is bounded");
            }
            Check.Equal(32, player.Personality.Communities.Count, "community knowledge is bounded");
        });
    }
}
