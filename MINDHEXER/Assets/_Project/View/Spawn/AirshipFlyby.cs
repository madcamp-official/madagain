using System.Collections.Generic;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Airship 스폰 플라이바이 연출 — 순수 View, Sim 무영향(스폰 자체는 WaveRunner가 그대로 담당).
    ///
    /// 실제 스폰하는 airship(realShip, 팬 부착)은 <b>위치 고정 + 평상시 투명</b>이고, 연출용
    /// <b>가짜 airship</b>(렌더러만 복제한 프롭)이 매 스폰 주기마다 날아와 바통터치한다:
    ///   ① WaitStart 진입(= 스폰 3초 전, 웨이브 startDelay) → 가짜가 -travelDirection 쪽 멀리서
    ///      나타나 <b>가속 → 도착 직전 급감속</b>으로 진짜 위치까지 비행.
    ///   ② Spawning 진입 → 가짜 숨김 + 진짜 드러남(바통터치). 진짜가 팬으로 몹을 뱉는다.
    ///   ③ 스폰 끝(최소 노출 시간 보장) → 진짜 숨김 + 가짜가 같은 자리에서 드러나
    ///      <b>+travelDirection으로 가속 퇴장</b>(진행 방향 유지 = 관통 연출) 후 소멸.
    ///
    /// 전환은 즉시 토글이다(같은 위치 바통터치라 어색함 최소) — 재질 페이드는 추후 개선.
    /// 상태 전이 감지는 웨이브 번호가 아니라 <b>WaitStart 진입</b> 기준 — 웨이브 1개짜리 loop
    /// 구성(Arena_4)에서는 CurrentWave가 늘 0이라 번호 비교로는 주기를 구분할 수 없다.
    /// </summary>
    [DisallowMultipleComponent]
    public class AirshipFlyby : MonoBehaviour
    {
        [Tooltip("감시할 WaveRunner. 비우면 같은 오브젝트에서 찾는다.")]
        public WaveRunner runner;

        [Tooltip("실제 스폰 airship(팬 부착, 위치 고정). 평상시 렌더러·콜라이더를 꺼서 투명하게 만든다.")]
        public Transform realShip;

        [Tooltip("비행 축(월드). 가짜는 -이 방향 × 진입거리에서 나타나 도착하고, +이 방향으로 퇴장한다.")]
        public Vector3 travelDirection = Vector3.right;

        [Tooltip("진입 시작 거리(m) — 진짜 위치 기준 뒤쪽.")]
        public float approachDistance = 120f;

        [Tooltip("퇴장 거리(m) — 진짜 위치 기준 앞쪽. 이만큼 가면 사라진다.")]
        public float departDistance = 120f;

        [Tooltip("퇴장 비행 시간(초).")]
        public float departSeconds = 3f;

        [Tooltip("진짜가 드러나 있는 최소 시간(초) — 스폰이 순간에 끝나도 이만큼은 보여준다.")]
        public float minRevealSeconds = 1.5f;

        [Tooltip("접근 진행 곡선(시간0~1 → 거리0~1). 기본: 가속 후 도착 직전 급감속.")]
        public AnimationCurve approachCurve = new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 0.2f),
            new Keyframe(0.7f, 0.88f, 2.0f, 2.0f),
            new Keyframe(1f, 1f, 0.12f, 0f));

        [Tooltip("퇴장 진행 곡선(시간0~1 → 거리0~1). 기본: 정지에서 가속.")]
        public AnimationCurve departCurve = new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 0.15f),
            new Keyframe(1f, 1f, 2.5f, 0f));

        enum Phase { Idle, Approach, Reveal, Depart }
        Phase phase = Phase.Idle;
        float phaseStart;
        float approachSeconds = 3f;   // 접근 시간 — 시작 시 현재 웨이브의 startDelay에서 읽어 동기화
        WaveRunner.State prevState = WaveRunner.State.Idle;

        Transform fake;
        readonly List<Renderer> realRenderers = new List<Renderer>();
        readonly List<Collider> realColliders = new List<Collider>();

        void Awake()
        {
            if (runner == null) runner = GetComponent<WaveRunner>();
            if (realShip == null)
            {
                Debug.LogWarning("[AirshipFlyby] realShip이 비어 있어 연출을 끕니다.");
                enabled = false;
                return;
            }

            // 처음 켜져 있던 것만 캐시 — 의도적으로 꺼둔 렌더러·콜라이더를 되살리지 않기 위해.
            foreach (var r in realShip.GetComponentsInChildren<Renderer>(true))
                if (r.enabled) realRenderers.Add(r);
            foreach (var c in realShip.GetComponentsInChildren<Collider>(true))
                if (c.enabled) realColliders.Add(c);

            BuildFake();
            SetRealVisible(false);                       // 평상시 투명
            if (fake != null) fake.gameObject.SetActive(false);
        }

        /// <summary>가짜 airship 생성 — 진짜를 복제하고 렌더 관련만 남긴다(판정·스폰 하드웨어 전부 제거).</summary>
        void BuildFake()
        {
            var go = Instantiate(realShip.gameObject, realShip.position, realShip.rotation);
            go.name = realShip.name + "_FakeFlyby";
            go.transform.SetParent(transform.parent, true);   // 콘텐츠 루트에 나란히(진짜와 같은 소속)

            // 판정·스폰 하드웨어만 제거하고 순수 렌더는 남긴다. ★ 전체 컴포넌트를 순서 없이 Destroy하면
            // URP가 Light에 붙인 UniversalAdditionalLightData의 RequireComponent 때문에
            // "Can't remove Light..." 오류가 난다 — Collider와 게임 로직(Game.*)만 지운다.
            foreach (var col in go.GetComponentsInChildren<Collider>(true))
                Destroy(col);
            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null) continue;
                var ns = mb.GetType().Namespace;
                if (ns != null && ns.StartsWith("Game")) Destroy(mb);   // FanSpawn·TraversalLink 등
            }
            // 라이트는 삭제하지 않고 끈다(삭제는 위 RequireComponent로 막힘 + 씬 조명 중복 방지).
            foreach (var lt in go.GetComponentsInChildren<Light>(true)) lt.enabled = false;
            // 복제 시점에 진짜가 이미 투명(렌더러 꺼짐)일 수 있으므로 전부 켠다.
            foreach (var r in go.GetComponentsInChildren<Renderer>(true)) r.enabled = true;
            fake = go.transform;
        }

        void SetRealVisible(bool on)
        {
            foreach (var r in realRenderers) if (r != null) r.enabled = on;
            // 콜라이더도 함께 — 투명한데 보스 빔·투사체가 허공에 막히는 것을 방지.
            foreach (var c in realColliders) if (c != null) c.enabled = on;
        }

        Vector3 Dir => travelDirection.sqrMagnitude > 1e-6f ? travelDirection.normalized : Vector3.right;

        void Update()
        {
            if (runner == null || realShip == null || fake == null) return;
            WaveRunner.State st = runner.CurrentState;

            // 러너가 꺼지면(Idle/Done) 연출도 정리.
            if (st == WaveRunner.State.Idle || st == WaveRunner.State.Done)
            {
                if (phase != Phase.Idle)
                {
                    fake.gameObject.SetActive(false);
                    SetRealVisible(false);
                    phase = Phase.Idle;
                }
                prevState = st;
                return;
            }

            switch (phase)
            {
                case Phase.Idle:
                    if (st == WaveRunner.State.WaitStart && prevState != WaveRunner.State.WaitStart)
                        BeginApproach();
                    else if (st == WaveRunner.State.Spawning && prevState != WaveRunner.State.Spawning)
                        BeginReveal();   // startDelay 0 등으로 접근을 놓친 경우 — 즉시 드러남
                    break;

                case Phase.Approach:
                {
                    float u = Mathf.Clamp01((Time.time - phaseStart) / Mathf.Max(0.01f, approachSeconds));
                    float s = approachCurve.Evaluate(u);
                    fake.position = realShip.position - Dir * (approachDistance * (1f - s));
                    if (st == WaveRunner.State.Spawning || u >= 1f) BeginReveal();
                    break;
                }

                case Phase.Reveal:
                    // 스폰이 끝났고 최소 노출 시간도 지났으면 퇴장 바통터치.
                    if (st != WaveRunner.State.Spawning && Time.time - phaseStart >= minRevealSeconds)
                        BeginDepart();
                    break;

                case Phase.Depart:
                {
                    float u = Mathf.Clamp01((Time.time - phaseStart) / Mathf.Max(0.01f, departSeconds));
                    float s = departCurve.Evaluate(u);
                    fake.position = realShip.position + Dir * (departDistance * s);
                    if (u >= 1f)
                    {
                        fake.gameObject.SetActive(false);
                        phase = Phase.Idle;
                    }
                    // 퇴장 중 다음 주기가 시작되면 즉시 접근으로 전환(30초 주기라 보통 안 겹침).
                    if (st == WaveRunner.State.WaitStart && prevState != WaveRunner.State.WaitStart)
                        BeginApproach();
                    break;
                }
            }
            prevState = st;
        }

        void BeginApproach()
        {
            approachSeconds = 3f;
            var cfg = runner.config;
            if (cfg != null && cfg.HasWave(runner.CurrentWave))
                approachSeconds = Mathf.Max(0.5f, cfg.waves[runner.CurrentWave].startDelay);

            SetRealVisible(false);
            fake.rotation = realShip.rotation;
            fake.position = realShip.position - Dir * approachDistance;
            fake.gameObject.SetActive(true);
            phase = Phase.Approach;
            phaseStart = Time.time;
        }

        void BeginReveal()
        {
            fake.gameObject.SetActive(false);
            SetRealVisible(true);
            phase = Phase.Reveal;
            phaseStart = Time.time;
        }

        void BeginDepart()
        {
            SetRealVisible(false);
            fake.position = realShip.position;
            fake.rotation = realShip.rotation;
            fake.gameObject.SetActive(true);
            phase = Phase.Depart;
            phaseStart = Time.time;
        }
    }
}
