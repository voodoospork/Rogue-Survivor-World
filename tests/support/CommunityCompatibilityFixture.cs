using System;
using djack.RogueSurvivor.Data;
using djack.RogueSurvivor.Engine;

// Run against the preserved pre-community executable to produce a genuine older format-5 graph.
static class CommunityCompatibilityFixture
{
    public static void Write(string path)
    {
        ScenarioWorld world = TownScenarioFactory.Arena(4810, ".........", ".........", ".........");
        Session.Get.GamePreset = GamePreset.BuiltIn(GameMode.GM_XPD);
        Actor player = NpcIntentSupport.Player(world, 1, 1);
        Actor leader = NpcIntentSupport.Actor(world, "Legacy leader", 3, 1);
        Actor follower = NpcIntentSupport.Actor(world, "Legacy follower", 4, 1);
        leader.AddFollower(follower);
        NpcIntentSupport.Food(world, follower, 2);
        BinarySaveStore.Save(path, Session.Get);
        Console.WriteLine("Wrote format-5 compatibility fixture: " + path);
    }
}
