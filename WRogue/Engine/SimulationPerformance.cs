using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace djack.RogueSurvivor.Engine
{
    // Opt-in diagnostics only. No actor state, RNG calls or serialized fields.
    static class SimulationPerformance
    {
        internal enum Part { Sense, Decision, Action, Knowledge, Adaptation, Culture, Maintenance, Render, Catchup, Perception, Goals, PlanBuild, PlanSearch, Routine, EventDelivery, Equipment, Navigation, Relations, Count }
        internal enum Reuse { FovHit, FovMiss, DepartureHit, DepartureMiss, Count }
        internal static readonly bool Enabled = Environment.GetEnvironmentVariable("ROGUE_PROFILE_TURNS") == "1";
        static readonly bool latencyEnabled = Enabled || Environment.GetEnvironmentVariable("ROGUE_LATENCY_ONLY") == "1";
        static SimulationPerformance() { if (Enabled) AppDomain.MonitoringIsEnabled = true; }
        static readonly long[] ticks = new long[(int)Part.Count];
        static readonly long[] calls = new long[(int)Part.Count];
        static readonly long[] reuse = new long[(int)Reuse.Count];
        internal static void Cache(Reuse kind) { if(Enabled)Interlocked.Increment(ref reuse[(int)kind]); }
        static readonly double[] latency = new double[512];
        static readonly object latencyLock = new object();
        static int latencyCount, latencyNext;
        static long inputStart;
        static bool inputSpent;
        static int[] gcBaseline = { GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2) };
        static long memoryBaseline = GC.GetTotalMemory(false);
        internal static long Start() { return Enabled ? Stopwatch.GetTimestamp() : 0; }
        internal static void End(Part part, long start)
        {
            if (start == 0) return;
            Interlocked.Add(ref ticks[(int)part], Stopwatch.GetTimestamp() - start);
            Interlocked.Increment(ref calls[(int)part]);
        }
        internal static void InputAccepted()
        { if (latencyEnabled) { inputStart = Stopwatch.GetTimestamp(); inputSpent = false; } }
        internal static void PlayerSpentAction()
        { if (latencyEnabled && inputStart != 0) inputSpent = true; }
        internal static void PlayerReady()
        {
            if (!latencyEnabled || !inputSpent || inputStart == 0) return;
            double ms = 1000.0 * (Stopwatch.GetTimestamp() - inputStart) / Stopwatch.Frequency;
            bool write;
            lock (latencyLock) {
                latency[latencyNext] = ms; latencyNext = (latencyNext + 1) % latency.Length;
                latencyCount++; write = latencyCount % 30 == 0;
            }
            inputStart = 0; inputSpent = false;
            string path = Environment.GetEnvironmentVariable("ROGUE_PROFILE_LOG");
            if (write && !String.IsNullOrEmpty(path)) {
                try { File.WriteAllText(path, Report()); }
                catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
        internal static void Reset()
        {
            Array.Clear(ticks, 0, ticks.Length); Array.Clear(calls, 0, calls.Length);
            Array.Clear(reuse, 0, reuse.Length);
            lock (latencyLock) { latencyCount = latencyNext = 0; inputStart = 0; inputSpent = false; }
            gcBaseline = new[] { GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2) };
            memoryBaseline = GC.GetTotalMemory(false);
        }
        internal static string Report()
        {
            var result = new StringBuilder("PROFILE inclusive component times; nested parts must not be summed\n");
            for (int i = 0; i < (int)Part.Count; i++) result.AppendFormat(CultureInfo.InvariantCulture,
                "PROFILE {0}: calls={1}; ms={2:F3}\n", (Part)i, Interlocked.Read(ref calls[i]),
                1000.0 * Interlocked.Read(ref ticks[i]) / Stopwatch.Frequency);
            for(int i=0;i<(int)Reuse.Count;i++)result.AppendFormat(CultureInfo.InvariantCulture,"PROFILE reuse {0}: {1}\n",(Reuse)i,Interlocked.Read(ref reuse[i]));
            lock (latencyLock) {
                int count = Math.Min(latency.Length, latencyCount);
                if (count > 0) {
                    var samples = new double[count]; Array.Copy(latency, samples, count); Array.Sort(samples);
                    result.AppendFormat(CultureInfo.InvariantCulture,
                        "PROFILE command-to-next-input wall: samples={0}; p50={1:F3}; p95={2:F3}; p99={3:F3} ms; confirmations can include user time\n",
                        count, samples[(count-1)/2], samples[(int)Math.Ceiling(count*.95)-1], samples[(int)Math.Ceiling(count*.99)-1]);
                }
            }
            result.AppendFormat(CultureInfo.InvariantCulture,"PROFILE GC={0}/{1}/{2}; live-managed-change={3} bytes\n",
                GC.CollectionCount(0)-gcBaseline[0],GC.CollectionCount(1)-gcBaseline[1],GC.CollectionCount(2)-gcBaseline[2],
                GC.GetTotalMemory(false)-memoryBaseline);
            if (Enabled) result.AppendFormat(CultureInfo.InvariantCulture, "PROFILE allocated-domain-total={0} bytes; runtime monitoring includes diagnostic work\n", AppDomain.CurrentDomain.MonitoringTotalAllocatedMemorySize);
            return result.ToString();
        }
    }
}
