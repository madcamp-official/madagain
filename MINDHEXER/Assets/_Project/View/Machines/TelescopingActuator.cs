using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 텔레스코핑 신축 연출 — Anchor(고정) / Shaft(연결, 늘어남) / Head(가동, 이동) 3파츠 구조.
    ///
    /// <para><b>왜 스케일인가</b>: 진짜 텔레스코핑(단이 서로 안으로 들어가는) 메시가 없다
    /// (AI 생성 모델이라 그런 서브메시가 안 나옴). 그래서 Shaft를 <b>축 방향으로 스케일</b>해서
    /// 늘어나는 것처럼 보이게 한다. 대가는 끝단 디테일(캡·볼트)이 같이 늘어나 뭉개지는 것 —
    /// 지금은 감수하고, 나중에 필요하면 캡만 별도 자식으로 분리해 스케일에서 뺄 수 있다.</para>
    ///
    /// <para><b>왜 자식 하나하나를 따로 스케일하는가</b>: Shaft가 통짜 메시가 아니라
    /// <b>여러 개의 개별 막대(rod) 파츠</b>로 구성돼 있다. 처음엔 Shaft 그룹 전체에 스케일 하나를
    /// 걸었는데, 그러면 <b>딱 한 지점(전체 바운즈를 아우르는 대표 좌표)만 고정되고 나머지 막대들은
    /// 그 기준점에서 떨어진 거리에 비례해 제각각 밀려나 부채꼴로 벌어진다</b>(실기 확인됨).
    /// 그래서 <see cref="Awake"/>에서 Shaft의 <b>직계 자식 각각</b>의 바운즈를 따로 재고,
    /// <see cref="Apply"/>에서도 자식마다 자기 자신의 anchor 쪽 끝을 기준으로 개별 스케일·위치
    /// 보정을 적용한다 — 모든 막대가 각자 Anchor 쪽에서 뻗어나가므로 서로 평행이 유지된다.</para>
    ///
    /// <para><b>피벗 보정</b>: 각 파츠의 피벗은 프리팹 원점 계열이라 그냥 스케일하면 원점 기준으로
    /// 양쪽으로 늘어난다. Anchor 쪽 끝이 고정돼 보이려면 스케일과 함께 위치도 같이 보정해야 한다.
    /// 그 보정 기준점(파츠별 anchor 쪽 끝의 로컬 좌표)은 Awake에서 <b>렌더러 바운즈를 실측</b>해
    /// 자동으로 구한다 — 좌표를 손으로 박아넣지 않으므로 모델이 나중에 바뀌어도 다시 잰다.
    /// 파츠 원래 스케일이 1이 아닌 경우까지 대비해 <c>S(t) = S_home · s(t)</c> 형태로 일반화했다.</para>
    ///
    /// <para><b>Shaft 늘어나는 배율은 독립 변수가 아니다.</b> Anchor 쪽 끝이 고정, Head가
    /// <see cref="strokeLength"/>만큼 이동하는 동안 Shaft의 Head 쪽 끝이 Head 근접면에 계속
    /// 맞닿아 있어야 한다 — 아니면 t에 따라 틈이 벌어지거나 겹친다. 그래서 배율은
    /// <c>s(t) = 1 + strokeLength·t / L0</c> (L0 = Awake에서 실측한 각 파츠 원래 길이)로
    /// <b>strokeLength에서 유도</b>한다. t=0에서 이미 맞닿아 있다고 가정한다(현재 배치 상태).</para>
    ///
    /// <para>구동은 <see cref="PdApproach"/>(스프링-댐퍼) — 기초_설계안 §6.2 "구동 = PD 제어".
    /// 유압프레스는 damping=1(느리고 묵직), 피스톤은 damping&lt;1(철컥) 프리셋을 인스펙터에서 고른다.</para>
    /// </summary>
    public class TelescopingActuator : MonoBehaviour
    {
        [Header("파츠")]
        [Tooltip("고정부. 위치 참조로만 쓴다(움직이지 않음).")]
        public Transform anchor;

        [Tooltip("연결부. 이 오브젝트의 '직계 자식' 각각이 독립적으로 축 방향으로 스케일된다 " +
                 "(Shaft 자체가 여러 막대 파츠로 구성돼 있다는 전제 — §클래스 주석 참조).")]
        public Transform shaft;

        [Tooltip("가동부. 축 방향으로 이동한다(그룹 전체가 강체 이동이라 스케일과 달리 파츠별 보정이 필요 없다).")]
        public Transform head;

        [Header("축")]
        [Tooltip("이동/신축 축(Shaft의 로컬 공간 기준, 카디널 축이어야 함: (±1,0,0) 등). " +
                 "피스톤=로컬 X, 프레스=로컬 -Y(아래로 뻗음) 같은 식.\n" +
                 "Head가 반대로 움직이면 부호를 뒤집을 것(예: (1,0,0)→(-1,0,0)).")]
        public Vector3 axis = Vector3.right;

        [Tooltip("각 파츠의 고정 끝(Anchor 쪽)을 자동판정 대신 반대쪽으로 강제한다. " +
                 "Anchor 위치 기준 자동판정이 애매한 모델에서, '고정점과 늘어나는 방향이 반대'로 보이면 켤 것.\n" +
                 "플레이 모드 중지 상태에서 바꾸고 다시 재생해야 반영된다(측정은 Awake에서 1회).")]
        public bool invertAnchorEnd = false;

        [Header("스트로크")]
        [Tooltip("완전 신장(t=1) 시 Head가 홈 위치에서 이동하는 거리(m, 월드 기준). 유일한 손튜닝 값 — " +
                 "Shaft 파츠들이 늘어나는 배율은 이 값과 각 파츠 실측 길이에서 자동으로 계산된다(맞물림 보장).")]
        public float strokeLength = 1f;

        [Header("구동")]
        [Tooltip("켜면 스프링(PD) 대신 등속 선형으로 움직인다 — 지금 기본. 나중에 감속/철컥 느낌을 " +
                 "다시 넣고 싶으면 끄고 아래 drive(PD) 값을 쓰면 된다(로직은 그대로 남겨뒀다).")]
        public bool useLinearMotion = true;

        [Tooltip("선형일 때 0→1 도달 시간(초). 반대 방향도 대칭으로 같은 시간.")]
        public float linearDuration = 1f;

        public PdApproach drive = new PdApproach();
        float _linearT;

        [Header("미리보기 (플레이 모드 테스트용 — 실제 조작 연결 전)")]
        [Tooltip("켜면 Space를 누르는 동안 t=1로, 떼면 t=0으로 향한다. 실제 입력 배선이 붙으면 꺼도 된다.")]
        public bool debugPreview = true;

        /// <summary>0=완전 수축, 1=완전 신장. 외부(조종 스킴)가 이 값을 밀어넣는다.</summary>
        public float Target { get => drive.Target; set => drive.Target = Mathf.Clamp01(value); }

        /// <summary>지금 신장 비율(0~1). 도착 여부 판단 등에 쓴다.</summary>
        public float Current => useLinearMotion ? _linearT : drive.Value;

        /// <summary>Shaft 직계 자식 파츠 하나의 홈 상태 + 스케일 보정에 필요한 실측값.</summary>
        struct ShaftPart
        {
            public Transform t;
            public Vector3 homePos;
            public float homeScaleAxis;   // 이 파츠의 원래 축 성분 스케일(보통 1)
            public float anchorEndLocal;  // 이 파츠 자신의 로컬 공간에서 Anchor 쪽 끝 좌표
            public float length0;         // 이 파츠 자신의 원래 길이(로컬)
        }

        Vector3 _axisN;
        Vector3 _headHomePos;
        float _worldPerLocal;    // 이 축 방향으로 로컬 1단위 = 월드 몇 미터인지(부모 스케일 반영)
        ShaftPart[] _parts;

        void Awake()
        {
            if (shaft == null || head == null) { enabled = false; return; }

            _axisN = axis.sqrMagnitude > 1e-6f ? axis.normalized : Vector3.right;
            _headHomePos = head.localPosition;

            int n = shaft.childCount;
            _parts = new ShaftPart[n];
            for (int i = 0; i < n; i++)
            {
                Transform c = shaft.GetChild(i);
                MeasurePart(c, out float anchorEnd, out float length0);
                float homeScaleAxis = Vector3.Dot(Abs(_axisN), c.localScale);
                if (Mathf.Abs(homeScaleAxis) < 1e-6f) homeScaleAxis = 1f;
                _parts[i] = new ShaftPart
                {
                    t = c, homePos = c.localPosition,
                    homeScaleAxis = homeScaleAxis, anchorEndLocal = anchorEnd, length0 = length0,
                };
            }

            // strokeLength는 인스펙터에 "월드 미터"로 설명돼 있는데 head.localPosition은 로컬 좌표라
            // 부모(프리팹 루트) 스케일만큼 단위가 다르다. 여기서 한 번 재서 Apply()가 항상 월드
            // 미터 기준으로 동작하게 한다 — 안 그러면 루트 스케일 11배 프리팹에서 실제로 11배
            // 과하게 늘어난다(이 프로젝트가 겪은 실제 버그). Shaft 자신은 이제 스케일이 안 바뀌므로
            // 이 배율은 재생 내내 상수다.
            Transform parent = shaft;   // Head·Shaft파츠 모두 Shaft와 같은 부모 체인 스케일을 공유
            _worldPerLocal = parent != null ? parent.TransformVector(_axisN).magnitude : 1f;
            if (_worldPerLocal < 1e-6f) _worldPerLocal = 1f;

            // Head 위에 서 있는 플레이어를 같이 실어 나르는 건 레일 세트(RailSet/RailPlatform)와
            // 완전히 같은 문제라, 새로 만들지 않고 그 컴포넌트를 그대로 재사용한다 — Head가 이번
            // 프레임 이동한 델타만큼 Carry()를 불러주기만 하면 된다(있으면).
            _headPlatform = head.GetComponent<RailPlatform>();
            _lastHeadWorldPos = head.position;

            drive.SnapTo(0f);
        }

        RailPlatform _headPlatform;
        Vector3 _lastHeadWorldPos;

        /// <summary>
        /// 파츠 하나의 렌더러 바운즈를 축 방향으로 재서, 그 파츠 자신의 Anchor 쪽 끝 로컬 좌표와
        /// 길이를 낸다. 파츠별로 독립 실행 — 전체 Shaft를 하나로 뭉뚱그리지 않는다.
        /// </summary>
        void MeasurePart(Transform part, out float anchorEnd, out float length)
        {
            var rends = part.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) { anchorEnd = 0f; length = 1f; return; }

            float min = float.PositiveInfinity, max = float.NegativeInfinity;
            foreach (var r in rends)
            {
                Bounds b = r.bounds;   // 월드 바운즈
                Vector3[] corners = BoundsCorners(b);
                foreach (var c in corners)
                {
                    Vector3 local = part.InverseTransformPoint(c);   // 이 파츠의 진짜 메시-로컬 좌표
                    float a = Vector3.Dot(local, _axisN);
                    if (a < min) min = a;
                    if (a > max) max = a;
                }
            }

            length = Mathf.Max(0.0001f, max - min);

            bool pickMin;
            if (anchor == null)
            {
                pickMin = true;   // 기준 없으면 관례상 축의 음(-) 쪽
            }
            else
            {
                Vector3 worldMin = part.TransformPoint(_axisN * min);
                Vector3 worldMax = part.TransformPoint(_axisN * max);
                float dMin = (worldMin - anchor.position).sqrMagnitude;
                float dMax = (worldMax - anchor.position).sqrMagnitude;
                pickMin = dMin <= dMax;
            }

            if (invertAnchorEnd) pickMin = !pickMin;   // 자동판정이 틀린 모델용 수동 뒤집기
            anchorEnd = pickMin ? min : max;
        }

        static Vector3[] BoundsCorners(Bounds b)
        {
            Vector3 c = b.center, e = b.extents;
            return new[]
            {
                c + new Vector3(-e.x, -e.y, -e.z), c + new Vector3(e.x, -e.y, -e.z),
                c + new Vector3(-e.x, e.y, -e.z),  c + new Vector3(e.x, e.y, -e.z),
                c + new Vector3(-e.x, -e.y, e.z),  c + new Vector3(e.x, -e.y, e.z),
                c + new Vector3(-e.x, e.y, e.z),   c + new Vector3(e.x, e.y, e.z),
            };
        }

        void Update()
        {
            if (debugPreview)
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb != null) Target = kb.spaceKey.isPressed ? 1f : 0f;
            }

            if (useLinearMotion)
            {
                float rate = 1f / Mathf.Max(0.001f, linearDuration);
                _linearT = Mathf.MoveTowards(_linearT, drive.Target, rate * Time.deltaTime);
                Apply(_linearT);
            }
            else
            {
                drive.Step(Time.deltaTime);
                Apply(drive.Value);
            }
        }

        void Apply(float t)
        {
            float localStroke = strokeLength / _worldPerLocal;   // 월드 미터 → 로컬 단위 환산
            head.localPosition = _headHomePos + _axisN * (localStroke * t);

            // Head가 이번 프레임 움직인 만큼, 그 위에 서 있는 CharacterController를 같이 실어 나른다.
            if (_headPlatform != null)
            {
                Vector3 newWorldPos = head.position;
                Vector3 delta = newWorldPos - _lastHeadWorldPos;
                if (delta.sqrMagnitude > 1e-10f) _headPlatform.Carry(delta);
                _lastHeadWorldPos = newWorldPos;
            }

            for (int i = 0; i < _parts.Length; i++)
            {
                ShaftPart p = _parts[i];
                if (p.t == null) continue;

                // s(t) = 1 + strokeLength·t / L0 — 이 파츠의 Anchor 반대쪽 끝이 그만큼 더 뻗어나가게.
                // 손튜닝 값이 아니라 strokeLength·파츠 실측 길이에서 유도(§클래스 주석).
                float s = 1f + (localStroke * t) / p.length0;
                float finalScaleAxis = p.homeScaleAxis * s;

                Vector3 scale = p.t.localScale;
                SetAxisComponent(ref scale, finalScaleAxis);
                p.t.localScale = scale;

                // 피벗 보정: 앵커 쪽 끝의 월드 위치가 스케일과 무관하게 고정되도록 위치를 같이 옮긴다.
                // 파츠 자신의 원래 스케일(homeScaleAxis)까지 반영한 일반형:
                //   P(t) = P_home + axis · anchorEndLocal · homeScaleAxis · (1 - s)
                p.t.localPosition = p.homePos + _axisN * (p.anchorEndLocal * p.homeScaleAxis * (1f - s));
            }
        }

        void SetAxisComponent(ref Vector3 v, float value)
        {
            if (Mathf.Abs(_axisN.x) > 0.5f) v.x = value;
            else if (Mathf.Abs(_axisN.y) > 0.5f) v.y = value;
            else if (Mathf.Abs(_axisN.z) > 0.5f) v.z = value;
        }

        static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            if (shaft == null) return;
            Vector3 a = axis.sqrMagnitude > 1e-6f ? axis.normalized : Vector3.right;
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(shaft.position, shaft.position + shaft.TransformDirection(a) * strokeLength);
            if (head != null) { Gizmos.color = Color.yellow; Gizmos.DrawWireSphere(head.position, 0.05f); }
            if (anchor != null) { Gizmos.color = Color.green; Gizmos.DrawWireSphere(anchor.position, 0.05f); }
        }
#endif
    }
}
