using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 빙의 시야 부채꼴 밖 차폐 — 카메라 바로 앞에 화면을 덮는 쿼드를 두고,
    /// <c>MINDHEXER/ViewConeMask</c> 셰이더가 픽셀마다 시선 각도를 재서 범위 밖만 칠한다.
    ///
    /// <para>이전의 "구 껍질" 방식은 대상 모델이 센티미터 스케일이면 껍질이 near clip 안으로 들어가
    /// 잘려나가(바닥이 뚫려 보임) 폐기했다. 화면 공간 방식은 스케일·near clip·지형과 무관하다.</para>
    ///
    /// ※ 단색 칠은 <b>임시 표현</b>이다. 노이즈·주사선·글리치로 교체 대상(§7).
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class ViewConeMask : MonoBehaviour
    {
        static readonly int PanId   = Shader.PropertyToID("_PanRange");
        static readonly int TiltId  = Shader.PropertyToID("_TiltRange");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int EyeId   = Shader.PropertyToID("_EyeWorldToLocal");

        Camera _cam;
        Transform _eye;
        Transform _quad;
        Material _mat;
        float _pan, _tilt;
        Color _color;

        void Awake() => _cam = GetComponent<Camera>();

        /// <summary>차폐 시작. eye 기준으로 좌우 ±pan, 상하 ±tilt 밖을 가린다.</summary>
        public void Begin(Transform eye, float panRange, float tiltRange, Color color)
        {
            _eye = eye; _pan = panRange; _tilt = tiltRange; _color = color;
            EnsureQuad();
            _quad.gameObject.SetActive(true);
        }

        public void End()
        {
            _eye = null;
            if (_quad != null) _quad.gameObject.SetActive(false);
        }

        void EnsureQuad()
        {
            if (_quad != null) return;

            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "[ViewConeMask]";
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);

            var shader = Shader.Find("MINDHEXER/ViewConeMask");
            if (shader == null)
            {
                Debug.LogError("[ViewConeMask] 셰이더를 못 찾음 — 빌드에선 Always Included Shaders에 넣어야 한다.");
                Destroy(go);
                return;
            }

            _mat = new Material(shader);
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = _mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;

            _quad = go.transform;
            _quad.SetParent(transform, false);
            _quad.gameObject.SetActive(false);
        }

        void LateUpdate()
        {
            if (_eye == null || _quad == null || _mat == null) return;

            // 쿼드를 카메라 바로 앞에 두고 화면을 정확히 덮게 크기를 맞춘다(FOV·종횡비 변화 대응).
            float dist = _cam.nearClipPlane * 2f;
            float h = 2f * dist * Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            _quad.localPosition = new Vector3(0f, 0f, dist);
            _quad.localRotation = Quaternion.identity;
            _quad.localScale = new Vector3(h * _cam.aspect, h, 1f);

            _mat.SetFloat(PanId, _pan);
            _mat.SetFloat(TiltId, _tilt);
            _mat.SetColor(ColorId, _color);
            _mat.SetMatrix(EyeId, _eye.worldToLocalMatrix);
        }
    }
}
