using UnityEditor;
using UnityEngine;
using Game.View;

namespace Game.EditorTools
{
    /// <summary>
    /// 보스 죽음 연출의 <b>아레나 전경 카메라 구도</b>를 잡는 툴. 씬에 <see cref="BossDeathCam"/> 마커를
    /// 만들어(없으면) 선택하고, 사용자가 씬 뷰에서 위치·회전·FOV로 구도를 맞춘 뒤 씬을 저장한다.
    /// 런타임엔 BossDeathDirector가 이 마커의 포즈·FOV로 카메라를 옮긴다.
    ///
    /// ★ 씬 데이터만 만든다(마커 1개). 저장은 평소처럼 씬 저장(Ctrl+S).
    /// </summary>
    public static class BossDeathCameraTool
    {
        const string CamName = "Arena4DeathCam";

        [MenuItem("Tools/보스 죽음 카메라 설정", false, 40)]
        static void SetupOrSelect()
        {
            var cam = BossDeathCam.Find();
            if (cam == null)
            {
                var go = new GameObject(CamName);
                cam = go.AddComponent<BossDeathCam>();
                // 현재 씬 뷰 시점을 초기 구도로(있으면). 없으면 위에서 내려다보는 기본값.
                var sv = SceneView.lastActiveSceneView;
                if (sv != null && sv.camera != null)
                {
                    go.transform.position = sv.camera.transform.position;
                    go.transform.rotation = sv.camera.transform.rotation;
                }
                else
                {
                    go.transform.position = new Vector3(0f, 22f, -24f);
                    go.transform.rotation = Quaternion.Euler(30f, 0f, 0f);
                }
                Undo.RegisterCreatedObjectUndo(go, "Create " + CamName);
                Debug.Log("[보스 죽음 카메라] '" + CamName + "' 생성. 씬 뷰에서 위치·회전·Inspector의 FOV로 구도를 잡고 " +
                          "씬을 저장하세요. 구도 확인: Tools ▸ '죽음 카메라로 씬뷰 정렬'.");
            }
            else
            {
                Debug.Log("[보스 죽음 카메라] 기존 마커를 선택했습니다.");
            }
            Selection.activeGameObject = cam.gameObject;
            EditorGUIUtility.PingObject(cam.gameObject);
        }

        [MenuItem("Tools/죽음 카메라로 씬뷰 정렬", false, 41)]
        static void AlignSceneView()
        {
            var cam = BossDeathCam.Find();
            if (cam == null)
            {
                Debug.LogWarning("[보스 죽음 카메라] 마커가 없습니다. 먼저 Tools ▸ '보스 죽음 카메라 설정'.");
                return;
            }
            var sv = SceneView.lastActiveSceneView;
            if (sv == null) { Debug.LogWarning("[보스 죽음 카메라] 활성 씬 뷰가 없습니다."); return; }

            // 씬 뷰를 마커 포즈에 근접시켜 '그 카메라로 보는' 구도를 미리본다(근사 — FOV는 씬뷰 값).
            Transform t = cam.transform;
            const float pivotDist = 8f;
            sv.rotation = t.rotation;
            sv.pivot = t.position + t.forward * pivotDist;
            sv.size = pivotDist;
            sv.Repaint();
            Debug.Log("[보스 죽음 카메라] 씬 뷰를 마커 포즈로 정렬(미리보기). 프러스텀 기즈모가 실제 화면 범위입니다.");
        }
    }
}
