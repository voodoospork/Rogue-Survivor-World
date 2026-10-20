using System;
using System.IO;
using System.Reflection;
using djack.RogueSurvivor.Data;
using djack.RogueSurvivor.Engine;
using djack.RogueSurvivor.Engine.Actions;

static class CommunityLegacySaveScenario
{
    public static void Register()
    {
        // This fixture was written by the preserved executable before any community fields existed.
        string legacy = Environment.GetEnvironmentVariable("ROGUE_COMMUNITY_LEGACY_FILE");
        if (String.IsNullOrEmpty(legacy) || !File.Exists(legacy)) return;
        ScenarioRunner.Add("storage/community-legacy-format-5", () => TownScenarioFactory.Arena(4914,
            ".........", ".........", "........."), world =>
        {
            Session loaded = BinarySaveStore.Load<Session>(legacy);
            ScenarioWorld restored = NpcIntentSupport.Restore(world, loaded);
            Actor player = null, leader = null, follower = null;
            foreach (Actor actor in restored.Map.Actors)
            {
                if (actor.IsPlayer) player = actor;
                if (actor.UnmodifiedName == "Legacy leader") leader = actor;
                if (actor.UnmodifiedName == "Legacy follower") follower = actor;
            }
            Check.Equal(false, loaded.GamePreset.InitialCommunities, "older preset defaults seeding off");
            Check.Equal(0, player.Personality.Communities.Count, "old save loads with empty community knowledge");
            Check.Equal(0, player.Personality.KnownDiplomacy.Count, "old save starts without diplomatic knowledge");
            Check.Equal(0, player.Personality.PeaceOffers.Count, "old save starts without carried peace offers");
            Check.Equal(null, leader.SocialGroup.Diplomacy, "loading an older group creates no diplomatic state or police units");
            Check.Equal(null, typeof(SocialGroup).GetField("m_CommunityName", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(leader.SocialGroup), "old group loads with optional name absent");
            Check.Same(leader, follower.Leader, "existing leader/follower relationship survives loading");
            Check.Equal(2, NpcIntentSupport.FoodUnits(follower), "existing resources survive loading");
            Check.Equal(null, leader.Predation, "old save has no invented predation duty");
            Check.Equal(0, leader.NextPredationClock, "old save has no invented predation cooldown");
            Guid identity = leader.SocialGroup.Identity;
            restored.Map.PlaceActorAt(player, new System.Drawing.Point(2, 1));
            RogueGame.KeyBindings.ResetToDefaults();
            Check.Equal(true, restored.Try(new ActionPlayerTalk(player, restored.Game, leader, null)), "old group's first encounter initializes name and records");
            Check.Equal("Legacy leader's circle", player.Personality.FindCommunity(identity).Name, "old group gains encounter name");
            string upgraded = Path.Combine(Environment.GetEnvironmentVariable("ROGUE_COMMUNITY_ARTIFACTS"), "legacy-reloaded-format-5.dat");
            BinarySaveStore.Save(upgraded, loaded);
            Session again = BinarySaveStore.Load<Session>(upgraded);
            Actor savedPlayer = NpcIntentSupport.Find(again.CurrentMap, player.PersonalityIdentity);
            Check.Equal("Legacy leader's circle", savedPlayer.Personality.FindCommunity(identity).Name, "new optional fields round-trip inside format 5");
        });
    }
}
