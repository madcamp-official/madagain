using System;
using Unity.Profiling;
using Stopwatch = System.Diagnostics.Stopwatch;
using Game.Sim;

namespace Game.Prediction
{
    public static class PredictionProfiler
    {
        internal enum Stage { CopyWorld, GenerateActions, Simulate, Navigation, Physics, Evaluate, Deduplicate, FinalResimulation }
        public static readonly ProfilerMarker TotalMarker = new ProfilerMarker("Prediction.Total");
        static readonly ProfilerMarker CopyMarker = new ProfilerMarker("Prediction.CopyWorld");
        static readonly ProfilerMarker ActionsMarker = new ProfilerMarker("Prediction.GenerateActions");
        static readonly ProfilerMarker SimMarker = new ProfilerMarker("Prediction.Simulate");
        public static readonly ProfilerMarker EnemyPreciseMarker = new ProfilerMarker("Prediction.EnemyPrecise");
        public static readonly ProfilerMarker EnemyCoarseMarker = new ProfilerMarker("Prediction.EnemyCoarse");
        static readonly ProfilerMarker NavMarker = new ProfilerMarker("Prediction.Navigation");
        static readonly ProfilerMarker PhysicsMarker = new ProfilerMarker("Prediction.Physics");
        static readonly ProfilerMarker EvalMarker = new ProfilerMarker("Prediction.Evaluate");
        static readonly ProfilerMarker DedupMarker = new ProfilerMarker("Prediction.Deduplicate");
        static readonly ProfilerMarker ReplayMarker = new ProfilerMarker("Prediction.FinalResimulation");

        static bool active;
        static long startedAt, allocatedAtStart;
        static readonly long[] stageTicks = new long[8];
        static int enemyCount, beamWidth, macroDepth, expandedNodes, simulatedTicks;
        static int navQueries, physicsQueries, duplicateStates, deadCandidates;

        public static void Begin(int enemies, in PredictionSettings settings)
        {
            Array.Clear(stageTicks, 0, stageTicks.Length);
            enemyCount = enemies; beamWidth = settings.beamWidth; macroDepth = settings.macroDepth;
            expandedNodes = simulatedTicks = navQueries = physicsQueries = duplicateStates = deadCandidates = 0;
            allocatedAtStart = GC.GetAllocatedBytesForCurrentThread();
            startedAt = Stopwatch.GetTimestamp();
            SimStepProfiler.BeginCapture();
            active = true;
        }

        public static string Finish(int finalCandidates)
        {
            long finishedAt = Stopwatch.GetTimestamp();
            long allocated = Math.Max(0L, GC.GetAllocatedBytesForCurrentThread() - allocatedAtStart);
            active = false;
            string simStepSummary = SimStepProfiler.EndCapture();
            return $"[Prediction Profile] enemies={enemyCount}, beam={beamWidth}, depth={macroDepth}, total={Ms(finishedAt - startedAt):0.0}ms, GC={allocated / 1024f:0.0}KB\n" +
                   $"stages(ms): copy={Ms(stageTicks[0]):0.0}, actions={Ms(stageTicks[1]):0.0}, simulate={Ms(stageTicks[2]):0.0}, nav={Ms(stageTicks[3]):0.0}, physics={Ms(stageTicks[4]):0.0}, evaluate={Ms(stageTicks[5]):0.0}, dedup={Ms(stageTicks[6]):0.0}, replay={Ms(stageTicks[7]):0.0}\n" +
                   $"counts: expanded={expandedNodes}, simTicks={simulatedTicks}, duplicates={duplicateStates}, dead={deadCandidates}, final={finalCandidates}, " +
                   "navQueries=n/a, physicsQueries=n/a (adapter boundary), enemyPrecise=0, enemyCoarse=0, enemyDormant=0 (LOD not implemented)\n" +
                   simStepSummary;
        }

        public static Scope CopyWorld() => new Scope(CopyMarker, Stage.CopyWorld);
        public static Scope GenerateActions() => new Scope(ActionsMarker, Stage.GenerateActions);
        public static Scope Simulate() => new Scope(SimMarker, Stage.Simulate);
        public static Scope NavigationQuery() => new Scope(NavMarker, Stage.Navigation, true);
        public static Scope PhysicsQuery() => new Scope(PhysicsMarker, Stage.Physics, true);
        public static Scope Evaluate() => new Scope(EvalMarker, Stage.Evaluate);
        public static Scope Deduplicate() => new Scope(DedupMarker, Stage.Deduplicate);
        public static Scope FinalResimulation() => new Scope(ReplayMarker, Stage.FinalResimulation);
        public static void RecordExpandedNode() { if (active) expandedNodes++; }
        public static void RecordSimulatedTick() { if (active) simulatedTicks++; }
        public static void RecordDuplicateState() { if (active) duplicateStates++; }
        public static void RecordDeadCandidate() { if (active) deadCandidates++; }
        static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

        public struct Scope : IDisposable
        {
            readonly ProfilerMarker.AutoScope markerScope;
            readonly Stage stage;
            readonly long start;
            readonly bool tracked;
            internal Scope(ProfilerMarker marker, Stage stage, bool query = false)
            {
                markerScope = marker.Auto(); this.stage = stage; tracked = active;
                start = tracked ? Stopwatch.GetTimestamp() : 0L;
                if (tracked && query) { if (stage == Stage.Navigation) navQueries++; else physicsQueries++; }
            }
            public void Dispose()
            {
                if (tracked) stageTicks[(int)stage] += Stopwatch.GetTimestamp() - start;
                markerScope.Dispose();
            }
        }
    }
}
