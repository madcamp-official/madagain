using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using static UnityEngine.Rendering.RenderGraphModule.Util.RenderGraphUtils;

namespace Game.View
{
    /// <summary>
    /// 예측 정지(F) 진입 시 화면 중심에서 퍼지는 원 안쪽을 색 반전하는 풀스크린 URP 패스.
    /// URP 표준 FullScreenPassRendererFeature와 달리 postProcessEnabled==false인 카메라
    /// (PredictionController.accentCamera — 예측 경로선·잔상·칼 전용 오버레이. FreezeFx의
    /// 데스퍼레이션 Volume도 같은 플래그로 이 카메라를 피해간다)는 건너뛴다. 그 결과 플레이어의
    /// 예측 궤적·잔상은 반전에서 자동으로 빠진다 — accent 카메라가 base 카메라 렌더 뒤에
    /// depth-only로 그 위에 덧그리므로 별도 스텐실/레이어 마스킹이 필요 없다.
    ///
    /// ★ 사용 전 1회 설정 필요(Unity Editor): Assets/Settings의 PC_Renderer(및 필요시
    ///   Mobile_Renderer) 에셋을 선택해 Inspector → Renderer Features → Add Renderer Feature →
    ///   "Radial Invert" 를 추가하고, 그 Material 필드에
    ///   Assets/_Project/Prefabs/Resources/Fx/RadialInvert.mat 을 드래그해 연결한다.
    /// </summary>
    public class RadialInvertFeature : ScriptableRendererFeature
    {
        public Material material;
        public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;

        RadialInvertPass pass;

        public override void Create()
        {
            pass = new RadialInvertPass();
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (material == null) return;
            if (material.HasProperty("_Radius") && material.GetFloat("_Radius") <= 0.001f) return;
            if (renderingData.cameraData.cameraType == CameraType.Preview ||
                renderingData.cameraData.cameraType == CameraType.Reflection)
                return;
            // ★ 이 플래그가 꺼진 카메라(예: accentCamera 오버레이)는 건너뛴다 — Volume 기반
            //   post-process가 같은 플래그로 스킵되는 것과 동일한 규칙이라, 예측 시각 요소를
            //   반전에서 제외하는 별도 로직 없이 그대로 일관된다.
            if (!renderingData.cameraData.postProcessEnabled) return;

            pass.renderPassEvent = renderPassEvent;
            pass.SetMaterial(material);
            renderer.EnqueuePass(pass);
        }

        class RadialInvertPass : ScriptableRenderPass
        {
            Material mat;
            static readonly ProfilingSampler Sampler = new ProfilingSampler("RadialInvert");
            static readonly int BlitTextureId = Shader.PropertyToID("_BlitTexture");
            static readonly int BlitScaleBiasId = Shader.PropertyToID("_BlitScaleBias");
            static MaterialPropertyBlock mpb;

            public void SetMaterial(Material m) => mat = m;

            class PassData
            {
                public Material material;
                public TextureHandle source;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                UniversalResourceData resourcesData = frameData.Get<UniversalResourceData>();
                if (!resourcesData.cameraColor.IsValid()) return;

                var srcDesc = renderGraph.GetTextureDesc(resourcesData.cameraColor);
                srcDesc.name = "_RadialInvertSource";
                srcDesc.clearBuffer = false;
                TextureHandle source = renderGraph.CreateTexture(srcDesc);
                renderGraph.AddBlitPass(resourcesData.cameraColor, source, Vector2.one, Vector2.zero,
                    passName: "RadialInvert Copy Color");

                using (var builder = renderGraph.AddRasterRenderPass<PassData>("RadialInvert", out var passData, Sampler))
                {
                    passData.material = mat;
                    passData.source = source;
                    builder.UseTexture(passData.source, AccessFlags.Read);
                    builder.SetRenderAttachment(resourcesData.activeColorTexture, 0, AccessFlags.Write);
                    builder.SetRenderFunc(static (PassData data, RasterGraphContext ctx) =>
                    {
                        if (mpb == null) mpb = new MaterialPropertyBlock();
                        mpb.Clear();
                        mpb.SetTexture(BlitTextureId, data.source);
                        mpb.SetVector(BlitScaleBiasId, new Vector4(1f, 1f, 0f, 0f));
                        ctx.cmd.DrawProcedural(Matrix4x4.identity, data.material, 0, MeshTopology.Triangles, 3, 1, mpb);
                    });
                }
            }
        }
    }

    /// <summary>
    /// RadialInvertFeature 구동용 정적 façade — ScreenFx/EnemyWeaponView와 같은 패턴(런타임에
    /// Resources 머티리얼을 로드해 프로퍼티를 직접 갱신). PredictionController가 카메라 pull-back
    /// 진행률(0~1)에 맞춰 매 프레임 SetRadius를 호출한다.
    /// </summary>
    public static class RadialInvertFx
    {
        const string ResourcePath = "Fx/RadialInvert";
        static Material mat;
        static bool loaded;

        /// <param name="t01">0~1 진행률(보통 카메라 pull-back 이징 값).</param>
        /// <param name="maxRadius">t01=1일 때 반경(화면비 보정 UV 단위) — 화면 구석까지 덮으려면 ~1.3 이상.</param>
        public static void SetRadius(float t01, float maxRadius)
        {
            if (!loaded) { mat = Resources.Load<Material>(ResourcePath); loaded = true; }
            if (mat == null) return;   // Renderer Feature 미설정 등 — 조용히 무시(그래픽 자체엔 영향 없음)
            mat.SetFloat("_Radius", Mathf.Clamp01(t01) * maxRadius);
        }
    }
}
