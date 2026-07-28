using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 레일 세트 — 레일 여러 칸이 이어진 트랙 + 그 위에 종속돼 움직이는 오브젝트들. (기초_설계안 §6.2)
    ///
    /// <para><b>구조</b>
    /// <code>
    /// [RailSet]            ← 이 컴포넌트 + Hackable(외부 조종). 조준·해킹 대상은 여기다.
    ///  ├ Rails/            ← 레일 칸들(두 줄짜리 한 벌 = 1칸). 움직이지 않는다.
    ///  └ Riders/           ← 통째로 움직이는 컨테이너. 터렛·벽·또 다른 RailSet을 넣는다.
    /// </code></para>
    ///
    /// <para><b>왜 컨테이너를 움직이나</b> — 라이더가 여럿이어도 하나만 옮기면 같이 가고,
    /// 라이더 안에 <b>레일 세트가 또 들어가도</b>(중첩) 자식 세트가 통째로 실려 간다.
    /// 모든 계산을 이 트랜스폼의 <b>로컬 공간</b>에서 하므로 부모가 움직여도 자식 좌표계는 안 흔들린다.</para>
    ///
    /// <para><b>기준은 트랙 중앙</b> — 이동량(<see cref="Offset"/>)은 라이더의 시작 위치가 아니라
    /// 레일 전체의 중앙에서 잰다. 그래서 라이더를 어디에 놓든 범위가 흔들리지 않는다.</para>
    ///
    /// <para><b>플릭 = 정확히 레일 1칸</b>(<see cref="railLength"/>). 현재 위치와 무관하게 다음 칸에
    /// 안착한다 — §6.2의 "스냅 격자"가 여기서 실체를 얻는다. 홀드는 연속 이동.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public class RailSet : MonoBehaviour, IExternalControl
    {
        [Header("구성")]
        [Tooltip("레일 칸들의 부모. 트랙 길이·중앙 계산에 쓴다(움직이지 않음).")]
        public Transform railRoot;

        [Tooltip("함께 움직이는 것들의 부모. 이 트랜스폼 하나만 옮긴다(라이더 여럿·중첩 세트 지원).")]
        public Transform riderRoot;

        [Tooltip("레일 1칸 길이를 재는 기준 오브젝트. 툴의 '레일 길이 측정'이 쓴다.")]
        public Transform referenceRail;

        [Header("트랙 (이 트랜스폼의 로컬 기준)")]
        [Tooltip("트랙 진행 방향(로컬). 정규화는 자동.")]
        public Vector3 axis = Vector3.right;

        [Tooltip("레일 1칸 길이 = 플릭 1회 이동량 = 스냅 단위(로컬 단위).")]
        public float railLength = 2f;

        [Tooltip("트랙 중앙(로컬 좌표). 툴의 '중앙 재계산'이 레일 바운즈에서 채운다.")]
        public Vector3 center;

        [Header("이동 범위 (중앙 기준, 로컬 단위)")]
        [Tooltip("중앙에서 음(−) 방향 한계. 툴의 '±N칸' 버튼으로도 채울 수 있다.")]
        public float rangeMin = -4f;

        [Tooltip("중앙에서 양(+) 방향 한계.")]
        public float rangeMax = 4f;

        [Header("조종")]
        [Tooltip("홀드 시 등속 크립 속도(단위/초).")]
        public float moveSpeed = 3f;

        [Tooltip("플릭 이동 속도(단위/초).")]
        public float flickSpeed = 30f;

        /// <summary>중앙 기준 현재 이동량(로컬 단위).</summary>
        public float Offset { get; private set; }

        Vector3 _riderAuthoredLocal;   // 씬에 배치된 그대로의 라이더 컨테이너 위치
        float _authoredOffset;         // 그 위치가 중앙에서 얼마나 떨어져 있었는지
        float _flickTarget;
        bool _flicking;

        public Vector3 AxisLocal => axis.sqrMagnitude > 1e-6f ? axis.normalized : Vector3.right;

        // ── IExternalControl ──────────────────────────────────────────────
        public int AxisCount => 1;
        public Vector3 AxisWorld(int slot) => transform.TransformDirection(AxisLocal).normalized;

        public void Drive(int slot, float analog, int flick)
        {
            if (slot != 0) return;

            if (flick != 0)
            {
                // 현재 위치와 무관하게 '다음 칸'으로(§6.2). 반올림으로 격자에 맞춘 뒤 한 칸 이동.
                float unit = Mathf.Max(0.0001f, railLength);
                int cell = Mathf.RoundToInt(Offset / unit);
                _flickTarget = Mathf.Clamp((cell + flick) * unit, rangeMin, rangeMax);
                _flicking = true;
                return;
            }

            // 플릭 중에는 아날로그를 무시한다(더블클릭 직후에도 버튼이 눌려 있어 즉시 취소되는 것 방지).
            if (_flicking) return;

            if (!Mathf.Approximately(analog, 0f))
                Offset = Mathf.Clamp(Offset + analog * moveSpeed * Time.deltaTime, rangeMin, rangeMax);
        }
        // ──────────────────────────────────────────────────────────────────

        void Awake()
        {
            if (riderRoot == null) return;
            _riderAuthoredLocal = riderRoot.localPosition;
            _authoredOffset = Vector3.Dot(_riderAuthoredLocal - center, AxisLocal);
            Offset = Mathf.Clamp(_authoredOffset, rangeMin, rangeMax);   // 배치된 자리에서 시작
        }

        void Update()
        {
            if (riderRoot == null) return;

            if (_flicking)
            {
                Offset = Mathf.MoveTowards(Offset, _flickTarget, flickSpeed * Time.deltaTime);
                if (Mathf.Approximately(Offset, _flickTarget)) _flicking = false;
            }

            // 배치 당시의 수직 오프셋 등은 보존하고, 축 방향 성분만 갈아끼운다.
            riderRoot.localPosition = _riderAuthoredLocal + AxisLocal * (Offset - _authoredOffset);
        }

        /// <summary>현재 위치에서 가장 가까운 칸의 중앙 기준 좌표.</summary>
        public float NearestCell(float offset)
        {
            float unit = Mathf.Max(0.0001f, railLength);
            return Mathf.Clamp(Mathf.RoundToInt(offset / unit) * unit, rangeMin, rangeMax);
        }
    }
}
