using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using djack.RogueSurvivor;
using djack.RogueSurvivor.Data;
using djack.RogueSurvivor.Engine;
using djack.RogueSurvivor.Gameplay;

// Real WinForms/GDI+ code, with an offscreen form and scripted legal wait commands.
// This measures the native command cycle, not desktop presentation or human play.
static class NativeTurnPerformanceBenchmarks
{
    sealed class BenchmarkForm : RogueForm, IRogueUI
    {
        Bitmap frame;
        Control canvas;
        internal readonly List<double> PaintTimes = new List<double>();
        public new KeyEventArgs UI_WaitKey() { throw new InvalidOperationException("Controlled native fixture requested a modal response."); }
        public new KeyEventArgs UI_WaitMenuKey() { throw new InvalidOperationException("Controlled native fixture requested a menu response."); }
        public new void UI_Repaint()
        {
            var watch = Stopwatch.StartNew();
            base.UI_Repaint();
            if (frame == null) frame = new Bitmap(RogueGame.CANVAS_WIDTH, RogueGame.CANVAS_HEIGHT);
            if (canvas == null) canvas = (Control)typeof(RogueForm)
                .GetField("m_GameCanvas", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(this);
            // A hidden form's DrawToBitmap skips its hidden children. Draw the native
            // canvas itself so its actual OnPaint executes without showing a window.
            canvas.DrawToBitmap(frame, new Rectangle(Point.Empty, frame.Size));
            watch.Stop(); PaintTimes.Add(watch.Elapsed.TotalMilliseconds);
        }
        internal void SaveFrame(string path)
        {
            int colored = 0;
            for (int y = 0; y < frame.Height; y += 8)
                for (int x = 0; x < frame.Width; x += 8)
                    if ((frame.GetPixel(x, y).ToArgb() & 0xFFFFFF) != 0) colored++;
            if (colored <= 100) throw new Exception("Native canvas must contain rendered gameplay, not a blank frame.");
            frame.Save(path);
        }
        protected override void Dispose(bool disposing)
        { if (disposing) frame?.Dispose(); base.Dispose(disposing); }
    }
    sealed class ScriptedInput : IPlayerInputSource
    {
        readonly BenchmarkForm form;
        bool discard = true;
        long accepted;
        internal readonly List<double> Latencies = new List<double>();
        internal ScriptedInput(BenchmarkForm native) { form = native; }
        public KeyEventArgs PeekKey()
        {
            if (discard) {
                if (accepted != 0) Latencies.Add(1000.0 * (Stopwatch.GetTimestamp() - accepted) / Stopwatch.Frequency);
                discard = false; return null;
            }
            discard = true; accepted = Stopwatch.GetTimestamp();
            return new KeyEventArgs(RogueGame.KeyBindings.Get(PlayerCommand.WAIT_OR_SELF));
        }
        public Point MousePosition() { return form.UI_GetMousePosition(); }
        public MouseButtons? PeekMouseButtons() { return form.UI_PeekMouseButtons(); }
    }
    public static void Run(string path)
    {
        Exception failed = null;
        var worker = new Thread(() => { try { RunNative(path); } catch (Exception error) { failed = error; } });
        worker.SetApartmentState(ApartmentState.STA); worker.Start(); worker.Join();
        if (failed != null) throw new Exception("Native command benchmark failed.", failed);
    }
    static void RunNative(string path)
    {
        var setup = TownScenarioFactory.Create(5900, false);
        SetupConfig.Load();
        using (var form = new BenchmarkForm()) {
            // RogueForm normally starts maximized. A hidden maximized form scales
            // to the desktop size and DrawToBitmap would crop the HUD from this fixture.
            form.WindowState = FormWindowState.Normal;
            form.ClientSize = new Size(RogueGame.CANVAS_WIDTH, RogueGame.CANVAS_HEIGHT);
            // Creating controls does not show the form or open the user's game.
            var handle = form.Handle;
            Check.Call(form.Game, "LoadData"); GameImages.LoadResources(form);
            var w = NpcIntentSupport.Restore(new ScenarioWorld(5900, setup.Map, form.Game), BinarySaveStore.Load<Session>(path));
            int mode = 0; Int32.TryParse(Environment.GetEnvironmentVariable("ROGUE_PERF_MODE"), out mode);
            var modeField = typeof(GamePreset).GetField("Simulation");
            if (modeField != null) modeField.SetValue(Session.Get.GamePreset, Enum.ToObject(modeField.FieldType, mode));
            var options = RogueGame.Options; options.IsAdvisorEnabled = false; options.IsAnimDelayOn = false;
            options.SimThread = false;
            typeof(RogueGame).GetField("s_Options", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, options);
            RogueGame.KeyBindings.ResetToDefaults();
            var input = new ScriptedInput(form);
            typeof(RogueGame).GetField("m_InputReader", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(w.Game, new PlayerInputReader(input));
            form.PaintTimes.Clear();
            long baseline = GC.GetTotalMemory(true), peak = baseline;
            for (int command = 0; command <= 80; command++) {
                var player = w.Game.Player; player.ActionPoints = Rules.BASE_ACTION_COST;
                using (CreateScope(w, mode)) {
                Check.Call(w.Game, "HandlePlayerActor", player);
                Check.Equal(0, player.ActionPoints, "native wait command pays ordinary action cost");
                if (command < 80) CommunityClarityScenario.AmbientTurn(w);
                }
                Session.Get.WorldTime.TurnCounter = w.Map.LocalTime.TurnCounter;
                peak = Math.Max(peak, GC.GetTotalMemory(false));
            }
            var sorted = input.Latencies.OrderBy(t => t).ToArray();
            var paint = form.PaintTimes.OrderBy(t => t).ToArray();
            Console.WriteLine(String.Format(CultureInfo.InvariantCulture,
                "NATIVE BENCH {0}: mode={1}; actors={2}; commands={3}; median={4:F3}; p95={5:F3}; p99={6:F3} ms; paints={7}; paint-median={8:F3}; paint-p95={9:F3}; managed-peak-delta={10}",
                Path.GetFileNameWithoutExtension(path), mode, w.Map.CountActors, sorted.Length,
                sorted[(sorted.Length-1)/2],sorted[(int)Math.Ceiling(sorted.Length*.95)-1],sorted[(int)Math.Ceiling(sorted.Length*.99)-1],
                paint.Length,paint[(paint.Length-1)/2],paint[(int)Math.Ceiling(paint.Length*.95)-1],peak-baseline));
            string label = Environment.GetEnvironmentVariable("ROGUE_PERF_LABEL") ?? "native";
            form.SaveFrame(Path.Combine(Environment.GetEnvironmentVariable("ROGUE_COMMUNITY_ARTIFACTS"), "native-" + label + ".png"));
            Console.WriteLine("NATIVE BENCH actual WinForms, native input reader, GDI+ repaint for every frame; offscreen fixture; scripted waits; no desktop compositor/input-device/human-play claim");
        }
    }
    static IDisposable CreateScope(ScenarioWorld world, int mode)
    {
        if (mode != 2) return null;
        var state = typeof(RogueGame).GetMethod("AdaptiveState", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(Map) }, null)
            .Invoke(world.Game, new object[] { world.Map });
        var type = typeof(RogueGame).Assembly.GetType("djack.RogueSurvivor.Engine.SimulationClock");
        return (IDisposable)Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.NonPublic, null,
            new object[] { Session.Get, state.GetType().GetField("WorldTurn").GetValue(state), state.GetType().GetField("Random").GetValue(state), null, state.GetType().GetField("Identities").GetValue(state) }, null);
    }
}
