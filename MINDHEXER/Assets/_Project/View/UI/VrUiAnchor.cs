using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// UI 요소 하나의 <b>배치 선언</b>. 방위·고도·각크기 세 값만 만지면 된다.
    ///
    /// <para><b>쓰는 법</b> — <see cref="VrUiSpace"/> 루트 아래에 오브젝트를 넣고 이걸 붙인 뒤
    /// 각도를 돌린다. <b>코드 수정은 없다.</b> 위치·회전·스케일은 전부 루트가 계산해 덮어쓰므로
    /// 트랜스폼을 직접 만지지 말 것 — 다음 프레임에 지워진다.</para>
    ///
    /// <para><b>각크기가 곧 크기다.</b> 거리(<see cref="VrUiSpace.distance"/>)를 바꿔도 시야에서
    /// 차지하는 비율이 유지된다. 픽셀 크기를 신경 쓸 필요가 없다는 뜻이다.
    /// <see cref="RectTransform"/>이면 <c>rect.height</c>를, 아니면 <see cref="referenceSize"/>를
    /// 기준 치수로 삼는다.</para>
    ///
    /// <para><b>성능</b> — 값이 바뀌지 않으면 계산을 건너뛴다. 매 프레임 삼각함수를 돌리지 않는다.</para>
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class VrUiAnchor : MonoBehaviour
    {
        [Header("배치 (도)")]
        [Tooltip("좌우 각도. 0 = 정면, + = 오른쪽.")]
        [Range(-80f, 80f)] public float azimuth;

        [Tooltip("상하 각도. 0 = 눈높이, + = 위.")]
        [Range(-60f, 60f)] public float elevation;

        [Tooltip("시야에서 차지할 세로 각크기. 2m에서 텍스트가 읽히려면 최소 5cm ≈ 1.5° 가 필요하다.")]
        [Range(1f, 60f)] public float angularSize = 20f;

        [Header("옵션")]
        [Tooltip("안전영역을 넘으면 안쪽으로 당긴다. 끄면 선언한 각도 그대로 둔다(의도적으로 밖에 둘 때).")]
        public bool clampToSafeArea = true;

        [Tooltip("RectTransform이 아닐 때 쓸 기준 치수(로컬 단위). 1×1 쿼드면 1.")]
        public float referenceSize = 1f;

        // 마지막으로 적용한 입력들 — 하나라도 달라졌을 때만 다시 계산한다.
        float _lastAz, _lastEl, _lastSize, _lastDist, _lastHalfX, _lastHalfY, _lastRef;
        bool _applied;

        void OnValidate()
        {
            _applied = false;   // 인스펙터로 바꿨으면 다음 틱에 반드시 다시 잡는다
        }

        /// <summary>루트가 호출한다. <paramref name="force"/>면 캐시를 무시하고 다시 계산.</summary>
        public void Apply(VrUiSpace space, bool force)
        {
            if (space == null) return;

            var rect = transform as RectTransform;
            float refSize = rect != null ? rect.rect.height : referenceSize;

            if (!force && _applied
                && Mathf.Approximately(_lastAz,    azimuth)
                && Mathf.Approximately(_lastEl,    elevation)
                && Mathf.Approximately(_lastSize,  angularSize)
                && Mathf.Approximately(_lastDist,  space.distance)
                && Mathf.Approximately(_lastHalfX, space.safeHalfAngleX)
                && Mathf.Approximately(_lastHalfY, space.safeHalfAngleY)
                && Mathf.Approximately(_lastRef,   refSize))
                return;

            float half = angularSize * 0.5f;
            float az = azimuth;
            float el = elevation;

            if (clampToSafeArea)
            {
                // 요소의 절반이 안전영역보다 크면 여유가 음수가 된다 — 그때는 정면으로 붙인다.
                float limitX = Mathf.Max(0f, space.safeHalfAngleX - half);
                float limitY = Mathf.Max(0f, space.safeHalfAngleY - half);
                az = Mathf.Clamp(az, -limitX, limitX);
                el = Mathf.Clamp(el, -limitY, limitY);
            }

            // 회전에 롤이 없어야 패널이 기울지 않는다 — Euler(-el, az, 0)이 그 조건을 만족한다.
            Quaternion rot = Quaternion.Euler(-el, az, 0f);
            transform.localRotation = rot;
            transform.localPosition = rot * Vector3.forward * space.distance;

            // 각크기 → 그 거리에서의 실제 세로 길이 → 기준 치수로 나눈 스케일.
            float worldSize = 2f * space.distance * Mathf.Tan(half * Mathf.Deg2Rad);
            float s = refSize > 1e-4f ? worldSize / refSize : 1f;
            transform.localScale = new Vector3(s, s, s);

            _lastAz = azimuth; _lastEl = elevation; _lastSize = angularSize;
            _lastDist = space.distance; _lastHalfX = space.safeHalfAngleX; _lastHalfY = space.safeHalfAngleY;
            _lastRef = refSize;
            _applied = true;
        }
    }
}
