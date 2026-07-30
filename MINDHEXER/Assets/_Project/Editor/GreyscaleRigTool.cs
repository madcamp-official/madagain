using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Game.View;

namespace Game.EditorTools
{
    /// <summary>
    /// 화면 톤 리그를 씬에 배치하는 도구.
    ///
    /// <para><b>왜 도구가 필요한가</b> — 예전에는 <c>OneBitControl</c>이 없으면 런타임에 자동 생성했다.
    /// 그러면 값이 <b>씬에 남지 않아</b> 매번 기본값으로 되돌아가고, 튜닝한 결과를 저장할 곳이 없다.
    /// 씬에 실제로 배치해야 하는데, 손으로 만들려면 볼륨·프로파일·우선순위·채널을 매번 맞춰야 해서
    /// 틀리기 쉽다. 그 조립을 도구가 한다.</para>
    ///
    /// <para><b>OneBit 컨트롤이 둘인 이유</b> — 손·거미(플레이어)와 해킹 대상은 재질 밝기가 정반대다.
    /// 팔은 거의 검정이라 <c>inWhite</c>를 크게 낮춰야 계단이 생기는데, 그 값을 밝은 금속에 그대로
    /// 쓰면 통째로 흰색이 된다. 그래서 전역 세트를 둘로 나눴고, 컨트롤도 채널별로 하나씩 둔다.</para>
    /// </summary>
    public static class GreyscaleRigTool
    {
        const string GreyProfile = "Assets/_Project/Settings/GreyscaleTest.asset";
        const string RedProfile  = "Assets/_Project/Settings/BossRedTint.asset";

        [MenuItem("Tools/흑백/씬에 흑백 리그 배치 (그레이스케일 볼륨 + OneBit 2채널)")]
        public static void PlaceRig()
        {
            var created = new System.Collections.Generic.List<GameObject>();

            GameObject grey = EnsureVolume("[GreyscaleTest]", GreyProfile, 100f, created);
            GameObject oneBit = EnsureOneBit(created);

            foreach (var go in created) Undo.RegisterCreatedObjectUndo(go, "흑백 리그 배치");
            if (oneBit != null) Selection.activeGameObject = oneBit;

            EditorUtility.DisplayDialog("흑백 리그",
                (grey != null ? "그레이스케일 볼륨: " + grey.name : "그레이스케일 볼륨: 프로파일 없음") + "\n" +
                (oneBit != null ? "OneBit 컨트롤: Player + Hackable 2채널" : "OneBit 실패") + "\n\n" +
                (created.Count == 0 ? "이미 다 있어서 새로 만든 것은 없습니다." : "새로 만든 오브젝트 " + created.Count + "개") +
                "\n\n씬을 저장하면 값이 남습니다.", "확인");
        }

        [MenuItem("Tools/흑백/보스 흑빨 볼륨 배치 (검정→빨강, weight 0으로 시작)")]
        public static void PlaceBossTint()
        {
            var created = new System.Collections.Generic.List<GameObject>();
            GameObject go = EnsureVolume("[BossRedTint]", RedProfile, 200f, created);
            if (go == null) { EditorUtility.DisplayDialog("보스 흑빨", "프로파일을 찾지 못했습니다:\n" + RedProfile, "확인"); return; }

            var tint = go.GetComponent<BossTintControl>();
            if (tint == null) tint = Undo.AddComponent<BossTintControl>(go);
            tint.weight = 0f;   // 기본은 완전 흑백 — 필요할 때 올린다

            foreach (var g in created) Undo.RegisterCreatedObjectUndo(g, "보스 흑빨 볼륨 배치");
            Selection.activeGameObject = go;
        }

        /// <summary>전역 볼륨 하나를 보장한다. 이미 있으면 프로파일·우선순위만 맞춘다.</summary>
        static GameObject EnsureVolume(string name, string profilePath, float priority,
                                       System.Collections.Generic.List<GameObject> created)
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(profilePath);
            if (profile == null)
            {
                Debug.LogWarning("[흑백 리그] 프로파일이 없습니다: " + profilePath);
                return null;
            }

            GameObject go = GameObject.Find(name);
            if (go == null)
            {
                go = new GameObject(name);
                created.Add(go);
            }

            var vol = go.GetComponent<Volume>();
            if (vol == null) vol = Undo.AddComponent<Volume>(go);

            Undo.RecordObject(vol, "볼륨 설정");
            vol.isGlobal = true;
            // ★ 우선순위가 곧 적용 순서다. 흑백(100)이 먼저, 흑빨(200)이 그 위에 얹혀야 한다.
            vol.priority = priority;
            vol.sharedProfile = profile;
            vol.weight = Mathf.Approximately(priority, 100f) ? 1f : vol.weight;
            EditorUtility.SetDirty(vol);
            return go;
        }

        /// <summary><c>[OneBit]</c> 하나에 Player·Hackable 컨트롤을 각각 붙인다.</summary>
        static GameObject EnsureOneBit(System.Collections.Generic.List<GameObject> created)
        {
            GameObject go = GameObject.Find("[OneBit]");
            if (go == null)
            {
                go = new GameObject("[OneBit]");
                created.Add(go);
            }

            var existing = go.GetComponents<OneBitControl>();
            OneBitControl player = null, hackable = null;
            for (int i = 0; i < existing.Length; i++)
            {
                if (existing[i].channel == OneBitChannel.Player) player = existing[i];
                else hackable = existing[i];
            }

            if (player == null)
            {
                player = Undo.AddComponent<OneBitControl>(go);
                player.channel = OneBitChannel.Player;
                // 손·거미 확정값 (ViewmodelStudio에서 잡은 것).
                player.levels = 8f; player.inBlack = 0f; player.inWhite = 0.5f;
                player.invert = false; player.dither = 1f; player.lightWrap = 1f;
                player.ambientFloor = 0f;   // ★ 비활성화됨 — ForceAmbientOff가 강제로 0 처리한다
                player.Apply();
            }

            if (hackable == null)
            {
                hackable = Undo.AddComponent<OneBitControl>(go);
                hackable.channel = OneBitChannel.Hackable;
                // 스페큘러 맵 실측 분포(중앙값 0.35, 99% 0.64)에 맞춘 값.
                hackable.levels = 4f; hackable.inBlack = 0f; hackable.inWhite = 0.34f;
                hackable.invert = false; hackable.dither = 0.25f; hackable.lightWrap = 1f;
                hackable.ambientFloor = 0f;   // ★ 비활성화됨
                hackable.Apply();
            }

            EditorUtility.SetDirty(go);
            return go;
        }
    }
}
