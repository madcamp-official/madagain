using UnityEngine;
using UnityEditor;
using Game.View;

namespace Game.EditorTools
{
    /// <summary>
    /// 맨손 1인칭 손 IK 설치 — 카타나 전용 <c>GripSetupTool</c>(무기 그립 정렬 자동화)의
    /// 단순화된 대체품. 무기가 없으므로 그립 정렬 로직 없이, 손 IK 타겟만 만든다.
    ///
    /// 하는 일 (한쪽 팔당):
    ///   1) 팔 뼈(R_Upperarm/R_Forearm/R_Hand — 우리 리그의 "R_"/"L_" 접두사 규칙) 자동 탐색
    ///   2) HandTarget_R/L 빈 오브젝트를 현재 손 위치·회전에 생성 (뷰모델 루트의 자식)
    ///   3) ElbowPole_R/L 을 팔꿈치 기준 아래·뒤쪽에 생성
    ///   4) 손 뼈에 HandIK 컴포넌트를 추가하고 upper/lower/end/target/pole 배선 + 기준 캡처
    ///
    /// 이미 HandIK가 있으면 건드리지 않는다(중복 설치 방지).
    /// </summary>
    public static class HandIKSetupTool
    {
        [MenuItem("Tools/뷰모델/손 IK 설치 (맨손)")]
        public static void Install()
        {
            var root = FindViewmodel();
            if (root == null)
            {
                EditorUtility.DisplayDialog("실패",
                    $"씬에서 {ViewmodelCamera.ViewmodelRootName}을 찾지 못했습니다.\n" +
                    "뷰모델 오브젝트를 선택하고 다시 실행하거나, 이름을 확인하십시오.", "확인");
                return;
            }

            int done = 0;
            string report = "";
            foreach (bool left in new[] { false, true })
            {
                string side = left ? "L" : "R";
                Transform upper = FindBone(root, side + "_Upperarm");
                Transform lower = FindBone(root, side + "_Forearm");
                Transform hand  = FindBone(root, side + "_Hand");

                if (upper == null || lower == null || hand == null)
                {
                    report += $"\n  [{side}] 팔 뼈를 못 찾음 (upper={upper != null}, lower={lower != null}, hand={hand != null}) — 건너뜀";
                    continue;
                }

                var existing = hand.GetComponent<HandIK>();
                if (existing != null)
                {
                    report += $"\n  [{side}] 이미 HandIK가 있어 건너뜀 ({hand.name})";
                    continue;
                }

                Transform handTarget = FindOrCreate(root, "HandTarget_" + side);
                handTarget.SetPositionAndRotation(hand.position, hand.rotation);

                Transform pole = FindOrCreate(root, "ElbowPole_" + side);
                Vector3 elbowBack = -root.forward + Vector3.down * 0.5f;
                pole.position = lower.position + elbowBack.normalized * 0.3f;

                Undo.AddComponent<HandIK>(hand.gameObject);
                var ik = hand.GetComponent<HandIK>();
                Undo.RecordObject(ik, "손 IK 설치");
                ik.upper  = upper;
                ik.lower  = lower;
                ik.end    = hand;
                ik.target = handTarget;
                ik.pole   = pole;
                ik.Capture();
                EditorUtility.SetDirty(ik);

                report += $"\n  [{side}] 설치 완료 — upper={upper.name}, lower={lower.name}, end={hand.name}";
                done++;
            }

            Debug.Log($"[손 IK 설치] {done}개 손 설치{report}");
            EditorUtility.DisplayDialog("손 IK 설치", $"{done}개 손에 HandIK를 설치했습니다.{report}", "확인");
        }

        static Transform FindBone(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        static Transform FindOrCreate(Transform root, string name)
        {
            var existing = FindBone(root, name);
            if (existing != null) return existing;
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "손 IK 설치");
            go.transform.SetParent(root, false);
            return go.transform;
        }

        static Transform FindViewmodel()
        {
            if (Selection.activeTransform != null) return Selection.activeTransform;
            var cam = Camera.main;
            if (cam != null)
            {
                var t = cam.transform.Find(ViewmodelCamera.ViewmodelRootName);
                if (t != null) return t;
            }
            var go = GameObject.Find(ViewmodelCamera.ViewmodelRootName);
            return go != null ? go.transform : null;
        }
    }
}
