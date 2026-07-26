using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using Game.Sim;
using Game.Prediction;

namespace Game.View
{
    /// <summary>
    /// 플레이 중 예측 결과를 실제 게임 화면에 오버레이로 보여주는 디버그 도구.
    /// Phase 5(시간정지·후보 선택·잔상·리듬 재생)의 정식 UX가 아니라, 그 전에
    /// "지금 상태에서 예측이 뭘 찾아내는지" 바로 확인하기 위한 개발용 미리보기다.
    /// 실제 게임 시간은 멈추지 않고, 계산된 경로를 월드 스페이스에 그리기만 한다.
    ///
    /// P: 현재 실제 상태(Main.Instance.World/Services — 실제 벽·NavMesh 반영)로 예측 실행 후 표시.
    /// O: 플레이어 정면에 적 1마리 추가(상황 설정용).
    /// K: 살아있는 적 전부 제거(시나리오 리셋).
    /// </summary>
    public class PredictionPreview : MonoBehaviour
    {
        CandidatePath lastPlan;
        long lastElapsedMs;

        LineRenderer line;
        readonly List<Transform> actionMarkers = new List<Transform>();
        readonly List<Transform> killMarkers = new List<Transform>();

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;

            if (kb.pKey.wasPressedThisFrame) RunPrediction();
            if (kb.oKey.wasPressedThisFrame) SpawnEnemyAhead();
            if (kb.kKey.wasPressedThisFrame) Main.Instance?.ClearAllEnemies();
        }

        void SpawnEnemyAhead()
        {
            Main main = Main.Instance;
            if (main == null) return;

            float yaw = main.World.player.yaw;
            Vector3 forward = new Vector3(Mathf.Sin(yaw * Mathf.Deg2Rad), 0f, Mathf.Cos(yaw * Mathf.Deg2Rad));
            main.SpawnEnemyNear(main.World.player.pos + forward * 4f);
        }

        void RunPrediction()
        {
            Main main = Main.Instance;
            if (main == null) return;

            SimServices services = main.Services;
            PredictionSettings settings = PredictionSettings.Full;

            var stopwatch = Stopwatch.StartNew();
            CandidatePath[] plans = PredictionPlanner.Plan(in main.World, in services, settings);
            stopwatch.Stop();
            CandidatePath plan = plans[0]; // 점수 최상위 후보만 표시(계약상 최대 3개까지 반환되지만 미리보기는 1개만 그림)
            lastPlan = plan;
            lastElapsedMs = stopwatch.ElapsedMilliseconds;

            // 실제 게임 상태는 안 건드리고, 복제본에 계획을 재생해 궤적만 뽑는다.
            SimWorld replay = Snapshot.Clone(in main.World);
            var waypoints = new List<Vector3> { replay.player.pos };
            var actionTypes = new List<MacroActionType>();

            for (int m = 0; m < plan.actions.Length; m++)
            {
                MacroAction action = plan.actions[m];
                actionTypes.Add(action.type);
                for (int t = 0; t < settings.macroTicks; t++)
                {
                    float aimYaw = AimAtNearestEnemy(in replay);
                    InputCmd cmd = action.ToInputCmd(action.ResolveYaw(in replay, aimYaw), t);
                    SimStep.Run(ref replay, in cmd, in services);
                    if (replay.player.combat.hp <= 0) break;
                }
                waypoints.Add(replay.player.pos);
                if (replay.player.combat.hp <= 0) break;
            }

            var killPositions = new List<Vector3>();
            for (int i = 0; i < replay.enemyCount; i++)
                if (!replay.enemies[i].alive) killPositions.Add(replay.enemies[i].pos);

            Draw(waypoints, actionTypes, killPositions);
        }

        static float AimAtNearestEnemy(in SimWorld world)
        {
            float best = float.MaxValue;
            Vector3 target = default;
            bool found = false;
            for (int i = 0; i < world.enemyCount; i++)
            {
                ref readonly EnemySim e = ref world.enemies[i];
                if (!e.alive) continue;
                float d = CombatMath.FlatDistance(world.player.pos, e.pos);
                if (d < best) { best = d; target = e.pos; found = true; }
            }
            if (!found) return world.player.yaw;
            Vector3 dir = target - world.player.pos;
            return Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
        }

        void Draw(List<Vector3> waypoints, List<MacroActionType> actionTypes, List<Vector3> killPositions)
        {
            EnsureLine();
            line.positionCount = waypoints.Count;
            for (int i = 0; i < waypoints.Count; i++)
                line.SetPosition(i, waypoints[i] + Vector3.up * 0.5f);

            EnsureMarkerPool(actionMarkers, actionTypes.Count);
            for (int i = 0; i < actionTypes.Count; i++)
            {
                actionMarkers[i].gameObject.SetActive(true);
                actionMarkers[i].position = waypoints[i + 1] + Vector3.up * 0.5f;
                SetColor(actionMarkers[i], ActionColor(actionTypes[i]));
            }
            for (int i = actionTypes.Count; i < actionMarkers.Count; i++)
                actionMarkers[i].gameObject.SetActive(false);

            EnsureMarkerPool(killMarkers, killPositions.Count);
            for (int i = 0; i < killPositions.Count; i++)
            {
                killMarkers[i].gameObject.SetActive(true);
                killMarkers[i].position = killPositions[i] + Vector3.up * 0.9f;
                killMarkers[i].localScale = Vector3.one * 0.45f;
                SetColor(killMarkers[i], Color.black);
            }
            for (int i = killPositions.Count; i < killMarkers.Count; i++)
                killMarkers[i].gameObject.SetActive(false);
        }

        void EnsureLine()
        {
            if (line != null) return;
            var go = new GameObject("PredictionPath");
            line = go.AddComponent<LineRenderer>();
            line.material = Mat(new Color(1f, 0f, 1f));
            line.startWidth = line.endWidth = 0.08f;
            line.numCapVertices = 4;
        }

        static void EnsureMarkerPool(List<Transform> pool, int needed)
        {
            while (pool.Count < needed)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.name = "PredictionMarker";
                Object.Destroy(go.GetComponent<Collider>());
                go.transform.localScale = Vector3.one * 0.3f;
                pool.Add(go.transform);
            }
        }

        static void SetColor(Transform t, Color c) => t.GetComponent<Renderer>().material = Mat(c);

        static Material Mat(Color c)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(sh) { color = c };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            return m;
        }

        static Color ActionColor(MacroActionType type)
        {
            switch (type)
            {
                case MacroActionType.Attack: return Color.red;
                case MacroActionType.Lunge: return new Color(1f, 0.5f, 0f);
                case MacroActionType.LungeStrike: return new Color(1f, 0.25f, 0.1f); // 공중 마무리 콤보(런지+좌클릭)
                case MacroActionType.JumpStrike: return new Color(1f, 0.75f, 0.1f);  // 대공 콤보(점프+좌클릭)
                case MacroActionType.DashForward:
                case MacroActionType.DashBackward:
                case MacroActionType.DashLeft:
                case MacroActionType.DashRight:
                    return Color.cyan;
                case MacroActionType.Retreat: return Color.blue;
                case MacroActionType.Wait: return Color.gray;
                default: return Color.white; // MoveForward/Left/Right
            }
        }

        void OnGUI()
        {
            if (UiVisibility.Skip) return;      // 콘솔 `ui off` 로 숨김
            if (lastPlan == null) return;

            var style = new GUIStyle(GUI.skin.label) { fontSize = 14 };
            style.normal.textColor = Color.white;

            var sb = new StringBuilder();
            sb.AppendLine($"[예측 미리보기] 계산 {lastElapsedMs}ms  (P=재계산, O=적 추가, K=적 전부 제거)");
            sb.AppendLine($"score={lastPlan.TotalScore:F1} (안전{lastPlan.safetyScore:F0}/처치{lastPlan.killScore:F0}/난이도{lastPlan.difficultyScore:F0})  " +
                          $"kills={lastPlan.killCount}  damage={lastPlan.damageDealt}  hitsTaken={lastPlan.expectedHits}  deadFallback={lastPlan.isDeadFallback}");
            sb.Append("행동: ");
            for (int i = 0; i < lastPlan.actions.Length; i++)
            {
                MacroAction a = lastPlan.actions[i];
                string label = a.type.ToString();
                if (a.type == MacroActionType.Lunge || a.type == MacroActionType.LungeStrike) label += $"(id={a.lungeTargetId})";
                sb.Append(i == 0 ? label : " -> " + label);
            }

            GUI.Label(new Rect(10, 10, Screen.width - 20, 100), sb.ToString(), style);
        }
    }
}
