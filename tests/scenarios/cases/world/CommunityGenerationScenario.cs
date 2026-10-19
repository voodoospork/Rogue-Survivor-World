using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using djack.RogueSurvivor.Data;
using djack.RogueSurvivor.Engine;
using djack.RogueSurvivor.Gameplay.Generators;
using djack.RogueSurvivor.Gameplay.Personality;

static class CommunityGenerationScenario
{
    public static void Register()
    {
        ScenarioRunner.Add("world/community-organic-police-conservation", () => TownScenarioFactory.Create(5231, false), world =>
        {
            Session.Get.GamePreset = GamePreset.World();
            BaseTownGenerator generator = new BaseTownGenerator(world.Game, BaseTownGenerator.DEFAULT_PARAMS);
            var officers = new List<Actor>();
            for (int i = 0; i < 4; i++)
            {
                Actor cop = generator.CreateNewPoliceman(0); Point? free = world.Map.FindFirstInMap(p => world.Map.IsWalkable(p.X, p.Y) && world.Map.GetActorAt(p) == null);
                world.Map.PlaceActorAt(cop, free.Value); officers.Add(cop);
            }
            var actors = world.Map.Actors.ToArray(); var positions = actors.ToDictionary(a => a, a => a.Location);
            var skills = actors.ToDictionary(a => a, a => world.Game.Rules.ActorMaxFollowers(a));
            var items = actors.ToDictionary(a => a, a => a.Inventory == null ? new Dictionary<Item, int>() : a.Inventory.Items.ToDictionary(i => i, i => i.Quantity));
            Check.Equal(0, world.Game.EstablishInitialCommunities(world.Map), "organic generation seeds no civilian home");
            foreach (Actor actor in actors)
            {
                Check.Equal(positions[actor], actor.Location, "organization preserves actor positions");
                Check.Equal(skills[actor], world.Game.Rules.ActorMaxFollowers(actor), "organization grants no command capacity");
                if (actor.Inventory != null) { Check.Equal(items[actor].Count, actor.Inventory.CountItems, "inventory stacks conserved");
                    foreach (Item item in actor.Inventory.Items) Check.Equal(items[actor][item], item.Quantity, "inventory quantities conserved"); }
            }
            foreach (Actor cop in officers)
            {
                Check.Equal(true, cop.SocialGroup != null, "every existing officer gains a local identity");
                Check.Equal(true, cop.GroupRoot.CountFollowers <= Math.Min(2, world.Game.Rules.ActorMaxFollowers(cop.GroupRoot)), "patrol respects real capacity and three-member limit");
                Check.Equal(true, cop.SocialGroup.CommunityName.Contains("police patrol"), "patrol receives persistent district name");
                CommunityAdaptationSystem.Upgrade(world.Game, cop);
                Check.Equal(null, cop.Community, "ordinary processing never converts police command groups into tribes");
            }
            Check.Equal(actors.Length, world.Map.Actors.Count(), "organization creates or removes no actor");
        });
        ScenarioRunner.Add("world/community-generation", () => TownScenarioFactory.Create(4911, false), world =>
        {
            Session.Get.GamePreset = GamePreset.World();
            Session.Get.GamePreset.Name = "Legacy settlement comparison";
            Session.Get.GamePreset.OrganicCommunities = false;
            Session.Get.GamePreset.Validate();
            BaseTownGenerator generator = new BaseTownGenerator(world.Game, BaseTownGenerator.DEFAULT_PARAMS);
            var before = new HashSet<Actor>();
            var supplies = new Dictionary<Item, int>();
            var ground = new Dictionary<Item, int>();
            foreach (Inventory inventory in world.Map.GroundInventories)
                foreach (Item item in inventory.Items) ground.Add(item, item.Quantity);
            for (int i = 0; i < 12; i++)
            {
                Actor actor = generator.CreateNewCivilian(0, 3, 1);
                if (i % 3 == 0) actor.Name = "Taylor"; // Duplicate personal names must not merge community labels.
                Point? free = world.Map.FindFirstInMap(p => world.Map.IsWalkable(p.X, p.Y) && world.Map.GetActorAt(p) == null);
                world.Map.PlaceActorAt(actor, free.Value);
                before.Add(actor);
                foreach (Item item in actor.Inventory.Items) supplies.Add(item, item.Quantity);
            }
            // Disabled saves do not get settlements merely by having eligible civilians.
            Session.Get.GamePreset.InitialCommunities = false;
            Check.Equal(0, world.Game.EstablishInitialCommunities(world.Map), "disabled generation creates no community");
            Session.Get.GamePreset.InitialCommunities = true;
            int count = world.Game.EstablishInitialCommunities(world.Map);
            Check.Equal(3, count, "three legal shelters are found in this generated residential district");
            var groups = new HashSet<Guid>(); var cells = new HashSet<Point>(); var names = new HashSet<string>();
            foreach (Actor actor in world.Map.Actors)
            {
                Check.Equal(true, before.Remove(actor), "every actor occurs exactly once and existed before seeding");
                foreach (Item item in actor.Inventory.Items)
                { Check.Equal(supplies[item], item.Quantity, "seeding preserves inventory quantities"); supplies.Remove(item); }
                if (actor.CountFollowers == 0) continue;
                Check.Equal(2, actor.CountFollowers, "each founding leader has exactly two followers");
                Check.Equal(true, world.Game.Rules.ActorMaxFollowers(actor) >= 2, "leadership legally supports both followers");
                Check.Equal(true, groups.Add(actor.SocialGroup.Identity), "groups have distinct identities");
                Check.Equal(true, names.Add(actor.SocialGroup.CommunityName), "names are distinct in this generated population");
                XpdBase shelter = world.Map.XpdBaseAt(actor.Location.Position);
                Check.Equal(true, shelter != null && shelter.Owns(actor), "leader stands in an owned shelter");
                Zone zone = Zone.BuildingAt(actor.Location);
                Check.Equal(true, zone != null && (zone.BuildingKind == BuildingKind.House || zone.BuildingKind == BuildingKind.Apartments), "shelter is residential");
                string reason;
                Check.Equal(false, world.Game.TryClaimXpdBase(actor, out reason), "overlapping claim is rejected by existing rules");
                foreach (Point cell in shelter.Cells) Check.Equal(true, cells.Add(cell), "claimed shelters do not overlap");
            }
            Check.Equal(0, before.Count, "no survivor was removed");
            Check.Equal(0, supplies.Count, "no inventory resource was removed");
            foreach (Inventory inventory in world.Map.GroundInventories)
                foreach (Item item in inventory.Items)
                { Check.Equal(ground[item], item.Quantity, "shelter stock is conserved"); ground.Remove(item); }
            Check.Equal(0, ground.Count, "no ground resource was removed");
        });
    }
}
