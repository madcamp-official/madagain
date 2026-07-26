using System;
using Unity.Profiling;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace Game.Sim
{
    public static class SimStepProfiler
    {
        internal enum Stage { PlayerMovement, PlayerCombat, SeparationForce, EnemyAI, Projectile, CombatResolve, CollisionSeparation, NavigationClamp }
        static readonly string[] Names = { "playerMove", "playerCombat", "separationForce", "enemyAI", "projectile", "combatResolve", "collisionSeparation", "navClamp" };
        static readonly ProfilerMarker[] Markers =
        {
            new ProfilerMarker("Prediction.SimStep.PlayerMovement"),
            new ProfilerMarker("Prediction.SimStep.PlayerCombat"),
            new ProfilerMarker("Prediction.SimStep.SeparationForce"),
            new ProfilerMarker("Prediction.SimStep.EnemyAI"),
            new ProfilerMarker("Prediction.SimStep.Projectile"),
            new ProfilerMarker("Prediction.SimStep.CombatResolve"),
            new ProfilerMarker("Prediction.SimStep.CollisionSeparation"),
            new ProfilerMarker("Prediction.SimStep.NavigationClamp"),
        };
        static readonly long[] Ticks = new long[8];
        static bool capture;
        static long enemySteps, projectileSlots;

        public static void BeginCapture()
        {
            Array.Clear(Ticks, 0, Ticks.Length);
            enemySteps = projectileSlots = 0;
            capture = true;
        }

        public static string EndCapture()
        {
            capture = false;
            string result = "simStep(ms): ";
            for (int i = 0; i < Ticks.Length; i++)
            {
                if (i > 0) result += ", ";
                result += $"{Names[i]}={ToMs(Ticks[i]):0.0}";
            }
            return result + $"\nsimStep counts: enemySteps={enemySteps}, projectileSlots={projectileSlots}";
        }

        public static Scope MeasurePlayerMovement() => new Scope(Stage.PlayerMovement);
        public static Scope MeasurePlayerCombat() => new Scope(Stage.PlayerCombat);
        public static Scope MeasureSeparationForce() => new Scope(Stage.SeparationForce);
        public static Scope MeasureEnemyAI(int count) { if (capture) enemySteps += count; return new Scope(Stage.EnemyAI); }
        public static Scope MeasureProjectile(int slots) { if (capture) projectileSlots += slots; return new Scope(Stage.Projectile); }
        public static Scope MeasureCombatResolve() => new Scope(Stage.CombatResolve);
        public static Scope MeasureCollisionSeparation() => new Scope(Stage.CollisionSeparation);
        public static Scope MeasureNavigationClamp() => new Scope(Stage.NavigationClamp);
        static double ToMs(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

        public struct Scope : IDisposable
        {
            readonly ProfilerMarker.AutoScope marker;
            readonly Stage stage;
            readonly long start;
            readonly bool tracked;
            internal Scope(Stage stage)
            {
                this.stage = stage;
                marker = Markers[(int)stage].Auto();
                tracked = capture;
                start = tracked ? Stopwatch.GetTimestamp() : 0L;
            }
            public void Dispose()
            {
                if (tracked) Ticks[(int)stage] += Stopwatch.GetTimestamp() - start;
                marker.Dispose();
            }
        }
    }
}
