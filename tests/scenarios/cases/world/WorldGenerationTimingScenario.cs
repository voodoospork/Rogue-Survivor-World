using System;
using System.Diagnostics;
using System.Reflection;
using djack.RogueSurvivor.Engine;
using djack.RogueSurvivor.Gameplay;

// Compiles unchanged against the preserved diplomacy executable and the scavenging build.
static class WorldGenerationTimingScenario
{
    public static void Register()
    {
        ScenarioRunner.Add("world/generation-responsiveness", () => TownScenarioFactory.Create(5030, false), w =>
        {
            Session.Get.GameMode = GameMode.GM_XPD; Session.Get.GamePreset = GamePreset.World();
            var options = RogueGame.Options; options.MaxCivilians = 40; options.DistrictSize = 100;
            typeof(RogueGame).GetField("s_Options", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, options);
            var field = typeof(RogueGame).GetField("m_CharGen", BindingFlags.Instance | BindingFlags.NonPublic);
            var character = Activator.CreateInstance(field.FieldType);
            field.FieldType.GetProperty("IsMale").SetValue(character, true, null);
            field.FieldType.GetProperty("StartingSkill").SetValue(character, Skills.IDs.CARPENTRY, null); field.SetValue(w.Game, character);
            var clock = Stopwatch.StartNew(); Check.Call(w.Game, "GenerateWorld", new[] { typeof(bool), typeof(int) }, false, 3); clock.Stop();
            Check.Equal(true, w.Game.Player != null && !w.Game.Player.IsDead, "fresh measured World creates an ordinary live player");
            Console.WriteLine("BENCH fresh World generation, nine districts 100x100, seed 5030: " + clock.Elapsed.TotalMilliseconds.ToString("F2") + " ms");
        });
    }
}
