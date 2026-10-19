using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using djack.RogueSurvivor.Data;
using djack.RogueSurvivor.Engine;

static class PopulationScalingBenchmarks
{
    public static void Write(string directory)
    {
        if (Directory.Exists(directory) && Directory.GetFiles(directory).Length > 0) throw new InvalidOperationException("Preserve generated population fixtures.");
        Directory.CreateDirectory(directory);
        foreach (int size in new[] { 50, 70, 100 }) {
            var setup = TownScenarioFactory.Create(6200 + size, false);
            Session.Get.GamePreset = GamePreset.World();
            // Isolate density from the creation of new, randomly identified tribes.
            Session.Get.GamePreset.OrganicCommunities = false;
            var options = RogueGame.Options; options.ResetToDefaultValues(); options.DistrictSize = size;
            PopulationScalingScenario.SetOptions(options);
            var timer = Stopwatch.StartNew(); var w = PopulationScalingScenario.Generate(setup, size, size, 15, true); timer.Stop();
            var population = w.Map.Actors.Where(a => a.Faction == w.Game.GameFactions.TheCivilians || a.Faction == w.Game.GameFactions.ThePolice).ToArray();
            // Exclude newly allocated identity GUIDs from the generation compatibility signature.
            var signature = new StringBuilder();
            foreach (var a in w.Map.Actors) {
                signature.Append(a.Name).Append(':').Append(a.Model.ID).Append(':').Append(a.Location.Position).Append(':').Append(a.HitPoints);
                if (a.Inventory != null) foreach (var item in a.Inventory.Items) signature.Append('/').Append(item.Model.ID).Append('=').Append(item.Quantity);
                signature.Append('|');
            }
            string digest;
            using (var sha = SHA256.Create()) digest = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(signature.ToString()))).Replace("-", "");
            var free = w.Map.FindFirstInMap(p => w.Map.IsWalkable(p.X, p.Y) && w.Map.GetTileAt(p.X, p.Y).IsInside && w.Map.GetActorAt(p) == null).Value;
            NpcIntentSupport.Player(w, free.X, free.Y);
            foreach (var map in w.Map.District.Maps) foreach (var actor in map.Actors)
                if (actor.Personality != null) actor.EnsurePersonalGroup();
            Session.Get.UniqueActors.TheSewersThing = new UniqueActor();
            Session.Get.WorldTime.TurnCounter = 400; // Away from midday/midnight event boundaries.
            foreach (var map in w.Map.District.Maps) map.LocalTime.TurnCounter = 400;
            CommunityMembershipScenario.Fov(w);
            BinarySaveStore.Save(Path.Combine(directory, "population-" + size + ".dat"), Session.Get);
            Console.WriteLine("GENERATED POPULATION " + size + ": residents=" + population.Length + "; police=" + population.Count(a => a.Faction == w.Game.GameFactions.ThePolice) + "; totalActors=" + w.Map.CountActors + "; generationMs=" + timer.Elapsed.TotalMilliseconds.ToString("F3") + "; signature=" + digest);
        }
    }
}
