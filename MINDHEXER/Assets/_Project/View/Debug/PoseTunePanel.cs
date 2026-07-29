using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.View
{
    /// <summary>
    /// F3 — 포즈 재생 튜닝 패널. PosePlayer의 타이밍·스프링 수치를 Play 중 실시간 조절한다.
    /// "다시 재생"으로 마지막 시퀀스를 현재 수치로 즉시 재생 → 보면서 튜닝.
    /// (Precog에서 포팅 — 원래 F2였으나 조명 튜닝 패널이 F2를 쓰고 있어 F3으로 옮김)
    ///
    /// 단축키 배치: F1 = 이동 튜닝 / F2 = 조명 튜닝 / <b>F3 = 포즈 재생</b> / F4 = 세그먼트별
    /// </summary>
    public class PoseTunePanel : MonoBehaviour
    {
        bool open;
        Vector2 scroll;
        /// <summary>패널이 열려 있는가(마우스 연동이 클릭을 먹지 않게 하기 위함).</summary>
        public static bool AnyOpen;

        void OnDisable() { AnyOpen = false; }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.f3Key.wasPressedThisFrame)
            {
                open = !open;
                AnyOpen = open;
                Cursor.lockState = open ? CursorLockMode.None : CursorLockMode.Locked;
                Cursor.visible = open;
            }
        }

        void OnGUI()
        {
            if (!open) return;
            var pp = PosePlayer.Instance;
            const float W = 340f;
            GUILayout.BeginArea(new Rect(12f, 12f, W, Mathf.Min(Screen.height - 24f, 620f)), GUI.skin.box);
            GUILayout.Label("<b>포즈 재생 튜닝 (F3)</b>", Rich());

            if (pp == null) { GUILayout.Label("PosePlayer 없음"); GUILayout.EndArea(); return; }

            scroll = GUILayout.BeginScrollView(scroll);   // 내용이 길어 잘리지 않게

            GUILayout.Label("<b>타이밍</b>", Rich());
            pp.segTime      = FSlider("포즈당 시간(초)",       pp.segTime,      0.05f, 0.8f);
            pp.holdLastPose = FSlider("마지막 포즈 정지(초)",  pp.holdLastPose, 0f,    0.8f);

            GUILayout.Label("<b>포즈 사이 이징</b>", Rich());
            pp.springBetweenPoses = GUILayout.Toggle(pp.springBetweenPoses, pp.springBetweenPoses ? " 스프링 (탄성)" : " 선형");
            pp.springDamp = FSlider("스프링 감쇠(잦아듦)",  pp.springDamp, 1f,  15f);
            pp.springFreq = FSlider("스프링 진동수(튕김)",  pp.springFreq, 4f,  30f);

            GUILayout.Label("<b>복귀</b>", Rich());
            pp.snapReturn = GUILayout.Toggle(pp.snapReturn,
                pp.snapReturn ? " 기본포즈 복귀 = <color=#ffb060>순간이동</color>" : " 기본포즈 복귀 = 블렌드", Rich());
            pp.holdBaseWhenIdle = GUILayout.Toggle(pp.holdBaseWhenIdle, " 평상시 기본포즈 유지");

            // ── 뷰모델 카메라 (팔 뚫림·벽 관통) ──
            GUILayout.Label("<b>뷰모델 카메라 — 진단</b>", Rich());
            var vc = ViewmodelCamera.Instance;
            if (vc == null) GUILayout.Label("<size=10>ViewmodelCamera 없음</size>", Rich());
            else
            {
                // 무엇이 안 됐는지 숫자로 보여준다 — 추측하지 않기 위해
                int wrong = vc.CountWrongLayer();
                float nz  = vc.NearestZ();
                GUILayout.Label(
                    Chk(vc.LayerExists, $"레이어 '{vc.layerName}' (idx {vc.LayerIndex})",
                        "레이어 없음 → Tools/뷰모델/① 실행") + "\n" +
                    Chk(vc.HasOverlay && vc.IsStacked(), "오버레이 카메라 스택 등록됨", "오버레이 미등록") + "\n" +
                    Chk(vc.BaseExcludesLayer, "메인 카메라가 뷰모델 제외함", "메인이 아직 뷰모델을 그림") + "\n" +
                    Chk(wrong == 0, "모든 렌더러가 전용 레이어", $"레이어 안 맞는 렌더러 {wrong}개"),
                    Rich());

                string zTxt = float.IsNaN(nz) ? "측정 불가"
                            : nz < 0f ? $"<color=#ff8080>{nz:0.000}m — 카메라 <b>뒤</b></color>"
                                      : $"<color=#80e080>{nz:0.000}m — 카메라 앞</color>";
                GUILayout.Label($"<size=11>최근접 깊이: {zTxt}   (필요 후퇴 ≥ {Mathf.Max(0f, vc.nearClip + 0.08f - nz):0.00}m)</size>", Rich());

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("후퇴량 자동 맞춤", GUILayout.Height(24f)))
                    Debug.Log(vc.AutoFitPullBack() ? $"[F3] 후퇴량 자동 설정 → {vc.pullBack:0.000}m" : "[F3] 측정 실패");
                if (GUILayout.Button("레이어 다시 입히기", GUILayout.Height(24f))) vc.RefreshLayers();
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("<b>카메라 값 저장</b>", Rich(), GUILayout.Height(22f))) vc.Save();
                if (!vc.IsDefault && GUILayout.Button("기본값으로", GUILayout.Width(90f), GUILayout.Height(22f)))
                    vc.ResetToDefaults();
                GUILayout.EndHorizontal();

                vc.nearClip = Fine("근평면 near",    vc.nearClip, 0.005f, 0.5f, 0.005f, "0.000");
                vc.pullBack = Fine("카메라 후퇴(m)", vc.pullBack, 0f,     3f,   0.05f,  "0.00");
                vc.autoFov  = GUILayout.Toggle(vc.autoFov, " 후퇴만큼 FOV 자동 보정");
                if (vc.autoFov) vc.refDist = FSlider("크기 기준 거리(m)", vc.refDist, 0.2f, 1.5f);
            }

            GUILayout.EndScrollView();

            GUILayout.Space(6f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("다시 재생", GUILayout.Height(26f)))
            {
                if (!pp.Replay()) Debug.Log("[F3] 먼저 포즈 시퀀스를 한 번 재생하십시오.");
            }
            if (GUILayout.Button("정지", GUILayout.Height(26f))) pp.Stop();
            GUILayout.EndHorizontal();

            GUILayout.Label("<size=10>구간별 상세 튜닝·저장은 F4.</size>", Rich());
            GUILayout.EndArea();
        }

        static float FSlider(string label, float v, float min, float max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{label}: {v:0.00}", GUILayout.Width(160f));
            float nv = GUILayout.HorizontalSlider(v, min, max);
            GUILayout.EndHorizontal();
            return nv;
        }

        /// <summary>슬라이더 + −/+ 미세조정 버튼.</summary>
        static float Fine(string label, float v, float min, float max, float step, string fmt)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{label}: {v.ToString(fmt)}", GUILayout.Width(150f));
            if (GUILayout.Button("−", GUILayout.Width(22f))) v -= step;
            v = GUILayout.HorizontalSlider(v, min, max);
            if (GUILayout.Button("+", GUILayout.Width(22f))) v += step;
            GUILayout.EndHorizontal();
            return Mathf.Clamp(v, min, max);
        }

        /// <summary>진단 한 줄 — 통과면 초록 ✔, 실패면 빨강 ✘ + 조치 안내.</summary>
        static string Chk(bool ok, string okText, string ngText) =>
            ok ? $"<size=11><color=#80e080>✔ {okText}</color></size>"
               : $"<size=11><color=#ff8080>✘ {ngText}</color></size>";

        static GUIStyle Rich() => new GUIStyle(GUI.skin.label) { richText = true };
    }

    /// <summary>Play 시 자동 부착.</summary>
    public static class PoseTunePanelBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Object.FindFirstObjectByType<PoseTunePanel>() == null)
                new GameObject("[PoseTunePanel]").AddComponent<PoseTunePanel>();
        }
    }
}
