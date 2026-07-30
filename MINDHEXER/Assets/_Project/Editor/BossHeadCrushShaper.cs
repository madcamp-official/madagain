using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Game.View;

namespace Game.EditorTools
{
    /// <summary>
    /// 보스 머리 찌그러짐 자세를 <b>자동으로 만든다</b>. (보스전_설계 §3)
    ///
    /// <para>판때기 23개를 손으로 잡는 대신, 파츠마다 같은 규칙을 적용해 계산으로 만든다.
    /// 찌그러짐은 본질적으로 "누름축으로 눌리고 그 수직으로 퍼지고 바깥일수록 젖혀지는" 변형이라
    /// 규칙으로 표현된다.</para>
    ///
    /// <code>
    /// 누름축 성분   × squash   (1 미만 = 눌림)
    /// 수직 성분     × bulge    (1 초과 = 옆으로 퍼짐)
    /// 젖힘          중심에서 먼 파츠일수록 크게
    /// 지터          고정 시드 — 균일하면 '풍선'처럼 보인다
    /// </code>
    ///
    /// <para><b>만들 자세는 둘뿐이다.</b> <c>crush</c>(가장 찌그러진 모습)와 <c>flat</c>(완전히 납작).
    /// 중간 단계는 <see cref="BossHeadCrush"/>가 <c>Lerp(home, crush, stage/stageCount)</c>로
    /// 보간하므로 <b>스테이지가 몇 개든 자세는 추가로 안 만든다.</b></para>
    ///
    /// <para><b>순서</b>: 홈 캡처 → 값 조절하며 [모양 적용]으로 눈으로 확인 → [crush로 캡처] →
    /// 값을 더 세게 → [flat으로 캡처] → <b>[홈으로 복원] 후 저장</b>.
    /// 복원을 잊고 저장하면 찌그러진 자세가 원본이 된다.</para>
    /// </summary>
    public class BossHeadCrushShaper : EditorWindow
    {
        BossHeadCrush _target;

        float _squash = 0.45f;
        float _bulge = 1.25f;
        float _tiltDeg = 35f;
        float _jitterPos = 0.06f;
        float _jitterRot = 12f;
        int _seed = 1234;
        bool _partSquash = true;

        Vector3 _pressAxis = Vector3.down;

        // 홈 자세 — 매번 여기서 다시 계산해야 값을 바꿔도 누적되지 않는다.
        readonly List<Transform> _parts = new List<Transform>();
        readonly List<Vector3> _homePos = new List<Vector3>();
        readonly List<Quaternion> _homeRot = new List<Quaternion>();
        readonly List<Vector3> _homeScale = new List<Vector3>();

        [MenuItem("Tools/보스/머리 찌그러짐 모양 만들기")]
        static void Open() => GetWindow<BossHeadCrushShaper>("머리 찌그러짐");

        void OnGUI()
        {
            _target = (BossHeadCrush)EditorGUILayout.ObjectField("대상", _target, typeof(BossHeadCrush), true);
            if (_target == null)
            {
                if (GUILayout.Button("씬에서 찾기"))
                    _target = FindAnyObjectByType<BossHeadCrush>();
                EditorGUILayout.HelpBox("BossHeadCrush가 붙은 보스를 넣으십시오.", MessageType.Info);
                return;
            }
            if (_target.headRoot == null)
            {
                EditorGUILayout.HelpBox("headRoot가 비어 있습니다. 강체 변환이 먼저입니다.", MessageType.Error);
                return;
            }

            if (_parts.Count == 0) CacheHome();
            EditorGUILayout.LabelField($"판때기 {_parts.Count}개  ·  홈 캐시됨");

            EditorGUILayout.Space();
            _squash = EditorGUILayout.Slider("누름 (작을수록 납작)", _squash, 0.05f, 1f);
            _bulge = EditorGUILayout.Slider("퍼짐 (옆으로)", _bulge, 1f, 2.5f);
            _tiltDeg = EditorGUILayout.Slider("젖힘 (도)", _tiltDeg, 0f, 90f);
            _jitterPos = EditorGUILayout.Slider("지터 위치", _jitterPos, 0f, 0.3f);
            _jitterRot = EditorGUILayout.Slider("지터 회전 (도)", _jitterRot, 0f, 45f);
            _partSquash = EditorGUILayout.Toggle("판때기 자체도 납작하게", _partSquash);
            _seed = EditorGUILayout.IntField("시드", _seed);

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("모양 적용 (미리보기)")) Shape();
                if (GUILayout.Button("홈으로 복원")) Restore();
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("캡처", EditorStyles.boldLabel);
            if (_target.pose == null)
            {
                EditorGUILayout.HelpBox("자세 애셋이 없습니다.", MessageType.Warning);
                if (GUILayout.Button("자세 애셋 만들어 물리기")) CreatePose();
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("① 홈 캡처")) CaptureTo(_target.pose.home, "홈");
                if (GUILayout.Button("② crush로 캡처")) { Shape(); CaptureTo(_target.pose.crush, "최종 찌그러짐"); }
                if (GUILayout.Button("③ flat으로 캡처")) { Shape(); CaptureTo(_target.pose.flat, "완전 납작"); }
            }

            EditorGUILayout.HelpBox(
                "권장 순서\n" +
                " 1. [① 홈 캡처] — 손대기 전에 먼저\n" +
                " 2. 값 조절 → [모양 적용]으로 확인 → [② crush로 캡처]\n" +
                " 3. 누름을 더 세게(0.15 근처), 퍼짐·젖힘도 크게 → [③ flat으로 캡처]\n" +
                " 4. ★ [홈으로 복원] 후 프리팹 저장 — 안 하면 찌그러진 게 원본이 된다",
                MessageType.None);

            EditorGUILayout.LabelField($"저장된 자세  홈 {_target.pose.home.Count} / crush {_target.pose.crush.Count} / flat {_target.pose.flat.Count}");
        }

        // ── 내부 ─────────────────────────────────────────────────────────

        void CacheHome()
        {
            _parts.Clear(); _homePos.Clear(); _homeRot.Clear(); _homeScale.Clear();
            foreach (var mr in _target.headRoot.GetComponentsInChildren<MeshRenderer>(true))
            {
                var t = mr.transform;
                _parts.Add(t);
                _homePos.Add(t.localPosition);
                _homeRot.Add(t.localRotation);
                _homeScale.Add(t.localScale);
            }
        }

        void Restore()
        {
            for (int i = 0; i < _parts.Count; i++)
            {
                Undo.RecordObject(_parts[i], "머리 자세 복원");
                _parts[i].localPosition = _homePos[i];
                _parts[i].localRotation = _homeRot[i];
                _parts[i].localScale = _homeScale[i];
            }
            SceneView.RepaintAll();
        }

        /// <summary>홈 자세를 기준으로 찌그러진 자세를 계산해 적용한다. 항상 홈에서 다시 계산하므로 누적되지 않는다.</summary>
        void Shape()
        {
            if (_parts.Count == 0) return;

            // 누름축을 headRoot 로컬로. 파츠가 그 공간의 자식이라 여기서 계산해야 회전이 섞이지 않는다.
            Vector3 axis = _target.headRoot.InverseTransformDirection(_pressAxis).normalized;

            Vector3 center = Vector3.zero;
            for (int i = 0; i < _homePos.Count; i++) center += _homePos[i];
            center /= Mathf.Max(1, _homePos.Count);

            float maxRadial = 0.0001f;
            for (int i = 0; i < _homePos.Count; i++)
            {
                Vector3 d = _homePos[i] - center;
                float r = Vector3.ProjectOnPlane(d, axis).magnitude;
                if (r > maxRadial) maxRadial = r;
            }

            var rnd = new System.Random(_seed);
            for (int i = 0; i < _parts.Count; i++)
            {
                Vector3 d = _homePos[i] - center;
                Vector3 along = Vector3.Project(d, axis);
                Vector3 radial = d - along;
                float rNorm = radial.magnitude / maxRadial;

                // 위치: 누름축으로 눌리고, 수직으로 퍼진다.
                Vector3 pos = center + along * _squash + radial * _bulge;

                // 젖힘: 바깥쪽 파츠일수록 크게. 회전축은 '누름축 × 바깥방향'이라
                // 판때기가 바깥으로 눕는 방향이 된다.
                Quaternion rot = _homeRot[i];
                if (_tiltDeg > 0.01f && radial.sqrMagnitude > 1e-8f)
                {
                    Vector3 tiltAxis = Vector3.Cross(axis, radial.normalized);
                    if (tiltAxis.sqrMagnitude > 1e-8f)
                        rot = Quaternion.AngleAxis(_tiltDeg * rNorm, tiltAxis.normalized) * rot;
                }

                // 지터 — 균일하면 풍선처럼 보인다. 고정 시드라 매번 같은 결과가 나온다.
                float jx = (float)rnd.NextDouble() - 0.5f, jy = (float)rnd.NextDouble() - 0.5f, jz = (float)rnd.NextDouble() - 0.5f;
                float ja = (float)rnd.NextDouble() - 0.5f, jb = (float)rnd.NextDouble() - 0.5f, jc = (float)rnd.NextDouble() - 0.5f;
                pos += new Vector3(jx, jy, jz) * (_jitterPos * maxRadial * 2f);
                rot = Quaternion.Euler(ja * _jitterRot, jb * _jitterRot, jc * _jitterRot) * rot;

                // 판때기 자체도 눌린다 — 파츠의 로컬 축 중 누름축에 가장 가까운 축을 줄인다.
                Vector3 scale = _homeScale[i];
                if (_partSquash)
                {
                    Vector3 localAxis = Quaternion.Inverse(_homeRot[i]) * axis;
                    int k = 0; float best = Mathf.Abs(localAxis.x);
                    if (Mathf.Abs(localAxis.y) > best) { k = 1; best = Mathf.Abs(localAxis.y); }
                    if (Mathf.Abs(localAxis.z) > best) { k = 2; }
                    scale[k] *= Mathf.Lerp(1f, _squash, 0.7f);   // 배치만큼 세게 누르면 종잇장이 된다
                }

                Undo.RecordObject(_parts[i], "머리 찌그러짐 생성");
                _parts[i].localPosition = pos;
                _parts[i].localRotation = rot;
                _parts[i].localScale = scale;
            }
            SceneView.RepaintAll();
        }

        void CaptureTo(List<BossHeadCrushPose.PartPose> list, string label)
        {
            Undo.RecordObject(_target.pose, "머리 자세 캡처");
            list.Clear();
            for (int i = 0; i < _parts.Count; i++)
                list.Add(new BossHeadCrushPose.PartPose
                {
                    name = _parts[i].name,
                    pos = _parts[i].localPosition,
                    rot = _parts[i].localRotation,
                    scale = _parts[i].localScale,
                });
            EditorUtility.SetDirty(_target.pose);
            AssetDatabase.SaveAssets();
            Debug.Log($"[머리 찌그러짐] '{label}' 캡처 — 파츠 {list.Count}개");
        }

        void CreatePose()
        {
            var asset = ScriptableObject.CreateInstance<BossHeadCrushPose>();
            const string dir = "Assets/_Project/Art/Boss";
            string path = AssetDatabase.GenerateUniqueAssetPath(dir + "/BossHeadCrushPose.asset");
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();

            Undo.RecordObject(_target, "자세 애셋 연결");
            _target.pose = asset;
            EditorUtility.SetDirty(_target);
            Debug.Log("[머리 찌그러짐] 자세 애셋 생성 → " + path);
        }
    }
}
