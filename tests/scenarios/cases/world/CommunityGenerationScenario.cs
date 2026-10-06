using System;
using System.Collections.Generic;
using System.Drawing;
using djack.RogueSurvivor.Data;
using djack.RogueSurvivor.Engine;
using djack.RogueSurvivor.Gameplay.Generators;

static class CommunityGenerationScenario
{
    public static void Register()
    {
        ScenarioRunner.Add("world/community-generation", () => TownScenarioFactory.Create(4911, false), world =>
        {
            Session.Get.GamePreset = GamePreset.World();
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
