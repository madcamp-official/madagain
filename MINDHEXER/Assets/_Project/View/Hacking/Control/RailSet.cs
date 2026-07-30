using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace Game.View
{
    /// <summary>
    /// 레일 세트 — 레일 여러 칸 + 그 위의 라이더가 <b>통째로</b> 미끄러지는 이송 기믹. (기초_설계안 §6.2)
    ///
    /// <para><b>구조</b>
    /// <code>
    /// [RailSet]            ← 이 컴포넌트 + Hackable(외부 조종). 조준·해킹 대상.  ★ 이 트랜스폼이 움직인다
    ///  ├ Rails/            ← 레일 칸들(두 줄 한 벌 = 1칸). 렌더러만, 물리 콜라이더 없음
    ///  └ Riders/           ← 터렛·벽·발판, 또는 또 다른 RailSet(중첩)
    /// </code></para>
    ///
    /// <para><b>레일이 고정 트랙이 아니다.</b> 레일 자체가 이동체의 일부라 세트 전체가 함께 밀린다.
    /// 그래서 레일은 보이는 구간보다 길게 만들어(§배치 규칙) 끝이 시야에 안 드러나게 한다.</para>
    ///
    /// <para><b>기준 = 앵커</b>(배치된 자리의 localPosition). 범위는 앵커에서 ±로 재므로 라이더를
    /// 어디에 놓든 흔들리지 않는다. 모든 계산이 <b>부모 공간</b>이라 중첩 시 바깥 세트가 움직여도
    /// 안쪽 좌표계는 그대로다(Unity 트랜스폼 계층이 합성을 해준다).</para>
    ///
    /// <para><b>플릭 = 정확히 레일 1칸.</b> 현재 위치를 격자에 반올림한 뒤 ±1칸으로 간다. 세트가
    /// 한 마디씩 밀리는 것으로 보여 "기계식 인덱싱"으로 읽힌다. 홀드는 연속 크립.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public class RailSet : MonoBehaviour, IExternalControl, IRunResettable
    {
        [Header("구성")]
        [Tooltip("레일 칸들의 부모. 길이 측정·범위 경고에 쓴다. (콜라이더 없이 렌더러만 둘 것)")]
        public Transform railRoot;

        [Tooltip("함께 실려 가는 것들의 부모. 터렛·벽·발판·중첩 레일 세트.")]
        public Transform riderRoot;

        [Tooltip("툴의 '레일 길이 측정' 버튼이 참고하는 오브젝트. <b>선택 사항</b> — 딸깍 간격은 아래 " +
                 "stepTarget이 소유하며, 이 필드는 그 값을 '모델에서 재서 채워 주는' 편의 기능일 뿐이다. " +
                 "비워 두고 stepTarget을 직접 넣어도 완전히 정상이다.")]
        public Transform referenceRail;

        [Header("트랙 (축은 이 트랜스폼의 로컬 기준)")]
        [Tooltip("트랙 진행 방향(로컬). 정규화는 자동.")]
        public Vector3 axis = Vector3.right;

        [FormerlySerializedAs("railLength")]   // 기존 씬/프리팹의 값을 그대로 물려받는다
        [FormerlySerializedAs("cellLength")]
        [Tooltip("플릭 1회 <b>목표</b> 이동량. 단위는 '부모 공간'(localPosition과 같은 단위).\n" +
                 "★ <b>레일 모델의 실제 길이와 무관하다.</b> 퍼즐에 맞는 값을 자유롭게 넣으면 된다 — " +
                 "모델이 2m짜리여도 여기에 3을 넣으면 3씩 움직인다(§6.2 '균등 간격이 아니라 퍼즐에 맞는 지점').\n" +
                 "눈금은 앵커에서 이 간격으로 찍히고, 범위 양 끝에 남는 자투리가 마지막 눈금이 된다.\n" +
                 "툴의 '레일 길이 측정'은 모델에서 재서 이 값을 채워 주는 편의 버튼일 뿐, 강제가 아니다.")]
        public float stepTarget = 2f;

        [Header("이동 범위 (앵커=배치 위치 기준, 부모 공간 단위)")]
        [Tooltip("앵커에서 음(−) 방향 한계.")]
        public float rangeMin = -4f;

        [Tooltip("앵커에서 양(+) 방향 한계.")]
        public float rangeMax = 4f;

        [Tooltip("범위 끝에 남는 자투리가 <b>목표 간격 × 이 비율</b>보다 짧으면, 직전 눈금을 버리고 " +
                 "끝 눈금에 합친다.\n" +
                 "예) 목표 5, 범위 −5.1 → 합치지 않으면 눈금이 0·−5·−5.1이라 마지막 플릭이 0.1만 " +
                 "움직여 '눌렀는데 안 움직인' 것처럼 보인다. 합치면 0·−5.1(한 칸 5.1)이 된다.\n" +
                 "0이면 합치지 않는다(자투리가 항상 별도 눈금).")]
        [Range(0f, 1f)] public float tailMergeRatio = 0.5f;

        [Header("조종 감각")]
        [Tooltip("홀드 시 크립 속도(단위/초).")]
        public float moveSpeed = 2f;

        [Tooltip("크립 가감속 램프(초). 0이면 즉시.")]
        public float accelTime = 0.08f;

        [Tooltip("플릭 1회에 걸리는 시간(초). 거리는 항상 1칸이라 속도가 아니라 시간으로 잡는다.")]
        public float flickTime = 0.28f;

        [Tooltip("플릭 끝의 오버슈트 세기 — '철컥' 안착감. 0이면 없음.")]
        [Range(0f, 3f)] public float overshoot = 1.6f;

        [Tooltip("홀드를 놓았을 때 가장 가까운 칸으로 붙일지. 기본 끔(크립은 미세 조정용).")]
        public bool snapAnalog = false;

        [Header("마일스톤 (홀드 전용 — 6DoF 떨림·튐 차단, §6.2)")]
        [Tooltip("홀드 이동에서 컨트롤러 노이즈만 걸러낸다. 스텝은 moveSpeed에서 자동 유도되므로 " +
                 "손으로 넣지 말 것 — 최고 속도에서는 완전히 부드럽고, 떨림만 걸러진다.")]
        public MilestoneStepper creepStep = new MilestoneStepper();

        /// <summary>앵커 기준 현재 이동량(부모 공간 단위).</summary>
        public float Offset { get; private set; }

        /// <summary>이번 프레임 월드 이동량. 발판 탑승(<see cref="RailPlatform"/>)이 쓴다.</summary>
        public Vector3 WorldDelta { get; private set; }

        /// <summary>플릭·크립이 모두 멈춘 상태.</summary>
        public bool AtRest => !_flicking && Mathf.Approximately(_vel, 0f);

        /// <summary>현재 위치가 앵커에서 몇 번째 눈금인지(앵커=0).</summary>
        public int CurrentCell { get { EnsureGrid(); return NearestIndex(Offset) - _anchorIndex; } }

        /// <summary>
        /// 플릭이 멈추는 지점들(앵커 기준 오름차순, <c>rangeMin … 0 … rangeMax</c>).
        ///
        /// <para><b>간격은 목표값 그대로다.</b> 범위를 균등 분할하지 않는다 — 앵커에서
        /// <see cref="stepTarget"/>씩 눈금을 찍고, 양 끝에 남는 자투리를 마지막 눈금으로 둔다.
        /// 그래서 (ㄱ) 간격이 목표에서 벗어나지 않고, (ㄴ) 앵커가 항상 눈금 위에 있으며,
        /// (ㄷ) 범위가 비대칭이어도 좌우 간격이 같다. 자투리만 짧다.</para>
        ///
        /// <para>너무 짧은 자투리는 <see cref="tailMergeRatio"/>로 직전 눈금에 합친다.</para>
        /// </summary>
        public IReadOnlyList<float> Grid { get { EnsureGrid(); return _grid; } }

        /// <summary>정지 상태에서 칸이 바뀌었을 때. 퍼즐 조건(게이트 개방 등) 배선용.</summary>
        public event Action<int> OnCellArrived;

        Vector3 _anchorLocal;
        Vector3 _axisParent = Vector3.right;
        float _analog, _vel;
        bool _flicking;
        float _flickFrom, _flickTo, _flickT;
        RailPlatform[] _platforms = Array.Empty<RailPlatform>();
        int _lastCell;

        float[] _grid = Array.Empty<float>();
        int _anchorIndex;
        float _gMin, _gMax, _gStep, _gMerge;   // 마지막으로 격자를 만든 입력값 — 바뀔 때만 다시 만든다

        public Vector3 AxisLocal => axis.sqrMagnitude > 1e-6f ? axis.normalized : Vector3.right;

        /// <summary>축을 부모 공간으로 옮긴 방향. 런타임엔 회전이 없으므로 상수다.</summary>
        public Vector3 AxisParent => (transform.localRotation * AxisLocal).normalized;

        /// <summary>범위의 기준점. 편집 중엔 지금 놓인 자리가 곧 앵커다.</summary>
        public Vector3 AnchorLocal => Application.isPlaying ? _anchorLocal : transform.localPosition;

        /// <summary>부모 공간 1단위가 월드 몇 미터인지(툴의 길이 환산용).</summary>
        public float ParentScaleAlongAxis
        {
            get
            {
                if (transform.parent == null) return 1f;
                return Mathf.Max(1e-4f, transform.parent.TransformVector(AxisParent).magnitude);
            }
        }

        // ── IExternalControl ──────────────────────────────────────────────
        public int AxisCount => 1;

        public Vector3 AxisWorld(int slot) => transform.TransformDirection(AxisLocal).normalized;

        /// <summary>레일은 양 끝이 대칭이라 "보이는 대로" 움직여야 한다 → 화면 기준 부호 보정을 쓴다.</summary>
        public bool ScreenRelativeSign => true;

        /// <summary>앵커=0, 양 끝=±1. 범위가 비대칭이어도 각 방향을 따로 정규화한다.</summary>
        public float GetNormalized(int slot)
        {
            if (slot != 0) return 0f;
            if (Offset >= 0f) return rangeMax > 1e-4f ? Mathf.Clamp01(Offset / rangeMax) : 0f;
            return rangeMin < -1e-4f ? -Mathf.Clamp01(Offset / rangeMin) : 0f;
        }

        public void Drive(int slot, float analog, int flick)
        {
            if (slot != 0) return;

            if (flick != 0) { StartFlick(NextCellTarget(flick)); return; }

            // 플릭 중에는 아날로그를 무시한다. 더블클릭 = 클릭 2회라 그 직후에도 버튼이 눌려 있어서,
            // 여기서 끊으면 플릭이 시작하자마자 1프레임 만에 죽는다(실제로 겪은 버그).
            if (_flicking) return;

            _analog = Mathf.Clamp(analog, -1f, 1f);
        }
        // ──────────────────────────────────────────────────────────────────

        void Awake()
        {
            _anchorLocal = transform.localPosition;
            _axisParent = AxisParent;
            _lastCell = 0;
            creepStep.SyncTo(moveSpeed);   // 스텝 = 최고속도 × 1프레임 (§MilestoneStepper)
            CachePlatforms();
        }

#if UNITY_EDITOR
        /// <summary>
        /// <see cref="riderRoot"/> 밑에 넣은 것에 <see cref="RailPlatform"/>을 <b>자동으로 붙인다</b>(편집 중에만).
        ///
        /// <para><b>왜 필요한가</b>: 발판을 <c>Riders/</c>로 끌어다 넣으면 트랜스폼 계층 덕에 <b>오브젝트는</b>
        /// 같이 움직인다. 그런데 <b>그 위에 선 플레이어</b>는 CharacterController라 계층으로 안 따라온다 —
        /// <see cref="RailPlatform"/>이 있어야 <see cref="RailPlatform.Carry"/>로 밀어 준다.
        /// 이걸 손으로 붙이는 걸 잊으면 <b>발판만 미끄러져 나가고 사람은 허공에 남는다</b>. 증상이
        /// "레일이 고장났다"로 보여 원인을 찾기 어렵다 — 그래서 잊을 수 없게 자동화한다.</para>
        ///
        /// <para>직계 자식에만 붙인다. 발판 안쪽 부품마다 붙으면 같은 사람을 여러 번 밀어 두 배로 간다.</para>
        /// </summary>
        void OnValidate()
        {
            creepStep.SyncTo(moveSpeed);   // 속도를 바꾸면 스텝이 따라온다
            if (riderRoot == null || Application.isPlaying) return;

            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this == null || riderRoot == null || Application.isPlaying) return;
                foreach (Transform child in riderRoot)
                {
                    if (child == null) continue;
                    // 중첩 세트는 그쪽이 알아서 나른다 — 여기서 또 붙이면 이중 이송이 된다.
                    if (child.GetComponent<RailSet>() != null) continue;

                    if (child.GetComponent<RailPlatform>() == null)
                    {
                        UnityEditor.Undo.AddComponent<RailPlatform>(child.gameObject);
                        Debug.Log($"[레일세트] '{child.name}'에 RailPlatform 자동 부착 — " +
                                  $"이게 있어야 발판 위의 플레이어가 같이 실려 간다.", child);
                    }

                    ClearBatchingStatic(child);
                }
                CachePlatforms();
            };
        }
        /// <summary>
        /// 발판과 그 자식들의 <b>Batching Static</b>을 끈다.
        ///
        /// <para><b>왜 필요한가</b> — 정적 배칭은 Play 진입 시 메시를 <b>월드 좌표로 구워</b> 한 버퍼에
        /// 합친다. 그 뒤로는 트랜스폼을 아무리 움직여도 구워진 정점이 그대로 쓰인다. 결과가 아주 헷갈린다:
        /// <b>콜라이더는 트랜스폼을 읽으므로 움직이는데, 렌더러는 제자리에 남는다.</b>
        /// 즉 <b>밟고 올라가지는데 발판은 안 따라오는</b> 것처럼 보인다(실제로 겪었다).</para>
        ///
        /// <para>레벨 지오메트리에서 발판을 가져오면 그쪽에서 켜 둔 static이 그대로 딸려 온다. 부모만
        /// 꺼져 있고 <b>자식 렌더러에 켜져 있는</b> 경우가 많아 인스펙터로는 눈에 안 띈다 —
        /// 그래서 자식까지 훑는다.</para>
        ///
        /// <para>성능 손해는 없다. 움직이는 물체는 애초에 정적 배칭 대상이 아니다.</para>
        /// </summary>
        static void ClearBatchingStatic(Transform root)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                var flags = UnityEditor.GameObjectUtility.GetStaticEditorFlags(t.gameObject);
                if ((flags & UnityEditor.StaticEditorFlags.BatchingStatic) == 0) continue;

                UnityEditor.Undo.RecordObject(t.gameObject, "Batching Static 해제");
                UnityEditor.GameObjectUtility.SetStaticEditorFlags(
                    t.gameObject, flags & ~UnityEditor.StaticEditorFlags.BatchingStatic);
                Debug.Log($"[레일세트] '{t.name}'의 Batching Static 해제 — " +
                          $"켜져 있으면 움직여도 화면에서 제자리에 남는다.", t);
            }
        }
#endif

        /// <summary>이 세트가 직접 나르는 발판들. 중첩 세트 소유분은 그쪽이 나르므로 제외한다(이중 이송 방지).</summary>
        public void CachePlatforms()
        {
            if (riderRoot == null) { _platforms = Array.Empty<RailPlatform>(); return; }

            var list = new List<RailPlatform>();
            foreach (var p in riderRoot.GetComponentsInChildren<RailPlatform>(true))
                if (p != null && p.Owner == this) list.Add(p);
            _platforms = list.ToArray();
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) { _analog = 0f; return; }

            Vector3 before = transform.position;

            if (_flicking) StepFlick(dt);
            else StepCreep(dt);

            transform.localPosition = _anchorLocal + _axisParent * Offset;
            WorldDelta = transform.position - before;

            // 발판 위의 것들을 같이 옮긴다. 계층 부모 지정 대신 델타를 더해 CharacterController와 안 싸운다.
            if (WorldDelta.sqrMagnitude > 1e-10f)
                for (int i = 0; i < _platforms.Length; i++)
                    if (_platforms[i] != null) _platforms[i].Carry(WorldDelta);

            int cell = CurrentCell;
            if (AtRest && cell != _lastCell)
            {
                _lastCell = cell;
                OnCellArrived?.Invoke(cell);
            }

            _analog = 0f;   // 매 프레임 소비. HackDriver는 입력이 있을 때만 Drive를 부른다.
        }

        void StepFlick(float dt)
        {
            _flickT += dt;
            float x = flickTime > 1e-4f ? Mathf.Clamp01(_flickT / flickTime) : 1f;
            Offset = Mathf.LerpUnclamped(_flickFrom, _flickTo, Mech(x));
            if (x >= 1f) { Offset = _flickTo; _flicking = false; }
        }

        void StepCreep(float dt)
        {
            float target = _analog * moveSpeed;
            float rate = accelTime > 1e-4f ? moveSpeed / accelTime : float.MaxValue;
            _vel = Mathf.MoveTowards(_vel, target, rate * dt);

            // 요구량을 마일스톤으로 양자화한다 — 0 아니면 정확히 ±스텝(§MilestoneStepper).
            // ⚠️ _vel이 0이어도 Advance는 불러야 한다: 쿨다운을 흘려보내고 잔량을 소진해야
            //    손을 뗀 뒤 밀린 딸깍이 다음 조종 첫 프레임에 튀지 않는다.
            float move = creepStep.Advance(_vel * dt, dt);

            if (!Mathf.Approximately(move, 0f))
            {
                float next = Mathf.Clamp(Offset + move, rangeMin, rangeMax);
                if (Mathf.Approximately(next, Offset)) { _vel = 0f; creepStep.Reset(); }   // 범위 끝
                else Offset = next;
            }

            if (snapAnalog && Mathf.Approximately(_analog, 0f) && Mathf.Approximately(_vel, 0f))
            {
                float n = NearestCell(Offset);
                if (Mathf.Abs(n - Offset) > 1e-3f) StartFlick(n);
            }
        }

        void StartFlick(float target)
        {
            if (Mathf.Approximately(target, Offset)) return;
            _flickFrom = Offset;
            _flickTo = target;
            _flickT = 0f;
            _flicking = true;
            _vel = 0f;
        }

        /// <summary>
        /// 현재 눈금에서 dir방향 한 칸. 범위 끝을 넘으면 끝에 머문다(무시하지 않는다).
        ///
        /// <para>예전엔 <c>round(Offset/간격)</c>으로 칸 번호를 계산했는데, 끝 자투리 자리에서는
        /// 반올림이 안쪽 칸으로 떨어져 <b>돌아올 때 한 칸을 건너뛰었다</b>(범위 −7·간격 5에서
        /// −7 → 0으로 점프, −5를 지나침). 지금은 격자 배열의 인덱스를 옮기므로 그런 비대칭이 없다.</para>
        /// </summary>
        float NextCellTarget(int dir)
        {
            EnsureGrid();
            if (_grid.Length == 0) return 0f;
            int i = Mathf.Clamp(NearestIndex(Offset) + Mathf.Clamp(dir, -1, 1), 0, _grid.Length - 1);
            return _grid[i];
        }

        /// <summary>현재 위치에서 가장 가까운 눈금의 앵커 기준 좌표.</summary>
        public float NearestCell(float offset)
        {
            EnsureGrid();
            return _grid.Length == 0 ? 0f : _grid[NearestIndex(offset)];
        }

        int NearestIndex(float offset)
        {
            int best = 0;
            float bd = float.MaxValue;
            for (int i = 0; i < _grid.Length; i++)
            {
                float d = Mathf.Abs(_grid[i] - offset);
                if (d >= bd) continue;
                bd = d; best = i;
            }
            return best;
        }

        /// <summary>입력값이 바뀌었을 때만 격자를 다시 만든다. 에디터에서도 그대로 돈다.</summary>
        void EnsureGrid()
        {
            if (_grid.Length > 0 && _gMin == rangeMin && _gMax == rangeMax
                && _gStep == stepTarget && _gMerge == tailMergeRatio) return;

            _gMin = rangeMin; _gMax = rangeMax; _gStep = stepTarget; _gMerge = tailMergeRatio;

            var list = new List<float> { 0f };
            AddSide(list, rangeMin, -1);
            AddSide(list, rangeMax, +1);
            list.Sort();
            _grid = list.ToArray();
            _anchorIndex = NearestIndex(0f);
        }

        /// <summary>앵커에서 한쪽 끝까지 눈금을 채운다. 간격은 목표 그대로, 끝 자투리만 짧다.</summary>
        void AddSide(List<float> into, float limit, int sign)
        {
            float span = limit * sign;                    // 항상 0 이상
            if (span <= 1e-4f) return;

            float step = Mathf.Max(1e-4f, stepTarget);
            int n = Mathf.FloorToInt(span / step + 1e-4f);
            float tail = span - n * step;

            if (tail <= 1e-4f)                            // 딱 떨어짐 — 끝이 곧 마지막 눈금
            {
                for (int i = 1; i <= n; i++) into.Add(sign * i * step);
                return;
            }

            // 자투리가 너무 짧으면 직전 눈금을 버려 마지막 한 칸에 합친다.
            if (n > 0 && tail < step * tailMergeRatio) n--;

            for (int i = 1; i <= n; i++) into.Add(sign * i * step);
            into.Add(limit);
        }

        /// <summary>기계식 이동 곡선 — 부드럽게 가속·감속하다 목표를 살짝 지나쳐 철컥 안착.</summary>
        float Mech(float x)
        {
            x = Mathf.Clamp01(x);
            float s = x * x * x * (x * (x * 6f - 15f) + 10f);          // smootherstep
            float k = Mathf.Clamp01((x - 0.6f) / 0.4f);                // 끝 40% 구간
            return s + overshoot * 0.08f * Mathf.Sin(k * Mathf.PI);    // x=1에서 0으로 돌아옴
        }

        // ── IRunResettable ────────────────────────────────────────────────
        /// <summary>아레나 리셋 — 연출 없이 앵커로 즉시 복귀.</summary>
        public void ResetForRestart()
        {
            _flicking = false;
            _vel = 0f;
            _analog = 0f;
            creepStep.Reset();   // 잔량이 남으면 재시작 첫 프레임에 튄다
            Offset = 0f;
            _lastCell = 0;
            WorldDelta = Vector3.zero;
            transform.localPosition = _anchorLocal;
        }
    }
}
