using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 베기 이펙트. ★ 칼의 실제 움직임과 무관한 독립 이펙트.
    ///
    /// 프로 슬래시 VFX 구조:
    ///   · 메시는 <b>정적</b> — 칼이 3D를 훑고 간 곡면(원뿔 단면). 움직임은 노이즈 스크롤이 만든다.
    ///   · 두께감은 기하학이 아니라 <b>부드러운 형태마스크 × 곱해진 노이즈 × 다중 레이어</b>에서 나온다.
    ///   · 레이어를 반경·노이즈 오프셋을 어긋내 겹치면 가산으로 쌓여 볼륨이 생긴다.
    ///
    /// 텍스처: Tools/이펙트/베기 텍스처 굽기 로 생성(Art/VFX/Resources).
    /// 적중 이펙트(방사형 스트릭·링·섬광)는 별도 — 여기 없음.
    /// </summary>
    public class SwordSlash : MonoBehaviour
    {
        [Header("형태 (직선 방추형 입체)")]
        [Tooltip("베는 선의 길이(로컬 +X 방향)")]
        public float length = 9f;
        [Tooltip("칼날이 벌어진 방향의 폭(반지름)")]
        public float radiusWide = 0.85f;
        [Tooltip("그와 수직인 두께(반지름). 0에 가까울수록 납작 → 크게 줘야 입체로 보임")]
        public float radiusThick = 0.30f;
        [Tooltip("양 끝이 뾰족해지는 정도(클수록 날카롭게)")]
        public float taperPower = 1.4f;
        [Tooltip("길이 방향 분할")]
        public int segmentsLength = 72;
        [Tooltip("단면 둘레 분할(입체감)")]
        public int segmentsRing = 14;

        [Header("볼륨 (중첩 셸)")]
        [Tooltip("겹칠 셸 수. 안쪽=흰 코어, 바깥=녹색 헤일로")]
        [Range(1, 6)] public int layerCount = 4;
        [Tooltip("가장 안쪽 셸의 크기 비율")]
        [Range(0.05f, 1f)] public float innerShellScale = 0.28f;
        [Tooltip("바깥 셸일수록 약하게")]
        [Range(0f, 1f)] public float layerFalloff = 0.55f;

        [Header("타이밍 (fast in, slow out)")]
        public float revealTime = 0.05f;
        public float holdTime   = 0.04f;
        public float fadeTime   = 0.20f;

        [Header("노이즈 (결)")]
        // v(반경) 타일을 낮게 두면 노이즈가 반경 방향으로 늘어나 "결"이 된다.
        public Vector2 noiseTile1   = new Vector2(3f, 0.35f);
        public Vector2 noiseScroll1 = new Vector2(-0.8f, 0.05f);
        public Vector2 noiseTile2   = new Vector2(7f, 0.5f);
        public Vector2 noiseScroll2 = new Vector2(-1.5f, -0.04f);
        [Tooltip("0=매끈한 면, 1=완전히 부서짐")]
        [Range(0f, 1f)] public float noiseAmount = 0.5f;
        [Range(0.2f, 6f)] public float contrast  = 1.35f;
        [Range(0f, 8f)]   public float intensity = 0.80f;

        [Header("색 (강도 기반 램프, HDR)")]
        [ColorUsage(true, true)] public Color colorLow  = new Color(0.12f, 1.0f, 0.10f, 1f);
        [ColorUsage(true, true)] public Color colorMid  = new Color(1.6f, 1.15f, 0.05f, 1f);
        [ColorUsage(true, true)] public Color colorHigh = new Color(3.2f, 3.2f, 2.6f, 1f);
        [Range(0f, 1f)] public float ramp1 = 0.18f;
        [Range(0f, 1f)] public float ramp2 = 0.55f;
        [Range(0f, 1f)] public float ramp3 = 0.85f;

        [Range(0.001f, 1f)] public float revealSoft = 0.12f;
        [Range(0f, 1f)]     public float tailFade   = 0.45f;

        // ── 합성 방식 ──
        // 가산은 더하기만 하므로 검정을 칠할 수 없다(검정=투명).
        // 검은 검기를 쓰려면 알파나 곱셈으로 바꿔야 한다.
        public const int BlendAdd = 0, BlendAlpha = 1, BlendMul = 2;
        public static readonly string[] BlendNames = { "가산(빛남)", "알파(검정 가능)", "곱셈(가장 검음)" };
        [Tooltip("0 가산 · 1 알파 · 2 곱셈")]
        public int blendMode = BlendAdd;

        [Header("튜닝")]
        [Tooltip("켜면 다 그어진 상태로 남아 사라지지 않음 → Inspector로 조절")]
        public bool hold;

        Mesh mesh;
        MeshRenderer[] layers;
        MaterialPropertyBlock mpb;
        float age;
        int builtHash;

        static readonly int IdShape = Shader.PropertyToID("_ShapeTex");
        static readonly int IdN1 = Shader.PropertyToID("_Noise1");
        static readonly int IdN2 = Shader.PropertyToID("_Noise2");
        static readonly int IdT1 = Shader.PropertyToID("_NoiseTile1");
        static readonly int IdS1 = Shader.PropertyToID("_NoiseScroll1");
        static readonly int IdT2 = Shader.PropertyToID("_NoiseTile2");
        static readonly int IdS2 = Shader.PropertyToID("_NoiseScroll2");
        static readonly int IdOff = Shader.PropertyToID("_NoiseOffset");
        static readonly int IdNAmt = Shader.PropertyToID("_NoiseAmount");
        static readonly int IdContrast = Shader.PropertyToID("_Contrast");
        static readonly int IdIntensity = Shader.PropertyToID("_Intensity");
        static readonly int IdCLow = Shader.PropertyToID("_ColorLow");
        static readonly int IdCMid = Shader.PropertyToID("_ColorMid");
        static readonly int IdCHigh = Shader.PropertyToID("_ColorHigh");
        static readonly int IdR1 = Shader.PropertyToID("_Ramp1");
        static readonly int IdR2 = Shader.PropertyToID("_Ramp2");
        static readonly int IdR3 = Shader.PropertyToID("_Ramp3");
        static readonly int IdRev = Shader.PropertyToID("_Reveal");
        static readonly int IdRevS = Shader.PropertyToID("_RevealSoft");
        static readonly int IdTail = Shader.PropertyToID("_TailFade");
        static readonly int IdFade = Shader.PropertyToID("_Fade");
        static readonly int IdSrc = Shader.PropertyToID("_SrcBlend");
        static readonly int IdDst = Shader.PropertyToID("_DstBlend");
        static readonly int IdMul = Shader.PropertyToID("_MulMode");

        Material mat;
        int builtBlend = -1;

        /// <summary>블렌드 인자는 재질 상태라 MaterialPropertyBlock으로 못 바꾼다 — 재질에 직접 쓴다.</summary>
        void ApplyBlend()
        {
            if (mat == null) return;
            const int One = 1, Zero = 0, DstColor = 2, OneMinusSrcAlpha = 10;
            switch (blendMode)
            {
                case BlendAlpha:                       // 프리멀티플라이드 알파 — 검정이 검정으로 찍힌다
                    mat.SetFloat(IdSrc, One); mat.SetFloat(IdDst, OneMinusSrcAlpha); mat.SetFloat(IdMul, 0f);
                    break;
                case BlendMul:                         // 곱셈 — 화면을 깎아내 가장 새까맣다
                    mat.SetFloat(IdSrc, DstColor); mat.SetFloat(IdDst, Zero); mat.SetFloat(IdMul, 1f);
                    break;
                default:                               // 가산 — 빛나는 검기(검정은 투명)
                    mat.SetFloat(IdSrc, One); mat.SetFloat(IdDst, One); mat.SetFloat(IdMul, 0f);
                    break;
            }
            // 곱셈·알파는 블룸에 태우지 않도록 큐를 살짝 뒤로 뺀다(가산과 섞이면 지저분해진다)
            mat.renderQueue = blendMode == BlendAdd ? 3020 : 3010;
            builtBlend = blendMode;
        }

        /// <summary>베기 생성. which: 1=평타1, 2=평타2(반대), t=찌르기.</summary>
        public static SwordSlash Spawn(Vector3 pos, Quaternion rot, string which)
        {
            var go = new GameObject("SwordSlash");
            var s = go.AddComponent<SwordSlash>();
            // 로컬 +X가 베는 선. Z롤로 화면상의 사선 각도를 정한다.
            float roll;
            switch (which)
            {
                case "2":                    // 평타2 — 좌상 → 우하
                    roll = -35f;
                    break;
                case "t": case "thrust":     // 찌르기 — 짧고 가늘게, 거의 수평
                    roll = 0f;
                    s.length = 7f; s.radiusWide = 0.45f; s.radiusThick = 0.30f;
                    s.taperPower = 1.9f;
                    s.revealTime = 0.035f; s.fadeTime = 0.13f;
                    break;
                default:                     // 평타1 — 좌하 → 우상
                    roll = 35f;
                    break;
            }
            go.transform.SetPositionAndRotation(pos, rot * Quaternion.Euler(0f, 0f, roll));
            return s;
        }

        void Awake()
        {
            var sh = Shader.Find("Precog/SwordSlash");
            if (sh == null) { Debug.LogError("[SwordSlash] 셰이더 Precog/SwordSlash 없음"); Destroy(gameObject); return; }

            var shapeTex = Resources.Load<Texture2D>("Slash_Shape");
            var n1 = Resources.Load<Texture2D>("Slash_Noise1");
            var n2 = Resources.Load<Texture2D>("Slash_Noise2");
            if (shapeTex == null || n1 == null || n2 == null)
                Debug.LogWarning("[SwordSlash] 텍스처 없음 — 메뉴 Tools/이펙트/베기 텍스처 굽기 를 먼저 실행하십시오");

            mesh = new Mesh { name = "SwordSlashMesh" };
            mesh.MarkDynamic();
            BuildMesh();

            mat = new Material(sh) { name = "SwordSlashMat" };
            ApplyBlend();
            if (shapeTex != null) mat.SetTexture(IdShape, shapeTex);
            if (n1 != null) mat.SetTexture(IdN1, n1);
            if (n2 != null) mat.SetTexture(IdN2, n2);

            // 중첩 셸 — 안쪽(작고 밝은 흰 코어)에서 바깥(크고 옅은 녹색 헤일로)으로.
            // 가산으로 쌓여 "속이 찬 빛 덩어리"가 된다. 길이는 유지하고 단면만 키운다.
            int n = Mathf.Clamp(layerCount, 1, 6);
            layers = new MeshRenderer[n];
            for (int i = 0; i < n; i++)
            {
                var lg = new GameObject("Shell" + i);
                lg.transform.SetParent(transform, false);
                float f = n == 1 ? 1f : i / (float)(n - 1);          // 0=안쪽, 1=바깥
                float sc = Mathf.Lerp(innerShellScale, 1f, f);
                lg.transform.localScale = new Vector3(Mathf.Lerp(0.94f, 1f, f), sc, sc);

                lg.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = lg.AddComponent<MeshRenderer>();
                r.sharedMaterial = mat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                layers[i] = r;
            }

            mpb = new MaterialPropertyBlock();
        }

        /// <summary>
        /// 직선 방추형(spindle) 입체. 로컬 +X를 따라 뻗고, 양 끝은 점으로 모이며
        /// 가운데가 가장 두껍다. 단면은 타원(넓은 축=칼날 방향, 짧은 축=두께).
        /// 닫힌 입체라 어느 각도에서 봐도 형태가 있다 — 면 한 겹(2D)이 아님.
        /// UV: u=길이 방향, v=단면을 가로지르는 폭 위치(0~1).
        /// </summary>
        void BuildMesh()
        {
            int nu = Mathf.Max(8, segmentsLength) + 1;
            int nr = Mathf.Max(4, segmentsRing);

            var verts = new Vector3[nu * nr];
            var uvs   = new Vector2[nu * nr];
            var cols  = new Color[nu * nr];
            var tris  = new int[(nu - 1) * nr * 6];

            for (int i = 0; i < nu; i++)
            {
                float t = i / (float)(nu - 1);
                float x = (t - 0.5f) * length;
                // 양 끝 0, 가운데 1 — 뾰족한 방추형.
                // ★ sin(π)는 부동소수점 오차로 미세한 음수가 나올 수 있고,
                //   음수의 비정수 거듭제곱은 NaN이 되어 메시 전체가 깨진다 → Max(0)로 막는다.
                float taper = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Mathf.PI * t)), taperPower);

                for (int j = 0; j < nr; j++)
                {
                    float ang = (j / (float)nr) * Mathf.PI * 2f;
                    float cy = Mathf.Cos(ang), sz = Mathf.Sin(ang);
                    int idx = i * nr + j;
                    verts[idx] = new Vector3(x, radiusWide * taper * cy, radiusThick * taper * sz);
                    // v: 단면을 가로지르는 위치(양 옆 가장자리=0, 넓은 면 중앙=1)
                    uvs[idx]  = new Vector2(t, 0.5f + 0.5f * cy);
                    cols[idx] = Color.white;
                }
            }

            int k2 = 0;
            for (int i = 0; i < nu - 1; i++)
                for (int j = 0; j < nr; j++)
                {
                    int jn = (j + 1) % nr;
                    int a = i * nr + j, b = i * nr + jn;
                    int c = (i + 1) * nr + j, d = (i + 1) * nr + jn;
                    tris[k2++] = a; tris[k2++] = c; tris[k2++] = b;
                    tris[k2++] = b; tris[k2++] = c; tris[k2++] = d;
                }

            mesh.Clear();
            mesh.vertices = verts; mesh.uv = uvs; mesh.colors = cols; mesh.triangles = tris;
            mesh.RecalculateBounds();
            builtHash = ShapeHash();
        }

        int ShapeHash()
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + length.GetHashCode();
                h = h * 31 + radiusWide.GetHashCode();
                h = h * 31 + radiusThick.GetHashCode();
                h = h * 31 + taperPower.GetHashCode();
                h = h * 31 + segmentsLength; h = h * 31 + segmentsRing;
                return h;
            }
        }

        void Update()
        {
            if (mesh == null || layers == null) return;
            if (ShapeHash() != builtHash) BuildMesh();
            if (blendMode != builtBlend) ApplyBlend();   // 런타임에 합성 방식을 바꿔도 즉시 반영

            float reveal, fade;
            if (hold) { reveal = 1f; fade = 1f; }
            else
            {
                age += Time.deltaTime;
                reveal = revealTime <= 0f ? 1f : Mathf.Clamp01(age / revealTime);
                fade = age <= revealTime + holdTime
                    ? 1f
                    : 1f - Mathf.Clamp01((age - revealTime - holdTime) / Mathf.Max(fadeTime, 0.0001f));
            }

            PushProps(reveal, fade);

            if (!hold && age >= revealTime + holdTime + fadeTime) Destroy(gameObject);
        }

        /// <summary>
        /// 값을 셰이더로 밀어넣는다.
        /// ★ 생성 직후에도 반드시 한 번 호출해야 한다. 안 그러면 첫 프레임이
        ///   재질 기본값(초록)으로 렌더되어 "초록이 번쩍했다가 바뀌는" 현상이 생긴다.
        ///   (Fire()는 코루틴에서 생성하므로 그 프레임엔 Update가 돌지 않는다)
        /// </summary>
        public void Refresh() => PushProps(0f, 1f);

        void PushProps(float reveal, float fade)
        {
            if (layers == null || mpb == null) return;
            for (int i = 0; i < layers.Length; i++)
            {
                var r = layers[i];
                if (r == null) continue;
                // 안쪽 셸일수록 밝고(흰 코어), 바깥일수록 옅게(녹색 헤일로)
                float f = layers.Length == 1 ? 0f : i / (float)(layers.Length - 1);
                float lay = Mathf.Lerp(1f, 1f - layerFalloff, f);

                mpb.Clear();
                mpb.SetVector(IdT1, noiseTile1); mpb.SetVector(IdS1, noiseScroll1);
                mpb.SetVector(IdT2, noiseTile2); mpb.SetVector(IdS2, noiseScroll2);
                mpb.SetVector(IdOff, new Vector4(i * 0.37f, i * 0.19f, i * 0.53f, i * 0.11f));
                mpb.SetFloat(IdNAmt, noiseAmount);
                mpb.SetFloat(IdContrast, contrast);
                mpb.SetFloat(IdIntensity, intensity * lay);
                mpb.SetColor(IdCLow, colorLow); mpb.SetColor(IdCMid, colorMid); mpb.SetColor(IdCHigh, colorHigh);
                mpb.SetFloat(IdR1, ramp1); mpb.SetFloat(IdR2, ramp2); mpb.SetFloat(IdR3, ramp3);
                mpb.SetFloat(IdRev, reveal); mpb.SetFloat(IdRevS, revealSoft);
                mpb.SetFloat(IdTail, tailFade); mpb.SetFloat(IdFade, fade);
                r.SetPropertyBlock(mpb);
            }
        }
    }
}

