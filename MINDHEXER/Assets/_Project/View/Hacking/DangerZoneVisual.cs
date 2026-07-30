using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 위험 구역을 흑백 노이즈로 그린다. (기초_설계안 §7)
    ///
    /// <para>플레이어를 죽일 수 있는 것은 <b>경비병 부채꼴</b>과 <b>터렛 사선</b> 둘뿐이고, 둘 다
    /// 같은 재질(<c>MINDHEXER/DangerNoise</c>)을 쓴다 — 지오메트리만 다르고 보이는 언어는 같다.
    /// "이 격자 위에 서면 죽는다"가 한 가지 신호로 통일된다.</para>
    ///
    /// <para><b>경비병은 바닥 면만 그린다.</b> 감지 영역은 키 높이까지 있는 쐐기지만(§GuardDetection)
    /// 옆면까지 그리면 화면이 노이즈로 덮여 아무것도 안 보인다. 경비병이 발판 위에 서 있으면
    /// 그 바닥 면이 <b>공중에 떠 있게</b> 된다 — 의도된 모습이다.</para>
    ///
    /// <para>⚠️ <b>알려진 한계</b>: 바닥 면만 보이므로 <b>높이 정보가 화면에 없다.</b> 공중에 뜬
    /// 부채꼴 아래를 지나가면 실제로는 안전한데 플레이어는 그걸 알 수 없다. 실기에서 헷갈리면
    /// 옆면을 옅게 넣거나 테두리를 세우는 보정이 필요하다.</para>
    ///
    /// <para><see cref="ExecuteAlways"/>라 편집 중에도 보인다 — 레벨을 배치하면서 위험 구역이
    /// 어디를 덮는지 바로 확인할 수 있어야 한다.</para>
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    // ★ RequireComponent로 보장한다. 예전엔 코드에서 GetComponent ?? AddComponent로 붙였는데,
    //   Unity의 "가짜 null"(네이티브 쪽이 없는 래퍼 객체)은 <c>??</c>를 그냥 통과한다 — ==만
    //   오버로드돼 있고 ??는 raw 참조를 보기 때문이다. 그래서 AddComponent가 불리지 않고
    //   비어 있는 참조에 대입하다 MissingComponentException이 났다(실제로 겪음).
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    public class DangerZoneVisual : MonoBehaviour
    {
        public const string ShaderName = "MINDHEXER/DangerNoise";

        [Tooltip("경비병 감지. 비우면 자기/부모에서 찾는다. 있으면 부채꼴 모드.")]
        public GuardDetection guard;

        [Tooltip("터렛 총. 비우면 자기/부모에서 찾는다. 있으면 사선(띠) 모드.")]
        public TurretGun turret;

        [Tooltip("부채꼴 호를 몇 조각으로 나눠 그릴지. 클수록 매끄럽다.")]
        [Range(4, 64)] public int arcSegments = 24;

        [Tooltip("경비병 발밑 쪽을 잘라 내는 반지름(m). ★ 꼭지점이 바늘처럼 뾰족하면 경비병 발에 " +
                 "노이즈가 달라붙어 지저분하고, 좁은 쪽 폭이 화면 몇 픽셀이라 격자도 안 보인다.\n" +
                 "이 값만큼 안쪽을 호로 잘라 내면 <b>양쪽이 다 둥근 고리 조각</b>이 된다.\n" +
                 "※ <b>표시 전용이다.</b> 판정(GuardDetection)에는 구멍이 없다 — 발밑에 붙어도 걸린다.")]
        public float innerRadius = 0.6f;

        [Tooltip("바닥에서 살짝 띄우는 높이(m). 0이면 z-파이팅이 난다.")]
        public float floorLift = 0.02f;

        [Tooltip("터렛 사선의 폭(m). ★ 너무 얇으면 화면에서 격자가 한두 개밖에 안 들어가 " +
                 "노이즈가 아니라 점선으로 보인다 — 총구 굵기 정도는 줘야 한다. " +
                 "0.35 → 0.21 (3/5)로 줄였고, 격자도 함께 촘촘해졌으므로 점선으로 보이지 않는다.")]
        public float beamWidth = 0.21f;

        [Tooltip("끄면 표시만 사라진다(판정은 그대로). 레벨 스크린샷 등에 쓴다.")]
        public bool show = true;

        MeshFilter _mf;
        MeshRenderer _mr;
        Mesh _mesh;
        Material _mat;

        // 마지막으로 그린 모양. 값이 안 바뀌면 메시를 다시 만들지 않는다.
        float _lastA, _lastB, _lastC;
        int _lastSeg;
        bool _lastFan;

        void OnEnable()
        {
            if (guard == null) guard = GetComponentInParent<GuardDetection>();
            if (turret == null) turret = GetComponentInParent<TurretGun>();

            // ★ 여기서 EnsureRenderer를 부르면 안 된다.
            //   TurretGun.BuildRangeGizmo가 <c>AddComponent&lt;DangerZoneVisual&gt;()</c>로 우리를 붙이는데,
            //   OnEnable은 그 AddComponent가 <b>아직 실행 중일 때 동기적으로</b> 불린다. 그 안에서 다시
            //   AddComponent(MeshFilter/MeshRenderer)를 하면 재진입이 되어, 반쯤 만들어진 렌더러 래퍼가
            //   돌아온다 — 뒤에 sharedMaterial을 쓰는 순간 MissingComponentException으로 터진다.
            //   LateUpdate가 어차피 매 프레임 부르므로 한 프레임 늦게 만들어도 아무 문제가 없다.
        }

        void OnDisable()
        {
            if (_mr != null) _mr.enabled = false;
        }

        /// <summary>
        /// 경비병 몸에 빙의 중인가. 그동안은 <b>모든</b> 경비병 부채꼴을 숨긴다.
        ///
        /// <para><see cref="GuardDetection"/>이 빙의 중엔 판정 자체를 건너뛰므로(위장 성립), 그때
        /// 부채꼴을 계속 그리면 <b>표시가 거짓말</b>이 된다. 판정과 표시가 같은 조건 하나를 보게 해
        /// 둘이 어긋날 수 없게 한다 — 내가 들어간 그 경비병만이 아니라 전부 숨기는 이유다.</para>
        /// </summary>
        static bool PossessingBody =>
            ViewEntryController.Current != null && ViewEntryController.Current.AllowsMove;

        void LateUpdate()
        {
            if (!EnsureRenderer()) return;

            bool fan = guard != null;
            bool active = show && (fan ? (guard.Active && !PossessingBody) : turret != null);
            _mr.enabled = active;
            if (!active) return;

            if (fan) BuildFan();
            else BuildBeam();
        }

        bool EnsureRenderer()
        {
            if (guard == null && turret == null) return false;

            // ⚠️ UnityEngine.Object에 `??`를 쓰면 안 된다 — `??`는 진짜 null만 보는데, 파괴됐거나
            //    제대로 만들어지지 않은 유니티 오브젝트는 <b>null이 아닌 래퍼</b>로 돌아온다.
            //    유니티가 오버로드한 `== null`로 검사해야 그것까지 걸린다.
            if (_mf == null)
            {
                _mf = GetComponent<MeshFilter>();
                if (_mf == null) _mf = gameObject.AddComponent<MeshFilter>();
            }
            if (_mr == null)
            {
                _mr = GetComponent<MeshRenderer>();
                if (_mr == null) _mr = gameObject.AddComponent<MeshRenderer>();
            }

            // 만들기에 실패했으면 조용히 물러난다 — 여기서 계속 가면 사용 시점에 예외로 터진다.
            if (_mf == null || _mr == null) return false;

            if (_mesh == null)
            {
                _mesh = new Mesh { name = "[DangerZone]" };
                _mesh.MarkDynamic();
                _mf.sharedMesh = _mesh;
            }

            if (_mat == null)
            {
                // 재질은 프로젝트 애셋을 쓰지 않는다 — 위험 구역마다 값을 다르게 줄 이유가 없고,
                // 애셋을 잃어버리면 조용히 분홍색이 되므로 셰이더에서 직접 만든다.
                var sh = Shader.Find(ShaderName);
                if (sh == null)
                {
                    Debug.LogWarning($"[위험구역] 셰이더 '{ShaderName}'를 찾지 못했습니다.", this);
                    return false;
                }
                _mat = new Material(sh) { name = "[DangerNoise]" };
                _mr.sharedMaterial = _mat;
            }

            _mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _mr.receiveShadows = false;
            return true;
        }

        /// <summary>
        /// 경비병 — 발밑 높이의 <b>고리 조각</b>(양쪽이 다 둥근 부채꼴).
        ///
        /// <para>꼭지점을 <see cref="innerRadius"/>만큼 <b>호로 잘라 낸다.</b> 바늘처럼 뾰족한 꼭지점은
        /// ① 경비병 발에 노이즈가 달라붙어 지저분하고 ② 좁은 쪽이 화면에서 몇 픽셀이라 격자가 안
        /// 보인다. 안쪽을 잘라 내면 가까운 쪽도 먼 쪽처럼 곡선이 되어 모양이 읽힌다.</para>
        /// </summary>
        void BuildFan()
        {
            float rOut = guard.DetectRadius;
            float rIn = Mathf.Clamp(innerRadius, 0f, rOut * 0.9f);
            float half = guard.halfAngleDeg;

            if (!Dirty(rOut, half, rIn, arcSegments, true)) { PlaceFan(); return; }

            int seg = Mathf.Max(4, arcSegments);
            // 안쪽 호와 바깥쪽 호를 한 쌍씩 → 사각형 띠를 이어 붙인다.
            var verts = new Vector3[(seg + 1) * 2];
            var tris = new int[seg * 6];

            for (int i = 0; i <= seg; i++)
            {
                float a = Mathf.Lerp(-half, half, i / (float)seg) * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                verts[i * 2 + 0] = dir * rIn;    // 안쪽 호
                verts[i * 2 + 1] = dir * rOut;   // 바깥쪽 호
            }
            for (int i = 0; i < seg; i++)
            {
                int a = i * 2, b = i * 2 + 1, c = i * 2 + 2, d = i * 2 + 3;
                tris[i * 6 + 0] = a; tris[i * 6 + 1] = b; tris[i * 6 + 2] = d;
                tris[i * 6 + 3] = a; tris[i * 6 + 4] = d; tris[i * 6 + 5] = c;
            }

            _mesh.Clear();
            _mesh.vertices = verts;
            _mesh.triangles = tris;
            _mesh.RecalculateBounds();
            PlaceFan();
        }

        /// <summary>
        /// 부채꼴을 경비병 발밑·수평으로 놓는다.
        ///
        /// <para>경비병이 고개를 숙이거나 기울어도 감지면은 수평이므로(§GuardDetection) 이 표시도
        /// <b>수평 yaw만</b> 따라간다. 부모 회전을 그대로 물려받으면 표시가 같이 기울어 거짓말이 된다.</para>
        /// </summary>
        void PlaceFan()
        {
            Vector3 f = guard.FacingFlat;
            transform.position = new Vector3(guard.transform.position.x,
                                             guard.FloorY + floorLift,
                                             guard.transform.position.z);
            transform.rotation = Quaternion.LookRotation(f, Vector3.up);
            transform.localScale = Vector3.one;
        }

        /// <summary>터렛 — 총구에서 사거리까지 뻗는 납작한 띠.</summary>
        void BuildBeam()
        {
            Transform m = turret.muzzle != null ? turret.muzzle : turret.transform;
            float len = turret.range;
            float w = Mathf.Max(0.01f, beamWidth) * 0.5f;

            if (Dirty(len, w, 0f, 2, false))
            {
                _mesh.Clear();
                // 십자로 겹친 두 장 — 어느 방향에서 봐도 띠가 사라지지 않는다(납작한 판 한 장이면
                // 정면에서 볼 때 선 하나로 수축해 격자가 안 보인다).
                _mesh.vertices = new[]
                {
                    new Vector3(-w, 0f, 0f), new Vector3(w, 0f, 0f),
                    new Vector3(w, 0f, len), new Vector3(-w, 0f, len),
                    new Vector3(0f, -w, 0f), new Vector3(0f, w, 0f),
                    new Vector3(0f, w, len), new Vector3(0f, -w, len),
                };
                _mesh.triangles = new[]
                {
                    0, 2, 1, 0, 3, 2,
                    4, 6, 5, 4, 7, 6,
                };
                _mesh.RecalculateBounds();
            }

            transform.SetPositionAndRotation(m.position, m.rotation);
            transform.localScale = Vector3.one;
        }

        /// <summary>모양 값이 바뀌었는지. 매 프레임 메시를 다시 만들면 GC가 쌓인다.</summary>
        bool Dirty(float a, float b, float c, int seg, bool fan)
        {
            bool changed = _mesh.vertexCount == 0
                        || fan != _lastFan || seg != _lastSeg
                        || !Mathf.Approximately(a, _lastA)
                        || !Mathf.Approximately(b, _lastB)
                        || !Mathf.Approximately(c, _lastC);
            _lastA = a; _lastB = b; _lastC = c; _lastSeg = seg; _lastFan = fan;
            return changed;
        }
    }
}
