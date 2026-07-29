using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 컨트롤러 6DoF 위치 → 부품 조종 지령. VIO 원본은 그대로 쓸 수 없어 다단 보정을 거친다.
    ///
    /// <para><b>단계</b>
    /// <list type="number">
    /// <item><b>점프 게이트</b> — 추적 상실→복구 순간 위치가 크게 튄다. 그 한 패킷이 부품을 후려치므로 버린다.</item>
    /// <item><b>저역통과</b> — VIO의 mm 단위 떨림. 겸사겸사 30Hz 입력을 프레임에 고르게 편다.</item>
    /// <item><b>데드존 + 히스테리시스</b> — 손을 가만히 둬도 중립이 흔들린다. 경계에서 켜졌다 꺼졌다
    ///   채터링하지 않게 진입/이탈 임계를 따로 둔다.</item>
    /// <item><b>커브</b> — 중심 근처를 둔감하게 해 미세 조작을 가능하게 한다.</item>
    /// <item><b>변화율 제한</b> — 지령 자체가 튀지 않게. 부품의 자체 가감속 램프가 6번째 필터로 공짜로 붙는다.</item>
    /// </list></para>
    ///
    /// <para><b>순수 로직</b>(Unity 시간 호출 없음 — dt를 받는다) → 합성 입력으로 단위 검증 가능.</para>
    /// </summary>
    public sealed class HandCalibration
    {
        [Header("보정")]
        [Tooltip("한 패킷에 이만큼(m) 넘게 튀면 그 패킷을 버린다. 추적 상실→복구 점프 방어.")]
        public float jumpGateM = 0.15f;

        [Tooltip("위치 저역통과 시정수(초). 0이면 평활 없음.")]
        public float lowPassTime = 0.05f;

        [Tooltip("데드존 진입 임계(m). 원점에서 이 안이면 입력 0.")]
        public float deadZoneEnterM = 0.015f;

        [Tooltip("데드존 이탈 임계(m). 진입보다 작아야 경계에서 채터링하지 않는다.")]
        public float deadZoneExitM = 0.010f;

        [Tooltip("최대 입력까지의 손 이동량(m). 앉은 자세에서 손을 다시 안 잡고 움직이는 범위가 약 0.15m.")]
        public float travelScaleM = 0.12f;

        [Tooltip("입력 커브 지수. 1이면 선형, 크면 중심 근처가 둔해진다.")]
        public float curveExponent = 1.5f;

        [Tooltip("지령 변화율 상한(1/초). 손이 순간이동해도 지령은 서서히 간다.")]
        public float rateLimitPerSec = 6f;

        // ── 상태 ──────────────────────────────────────────────────────────
        Vector3 _origin;          // 리센터 원점(월드)
        Vector3 _basisRight;      // 수평 기준축 — 손 좌우
        Vector3 _basisUp;         // 상하
        bool _hasOrigin;

        Vector3 _filtered;        // 저역통과 후 위치
        bool _hasFiltered;

        bool _outH, _outV;        // 데드존 이탈 상태(히스테리시스)
        float _cmdH, _cmdV;       // 변화율 제한 후 지령

        /// <summary>리센터가 잡혔는가. 아니면 지령이 0이다.</summary>
        public bool Ready => _hasOrigin && _hasFiltered;

        /// <summary>원점 기준 보정된 변위(m). 진단용.</summary>
        public Vector2 DisplacementM { get; private set; }

        /// <summary>최종 지령(-1~1). 슬롯0=H, 슬롯1=V.</summary>
        public float CommandH => _cmdH;
        public float CommandV => _cmdV;

        /// <summary>
        /// 원점과 기준축을 잡는다. <b>조종 대상이 잡히는 순간</b>에 부른다.
        /// </summary>
        /// <param name="pos">현재 컨트롤러 위치(ARCore 세션 좌표).</param>
        /// <param name="controllerYawDeg">컨트롤러 자세의 yaw. 폰을 기울여 들어도 "좌우"가 대각선이 되지 않게 yaw만 쓴다.</param>
        /// <param name="headYawDeg">그 순간 카메라(머리) yaw. 손 좌우를 화면 좌우에 맞추는 데 쓴다.</param>
        public void Recenter(Vector3 pos, float controllerYawDeg, float headYawDeg)
        {
            _origin = pos;
            _hasOrigin = true;

            // 컨트롤러 기준 수평축을 만들고, 머리와의 yaw 차이만큼 돌려 화면 좌우에 맞춘다.
            // 두 기기 좌표계를 <b>이 한 순간만</b> 맞추면 되므로 지속적인 캘리브레이션이 필요 없다.
            Quaternion align = Quaternion.Euler(0f, headYawDeg - controllerYawDeg, 0f);
            _basisRight = align * (Quaternion.Euler(0f, controllerYawDeg, 0f) * Vector3.right);
            _basisRight.y = 0f;
            _basisRight = _basisRight.sqrMagnitude > 1e-6f ? _basisRight.normalized : Vector3.right;
            _basisUp = Vector3.up;

            _filtered = pos;
            _hasFiltered = true;
            _outH = _outV = false;
            _cmdH = _cmdV = 0f;
            DisplacementM = Vector2.zero;
        }

        /// <summary>리센터를 버린다(조종 해제).</summary>
        public void Clear()
        {
            _hasOrigin = false;
            _hasFiltered = false;
            _cmdH = _cmdV = 0f;
            DisplacementM = Vector2.zero;
        }

        /// <summary>원점만 현재 위치로 옮긴다(클러치·플릭 후 재영점). 기준축은 유지.</summary>
        public void ReanchorTo(Vector3 pos)
        {
            if (!_hasOrigin) return;
            _origin = pos;
            _filtered = pos;
            _outH = _outV = false;
            DisplacementM = Vector2.zero;
        }

        /// <summary>현재 저역통과된 위치. 재영점·플릭 판정이 쓴다.</summary>
        public Vector3 FilteredPosition => _filtered;

        /// <summary>매 프레임. <paramref name="valid"/>가 false면(추적 상실) 지령을 0으로 되돌린다.</summary>
        public void Step(Vector3 rawPos, bool valid, float dt)
        {
            if (!_hasOrigin || dt <= 0f) return;

            if (!valid)
            {
                // 추적이 끊기면 손 위치를 믿을 수 없다. 마지막 값을 유지하면 부품이 그 자리에
                // 붙박이므로, 지령을 0으로 되돌려 부품이 멈추게 한다.
                _cmdH = Mathf.MoveTowards(_cmdH, 0f, rateLimitPerSec * dt);
                _cmdV = Mathf.MoveTowards(_cmdV, 0f, rateLimitPerSec * dt);
                return;
            }

            // 1) 점프 게이트
            if (_hasFiltered && (rawPos - _filtered).sqrMagnitude > jumpGateM * jumpGateM)
                return;   // 이 패킷은 버린다 — _filtered를 안 옮기므로 다음 패킷에서 회복

            // 2) 저역통과
            float k = lowPassTime > 1e-4f ? 1f - Mathf.Exp(-dt / lowPassTime) : 1f;
            _filtered = _hasFiltered ? Vector3.Lerp(_filtered, rawPos, k) : rawPos;
            _hasFiltered = true;

            Vector3 d = _filtered - _origin;
            float dh = Vector3.Dot(d, _basisRight);
            float dv = Vector3.Dot(d, _basisUp);
            DisplacementM = new Vector2(dh, dv);

            _cmdH = ShapeAxis(dh, ref _outH, _cmdH, dt);
            _cmdV = ShapeAxis(dv, ref _outV, _cmdV, dt);
        }

        /// <summary>데드존(히스테리시스) → 정규화 → 커브 → 변화율 제한.</summary>
        float ShapeAxis(float dMeters, ref bool outside, float prevCmd, float dt)
        {
            float a = Mathf.Abs(dMeters);

            // 진입은 큰 임계, 이탈은 작은 임계 — 경계에서 켜졌다 꺼졌다 하지 않게.
            if (outside) { if (a < deadZoneExitM) outside = false; }
            else { if (a > deadZoneEnterM) outside = true; }

            float target = 0f;
            if (outside)
            {
                float dz = deadZoneExitM;
                float span = Mathf.Max(1e-4f, travelScaleM - dz);
                float unit = Mathf.Clamp01((a - dz) / span);
                target = Mathf.Sign(dMeters) * Mathf.Pow(unit, Mathf.Max(0.1f, curveExponent));
            }

            return Mathf.MoveTowards(prevCmd, target, Mathf.Max(0.01f, rateLimitPerSec) * dt);
        }
    }
}
