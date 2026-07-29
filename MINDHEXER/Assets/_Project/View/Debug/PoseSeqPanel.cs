using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.View
{
    /// <summary>
    /// F4 — 시퀀스 세그먼트 튜닝 패널. 현재 재생 시퀀스의 "포즈 사이 간격"을 구간별로 각각 조절한다.
    /// 스프링·정지·전체 배속까지 여기서 전부. 값 바꾸고 "다시 재생"으로 즉시 확인.
    /// (Precog에서 포팅 — 원래 F3이었으나 F3이 포즈 재생 패널로 밀려 F4로 옮김)
    ///
    /// 단축키 배치: F1 = 이동 튜닝 / F2 = 조명 튜닝 / F3 = 포즈 재생 / <b>F4 = 세그먼트별</b>
    /// </summary>
    public class PoseSeqPanel : MonoBehaviour
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
            if (kb.f4Key.wasPressedThisFrame)
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
            const float W = 400f;
            float H = Mathf.Min(Screen.height - 24f, 460f);
            GUILayout.BeginArea(new Rect(Screen.width - W - 12f, 12f, W, H), GUI.skin.box);
            GUILayout.Label("<b>시퀀스 세그먼트 튜닝 (F4)</b>", Rich());

            if (pp == null) { GUILayout.Label("PosePlayer 없음"); GUILayout.EndArea(); return; }
            if (pp.SeqNames == null || pp.SeqNames.Count < 2)
            {
                GUILayout.Label("재생된 시퀀스가 없습니다.\n먼저 포즈 시퀀스를 한 번 재생하십시오\n(PoseInputBinder 바인딩 키 또는 콘솔).");
                GUILayout.EndArea(); return;
            }

            // 총 시간 표시
            float total = pp.holdLastPose;
            for (int i = 0; i < pp.segTimes.Count; i++)
                if (!pp.IsSnapSeg(i) && !(pp.snapReturn && i == pp.segTimes.Count - 1))
                    total += pp.segTimes[i];
            GUILayout.Label($"<size=11>{(pp.HasSavedTiming() ? "<color=#80e080>저장됨</color>" : "<color=#c0c0c0>미저장</color>")}" +
                            $"  ·  총 {total:0.000}초  ({total * 60f:0.0}프레임 @60)</size>", Rich());

            scroll = GUILayout.BeginScrollView(scroll);

            // ── 구간별 시간 + 이징 ──
            int segCount = pp.SeqNames.Count - 1;
            pp.EnsureSegLists(segCount);

            for (int i = 0; i < segCount; i++)
            {
                string a = pp.SeqNames[i], b = pp.SeqNames[i + 1];
                bool isReturn = (i == segCount - 1) && pp.snapReturn;
                if (pp.IsSnapSeg(i) || isReturn)
                {
                    // 순간이동 구간 — 블렌드 없이 뚝 끊김(조절 대상 아님)
                    GUILayout.Label($"<color=#ffb060>{a} ━━▶ {b}   순간이동</color>", Rich());
                    continue;
                }

                GUILayout.Space(3f);
                GUILayout.Label($"<b>{a} → {b}</b>", Rich());
                pp.segTimes[i] = FineSlider("  시간(초)", pp.segTimes[i], 0.02f, 1.0f);

                // 이징 종류
                int cur = pp.GetEase(i);
                int now = GUILayout.Toolbar(cur, PosePlayer.EaseNames);
                if (now != cur) pp.SetEase(i, now);

                // 종류에 맞는 설정만 보여준다
                switch (now)
                {
                    case PosePlayer.EzIn:
                    case PosePlayer.EzOut:
                    case PosePlayer.EzInOut:
                        pp.SetPower(i, FineSlider("  가속 강도", pp.GetPower(i), 1f, 6f, 0.1f, "0.0"));
                        GUILayout.Label($"<size=10>  {EaseHint(now)}</size>", Rich());
                        break;
                    case PosePlayer.EzSpring:
                        pp.SetDamp(i, FineSlider("  감쇠(잦아듦)", pp.GetDamp(i), 1f, 15f, 0.5f, "0.0"));
                        pp.SetFreq(i, FineSlider("  진동수(튕김)", pp.GetFreq(i), 4f, 30f, 1f, "0.0"));
                        GUILayout.Label("<size=10>  목표를 살짝 지나쳤다가 되돌아와 정착</size>", Rich());
                        break;
                    case PosePlayer.EzSnap:
                        GUILayout.Label("<size=10>  <color=#ffb060>구간 끝까지 이전 포즈 유지 → 끝에서 뚝</color></size>", Rich());
                        break;
                    default:
                        GUILayout.Label("<size=10>  일정한 속도</size>", Rich());
                        break;
                }
            }

            GUILayout.Space(4f);
            GUILayout.Label("<b>정지·복귀</b>", Rich());
            pp.holdLastPose = FineSlider("마지막 포즈 정지", pp.holdLastPose, 0f, 1.0f);
            pp.snapReturn = GUILayout.Toggle(pp.snapReturn,
                pp.snapReturn ? " 기본포즈 복귀 = <color=#ffb060>순간이동</color>" : " 기본포즈 복귀 = 블렌드", Rich());

            GUILayout.Space(4f);
            GUILayout.Label("<b>전 구간 일괄 이징</b>", Rich());
            GUILayout.BeginHorizontal();
            for (int e = 0; e < PosePlayer.EaseNames.Length; e++)
                if (GUILayout.Button(PosePlayer.EaseNames[e]))
                    for (int i = 0; i < segCount; i++) pp.SetEase(i, e);
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);
            GUILayout.Label("<b>일괄 배속</b>", Rich());
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("×0.5")) Scale(pp, 0.5f);
            if (GUILayout.Button("×0.8")) Scale(pp, 0.8f);
            if (GUILayout.Button("×1.25")) Scale(pp, 1.25f);
            if (GUILayout.Button("×2.0")) Scale(pp, 2f);
            GUILayout.EndHorizontal();

            GUILayout.EndScrollView();

            // ── 저장 ──
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("<b>이 애니메이션 타이밍 저장</b>", Rich(), GUILayout.Height(26f)))
                Debug.Log(pp.SaveTiming() ? "[F4] 타이밍 저장: " + pp.CurrentKey : "[F4] 저장 실패");
            if (GUILayout.Button("불러오기", GUILayout.Width(80f), GUILayout.Height(26f)))
            { PosePlayer.ReloadTimings(); pp.Replay(); }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("다시 재생", GUILayout.Height(28f))) pp.Replay();
            if (GUILayout.Button("정지", GUILayout.Height(28f))) pp.Stop();
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        static void Scale(PosePlayer pp, float m)
        {
            for (int i = 0; i < pp.segTimes.Count; i++)
                pp.segTimes[i] = Mathf.Clamp(pp.segTimes[i] * m, 0.02f, 1.0f);
        }

        /// <summary>이징 종류별 한 줄 설명 — 어떤 느낌인지 바로 알게.</summary>
        static string EaseHint(int type) =>
            type == PosePlayer.EzIn    ? "천천히 시작 → 빠르게 (뻗기 시작)"
          : type == PosePlayer.EzOut   ? "빠르게 시작 → 천천히 (도착 후 감속)"
          : type == PosePlayer.EzInOut ? "양끝 느리고 가운데 빠름 (부드러운 이동)"
          : "";

        /// <summary>슬라이더 + −/+ 미세조정 버튼. 정밀한 타이밍 튜닝용.</summary>
        static float FineSlider(string label, float v, float min, float max, float step = 0.01f, string fmt = "0.000")
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{label}: {v.ToString(fmt)}", GUILayout.Width(185f));
            if (GUILayout.Button("−", GUILayout.Width(22f))) v -= step;
            v = GUILayout.HorizontalSlider(v, min, max);
            if (GUILayout.Button("+", GUILayout.Width(22f))) v += step;
            GUILayout.EndHorizontal();
            return Mathf.Clamp(v, min, max);
        }

        static GUIStyle Rich() => new GUIStyle(GUI.skin.label) { richText = true };
    }

    /// <summary>Play 시 자동 부착.</summary>
    public static class PoseSeqPanelBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Object.FindFirstObjectByType<PoseSeqPanel>() == null)
                new GameObject("[PoseSeqPanel]").AddComponent<PoseSeqPanel>();
        }
    }
}
