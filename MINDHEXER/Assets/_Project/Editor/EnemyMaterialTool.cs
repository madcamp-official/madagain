using UnityEditor;
using UnityEngine;

namespace Game.EditorTools
{
    /// <summary>
    /// 몹 머티리얼을 전용 셰이더(Game/EnemyBody)로 맞추고 녹·반사 기본값을 일괄 적용한다.
    /// 셰이더 프로퍼티를 손볼 때마다 머티리얼 5개를 일일이 고치지 않아도 되게.
    /// </summary>
    public static class EnemyMaterialTool
    {
        const string ShaderName = "Game/EnemyBody";

        static readonly string[] Mats =
        {
            "Assets/_Project/Art/FootSoldier/FootSoldierBody.mat",
            "Assets/_Project/Art/GunSoldier/GunSoldierBody.mat",
            "Assets/_Project/Art/MonolithSentinel/MonolithSentinelBody.mat",
            "Assets/_Project/Art/ObsidianSentinel/ObsidianSentinel.mat",
            "Assets/_Project/Art/CrimsonSentinelBiped/CrimsonSentinelBiped.mat",
        };

        [MenuItem("Tools/몹/머티리얼 기본값 적용")]
        public static void Apply()
        {
            var sh = Shader.Find(ShaderName);
            if (sh == null) { Debug.LogError($"[몹 머티리얼] '{ShaderName}' 셰이더를 못 찾았습니다(컴파일 실패?)."); return; }

            int n = 0;
            foreach (var path in Mats)
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null) { Debug.LogWarning($"[몹 머티리얼] 없음: {path}"); continue; }

                // 셰이더가 다르면 기존 텍스처 슬롯을 옮기고 교체
                if (m.shader != sh)
                {
                    var baseT = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : null;
                    if (baseT == null && m.HasProperty("_MainTex")) baseT = m.GetTexture("_MainTex");
                    var bump = m.HasProperty("_BumpMap") ? m.GetTexture("_BumpMap") : null;
                    var mg   = m.HasProperty("_MetallicGlossMap") ? m.GetTexture("_MetallicGlossMap") : null;
                    m.shader = sh;
                    if (baseT != null) m.SetTexture("_BaseMap", baseT);
                    if (bump  != null) m.SetTexture("_BumpMap", bump);
                    if (mg    != null) m.SetTexture("_MetallicGlossMap", mg);
                }

                // ── 녹: 어두운 배경에서 보이려면 밝은 쪽으로 가야 한다 ──
                m.SetColor("_RustColor", new Color(0.72f, 0.55f, 0.34f));   // 밝은 황토
                m.SetColor("_RustDark",  new Color(0.55f, 0.50f, 0.45f));   // 탁한 회백
                m.SetFloat("_RustBoost", 1.35f);
                m.SetFloat("_Darken", 0.10f);          // 어둡게는 거의 안 한다
                m.SetFloat("_GrungeScale", 9f);
                m.SetFloat("_StreakStrength", 0.6f);

                // ── 반사 대비: 어둠 속 최고의 신호 ──
                m.SetFloat("_CleanSmoothness", 0.85f); // 깨끗한 금속 = 번쩍
                m.SetFloat("_RustSmoothness", 0.04f);  // 녹 = 완전 무광
                m.SetFloat("_RustMetallic", 0.05f);    // 녹은 금속이 아니다

                // ── 발광(런타임에 EnemyGlow가 덮어씀) ──
                m.SetFloat("_RedThreshold", 0.25f);
                m.SetFloat("_RedSoftness", 0.15f);
                m.SetFloat("_GlowIntensity", 0f);
                m.SetColor("_GlowColor", Color.white);
                m.SetFloat("_GrungeAmount", 0f);       // 개체별로 런타임 지정

                m.enableInstancing = true;             // MaterialPropertyBlock 써도 배칭 유지
                EditorUtility.SetDirty(m);
                n++;
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[몹 머티리얼] {n}개 적용 완료 — 녹은 밝은 황토·회백, 반사 대비 0.85↔0.04");
        }
    }
}
