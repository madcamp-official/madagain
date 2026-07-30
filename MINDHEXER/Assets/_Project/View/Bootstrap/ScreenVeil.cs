using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 화면 전체를 덮는 검정막 + 하얀 가로선. 사망·기상 연출의 그림 담당. (사망_부활_연출_설계 §2)
    ///
    /// <para><b>캔버스를 쓰지 않는 이유</b>: <see cref="PossessionTransition"/>이 쓰는
    /// <c>ScreenSpaceOverlay</c> 캔버스는 <b>VR 스테레오에서 보이지 않는다</b>(확인된 결함).
    /// <c>VrStatsHud</c>가 같은 이유로 월드 스페이스를 쓴다. 그래서 여기도 카메라 자식 쿼드다.</para>
    ///
    /// <para><b>어느 카메라에 붙는가 — 여기가 함정이다.</b>
    /// <see cref="ViewmodelCamera"/>가 팔을 <b>별도 카메라</b>로 그리고 그 카메라의 <c>depth</c>가
    /// 메인보다 크다(= 나중에 그린다). 그래서 메인 카메라에 덮개를 붙이면 <b>팔이 덮개 위에 그려진다.</b>
    /// 반드시 <b>depth가 가장 큰 활성 카메라</b>에 붙고, 그 카메라가 보는 레이어에 둬야 한다.</para>
    ///
    /// <para>전용 카메라를 하나 더 두는 방법도 있지만 쓰지 않는다 — 모바일 VR에서 카메라 하나가
    /// 렌더 패스 하나이고, 이미 S24+에서 35fps까지 떨어진 적이 있다(ADR-0007). 쿼드 두 장 그리려고
    /// 패스를 늘릴 이유가 없다.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public class ScreenVeil : MonoBehaviour
    {
        [Header("검정막")]
        [Range(0f, 1f)]
        [Tooltip("0 = 투명, 1 = 완전 암전. 연출이 이 값을 움직인다.")]
        public float black;

        [Header("치지직")]
        [Range(0f, 1f)]
        [Tooltip("치지직 강도. 0 = 없음. 프로젝트 기존 치지직과 같은 셰이더라 " +
                 "강도는 '가로선이 몇 개 켜지는지'를 바꾼다(선이 흐려지는 게 아니다).")]
        public float glitch;

        [Tooltip("치지직 색. 프로젝트 기본은 인광 초록이고, 사망 연출은 흰색이 기본이다.")]
        public Color glitchColor = Color.white;

        [Tooltip("화면 높이 전체에 들어가는 가로줄 수. 클수록 얇고 촘촘하다.")]
        public float glitchRows = 220f;

        [Tooltip("줄 갱신 속도(초당 스텝). 클수록 격렬하다.")]
        public float glitchScrollSpeed = 30f;

        [Header("눈꺼풀")]
        [Range(0f, 1f)]
        [Tooltip("1 = 완전히 뜸(마스크 없음), 0 = 완전히 감음(화면 검정). 기상 연출이 움직인다.")]
        public float eyelidOpen = 1f;

        [Range(0f, 1f)]
        [Tooltip("다 뜬 뒤에도 남는 코너 어둠. 정신이 덜 든 여운.")]
        public float eyelidVignette;

        [Range(0.01f, 1f)]
        [Tooltip("눈꺼풀 가장자리 부드러움.")]
        public float eyelidFeather = 0.35f;

        [Header("배치")]
        [Tooltip("근평면에서 이만큼 앞에 둔다(m). 너무 작으면 잘리고, 크면 씬 물체와 겹칠 수 있다.")]
        public float distanceFromNear = 0.02f;

        [Tooltip("계산한 크기에 곱하는 여유. 1보다 조금 크게 둬 모서리가 새지 않게 한다.\n" +
                 "★ VR은 양안 투영이 비대칭이라 PC보다 더 필요할 수 있다 — 실기에서 봐야 한다.")]
        public float sizeMargin = 1.15f;

        Camera _cam;
        Transform _root;
        Transform _blackQuad, _glitchQuad, _eyelidQuad;
        Material _blackMat, _glitchMat, _eyelidMat;

        /// <summary>지금 화면을 조금이라도 가리고 있는가.</summary>
        public bool Covering => black > 0.001f || glitch > 0.001f
                             || eyelidOpen < 0.999f || eyelidVignette > 0.001f;

        void OnEnable() => Ensure();

        void OnDestroy()
        {
            if (_root != null) Destroy(_root.gameObject);
            if (_blackMat != null) Destroy(_blackMat);
            if (_glitchMat != null) Destroy(_glitchMat);
            if (_eyelidMat != null) Destroy(_eyelidMat);
        }

        /// <summary>즉시 완전 암전.</summary>
        public void BlackOut() { black = 1f; glitch = 0f; Apply(); }

        /// <summary>즉시 걷어냄. 눈꺼풀도 완전히 뜬 상태로 되돌린다.</summary>
        public void Clear()
        {
            black = 0f; glitch = 0f;
            eyelidOpen = 1f; eyelidVignette = 0f;
            Apply();
        }

        void LateUpdate()
        {
            // 카메라가 바뀔 수 있다(뷰모델 카메라가 늦게 생기거나 VR로 갈아타는 등).
            if (_cam == null || !_cam.isActiveAndEnabled || _root == null) Ensure();
            Apply();
        }

        /// <summary>depth가 가장 큰 활성 카메라를 찾아 그 밑에 쿼드를 만든다.</summary>
        void Ensure()
        {
            Camera top = FindTopCamera();
            if (top == null) return;

            if (_cam == top && _root != null) return;   // 그대로면 다시 만들지 않는다

            _cam = top;
            if (_root != null) Destroy(_root.gameObject);

            var rootGo = new GameObject("[ScreenVeil]");
            rootGo.transform.SetParent(_cam.transform, false);
            rootGo.layer = _cam.gameObject.layer;   // 그 카메라가 보는 레이어여야 그려진다
            _root = rootGo.transform;

            _blackQuad = MakeQuad("Black", MakeUnlit(Color.black), out _blackMat);
            _eyelidQuad = MakeQuad("Eyelid", MakeEyelid(), out _eyelidMat);
            _glitchQuad = MakeQuad("Glitch", MakeGlitch(), out _glitchMat);
        }

        /// <summary>
        /// 켜져 있는 카메라 중 <c>depth</c>가 가장 큰 것. 그게 마지막에 그리는 카메라다.
        /// 동점이면 뒤에 오는 것을 쓴다 — Unity의 실제 순서와 같다.
        /// </summary>
        static Camera FindTopCamera()
        {
            Camera best = null;
            var all = Camera.allCameras;   // 활성 카메라만 들어 있다
            for (int i = 0; i < all.Length; i++)
                if (best == null || all[i].depth >= best.depth) best = all[i];
            return best;
        }

        Transform MakeQuad(string name, Material mat, out Material stored)
        {
            stored = mat;

            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;

            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);   // 조준 레이·이동 판정을 가로막으면 안 된다

            go.transform.SetParent(_root, false);
            go.layer = _root.gameObject.layer;

            var mr = go.GetComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.sharedMaterial = mat;
            return go.transform;
        }

        /// <summary>무엇이 앞에 있든 덮는 unlit 반투명 재질(검정막용).</summary>
        static Material MakeUnlit(Color color)
        {
            var m = Load("Universal Render Pipeline/Unlit");
            if (m == null) return null;

            m.SetFloat("_Surface", 1f);                     // Transparent
            m.SetFloat("_Blend", 0f);                       // Alpha
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetColor("_BaseColor", color);
            return m;
        }

        /// <summary>
        /// 치지직 재질. <b>덮개 전용 <c>MINDHEXER/VeilGlitch</c></b>를 쓴다.
        ///
        /// <para>해킹 대상용 <c>HackGlitch</c>를 쓰지 않는 이유: 그건 <b>월드 좌표</b>로 줄을 만들어서,
        /// 카메라에 붙은 쿼드에 쓰면 (ㄱ) 둘러볼 때 UV 평면이 스왑되며 줄이 기울어지고
        /// (ㄴ) 쿼드가 월드에서 몇 cm뿐이라 줄이 화면의 5~9%로 굵어진다. 실제로 그렇게 보였다.
        /// 덮개용은 화면 좌표로 줄을 만들어 항상 수평이고 밀도가 화면 기준이다.</para>
        ///
        /// <para>강도 모델은 양쪽이 같다 — <b>알파가 아니라 켜진 줄의 개수</b>를 바꾼다.
        /// 두 치지직이 게임 안에서 같은 성격으로 읽혀야 하기 때문이다.</para>
        /// </summary>
        Material MakeGlitch()
        {
            var m = Load("MINDHEXER/VeilGlitch");
            if (m == null) return null;

            m.SetColor("_GlitchColor", glitchColor);
            m.SetFloat("_RowCount", glitchRows);
            m.SetFloat("_ScrollSpeed", glitchScrollSpeed);
            m.SetFloat("_GlitchIntensity", 0f);
            return m;
        }

        /// <summary>
        /// 눈꺼풀 마스크. 화면 위아래에서 닫히는 눌린 타원이다.
        ///
        /// <para>레퍼런스(Unreal 포럼 "Waking Up effect in First Person")의 결론이
        /// <b>카메라 움직임이 아니라 화면 마스크</b>였다 — 좁게 닫힌 반경 그라디언트를 애니메이션해
        /// 넓히는 것이 "눈을 뜬다"의 실제 시각 언어다. 카메라만 흔들면 깨어남이 아니라
        /// "취한 채 서 있음"이 된다.</para>
        /// </summary>
        Material MakeEyelid()
        {
            var m = Load("MINDHEXER/VeilEyelid");
            if (m == null) return null;

            m.SetColor("_Color", Color.black);
            m.SetFloat("_Open", 1f);
            m.SetFloat("_Vignette", 0f);
            m.SetFloat("_Feather", eyelidFeather);
            return m;
        }

        /// <summary>
        /// 셰이더를 찾아 재질을 만든다. 항상 앞에 그려지도록 공통 설정을 건다.
        ///
        /// <para><c>Shader.Find</c>의 결과를 <b>반드시 확인한다</b> — 에디터에서는 항상 찾아지지만
        /// 빌드에서는 Always Included Shaders에 없으면 null이 온다. 확인 없이 진행해서
        /// 프레임마다 예외 + 오브젝트 누수가 났던 전례가 있다(ControlTether).</para>
        /// </summary>
        static Material Load(string shaderName)
        {
            var shader = Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogError($"[화면덮개] '{shaderName}' 셰이더를 찾지 못했습니다. " +
                               "Project Settings ▸ Graphics ▸ Always Included Shaders 에 추가하십시오.");
                return null;
            }

            var m = new Material(shader);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.Always);
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = 4000;   // Overlay 근처 — 투명 물체보다도 뒤에 그린다
            return m;
        }

        /// <summary>
        /// 근평면 앞에 두고 FOV로 화면을 채우는 크기를 계산한다. 매 프레임 다시 계산한다 —
        /// 빙의 줌(<c>zoomFovDelta</c>) 등으로 FOV가 바뀌는 동안에도 새면 안 된다.
        /// </summary>
        void Apply()
        {
            if (_cam == null || _root == null) return;

            float z = _cam.nearClipPlane + Mathf.Max(0.001f, distanceFromNear);
            float h = 2f * z * Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * sizeMargin;
            float w = h * Mathf.Max(0.01f, _cam.aspect);

            if (_blackQuad != null)
            {
                Place(_blackQuad, z, w, h);
                bool on = black > 0.001f;
                Show(_blackQuad, on);
                if (on && _blackMat != null)
                {
                    Color c = _blackMat.GetColor("_BaseColor");
                    c.a = Mathf.Clamp01(black);
                    _blackMat.SetColor("_BaseColor", c);
                }
            }

            if (_eyelidQuad != null)
            {
                // 눈꺼풀은 검정막보다 앞 — 암전이 걷힌 뒤에도 남아야 한다.
                Place(_eyelidQuad, z * 0.997f, w, h);
                bool on = eyelidOpen < 0.999f || eyelidVignette > 0.001f;
                Show(_eyelidQuad, on);
                if (on && _eyelidMat != null)
                {
                    _eyelidMat.SetFloat("_Open", Mathf.Clamp01(eyelidOpen));
                    _eyelidMat.SetFloat("_Vignette", Mathf.Clamp01(eyelidVignette));
                    _eyelidMat.SetFloat("_Feather", eyelidFeather);
                }
            }

            if (_glitchQuad != null)
            {
                // 치지직이 제일 앞 — 눈꺼풀 위로도 튀어야 한다.
                Place(_glitchQuad, z * 0.995f, w, h);
                bool on = glitch > 0.001f;
                Show(_glitchQuad, on);
                if (on && _glitchMat != null)
                {
                    _glitchMat.SetFloat("_GlitchIntensity", Mathf.Clamp01(glitch));
                    _glitchMat.SetColor("_GlitchColor", glitchColor);
                    _glitchMat.SetFloat("_RowCount", glitchRows);
                    _glitchMat.SetFloat("_ScrollSpeed", glitchScrollSpeed);
                }
            }
        }

        static void Place(Transform t, float z, float w, float h)
        {
            t.localPosition = new Vector3(0f, 0f, z);
            t.localRotation = Quaternion.identity;
            t.localScale = new Vector3(w, h, 1f);
        }

        static void Show(Transform t, bool on)
        {
            if (t.gameObject.activeSelf != on) t.gameObject.SetActive(on);
        }
    }
}
