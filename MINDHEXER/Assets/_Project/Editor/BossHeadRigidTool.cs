using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Game.EditorTools
{
    /// <summary>
    /// 보스 머리 판때기를 <b>강체로 변환</b>한다 — 찌그러짐 연출을 위해 판때기를 하나씩 움직일 수 있게.
    ///
    /// <para><b>왜 필요한가</b>: 머리 판때기는 전부 <see cref="SkinnedMeshRenderer"/>이고, bones가
    /// 할당된 스킨드 메시는 정점을 <b>본 행렬로만</b> 계산하고 렌더러 자신의 Transform은 무시한다
    /// (유니티 스키닝 규칙). 그래서 인스펙터에서 판때기를 옮겨도 화면상 아무 변화가 없다 —
    /// 자세를 손으로 잡는 것이 불가능하다. 머리 본도 <c>Head</c> 하나뿐이라 나눠 돌릴 수도 없다.</para>
    ///
    /// <para><b>무엇을 하나</b>: 현재 자세(=바인드 포즈)로 메시를 구워 <c>MeshFilter</c>+<c>MeshRenderer</c>로
    /// 바꾸고 <c>Head</c>의 자식으로 붙인다. 그때부터 개별 이동·회전·스케일이 자유롭다.</para>
    ///
    /// <para><b>왜 형태가 안 변하나</b>: 대상 파츠 일부는 Head 외에 목 본(<c>NeckTwist02</c> 등)에도
    /// 걸쳐 있다. 그런데 우리 규약상 <b>목은 움직이지 않는다</b>(동결) — 움직이지 않는 본의 영향은
    /// 상수이므로, 바인드 포즈에서 구우면 그 영향이 그대로 굳어 형태가 유지된다.
    /// <b>반드시 바인드 포즈에서 실행할 것</b>(애니메이터 끄고, IK 미리보기 끄고).</para>
    ///
    /// <para><b>실행 방법</b>: 프리팹을 <b>프리팹 모드로 열고</b> 루트를 선택한 뒤 메뉴 실행. 씬
    /// 인스턴스에서 하면 프리팹에 반영되지 않는다. 변환 후 오차를 로그로 찍으니 확인할 것.</para>
    /// </summary>
    static class BossHeadRigidTool
    {
        const string MeshDir = "Assets/_Project/Art/Boss/HeadRigid";

        /// <summary>Head 본의 가중치 비중이 이 값 이상이면 "머리 판때기"로 본다.</summary>
        const float HeadDominance = 0.5f;

        [MenuItem("Tools/보스/머리 판때기 강체로 변환", false, 10)]
        static void Convert()
        {
            GameObject root = Selection.activeGameObject;
            if (root == null) { Warn("보스 루트를 선택하고 실행하십시오."); return; }

            Transform head = FindByName(root, "Head");
            if (head == null) { Warn("'Head' 본을 찾지 못했습니다. 보스 루트를 선택했는지 확인하십시오."); return; }

            var targets = new List<SkinnedMeshRenderer>();
            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (HeadRatio(smr, head) >= HeadDominance) targets.Add(smr);

            if (targets.Count == 0) { Warn("Head 본이 주인인 파츠를 찾지 못했습니다."); return; }

            string msg = $"머리 판때기 {targets.Count}개를 강체(MeshRenderer)로 바꿉니다.\n\n" +
                         "⚠ 반드시 바인드 포즈여야 합니다 — 애니메이터와 IK 미리보기를 끈 상태인지 확인하십시오.\n" +
                         "⚠ 프리팹 모드에서 실행해야 프리팹에 남습니다.\n\n" +
                         $"구운 메시는 {MeshDir}에 저장됩니다.";
            if (!EditorUtility.DisplayDialog("머리 강체 변환", msg, "진행", "취소")) return;

            Directory.CreateDirectory(MeshDir);
            AssetDatabase.Refresh();

            int done = 0;
            float worstError = 0f;
            string worstName = "";

            foreach (var smr in targets)
            {
                if (smr.sharedMesh == null) continue;

                Vector3 beforeCenter = smr.bounds.center;

                // 현재 자세를 그대로 굽는다. useScale=true라 렌더러 스케일까지 정점에 반영된다
                // → 새 오브젝트는 스케일 1로 두면 된다.
                var baked = new Mesh { name = smr.name + "_rigid" };
                smr.BakeMesh(baked, true);
                baked.RecalculateBounds();

                string path = AssetDatabase.GenerateUniqueAssetPath($"{MeshDir}/{smr.name}_rigid.asset");
                AssetDatabase.CreateAsset(baked, path);

                var go = new GameObject(smr.name);
                Undo.RegisterCreatedObjectUndo(go, "머리 강체 변환");
                go.transform.SetParent(smr.transform.parent, false);
                go.transform.localPosition = smr.transform.localPosition;
                go.transform.localRotation = smr.transform.localRotation;
                go.transform.localScale = Vector3.one;      // 스케일은 이미 메시에 구워졌다

                var mf = go.AddComponent<MeshFilter>();
                mf.sharedMesh = baked;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterials = smr.sharedMaterials;
                mr.shadowCastingMode = smr.shadowCastingMode;
                mr.receiveShadows = smr.receiveShadows;

                // 월드 자세를 유지한 채 Head 아래로 옮긴다 — 이제 머리를 따라 움직인다.
                go.transform.SetParent(head, true);

                // 검증: 변환 전후 월드 바운즈 중심이 얼마나 어긋났나.
                float err = Vector3.Distance(beforeCenter, mr.bounds.center);
                if (err > worstError) { worstError = err; worstName = smr.name; }

                Undo.DestroyObjectImmediate(smr.gameObject);
                done++;
            }

            AssetDatabase.SaveAssets();

            string result = $"{done}개 변환 완료.\n\n최대 위치 오차 {worstError:F3} (파츠 {worstName})";
            if (worstError > 0.05f)
                result += "\n\n⚠ 오차가 큽니다 — 바인드 포즈가 아니었을 수 있습니다. Ctrl+Z로 되돌리십시오.";
            Debug.Log("[머리 강체] " + result);
            EditorUtility.DisplayDialog("머리 강체 변환", result, "확인");
        }

        [MenuItem("Tools/보스/머리 판때기 강체로 변환", true)]
        static bool ConvertValidate() => Selection.activeGameObject != null;

        // ── 내부 ─────────────────────────────────────────────────────────

        static Transform FindByName(GameObject root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        /// <summary>이 렌더러의 가중치 중 Head 본이 차지하는 비율. Head가 없으면 0.</summary>
        static float HeadRatio(SkinnedMeshRenderer smr, Transform head)
        {
            var mesh = smr.sharedMesh;
            if (mesh == null || smr.bones == null) return 0f;

            int headIdx = -1;
            for (int i = 0; i < smr.bones.Length; i++)
                if (smr.bones[i] == head) { headIdx = i; break; }
            if (headIdx < 0) return 0f;

            var bw = mesh.GetAllBoneWeights();
            if (bw.Length == 0) return 0f;

            float headW = 0f, allW = 0f;
            for (int i = 0; i < bw.Length; i++)
            {
                allW += bw[i].weight;
                if (bw[i].boneIndex == headIdx) headW += bw[i].weight;
            }
            return allW > 1e-6f ? headW / allW : 0f;
        }

        static void Warn(string s)
        {
            Debug.LogWarning("[머리 강체] " + s);
            EditorUtility.DisplayDialog("머리 강체 변환", s, "확인");
        }
    }
}
