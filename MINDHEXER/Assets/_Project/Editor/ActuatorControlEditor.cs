using UnityEditor;
using UnityEngine;
using Game.View;

namespace Game.EditorTools
{
    /// <summary>
    /// <see cref="ActuatorControl"/> 인스펙터 — 자식 <see cref="TelescopingActuator"/>의 <b>행정·시작 상태를
    /// 여기서 바로</b> 편집한다.
    ///
    /// <para><b>왜 필요한가</b>: 두 컴포넌트가 서로 다른 오브젝트에 있어야 한다.
    /// <see cref="TelescopingActuator"/>는 Anchor/Shaft/Head 참조 때문에 <b>모델 프리팹 안</b>에,
    /// <see cref="ActuatorControl"/>은 <see cref="Hackable"/>과 같은 오브젝트여야 한다
    /// (<see cref="HackDriver"/>가 <c>GetComponent</c>로 찾는다). 그 결과 "프레스를 얼마나 내려오게 할까"를
    /// 조절하려면 매번 중첩 프리팹 자식을 찾아 들어가야 했다.</para>
    ///
    /// <para><b>값을 복제하지 않는다.</b> 필드를 여기에 또 만들면 둘이 반드시 어긋난다 — 자식 컴포넌트의
    /// <see cref="SerializedObject"/>를 직접 그린다. 여기서 바꾸든 자식에서 바꾸든 같은 필드다.</para>
    /// </summary>
    [CustomEditor(typeof(ActuatorControl))]
    public class ActuatorControlEditor : Editor
    {
        SerializedObject _actSo;
        TelescopingActuator _act;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var ctl = (ActuatorControl)target;
            var act = ctl.actuator != null ? ctl.actuator : ctl.GetComponentInChildren<TelescopingActuator>(true);

            EditorGUILayout.Space(6f);
            if (act == null)
            {
                EditorGUILayout.HelpBox(
                    "자식에서 TelescopingActuator를 찾지 못했습니다.\n" +
                    "모델 프리팹(Model_Piston / Model_Presser)이 자식으로 들어 있어야 합니다.",
                    MessageType.Warning);
                return;
            }

            if (_act != act || _actSo == null) { _act = act; _actSo = new SerializedObject(act); }
            _actSo.Update();

            EditorGUILayout.LabelField($"신축부 — {act.gameObject.name}", EditorStyles.boldLabel);

            // 이 액추에이터가 '공유 프리팹 안'에 있으면, 여기서 고친 값이 그 프리팹을 쓰는
            // 다른 개체까지 바꾸는지 여부가 갈린다. 모르고 고치면 5개가 같이 움직인다.
            WarnIfShared(act);

            EditorGUI.indentLevel++;
            Field("startExtension", "시작 신장 (0=수축, 1=신장)");
            Field("strokeMin", "최소 행정 (m, 월드)");
            Field("strokeMax", "최대 행정 (m, 월드)");
            Field("linearDuration", "홀드 0→1 시간 (초)");
            Field("flickDuration", "플릭 0→1 시간 (초)");
            Field("axis", "축 (신장 방향)");
            EditorGUI.indentLevel--;

            EditorGUILayout.HelpBox(
                $"현재 행정 {act.strokeMin:0.##} ~ {act.strokeMax:0.##} m " +
                $"(총 {act.strokeMax - act.strokeMin:0.##} m)\n" +
                "★ 단위는 월드 미터다 — 오브젝트 스케일을 키워도 이 값은 따라 커지지 않는다. " +
                "큰 프레스를 만들었다면 여기도 같이 키울 것.",
                MessageType.None);

            if (GUILayout.Button("신축부 선택 (자식으로 이동)"))
                Selection.activeGameObject = act.gameObject;

            _actSo.ApplyModifiedProperties();
        }

        void Field(string name, string label)
        {
            var p = _actSo.FindProperty(name);
            if (p != null) EditorGUILayout.PropertyField(p, new GUIContent(label));
        }

        /// <summary>
        /// 값이 <b>어디에 저장되는지</b> 알려 준다. 중첩 프리팹 안의 컴포넌트라 무심코 고치면
        /// 그 모델 프리팹을 쓰는 모든 개체가 같이 바뀐다.
        /// </summary>
        static void WarnIfShared(TelescopingActuator act)
        {
            var src = PrefabUtility.GetCorrespondingObjectFromSource(act.gameObject);
            if (src == null) return;   // 씬에 직접 놓인 것 — 이 개체만 바뀐다

            string path = AssetDatabase.GetAssetPath(src);
            if (string.IsNullOrEmpty(path)) return;

            bool inScene = act.gameObject.scene.IsValid();
            EditorGUILayout.HelpBox(
                inScene
                    ? $"이 신축부는 중첩 프리팹({System.IO.Path.GetFileName(path)}) 인스턴스다.\n" +
                      "여기서 고치면 이 개체의 오버라이드가 되어 이 개체만 바뀐다."
                    : $"지금 편집 중인 것은 프리팹 애셋({System.IO.Path.GetFileName(path)})이다.\n" +
                      "★ 여기서 고치면 이 프리팹을 쓰는 모든 개체가 같이 바뀐다.",
                inScene ? MessageType.Info : MessageType.Warning);
        }
    }
}
