using UnityEditor;
using UnityEngine;
using Game.View;

namespace Game.EditorTools
{
    /// <summary>
    /// 회전 플랫폼 authoring 보조 — Scene 뷰에서 rangeMinDeg~rangeMaxDeg 사이를 부채꼴로 그려
    /// 회전 가능 범위를 눈으로 확인한다. (RailSet의 이동범위 라인 표시와 동일한 역할)
    /// 값 편집은 Inspector 숫자 필드로 한다(각도 드래그 핸들은 아직 없음).
    /// </summary>
    [CustomEditor(typeof(RotationPlatform))]
    public class RotationPlatformEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var rp = (RotationPlatform)target;

            EditorGUILayout.Space();
            float span = rp.rangeMaxDeg - rp.rangeMinDeg;
            EditorGUILayout.HelpBox(
                $"회전 폭 {span:0.#}도 (부착각 기준 {rp.rangeMinDeg:0.#} ~ {rp.rangeMaxDeg:0.#})\n" +
                $"플릭 격자 {rp.flickStepDeg:0.#}도 단위. 홀드(크립)는 격자에 안 묶임.",
                MessageType.Info);

            if (rp.riderRoot == null)
                EditorGUILayout.HelpBox("riderRoot가 비어 있습니다. 실제로 회전할 자식 트랜스폼을 물리십시오.", MessageType.Warning);
        }

        void OnSceneGUI()
        {
            var rp = (RotationPlatform)target;
            if (rp.riderRoot == null) return;

            Vector3 center = rp.riderRoot.position;
            Vector3 normal = rp.transform.TransformDirection(rp.AxisLocal).normalized;
            Vector3 fromDir = ReferenceDir(normal);
            float radius = HandleUtility.GetHandleSize(center) * 1.2f;

            // 0도(부착 각도) 기준선
            Handles.color = new Color(1f, 1f, 1f, 0.5f);
            Handles.DrawLine(center, center + fromDir * radius);

            // min~max 부채꼴
            Vector3 minDir = Quaternion.AngleAxis(rp.rangeMinDeg, normal) * fromDir;
            Handles.color = new Color(0.2f, 1f, 0.6f, 0.25f);
            Handles.DrawSolidArc(center, normal, minDir, rp.rangeMaxDeg - rp.rangeMinDeg, radius);

            Handles.color = Color.cyan;
            Handles.DrawWireArc(center, normal, minDir, rp.rangeMaxDeg - rp.rangeMinDeg, radius, 2.5f);

            // 양 끝 표시
            Handles.color = Color.yellow;
            float hs = radius * 0.08f;
            Vector3 maxDir = Quaternion.AngleAxis(rp.rangeMaxDeg, normal) * fromDir;
            Handles.SphereHandleCap(0, center + minDir * radius, Quaternion.identity, hs, EventType.Repaint);
            Handles.SphereHandleCap(0, center + maxDir * radius, Quaternion.identity, hs, EventType.Repaint);
            Handles.Label(center + minDir * radius, $"{rp.rangeMinDeg:0.#}°");
            Handles.Label(center + maxDir * radius, $"{rp.rangeMaxDeg:0.#}°");

            // 45도 플릭 격자 눈금
            if (rp.flickStepDeg > 1e-3f)
            {
                Handles.color = new Color(1f, 1f, 1f, 0.4f);
                int lo = Mathf.CeilToInt(rp.rangeMinDeg / rp.flickStepDeg);
                int hi = Mathf.FloorToInt(rp.rangeMaxDeg / rp.flickStepDeg);
                for (int i = lo; i <= hi; i++)
                {
                    Vector3 dir = Quaternion.AngleAxis(i * rp.flickStepDeg, normal) * fromDir;
                    Vector3 p = center + dir * radius;
                    Handles.SphereHandleCap(0, p, Quaternion.identity, radius * 0.03f, EventType.Repaint);
                }
            }

            // 현재 각도 표시
            Vector3 curDir = Quaternion.AngleAxis(rp.AngleOffset, normal) * fromDir;
            Handles.color = Color.red;
            Handles.DrawLine(center, center + curDir * radius * 1.1f, 3f);
        }

        /// <summary>축에 수직인 임의의 기준 방향(부채꼴 그리기용 시작 벡터).</summary>
        static Vector3 ReferenceDir(Vector3 normal)
        {
            Vector3 up = Mathf.Abs(Vector3.Dot(normal, Vector3.forward)) < 0.99f ? Vector3.forward : Vector3.right;
            return Vector3.ProjectOnPlane(up, normal).normalized;
        }
    }
}
