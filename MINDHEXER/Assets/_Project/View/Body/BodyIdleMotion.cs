using System;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 3인칭 전신 절차 idle — 뼈 단위 additive.
    /// 설계: `docs/KJH/design/플레이어_신체표현_설계.md` §5.3
    ///
    /// <para><b>왜 ViewmodelMotion 방식을 못 쓰나</b>: 저쪽은 루트 하나를 통째로 흔든다.
    /// 1인칭 팔은 카메라에 붙어 있으니 그게 맞지만, 전신에 같은 짓을 하면 <b>발이 미끄러진다.</b>
    /// 그래서 <see cref="FingerPoser"/>와 같은 방식으로 간다 — 기준 자세(rest)를 캡처해두고
    /// 매 LateUpdate에 그 위에 각도를 <b>더한다</b>.</para>
    ///
    /// <para><b>발·다리는 건드리지 않는다.</b> 접지가 유지돼야 한다.</para>
    ///
    /// <para><b>기준 자세는 포즈 JSON이 잡는다</b>(§5.2). 셸의 자세는 §2.6에 따라
    /// "하늘색 생명줄을 손으로 꽉 쥔 자세"이며, 그건 <c>PoseJsonTool</c>로 저장한 포즈를 적용해 만든다.
    /// 이 컴포넌트는 그 위에 생명감만 얹는다. 그래서 <see cref="CaptureRest"/>를
    /// <b>포즈를 적용한 뒤에</b> 불러야 한다.</para>
    ///
    /// <para><b>오른팔 진폭이 따로</b> 있는 이유: 오른손은 생명줄을 쥐고 있어야 하므로
    /// 왼팔처럼 흔들리면 안 된다.</para>
    ///
    /// <para>실행 순서 −30 — ViewmodelMotion(−50)·MantleRig(−40) 뒤, HandIK(0) 앞.
    /// <see cref="PlayerBodyModeController"/>가 3인칭에서만 켠다.</para>
    /// </summary>
    [DefaultExecutionOrder(-30)]
    public class BodyIdleMotion : MonoBehaviour
    {
        [Serializable]
        public class BoneRef
        {
            [Tooltip("찾을 뼈 이름")]
            public string name;
            [NonSerialized] public Transform t;
            [NonSerialized] public Quaternion rest;
        }

        [Header("뼈 이름 (리그가 바뀌면 여기만 고친다)")]
        public string[] breathBones = { "Spine02", "NeckTwist01", "NeckTwist02" };
        public string[] swayBones   = { "Waist", "Pelvis" };
        public string   headBone    = "Head";
        public string   armBoneR    = "R_Upperarm";
        public string   armBoneL    = "L_Upperarm";
        public string   clavicleR   = "R_Clavicle";
        public string   clavicleL   = "L_Clavicle";

        [Header("호흡")]
        public bool  enableBreathe = true;
        [Tooltip("가슴·목이 부풀었다 꺼지는 각도(도).")]
        public float breatheAmp = 1.4f;
        [Tooltip("주기(Hz). 0.25 = 4초에 한 번.")]
        public float breatheRate = 0.25f;
        [Tooltip("목이 따라 움직이는 비율. 1이면 가슴과 같은 크기.")]
        [Range(0f, 1f)] public float breatheNeck = 0.45f;

        [Header("체중 이동")]
        public bool  enableSway = true;
        [Tooltip("골반이 좌우로 기우는 각도(도).")]
        public float swayAmp = 1.1f;
        [Tooltip("주기(Hz). 호흡보다 훨씬 느려야 자연스럽다.")]
        public float swayRate = 0.11f;

        [Header("팔 늘어짐")]
        public bool  enableArm = true;
        [Tooltip("팔이 미세하게 흔들리는 각도(도).")]
        public float armAmp = 0.9f;
        [Tooltip("오른팔 배율. 오른손은 생명줄을 쥐고 있어야 해서 기본값이 작다.")]
        [Range(0f, 1f)] public float armRightScale = 0.25f;
        [Tooltip("호흡과의 위상차(도). 0이면 같이 움직여 뻣뻣해 보인다.")]
        public float armPhaseOffset = 70f;

        [Header("머리")]
        public bool  enableHeadDrift = true;
        [Tooltip("고개가 아주 천천히 흔들리는 각도(도).")]
        public float headAmp = 0.8f;
        public float headRate = 0.07f;

        [Header("공통")]
        [Tooltip("시작 위상(초). 여러 개가 동시에 서 있을 때 똑같이 숨쉬지 않게 어긋내는 값.")]
        public float phaseOffset;

        [Tooltip("전체 세기. 0이면 완전히 정지.")]
        [Range(0f, 2f)] public float masterScale = 1f;

        readonly System.Collections.Generic.List<BoneRef> _breath = new System.Collections.Generic.List<BoneRef>();
        readonly System.Collections.Generic.List<BoneRef> _sway   = new System.Collections.Generic.List<BoneRef>();
        BoneRef _head, _armR, _armL, _clavR, _clavL;
        bool _ready;
        float _time;

        void OnEnable()
        {
            // 켜질 때마다 기준을 다시 잡는다 — 3인칭 진입 시 포즈가 먼저 적용됐을 수 있다.
            Rebuild();
        }

        /// <summary>뼈를 찾고 현재 자세를 기준으로 캡처한다.</summary>
        [ContextMenu("본 다시 찾기 + 기준 자세 캡처")]
        public void Rebuild()
        {
            _breath.Clear(); _sway.Clear();

            foreach (var n in breathBones) AddTo(_breath, n);
            foreach (var n in swayBones)   AddTo(_sway,   n);

            _head  = Make(headBone);
            _armR  = Make(armBoneR);
            _armL  = Make(armBoneL);
            _clavR = Make(clavicleR);
            _clavL = Make(clavicleL);

            _ready = _breath.Count > 0 || _sway.Count > 0 || _head != null || _armR != null || _armL != null;
            if (!_ready)
                Debug.LogWarning($"[BodyIdleMotion] 뼈를 하나도 못 찾았습니다. 이름 배열을 확인하십시오. " +
                                 $"(대상: {name}, 하위 Transform {GetComponentsInChildren<Transform>(true).Length}개)");
        }

        /// <summary>기준 자세만 다시 잡는다(뼈 탐색은 유지). 포즈를 새로 적용한 직후에 부른다.</summary>
        public void CaptureRest()
        {
            foreach (var b in _breath) if (b.t != null) b.rest = b.t.localRotation;
            foreach (var b in _sway)   if (b.t != null) b.rest = b.t.localRotation;
            Cap(_head); Cap(_armR); Cap(_armL); Cap(_clavR); Cap(_clavL);
        }

        static void Cap(BoneRef b) { if (b != null && b.t != null) b.rest = b.t.localRotation; }

        void AddTo(System.Collections.Generic.List<BoneRef> list, string boneName)
        {
            var b = Make(boneName);
            if (b != null) list.Add(b);
        }

        BoneRef Make(string boneName)
        {
            if (string.IsNullOrEmpty(boneName)) return null;
            var t = Find(boneName);
            if (t == null) return null;
            return new BoneRef { name = boneName, t = t, rest = t.localRotation };
        }

        Transform Find(string boneName)
        {
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (t.name == boneName) return t;
            return null;
        }

        void LateUpdate()
        {
            if (!_ready || masterScale <= 0f) return;

            _time += Time.deltaTime;
            float p = (_time + phaseOffset) * Mathf.PI * 2f;
            float m = masterScale;

            // ── 호흡: 가슴이 부풀고 목이 살짝 따라 든다 ──
            if (enableBreathe)
            {
                float s = Mathf.Sin(p * breatheRate);
                for (int i = 0; i < _breath.Count; i++)
                {
                    var b = _breath[i];
                    if (b.t == null) continue;
                    // 0번(가슴)은 전량, 나머지(목)는 감쇠 — 목이 가슴만큼 움직이면 고개가 까딱거린다.
                    float amp = breatheAmp * (i == 0 ? 1f : breatheNeck) * m;
                    b.t.localRotation = b.rest * Quaternion.Euler(-s * amp, 0f, 0f);
                }
            }

            // ── 체중 이동: 골반이 아주 느리게 좌우로 ──
            if (enableSway)
            {
                float s = Mathf.Sin(p * swayRate);
                foreach (var b in _sway)
                {
                    if (b.t == null) continue;
                    b.t.localRotation = b.rest * Quaternion.Euler(0f, 0f, s * swayAmp * m);
                }
            }

            // ── 팔: 호흡과 위상을 어긋내 뻣뻣함을 없앤다 ──
            if (enableArm)
            {
                float ph = armPhaseOffset * Mathf.Deg2Rad;
                float sR = Mathf.Sin(p * breatheRate + ph);
                float sL = Mathf.Sin(p * breatheRate + ph * 1.6f);   // 좌우도 어긋내야 대칭으로 안 보인다

                Apply(_armR,  Quaternion.Euler(sR * armAmp * armRightScale * m, 0f, 0f));
                Apply(_armL,  Quaternion.Euler(sL * armAmp * m, 0f, 0f));
                Apply(_clavR, Quaternion.Euler(sR * armAmp * 0.3f * armRightScale * m, 0f, 0f));
                Apply(_clavL, Quaternion.Euler(sL * armAmp * 0.3f * m, 0f, 0f));
            }

            // ── 머리: 아주 느린 표류 ──
            if (enableHeadDrift && _head != null && _head.t != null)
            {
                float y = Mathf.Sin(p * headRate) * headAmp * m;
                float x = Mathf.Sin(p * headRate * 0.7f + 1.3f) * headAmp * 0.5f * m;
                _head.t.localRotation = _head.rest * Quaternion.Euler(x, y, 0f);
            }
        }

        static void Apply(BoneRef b, Quaternion add)
        {
            if (b == null || b.t == null) return;
            b.t.localRotation = b.rest * add;
        }
    }
}
