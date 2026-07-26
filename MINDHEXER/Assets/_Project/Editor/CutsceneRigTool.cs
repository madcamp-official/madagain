using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using UnityEditor;
using Unity.Cinemachine;

namespace Game.EditorTools
{
    /// <summary>
    /// 1인칭 컷신 authoring 환경을 현재 씬에 한 번에 설치한다.
    /// CutsceneManager(다른 세션 작업)가 이름으로 찾는 오브젝트들을 규격대로 만들어 준다.
    ///
    /// 설치물:
    ///   CutsceneRig            — 재생 시작 시 현재 카메라 pose로 스냅되는 기준 리그
    ///     └ CutsceneVCam       — 컷신 카메라(CinemachineCamera + Animator). Timeline이 이걸 구동
    ///   CutsceneDirector       — PlayableDirector + Timeline 에셋(Animation Track이 VCam에 바인딩)
    ///   [CutsceneManager]      — 재생/잠금/핸드오프 + 임펄스
    ///
    /// 왜 vcam 하나인가: 고품질 1인칭 컷신은 vcam 여러 대 블렌드가 아니라
    ///   "카메라 한 대의 모션을 손 키프레임으로 authoring"하는 방식이다(예비동작·이징·오버슛).
    /// </summary>
    public static class CutsceneRigTool
    {
        const string RigName      = "CutsceneRig";
        const string VCamName     = "CutsceneVCam";
        const string DirectorName = "CutsceneDirector";
        const string ManagerName  = "[CutsceneManager]";
        const string CutsceneDir  = "Assets/_Project/Cutscenes";

        [MenuItem("Tools/컷신/① 현재 씬에 컷신 리그 설치")]
        public static void InstallRig()
        {
            // ── 리그 + vcam ──
            var rig = GameObject.Find(RigName) ?? new GameObject(RigName);
            Undo.RegisterCreatedObjectUndo(rig, "컷신 리그 설치");

            Transform vcamT = rig.transform.Find(VCamName);
            GameObject vcamGo = vcamT != null ? vcamT.gameObject : new GameObject(VCamName);
            vcamGo.transform.SetParent(rig.transform, false);
            vcamGo.transform.localPosition = Vector3.zero;
            vcamGo.transform.localRotation = Quaternion.identity;

            var vcam = vcamGo.GetComponent<CinemachineCamera>() ?? vcamGo.AddComponent<CinemachineCamera>();
            vcam.Priority.Value = -10;   // 평소엔 게임플레이 vcam이 이김. 재생 시 매니저가 올림

            // Animation Track이 붙으려면 Animator가 필요하다(Timeline이 이 Animator를 통해 구동)
            var animator = vcamGo.GetComponent<Animator>() ?? vcamGo.AddComponent<Animator>();
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            // ── 디렉터 + 타임라인 ──
            var dirGo = GameObject.Find(DirectorName) ?? new GameObject(DirectorName);
            var director = dirGo.GetComponent<PlayableDirector>() ?? dirGo.AddComponent<PlayableDirector>();
            director.playOnAwake = false;                       // ★ 자동재생 금지(안 끄면 씬 열자마자 재생)
            director.extrapolationMode = DirectorWrapMode.None; // 끝나면 정지 → stopped 이벤트로 핸드오프

            if (director.playableAsset == null)
            {
                var timeline = CreateTimeline("Cutscene_봉인해제");
                director.playableAsset = timeline;
                BindFirstAnimationTrack(director, timeline, animator);
            }

            // ── 매니저 ──
            var mgr = Object.FindFirstObjectByType<Game.View.CutsceneManager>();
            if (mgr == null)
            {
                var mgrGo = new GameObject(ManagerName);
                mgr = mgrGo.AddComponent<Game.View.CutsceneManager>();
                mgrGo.AddComponent<CinemachineImpulseSource>();   // 착지 "쿵"
            }
            else if (mgr.GetComponent<CinemachineImpulseSource>() == null)
                mgr.gameObject.AddComponent<CinemachineImpulseSource>();

            EditorUtility.SetDirty(rig);
            EditorUtility.SetDirty(dirGo);
            Selection.activeGameObject = dirGo;

            Debug.Log(
                "[컷신] 리그 설치 완료.\n" +
                "  1) CutsceneDirector 선택 → Window/Sequencing/Timeline 열기\n" +
                "  2) CutsceneVCam 트랙에서 녹화(●) 켜고 Game 뷰 보며 키프레임\n" +
                "  3) Play 후 C 키로 재생 (또는 콘솔 `cut play`)");
        }

        [MenuItem("Tools/컷신/② 새 컷신 타임라인 만들기")]
        public static void NewTimeline()
        {
            var dirGo = GameObject.Find(DirectorName);
            if (dirGo == null) { Debug.LogError("[컷신] CutsceneDirector 없음 — ① 먼저 실행하십시오."); return; }
            var director = dirGo.GetComponent<PlayableDirector>();
            var animator = GameObject.Find(RigName)?.transform.Find(VCamName)?.GetComponent<Animator>();
            if (director == null || animator == null) { Debug.LogError("[컷신] 리그가 불완전합니다 — ①을 다시 실행하십시오."); return; }

            string name = "Cutscene_" + System.DateTime.Now.ToString("HHmmss");
            var timeline = CreateTimeline(name);
            director.playableAsset = timeline;
            BindFirstAnimationTrack(director, timeline, animator);
            EditorUtility.SetDirty(director);
            Selection.activeObject = timeline;
            Debug.Log($"[컷신] 새 타임라인 생성 후 디렉터에 연결: {name}\n  다른 컷신으로 바꾸려면 디렉터의 Playable 슬롯에 원하는 타임라인을 넣으십시오.");
        }

        [MenuItem("Tools/컷신/③ 컷신 폴더 열기")]
        public static void PingFolder()
        {
            EnsureFolder(CutsceneDir);
            var obj = AssetDatabase.LoadAssetAtPath<Object>(CutsceneDir);
            if (obj != null) { Selection.activeObject = obj; EditorGUIUtility.PingObject(obj); }
        }

        /// <summary>Animation Track 하나를 가진 타임라인 에셋 생성.</summary>
        static TimelineAsset CreateTimeline(string name)
        {
            EnsureFolder(CutsceneDir);
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            string path = AssetDatabase.GenerateUniqueAssetPath($"{CutsceneDir}/{name}.playable");
            AssetDatabase.CreateAsset(timeline, path);
            timeline.CreateTrack<AnimationTrack>(null, VCamName);
            AssetDatabase.SaveAssets();
            return timeline;
        }

        /// <summary>타임라인의 첫 Animation Track을 vcam의 Animator에 바인딩.</summary>
        static void BindFirstAnimationTrack(PlayableDirector director, TimelineAsset timeline, Animator animator)
        {
            foreach (var t in timeline.GetOutputTracks())
                if (t is AnimationTrack)
                {
                    director.SetGenericBinding(t, animator);
                    break;
                }
            EditorUtility.SetDirty(director);
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf   = System.IO.Path.GetFileName(path);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
