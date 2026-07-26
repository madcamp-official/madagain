using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using Game.View;

namespace Game.EditorTools
{
    /// <summary>
    /// 컷신·칼 애니메이션 authoring 전용 씬을 생성한다.
    /// 메뉴 한 번으로 재생성 가능(맵·조명·카메라·뷰모델·스폰끄기·봉인박스 포함).
    ///
    /// 핵심: Main Camera의 자식으로 KatanaViewmodel을 붙여두므로,
    ///   씬을 열면 Game 뷰가 곧 1인칭 화면 → 그 화면을 보며 키프레임을 찍을 수 있다.
    ///   SwordView는 씬에 이미 있는 뷰모델을 재사용하므로 Play해도 칼이 중복되지 않는다.
    /// </summary>
    public static class CutsceneStudioTool
    {
        const string SceneDir  = "Assets/Scenes/test";
        const string ScenePath = SceneDir + "/Cutscene_Studio.unity";
        const string ViewmodelPrefab = "Assets/_Project/Prefabs/Resources/KatanaViewmodel.prefab";

        [MenuItem("Tools/컷신/컷신 스튜디오 씬 생성")]
        static void CreateStudioScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;   // 현재 씬 저장 확인

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            Material floorMat = Mat(new Color(0.58f, 0.61f, 0.68f));
            Material wallMat  = Mat(new Color(0.34f, 0.36f, 0.44f));
            Material propMat  = Mat(new Color(0.26f, 0.28f, 0.36f));

            // ── 맵: 바닥 + 벽 4면(박스 룸) ──
            const float room = 24f, wallH = 6f, thick = 0.5f, half = room * 0.5f;
            Box("Floor",  new Vector3(0f, -0.25f, 0f),            new Vector3(room, 0.5f, room),  floorMat);
            Box("Wall_N", new Vector3(0f, wallH * 0.5f,  half),   new Vector3(room, wallH, thick), wallMat);
            Box("Wall_S", new Vector3(0f, wallH * 0.5f, -half),   new Vector3(room, wallH, thick), wallMat);
            Box("Wall_E", new Vector3( half, wallH * 0.5f, 0f),   new Vector3(thick, wallH, room), wallMat);
            Box("Wall_W", new Vector3(-half, wallH * 0.5f, 0f),   new Vector3(thick, wallH, room), wallMat);

            // ── 조명 ──
            var lgo = new GameObject("Directional Light");
            var lt = lgo.AddComponent<Light>();
            lt.type = LightType.Directional;
            lt.intensity = 1.2f;
            lt.shadows = LightShadows.Soft;
            lgo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            // ── 카메라 + 1인칭 뷰모델(자식) ──
            var cgo = new GameObject("Main Camera");
            cgo.tag = "MainCamera";
            var cam = cgo.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f;
            cam.fieldOfView   = 60f;
            cgo.AddComponent<AudioListener>();
            cgo.transform.position = new Vector3(0f, 1.25f, -8f);   // 방 안, 봉인박스를 바라봄
            cgo.transform.rotation = Quaternion.identity;

            var vmPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ViewmodelPrefab);
            if (vmPrefab != null)
            {
                var vm = (GameObject)PrefabUtility.InstantiatePrefab(vmPrefab);
                vm.transform.SetParent(cgo.transform, false);
                vm.transform.localPosition = Vector3.zero;
                vm.transform.localRotation = Quaternion.identity;
            }
            else Debug.LogWarning($"[컷신 스튜디오] 뷰모델 프리팹 없음: {ViewmodelPrefab}");

            // ── 몹 스폰 끄기(작업 방해 방지) ──
            var sgo = new GameObject("SpawnConfig");
            var sc = sgo.AddComponent<MapSpawnConfig>();
            sc.autoSpawnOnStart = false;

            // ── 봉인 박스(봉인해제 컷신용 임시 프롭) ──
            Box("Restraint", new Vector3(0f, 2.5f, 0f), new Vector3(3f, 5f, 2f), propMat);

            // ── 컷신 리그(Rig/VCam/Director/Manager + 타임라인)까지 설치 ──
            CutsceneRigTool.InstallRig();

            // ── 저장 ──
            System.IO.Directory.CreateDirectory(SceneDir);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();

            Debug.Log($"[컷신 스튜디오] 생성 완료: {ScenePath}\n" +
                      "  Game 뷰 = 1인칭 화면.\n" +
                      "  · 칼 스윙 authoring: Main Camera > KatanaViewmodel > PoseTarget 에 키프레임(Ctrl+6)\n" +
                      "  · 컷신 authoring: CutsceneDirector 선택 → Timeline 창 → CutsceneVCam 트랙 녹화\n" +
                      "  · 재생 확인: Play 후 C 키");
        }

        /// <summary>콜라이더 포함 박스 생성(맵 지형용).</summary>
        static GameObject Box(string name, Vector3 pos, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.position   = pos;
            go.transform.localScale = size;
            if (mat != null) go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        static Material Mat(Color c)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(sh) { color = c };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            return m;
        }
    }
}
