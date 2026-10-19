using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using djack.RogueSurvivor.Data;
using djack.RogueSurvivor.Engine;
using djack.RogueSurvivor.Gameplay;

static class CommunityNewWorldScenario
{
    internal static Actor Generate(ScenarioWorld world, bool communities = true, bool patchy = true, Skills.IDs startingSkill = Skills.IDs.CARPENTRY, bool organic = false)
    {
            Session.Get.GameMode = GameMode.GM_XPD;
            Session.Get.GamePreset = GamePreset.World();
            Session.Get.GamePreset.Name = organic ? "World" : "Legacy settlement comparison";
            Session.Get.GamePreset.OrganicCommunities = organic;
            Session.Get.GamePreset.InitialCommunities = communities;
            Session.Get.GamePreset.PatchySupplies = patchy;
            GameOptions options = RogueGame.Options;
            // These dated scripted journeys retain their original 40 residents per 100x100 map.
            // Population-scaling scenarios exercise the new 25/49/100 density separately.
            options.MaxCivilians = 10; options.DistrictSize = 100;
            typeof(RogueGame).GetField("s_Options", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, options);
            FieldInfo field = typeof(RogueGame).GetField("m_CharGen", BindingFlags.Instance | BindingFlags.NonPublic);
            object character = Activator.CreateInstance(field.FieldType);
            field.FieldType.GetProperty("IsMale").SetValue(character, true, null);
            field.FieldType.GetProperty("StartingSkill").SetValue(character, startingSkill, null);
            field.SetValue(world.Game, character);
            ScenarioUI ui = (ScenarioUI)world.Game.UI;
            ui.LogProgress = true;
            Check.Call(world.Game, "GenerateWorld", new[] { typeof(bool), typeof(int) }, true, 3);
            ui.LogProgress = false;
            Actor player = world.Game.Player;
            Actor encounterLeader = null;
            var seeded = new HashSet<Guid>(); var allActors = new HashSet<Actor>();
            for (int x = 0; x < 3; x++) for (int y = 0; y < 3; y++)
                foreach (Map map in Session.Get.World[x, y].Maps)
                    foreach (Actor actor in map.Actors)
                    {
                        Check.Equal(true, allActors.Add(actor), "new generation registers each actor once across all maps");
                        if (actor.Faction != world.Game.GameFactions.TheCivilians || actor.IsUnique || actor.CountFollowers != 2) continue;
                        XpdBase shelter = map.XpdBaseAt(actor.Location.Position);
                        if (shelter == null || !shelter.Owns(actor)) continue;
                        seeded.Add(actor.SocialGroup.Identity);
                        if (map == player.Location.Map && encounterLeader == null) encounterLeader = actor;
                        Check.Equal(true, Math.Abs(x - 1) + Math.Abs(y - 1) <= 1, "initial communities stay in or beside starting district");
                        Check.Equal(false, map == player.Location.Map && shelter.Contains(player.Location.Position), "player's starting room is not claimed");
                        Check.Equal(false, actor.IsPlayer || actor.HasLeader, "communities are independent of player");
                    }
            Check.Equal(communities ? 3 : 0, seeded.Count, "real new-world path respects initial communities option");
            Check.Equal(false, player.Personality != null && player.Personality.Communities.Count > 0, "new player has no omniscient community roster");
            string path = Path.Combine(Environment.GetEnvironmentVariable("ROGUE_COMMUNITY_ARTIFACTS"), "new-world-format-5.dat");
            BinarySaveStore.Save(path, Session.Get);
            Session loaded = BinarySaveStore.Load<Session>(path);
            Check.Equal(communities, loaded.GamePreset.InitialCommunities, "World preset flag persists");
            Check.Equal(communities, encounterLeader != null, "starting district follows settlement option");
            return encounterLeader;
    }
    public static void Register()
    {
        ScenarioRunner.Add("world/community-new-world", () => TownScenarioFactory.Create(4921, false), world =>
        {
            Actor encounterLeader = Generate(world);
            CommunityClarityScenario.FreshEncounter(world.Game, encounterLeader);
            Console.WriteLine("Fresh World generated: three independent communities; saved and reloaded without duplicating survivors or claiming the player's shelter.");
        });
    }
}
