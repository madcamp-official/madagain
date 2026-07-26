using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Playables;
using Unity.Cinemachine;

namespace Game.View
{
    /// <summary>컷신 재생 여부(전역). Main의 시점·시뮬 게이트가 이 값을 읽어 조작을 완전히 잠근다.</summary>
    public static class Cutscene
    {
        public static bool Active;
    }

    /// <summary>
    /// 봉인해제 컷신 컨트롤러(Timeline/B, 단일 vcam + Animation Track 방식). ★ 화면연출 계층.
    /// 읽기 전용(SimWorld 직접 안 씀, 시작/끝 핸드오프만 Main 훅으로).
    ///
    /// 흐름: 키(C) → CutsceneRig를 현재 시점 pose로 스냅(이음새 제거) → 컷신 vcam 우선순위를 올려
    /// Brain이 그쪽을 잡음 → PlayableDirector가 Animation Track으로 컷신 vcam의 로컬 transform·FOV를
    /// 손 키프레임대로 구동("고개 돌리듯" 좌우 살핌 → 봉인 부숨 → 정면+전진) → director.stopped →
    /// 시선 인계 + 플레이어 박스 탈출 + vcam 우선순위 원복.
    ///
    /// 왜 단일 vcam인가: 고품질 1인칭 컷신은 vcam 여러 대 블렌드가 아니라 "카메라 하나의 모션을
    /// 손 키프레임 authoring"으로 만든다(이징·예비동작·오버슛). 그 키는 Unity Animation 창에서 찍는다.
    ///
    /// 조작 잠금: 재생 동안 Cutscene.Active=true → Main이 입력·1인칭 pose·sim 전진을 모두 정지.
    /// UI: 우하단에 재생 중 "● CUTSCENE" 표시.
    /// </summary>
    public class CutsceneManager : MonoBehaviour
    {
        // 씬 오브젝트를 이름으로 찾는다(MCP로 만들 때 동일 이름 사용).
        const string DirectorName = "CutsceneDirector";
        const string RigName      = "CutsceneRig";

        const int CutscenePriority = 100;   // 재생 중: 게임플레이 vcam(0)보다 높여 Brain이 컷신 vcam을 잡음
        const int IdlePriority     = -10;   // 평소: 게임플레이 vcam이 이김

        // 컷신 끝에 플레이어를 정면으로 이 거리만큼 내보낸다(박스 탈출). 애니메이션 최종 전진량과 맞출 것.
        [SerializeField] float forwardExit = 2.5f;
        // 착지 "쿵" 임펄스 세기(임시: 종료 시점에 1회. 정밀 프레임 동기화는 Signal로 이관 예정).
        [SerializeField] float landingImpulse = 1.2f;

        PlayableDirector         director;
        Transform                rig;
        CinemachineCamera        vcam;      // 컷신 카메라 1대(Animation Track이 이 transform·렌즈를 구동)
        CinemachineImpulseSource impulse;

        void Start()
        {
            var d = GameObject.Find(DirectorName);
            if (d != null) director = d.GetComponent<PlayableDirector>();
            var r = GameObject.Find(RigName);
            if (r != null) { rig = r.transform; vcam = r.GetComponentInChildren<CinemachineCamera>(); }
            impulse = GetComponent<CinemachineImpulseSource>();
            if (vcam != null) vcam.Priority.Value = IdlePriority;
            if (director != null) director.stopped += OnDirectorStopped;
        }

        void OnDestroy()
        {
            if (director != null) director.stopped -= OnDirectorStopped;
            Cutscene.Active = false;   // 씬 종료 시 잠금 해제 보장
        }

        void Update()
        {
            if (Cutscene.Active) return;   // 재생 중 재입력 무시
            var kb = Keyboard.current;
            if (kb == null || !kb.cKey.wasPressedThisFrame) return;
            if (CanPlay()) Play();
        }

        bool CanPlay()
        {
            var main = Main.Instance;
            return main != null && director != null && rig != null && vcam != null && main.GameplayVcam != null;
        }

        void Play()
        {
            var main = Main.Instance;
            var gp = main.GameplayVcam;
            // CutsceneRig를 현재 카메라 pose로 스냅 → 컷신 시작이 플레이어 시점과 정확히 이어짐.
            // 컷신 vcam은 이 rig의 자식이고 Animation Track이 로컬 transform을 구동 → 살핌이 현재 정면 기준 상대.
            rig.SetPositionAndRotation(gp.transform.position, gp.transform.rotation);

            // 렌즈(FOV·클립) 동기화: 컷신 vcam 기본 FOV(40)가 게임플레이(60)와 달라 확대돼 보이는 문제 해소.
            // 1인칭 컷신은 플레이어의 현재 렌즈를 그대로 이어받아야 이음새가 없다(gameplayFov 오버라이드도 반영).
            vcam.Lens = gp.Lens;

            // 컷신 vcam 우선순위를 올려 Brain이 그쪽을 잡게 한다(DefaultBlend=Cut이라 즉시 전환).
            vcam.Priority.Value = CutscenePriority;

            Cutscene.Active = true;
            director.time = 0;
            director.Play();
        }

        // >>> [이펙트/도구 세션 추가] 콘솔에서 컷신을 재생·중단하기 위한 공개 진입점.
        //     기존 흐름(C 키)은 그대로. 테스트 편의용이며 로직은 Play/EndCutscene 재사용.
        /// <summary>콘솔용: 즉시 재생. 준비가 안 됐거나 이미 재생 중이면 false.</summary>
        public bool PlayFromConsole()
        {
            if (Cutscene.Active || !CanPlay()) return false;
            Play();
            return true;
        }

        /// <summary>콘솔용: 즉시 중단(핸드오프까지 정상 수행).</summary>
        public void StopFromConsole()
        {
            if (!Cutscene.Active) return;
            if (director != null) director.Stop();   // stopped 이벤트 → EndCutscene
            else EndCutscene();
        }
        // <<< [추가 끝]

        void OnDirectorStopped(PlayableDirector d) => EndCutscene();

        void EndCutscene()
        {
            var main = Main.Instance;
            if (main != null)
            {
                // 카메라 핸드오프: 컷신 끝 시선을 입력에 인계 → 게임플레이 vcam이 같은 방향으로 복귀(스냅백 없음).
                var cam = main.Cam;
                if (cam != null)
                {
                    Vector3 e = cam.transform.rotation.eulerAngles;
                    main.SetLookPitch(e.x > 180f ? e.x - 360f : e.x);
                    main.SetLookYaw(e.y);
                }
                // 플레이어를 정면으로 내보냄(봉인 박스 탈출) → 복귀 카메라가 컷신 종료 위치와 이어짐.
                main.AdvancePlayerForward(forwardExit);
            }

            if (vcam != null) vcam.Priority.Value = IdlePriority;   // 게임플레이 vcam으로 복귀
            Cutscene.Active = false;
            if (impulse != null) impulse.GenerateImpulseWithForce(landingImpulse);   // 착지 쿵(임시방편: 종료 시점 1회)
        }

        void OnGUI()
        {
            if (!Cutscene.Active) return;
            // 개발 표시라 UI 숨김을 따른다 — 등장 컷신처럼 화면을 통째로 쓰는 연출 위에
            // 빨간 "● CUTSCENE"이 얹히면 그대로 완성 화면을 버린다.
            if (UiVisibility.Skip) return;
            const float w = 170f, h = 26f;
            var rect = new Rect(Screen.width - w - 14f, Screen.height - h - 14f, w, h);
            var style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleRight,
                richText  = true,
                fontSize  = 15,
                fontStyle = FontStyle.Bold,
            };
            GUI.Label(rect, "<color=#ff4444>● CUTSCENE</color>", style);
        }
    }

    /// <summary>Play 시 자동 부착(씬에 CutsceneManager 컴포넌트가 없을 때만). MCP로 붙이면 중복 안 됨.</summary>
    public static class CutsceneManagerBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Object.FindFirstObjectByType<CutsceneManager>() == null)
                new GameObject("[CutsceneManager]").AddComponent<CutsceneManager>();
        }
    }
}
