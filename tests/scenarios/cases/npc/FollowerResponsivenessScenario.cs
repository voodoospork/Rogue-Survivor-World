using System.Drawing;
using djack.RogueSurvivor.Data;
using djack.RogueSurvivor.Engine;

// Uses only pre-milestone interfaces so the same workload runs against the preserved executable.
static class FollowerResponsivenessScenario
{
    public static void Register()
    {
        ScenarioRunner.Add("npc/follower-responsiveness", () => TownScenarioFactory.Arena(5120,
            "................", "................", "................", "................", "................"), w =>
        {
            Session.Get.GamePreset = GamePreset.World(); Session.Get.UniqueActors.TheSewersThing = new UniqueActor();
            w.Map.LocalTime.TurnCounter = 360; Session.Get.WorldTime.TurnCounter = 360;
            Actor p = NpcIntentSupport.Player(w, 8, 2), f = NpcIntentSupport.Actor(w, "Sam", 3, 2); p.AddFollower(f);
            PerformanceBenchmarks.Measure("following civilian selection", 1000, () => f.Controller.GetAction(w.Game));
        });
    }
}
