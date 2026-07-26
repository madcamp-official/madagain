using UnityEngine;
using UnityEditor;

namespace Game.EditorTools
{
    /// <summary>
    /// 예측 잔상(PlayerGhost)의 오른손에 <b>칼</b>을 붙인다.
    ///
    /// 왜 전용 저폴리 칼인가: 프로젝트의 카타나(Meshy 생성)는 25k버텍스·46k삼각형이라, 잔상이
    /// 145장(PredictionConfig.PreviewAfterimageCount) 깔리는 예측 화면에서 그대로 쓰면 칼만으로
    /// 670만 삼각형이 된다. 잔상은 반투명 실루엣이라 형태만 읽히면 되므로 박스 3개(손잡이·코등이·
    /// 칼날)를 합친 36삼각형 메시를 굽는다 — 잔상 145장 전체가 5천 삼각형.
    ///
    /// 그립: 손뼈(mixamorig:RightHand)의 자식으로 붙이고, 칼날 방향은 <b>팔뚝축과 주먹 관절축을
    /// 섞어</b> 잡는다. 관절축(검지-새끼)만 쓰면 해부학적으로는 맞지만 칼이 어깨 뒤로 서고,
    /// 팔뚝축만 쓰면 칼끝이 아래로 처진다 — 0.75가 찌르기에서 칼날이 정면 수평이 되는 값(렌더 확인).
    /// </summary>
    public static class GhostSwordTool
    {
        const string GhostPrefabPath = "Assets/_Project/Prefabs/Resources/PlayerGhost.prefab";
        const string MeshAssetPath = "Assets/_Project/Prefabs/Resources/GhostKatanaMesh.asset";
        const string SwordObjectName = "GhostKatana";

        /// <summary>칼날 방향 = 주먹 관절축 → 팔뚝축 보간 비율(1이면 완전히 팔뚝 연장선).</summary>
        const float ForearmBlend = 0.75f;
        /// <summary>손목에서 손가락 쪽으로 살짝 밀어 손 한가운데서 쥐게 한다(m).</summary>
        const float GripForwardOffset = 0.03f;

        [MenuItem("Tools/예측/잔상 칼 붙이기")]
        public static void Attach()
        {
            Mesh mesh = BuildAndSaveMesh();

            // 그립 로컬 변환은 손뼈 기준이라 포즈와 무관하게 일정하다 — 프리팹 인스턴스에서 한 번 잰다.
            var probe = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(GhostPrefabPath));
            Vector3 localPos; Quaternion localRot; Vector3 localScale;
            try
            {
                if (!MeasureGrip(probe, out localPos, out localRot, out localScale)) return;
            }
            finally { Object.DestroyImmediate(probe); }

            GameObject root = PrefabUtility.LoadPrefabContents(GhostPrefabPath);
            try
            {
                var animator = root.GetComponentInChildren<Animator>(true);
                var hand = animator != null ? animator.GetBoneTransform(HumanBodyBones.RightHand) : null;
                if (hand == null)
                {
                    Debug.LogError("[잔상 칼] PlayerGhost에서 오른손 뼈를 찾지 못했습니다.");
                    return;
                }

                var existing = hand.Find(SwordObjectName);
                if (existing != null) Object.DestroyImmediate(existing.gameObject);

                var sword = new GameObject(SwordObjectName);
                sword.transform.SetParent(hand, false);
                sword.transform.localPosition = localPos;
                sword.transform.localRotation = localRot;
                sword.transform.localScale = localScale;
                sword.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = sword.AddComponent<MeshRenderer>();
                mr.sharedMaterial = DefaultMaterial();
                // 잔상 재질은 PredictionController가 런타임에 전부 ghostMat으로 덮어쓴다.
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;

                PrefabUtility.SaveAsPrefabAsset(root, GhostPrefabPath);
                Debug.Log($"[잔상 칼] PlayerGhost 오른손에 칼을 붙였습니다 " +
                    $"({mesh.triangles.Length / 3}삼각형, localEuler={localRot.eulerAngles:F1}).");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        [MenuItem("Tools/예측/잔상 칼 떼기")]
        public static void Detach()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(GhostPrefabPath);
            try
            {
                var animator = root.GetComponentInChildren<Animator>(true);
                var hand = animator != null ? animator.GetBoneTransform(HumanBodyBones.RightHand) : null;
                var existing = hand != null ? hand.Find(SwordObjectName) : null;
                if (existing == null) { Debug.Log("[잔상 칼] 붙어 있는 칼이 없습니다."); return; }
                Object.DestroyImmediate(existing.gameObject);
                PrefabUtility.SaveAsPrefabAsset(root, GhostPrefabPath);
                Debug.Log("[잔상 칼] 떼어냈습니다.");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        /// <summary>손뼈 기준 그립 변환을 잰다. 칼 메시는 그립이 원점, 칼날 +Z, 날 +Y 기준으로 굽는다.</summary>
        static bool MeasureGrip(GameObject instance, out Vector3 localPos, out Quaternion localRot, out Vector3 localScale)
        {
            localPos = Vector3.zero; localRot = Quaternion.identity; localScale = Vector3.one;
            var animator = instance.GetComponentInChildren<Animator>(true);
            if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
            {
                Debug.LogError("[잔상 칼] PlayerGhost에 휴머노이드 Animator가 없습니다.");
                return false;
            }

            Transform hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            Transform fore = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            Transform index = animator.GetBoneTransform(HumanBodyBones.RightIndexProximal);
            Transform little = animator.GetBoneTransform(HumanBodyBones.RightLittleProximal);
            Transform middle = animator.GetBoneTransform(HumanBodyBones.RightMiddleProximal);
            if (hand == null || fore == null || index == null || little == null || middle == null)
            {
                Debug.LogError("[잔상 칼] 오른손/손가락 뼈 매핑이 비어 있어 그립을 잴 수 없습니다.");
                return false;
            }

            Vector3 forearmDir = (hand.position - fore.position).normalized;
            Vector3 knuckleDir = (index.position - little.position).normalized;
            Vector3 bladeDir = Vector3.Slerp(knuckleDir, forearmDir, ForearmBlend).normalized;
            Vector3 fingerDir = (middle.position - hand.position).normalized;
            Vector3 upDir = Vector3.Cross(bladeDir, fingerDir).normalized;

            localRot = Quaternion.Inverse(hand.rotation) * Quaternion.LookRotation(bladeDir, upDir);
            localPos = hand.InverseTransformPoint(hand.position + fingerDir * GripForwardOffset);
            // 리그가 90배로 커져 있어(PlayerGhost 루트 스케일), 메시를 실제 미터로 보이게 되돌린다.
            localScale = Vector3.one / hand.lossyScale.x;
            return true;
        }

        /// <summary>손잡이·코등이·칼날 박스 3개를 하나로 합친 메시를 굽고 에셋으로 저장한다.
        /// 그립이 원점, 칼날은 +Z, 날 서는 쪽이 +Y.</summary>
        static Mesh BuildAndSaveMesh()
        {
            var boxes = new[]
            {
                new { center = new Vector3(0f, 0f, -0.02f), size = new Vector3(0.030f, 0.042f, 0.22f) },   // 손잡이
                new { center = new Vector3(0f, 0f, 0.105f), size = new Vector3(0.105f, 0.022f, 0.028f) },  // 코등이
                // 칼날 길이 0.78m — 1m로 잡으면 달리기·점프 잔상에서 팔이 내려갈 때 칼끝이
                // 바닥을 뚫는다(캐릭터 키가 1.7m라 그때는 사실상 대검이었다).
                new { center = new Vector3(0f, 0f, 0.52f), size = new Vector3(0.014f, 0.052f, 0.78f) },    // 칼날
            };

            var temp = new GameObject[boxes.Length];
            var combine = new CombineInstance[boxes.Length];
            for (int i = 0; i < boxes.Length; i++)
            {
                temp[i] = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.DestroyImmediate(temp[i].GetComponent<Collider>());
                combine[i].mesh = temp[i].GetComponent<MeshFilter>().sharedMesh;
                combine[i].transform = Matrix4x4.TRS(boxes[i].center, Quaternion.identity, boxes[i].size);
            }

            var mesh = new Mesh { name = "GhostKatanaMesh" };
            mesh.CombineMeshes(combine, true, true);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            for (int i = 0; i < temp.Length; i++) Object.DestroyImmediate(temp[i]);

            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(MeshAssetPath);
            if (existing != null)
            {
                // 같은 에셋을 덮어써야 프리팹의 참조가 끊기지 않는다.
                existing.Clear();
                existing.vertices = mesh.vertices;
                existing.normals = mesh.normals;
                existing.uv = mesh.uv;
                existing.triangles = mesh.triangles;
                existing.RecalculateBounds();
                EditorUtility.SetDirty(existing);
                AssetDatabase.SaveAssets();
                return existing;
            }

            AssetDatabase.CreateAsset(mesh, MeshAssetPath);
            AssetDatabase.SaveAssets();
            return mesh;
        }

        /// <summary>프로젝트 기본 재질(런타임엔 어차피 잔상 재질로 덮어쓴다).</summary>
        static Material DefaultMaterial()
        {
            var probe = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Material mat = probe.GetComponent<MeshRenderer>().sharedMaterial;
            Object.DestroyImmediate(probe);
            return mat;
        }
    }
}
