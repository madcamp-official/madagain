using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 보스 머리 찌그러짐 — 프레스에 찍힐 때마다 단계가 올라간다. (보스전_설계 §1·§3)
    ///
    /// <para><b>최종 자세만 잡으면 단계는 자동으로 나뉜다.</b> 스테이지가 4개→6개로 늘어도
    /// <see cref="stageCount"/>만 바꾸면 되고 자세는 다시 안 잡는다.</para>
    ///
    /// <code>
    /// 첫 조우 + 스테이지마다 1회 = stageCount 회
    /// stage 0..stageCount-1  →  Lerp(home, crush, curve(stage/stageCount))   ← 점점 찌그러짐
    /// 마지막(stageCount)     →  crush 도달  →  deathHold 초  →  flat  →  사망
    /// </code>
    ///
    /// <para>보간이 <see cref="crushTime"/>(기본 0.15초)로 짧은 이유: 유압프레스가 머리를 가리는
    /// 동안 바뀌므로 튀어도 안 보인다(§11.2 "프레스가 가려주니 모프 불필요"와 같은 트릭).</para>
    ///
    /// <para>대상 파츠는 <see cref="headRoot"/> 아래의 <b>MeshRenderer를 가진 자식들</b>이다.
    /// 스킨드 메시는 자기 트랜스폼을 무시하므로 <b>먼저 강체로 변환</b>돼 있어야 한다
    /// (Tools ▸ 보스 ▸ 머리 판때기 강체로 변환).</para>
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class BossHeadCrush : MonoBehaviour
    {
        [Header("대상")]
        [Tooltip("머리 본(Head). 비우면 이름으로 찾는다. 이 아래의 MeshRenderer 자식들이 판때기다.")]
        public Transform headRoot;

        [Tooltip("자세 3개가 담긴 애셋. 캡처 툴로 채운다.")]
        public BossHeadCrushPose pose;

        [Header("단계")]
        [Tooltip("총 찍는 횟수 = 첫 조우 1 + 스테이지 수. 스테이지가 늘면 이 값만 바꾼다.")]
        [Min(1)] public int stageCount = 5;

        [Tooltip("현재 단계(0=멀쩡). stageCount에 도달하면 최종 찌그러짐이다.")]
        public int stage;

        [Tooltip("단계 진행 곡선. 오른쪽으로 갈수록 급격하게 하면 뒤 단계에서 확 찌그러진다.")]
        public AnimationCurve curve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        [Header("연출")
        ]
        [Tooltip("한 단계 올라갈 때 보간 시간(초). 프레스가 가리는 동안이라 짧아도 된다.")]
        public float crushTime = 0.15f;

        [Tooltip("최종 찌그러짐에 도달한 뒤, 완전히 납작해지기까지의 텀(초).")]
        public float deathHold = 0.6f;

        [Tooltip("완전히 납작해지는 데 걸리는 시간(초).")]
        public float flatTime = 0.35f;

        [Header("미리보기 (편집 모드)")]
        [Tooltip("켜면 Play 없이 아래 슬라이더로 단계를 확인한다. 끄면 원래 자세로 돌아온다.")]
        public bool preview;

        [Tooltip("0 = 멀쩡, stageCount = 최종 찌그러짐, stageCount+1 = 완전 납작.")]
        public float previewStage;

        /// <summary>완전히 납작해진 뒤(사망 연출 끝). 사망 처리가 이걸 구독한다.</summary>
        public event System.Action OnFlattened;

        /// <summary>지금 최종 찌그러짐까지 갔는가 — 마지막 프레스를 받을 준비가 됐다는 뜻.</summary>
        public bool AtFinalCrush => stage >= stageCount;

        readonly List<Transform> _parts = new List<Transform>();
        float _t;            // 0=home … 1=crush
        float _flat;         // 0=crush … 1=flat
        bool _prevPreview;
        Coroutine _anim;

        void OnEnable()
        {
            Collect();
            Apply(StageToT(stage), 0f);
        }

        void OnDisable()
        {
            // 편집 모드에서 미리보기 자세가 씬·프리팹에 굳지 않게 원래대로 돌려놓는다.
            Apply(0f, 0f);
        }

        void Collect()
        {
            if (headRoot == null)
                foreach (var t in GetComponentsInChildren<Transform>(true))
                    if (t.name == "Head") { headRoot = t; break; }

            _parts.Clear();
            if (headRoot == null) return;
            foreach (var mr in headRoot.GetComponentsInChildren<MeshRenderer>(true))
                _parts.Add(mr.transform);
        }

        float StageToT(int s)
        {
            if (stageCount <= 0) return 0f;
            return curve.Evaluate(Mathf.Clamp01((float)s / stageCount));
        }

        // ── 외부에서 부르는 것 ────────────────────────────────────────────

        /// <summary>프레스에 한 번 찍혔다. 단계를 올리고 보간한다.</summary>
        public void Crush()
        {
            if (_parts.Count == 0) Collect();
            stage = Mathf.Min(stage + 1, stageCount);
            StartAnim(CoCrush(StageToT(stage)));
        }

        /// <summary>단계를 직접 지정(로드·리셋용). 연출 없이 즉시.</summary>
        public void SetStage(int s)
        {
            if (_parts.Count == 0) Collect();
            stage = Mathf.Clamp(s, 0, stageCount);
            _flat = 0f;
            Apply(StageToT(stage), 0f);
        }

        /// <summary>마지막 프레스 — 최종 찌그러짐 → 텀 → 완전 납작 → <see cref="OnFlattened"/>.</summary>
        public void Kill()
        {
            if (_parts.Count == 0) Collect();
            stage = stageCount;
            StartAnim(CoKill());
        }

        void StartAnim(IEnumerator co)
        {
            if (!Application.isPlaying) return;   // 편집 모드에선 코루틴이 안 돈다
            if (_anim != null) StopCoroutine(_anim);
            _anim = StartCoroutine(co);
        }

        IEnumerator CoCrush(float to)
        {
            float from = _t, e = 0f;
            while (e < crushTime)
            {
                e += Time.deltaTime;
                Apply(Mathf.Lerp(from, to, Mathf.Clamp01(e / crushTime)), 0f);
                yield return null;
            }
            Apply(to, 0f);
        }

        IEnumerator CoKill()
        {
            yield return CoCrush(StageToT(stageCount));
            yield return new WaitForSeconds(deathHold);

            float e = 0f;
            while (e < flatTime)
            {
                e += Time.deltaTime;
                Apply(1f, Mathf.Clamp01(e / flatTime));
                yield return null;
            }
            Apply(1f, 1f);
            OnFlattened?.Invoke();
        }

        // ── 적용 ─────────────────────────────────────────────────────────

        void LateUpdate()
        {
            if (Application.isPlaying) return;

            if (!preview)
            {
                if (_prevPreview) { Apply(0f, 0f); _prevPreview = false; }
                return;   // 미리보기가 꺼져 있으면 아무것도 건드리지 않는다 — 손으로 자세를 잡을 수 있게
            }
            _prevPreview = true;

            // previewStage: 0~stageCount = 찌그러짐, stageCount~+1 = 납작
            float s = Mathf.Max(0f, previewStage);
            float t = curve.Evaluate(Mathf.Clamp01(s / Mathf.Max(1, stageCount)));
            float f = Mathf.Clamp01(s - stageCount);
            Apply(t, f);
        }

        /// <summary><paramref name="t"/>: home→crush, <paramref name="flatT"/>: crush→flat.</summary>
        void Apply(float t, float flatT)
        {
            _t = t; _flat = flatT;
            if (pose == null || _parts.Count == 0) return;

            for (int i = 0; i < _parts.Count; i++)
            {
                Transform p = _parts[i];
                if (p == null) continue;

                var h = BossHeadCrushPose.Find(pose.home, p.name);
                var c = BossHeadCrushPose.Find(pose.crush, p.name);
                if (h == null || c == null) continue;   // 캡처 안 된 파츠는 건드리지 않는다

                Vector3 pos = Vector3.LerpUnclamped(h.pos, c.pos, t);
                Quaternion rot = Quaternion.SlerpUnclamped(h.rot, c.rot, t);
                Vector3 scl = Vector3.LerpUnclamped(h.scale, c.scale, t);

                if (flatT > 0f)
                {
                    var f = BossHeadCrushPose.Find(pose.flat, p.name);
                    if (f != null)
                    {
                        pos = Vector3.LerpUnclamped(pos, f.pos, flatT);
                        rot = Quaternion.SlerpUnclamped(rot, f.rot, flatT);
                        scl = Vector3.LerpUnclamped(scl, f.scale, flatT);
                    }
                }

                p.localPosition = pos;
                p.localRotation = rot;
                p.localScale = scl;
            }
        }
    }
}
