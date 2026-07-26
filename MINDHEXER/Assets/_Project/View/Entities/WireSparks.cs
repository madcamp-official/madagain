using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 전선 끝 스파크. 파티클 시스템 하나를 공유해 전선 개수와 무관하게 비용이 일정하다.
    /// (전선마다 ParticleSystem을 붙이면 몹 수십 마리에서 드로우콜이 터진다)
    /// </summary>
    public static class WireSparks
    {
        static ParticleSystem ps;

        public static void Emit(Vector3 pos)
        {
            if (ps == null) Build();
            if (ps == null) return;
            var ep = new ParticleSystem.EmitParams { position = pos, applyShapeToPosition = true };
            ps.Emit(ep, Random.Range(3, 7));
        }

        /// <summary>
        /// 크기·개수·속도를 지정해 튀긴다(발 스파크처럼 더 작게 쓰고 싶을 때).
        /// 파티클 시스템은 그대로 공유하므로 드로우콜이 늘어나지 않는다.
        /// </summary>
        public static void EmitScaled(Vector3 pos, int count, float sizeMul, float speedMul, float lifeMul = 1f)
        {
            if (ps == null) Build();
            if (ps == null || count <= 0) return;
            var ep = new ParticleSystem.EmitParams
            {
                position = pos,
                applyShapeToPosition = true,
                startSize     = Random.Range(0.012f, 0.03f) * Mathf.Max(0.05f, sizeMul),
                startLifetime = 0.35f * Mathf.Max(0.05f, lifeMul),
                velocity      = (Vector3.up * 0.6f + Random.insideUnitSphere) * (2.4f * Mathf.Max(0f, speedMul)),
            };
            ps.Emit(ep, count);
        }

        static void Build()
        {
            var go = new GameObject("[WireSparks]");
            Object.DontDestroyOnLoad(go);
            ps = go.AddComponent<ParticleSystem>();

            var main = ps.main;
            main.startLifetime = 0.35f;
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 3.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.03f);
            main.gravityModifier = 1.6f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 600;
            main.playOnAwake = false;
            // 흰 → 주황으로 식는 불꽃. HDR로 띄워 블룸이 걸리게 한다.
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(3.2f, 2.4f, 1.2f), new Color(3.0f, 1.1f, 0.25f));

            var em = ps.emission; em.enabled = false;    // 수동 Emit만
            var sh = ps.shape;    sh.enabled = false;

            var col = ps.colorOverLifetime; col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(grad);

            var pr = ps.GetComponent<ParticleSystemRenderer>();
            pr.renderMode = ParticleSystemRenderMode.Stretch;   // 튀는 불똥 — 늘어진 선으로
            pr.velocityScale = 0.06f;
            pr.material = WireMaterials.Spark;
            pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }

    /// <summary>전선·스파크 머티리얼 — 에셋 없이 코드로 만든다(URP).</summary>
    public static class WireMaterials
    {
        static Material wire, spark;

        /// <summary>금속 케이블 — 어둡고 거친 금속에 구릿빛.</summary>
        public static Material Wire
        {
            get
            {
                if (wire != null) return wire;
                var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                wire = new Material(sh) { name = "WireMat" };
                if (wire.HasProperty("_BaseColor")) wire.SetColor("_BaseColor", new Color(0.42f, 0.26f, 0.14f));
                wire.color = new Color(0.42f, 0.26f, 0.14f);          // 노출된 구리
                if (wire.HasProperty("_Metallic"))  wire.SetFloat("_Metallic", 0.85f);
                if (wire.HasProperty("_Smoothness")) wire.SetFloat("_Smoothness", 0.35f);   // 거칠게
                return wire;
            }
        }

        /// <summary>스파크 — 가산 합성(Additive)으로 빛나게.</summary>
        public static Material Spark
        {
            get
            {
                if (spark != null) return spark;
                var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                      ?? Shader.Find("Particles/Standard Unlit")
                      ?? Shader.Find("Sprites/Default");
                spark = new Material(sh) { name = "SparkMat" };
                spark.SetOverrideTag("RenderType", "Transparent");
                if (spark.HasProperty("_Surface")) spark.SetFloat("_Surface", 1f);
                if (spark.HasProperty("_Blend"))   spark.SetFloat("_Blend", 1f);   // Additive
                spark.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                spark.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
                spark.SetInt("_ZWrite", 0);
                spark.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                spark.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                return spark;
            }
        }
    }
}
