using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 녹슨 로봇 관절 — Animator가 만든 자세를 <b>관절마다 다른 고착·스프링</b>으로 뒤따라가게 한다.
    ///
    /// ── 왜 스프링만으로는 안 되는가 ──
    /// 순수 스프링은 젤리처럼 흐물거린다. 녹슨 느낌은 "버티다가 툭 풀리는" 고착(stiction)에서 나온다.
    ///   ① 애니메이션은 움직이는데 관절은 붙어 있음
    ///   ② 벌어진 각도가 임계를 넘음
    ///   ③ 툭 풀리며 밀린 만큼 따라잡음   ← "딱"
    ///   ④ 지나쳐서 출렁 → 잦아들고 다시 고착
    ///
    /// ── 안전성 ──
    /// <b>bone.localRotation만 읽고 쓴다.</b> Animator.enabled·Animator.Update()·루트 트랜스폼을
    /// 일절 건드리지 않으므로, 루트 모션 누적(빙글빙글)·속도 이중곱 같은 사고가 구조적으로 불가능하다.
    /// 목표를 매 프레임 Animator에서 새로 읽으므로 오차도 누적되지 않는다.
    ///
    /// 실행 위치: EntityViews.LateSync (Animator가 본을 쓴 뒤). EnemyMotion과 같은 경로다.
    /// </summary>
    public struct RustyJoints
    {
        const int MaxJoints = 12;

        public bool bound;
        Transform[] joints;
        Vector3[]   vel;        // 관절별 각속도(도/초, 축 포함)
        Quaternion[] cur;       // 관절별 현재 회전(로컬)
        float[]     stiff, damp, stick;   // 관절별 성격
        float[]     phase;      // 고착 임계를 흔드는 위상(같은 지점에서 반복해 걸리지 않게)
        bool[]      stuck;
        float       gate;       // 0=정지(효과 없음) ~ 1=이동 중(효과 최대)

        /// <summary>모델 계층에서 팔·다리 관절을 찾아 캐시한다. 개체당 1회.</summary>
        public void Bind(Transform root, int seed, in RustyJointSettings s)
        {
            bound = true;
            joints = null;
            if (root == null) return;

            var found = new Transform[MaxJoints];
            var grade = new float[MaxJoints];   // 0=몸통(뻑뻑) ~ 1=말단(헐거움)
            int n = 0;

            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (n >= MaxJoints) break;
                string nm = t.name.ToLowerInvariant();
                float g;
                if      (Has(nm, "shoulder") || Has(nm, "clavicle")) g = 0.15f;
                else if (Has(nm, "upperarm") || Has(nm, "arm") && !Has(nm, "forearm") && !Has(nm, "lowerarm")) g = 0.35f;
                else if (Has(nm, "forearm")  || Has(nm, "lowerarm") || Has(nm, "elbow")) g = 0.6f;
                else if (Has(nm, "hand")     && !Has(nm, "handle")) g = 0.9f;
                else if (Has(nm, "thigh")    || Has(nm, "upleg")) g = 0.2f;
                else if (Has(nm, "calf")     || Has(nm, "leg") && !Has(nm, "upleg")) g = 0.5f;
                else if (Has(nm, "foot")     || Has(nm, "ankle")) g = 0.85f;
                else continue;
                // 손가락 등 말단 세부는 제외 — 개수만 잡아먹고 눈에 안 띈다
                if (Has(nm, "thumb") || Has(nm, "index") || Has(nm, "middle") ||
                    Has(nm, "ring")  || Has(nm, "pinky") || Has(nm, "toe")) continue;

                found[n] = t; grade[n] = g; n++;
            }

            if (n == 0) return;
            joints = new Transform[n];
            vel = new Vector3[n]; cur = new Quaternion[n];
            stiff = new float[n]; damp = new float[n]; stick = new float[n];
            phase = new float[n]; stuck = new bool[n];

            for (int i = 0; i < n; i++)
            {
                joints[i] = found[i];
                cur[i]    = found[i].localRotation;

                // ── 관절별 성격 ──
                // 말단(grade↑)일수록 헐겁다: 약한 강성·약한 감쇠·작은 고착 → 크게 출렁
                // 몸통(grade↓)일수록 뻑뻑하다: 강한 강성·강한 감쇠·큰 고착 → 무겁게 툭
                float g = grade[i];
                // 개체·관절마다 다른 편차(시드 고정 — 같은 몹은 항상 같은 개성)
                float r = Frac((seed * 0.6180339887f) + (i + 1) * 0.7548776662f) * 2f - 1f;
                float j = 1f + r * s.jitter;

                stiff[i] = Mathf.Lerp(s.stiffBody, s.stiffTip, g) * j;
                damp[i]  = Mathf.Lerp(s.dampBody,  s.dampTip,  g) * j;
                stick[i] = Mathf.Lerp(s.stickBody, s.stickTip, g) * j;
                phase[i] = Frac(r * 13.37f) * 6.283f;
                stuck[i] = true;
            }
        }

        static bool Has(string n, string k) => n.Contains(k);
        static float Frac(float v) => v - Mathf.Floor(v);

        public bool HasJoints => joints != null && joints.Length > 0;

        /// <summary>
        /// Animator가 써넣은 자세를 목표로 삼아 관절을 뒤따르게 한다.
        /// 반드시 Animator 평가 <b>이후</b>(LateUpdate 경로)에 불러야 한다.
        /// </summary>
        public void Apply(in RustyJointSettings s, float dt, float now, float speed)
        {
            if (joints == null || dt <= 0f) return;

            // ── 이동 게이트: 걷거나 뛸 때만 적용한다 ──
            // 서 있거나 공격 중이면 속도가 0에 가까워 자동으로 꺼진다(별도 상태 분기 불필요).
            // 뚝 끊기지 않게 부드럽게 여닫는다.
            float want = Mathf.InverseLerp(s.moveMin, Mathf.Max(s.moveMin + 0.01f, s.moveFull), speed);
            gate = Mathf.MoveTowards(gate, want, s.gateRate * dt);

            if (gate <= 0.001f)
            {
                // 완전히 꺼진 동안엔 현재 자세를 계속 따라가게 둔다 — 다시 켜질 때 튀지 않는다.
                for (int k = 0; k < joints.Length; k++)
                {
                    if (joints[k] == null) continue;
                    cur[k] = joints[k].localRotation;
                    vel[k] = Vector3.zero;
                    stuck[k] = true;
                }
                return;
            }

            for (int i = 0; i < joints.Length; i++)
            {
                Transform t = joints[i];
                if (t == null) continue;

                Quaternion target = t.localRotation;   // ★ Animator가 방금 만든 값 = 목표

                // 현재 → 목표의 최단 회전차를 각도·축으로 분해
                Quaternion diff = target * Quaternion.Inverse(cur[i]);
                diff.ToAngleAxis(out float ang, out Vector3 axis);
                if (float.IsNaN(axis.x) || axis.sqrMagnitude < 1e-8f) { cur[i] = target; continue; }
                if (ang > 180f) { ang -= 360f; }        // 최단 경로

                // ── 고착: 임계보다 작게 벌어졌고 거의 멈춰 있으면 아예 안 움직인다 ──
                // 임계를 느린 노이즈로 흔들어 같은 지점에서 반복해 걸리지 않게 한다.
                float wobble = 1f + Mathf.Sin(now * s.stickWobbleRate + phase[i]) * s.stickWobble;
                float th = stick[i] * wobble;
                float aAbs = Mathf.Abs(ang);

                if (stuck[i])
                {
                    if (aAbs < th) { Write(t, target, cur[i]); continue; }   // 아직 붙어 있음
                    stuck[i] = false;                                        // 툭 — 풀림
                }
                else if (aAbs < s.releaseEnd && vel[i].sqrMagnitude < s.releaseVel * s.releaseVel)
                {
                    stuck[i] = true;                                         // 잦아들었으니 다시 고착
                    vel[i] = Vector3.zero;
                    Write(t, target, cur[i]);
                    continue;
                }

                // ── 각도 스프링 (풀린 동안) ──
                Vector3 torque = axis.normalized * ang;                      // 도 단위
                vel[i] += (torque * stiff[i] - vel[i] * damp[i]) * dt;

                float spd = vel[i].magnitude;
                if (spd > 1e-4f)
                    cur[i] = Quaternion.AngleAxis(spd * dt, vel[i] / spd) * cur[i];

                Write(t, target, cur[i]);
            }
        }

        /// <summary>게이트 비율만큼 섞어 쓴다. gate=0이면 Animator 원본, 1이면 완전히 녹슨 자세.
        /// 걷기 시작·멈춤에서 뚝 끊기지 않고 서서히 뻑뻑해진다.</summary>
        void Write(Transform t, Quaternion target, Quaternion rusty)
            => t.localRotation = gate >= 0.999f ? rusty : Quaternion.Slerp(target, rusty, gate);

        /// <summary>기능을 끌 때 — 다음에 켤 때 튀지 않게 현재 자세를 기준으로 재동기화.</summary>
        public void Resync()
        {
            if (joints == null) return;
            for (int i = 0; i < joints.Length; i++)
            {
                if (joints[i] == null) continue;
                cur[i] = joints[i].localRotation;
                vel[i] = Vector3.zero;
                stuck[i] = true;
            }
        }
    }

    /// <summary>관절 스프링 전역 수치. 콘솔 <c>rust</c> 가 실시간으로 만진다.</summary>
    [System.Serializable]
    public struct RustyJointSettings
    {
        public float stiffBody, stiffTip;   // 강성 — 목표를 얼마나 세게 당기나
        public float dampBody,  dampTip;    // 감쇠 — 출렁임이 얼마나 빨리 잦아드나
        public float stickBody, stickTip;   // 고착 임계(도) — 이만큼 벌어져야 풀린다
        public float jitter;                // 개체·관절별 편차
        public float stickWobble;           // 임계 흔들림 비율
        public float stickWobbleRate;       // 흔들림 속도
        public float releaseEnd;            // 이 각도 미만 + 저속이면 다시 고착(도)
        public float releaseVel;            // 다시 고착으로 보는 각속도(도/초)

        // ── 이동 게이트: 걷거나 뛸 때만 적용 ──
        public float moveMin;               // 이 속도 이하면 효과 0 (서 있음·공격 중)
        public float moveFull;              // 이 속도 이상이면 효과 최대
        public float gateRate;              // 게이트 여닫는 속도(초당) — 뚝 끊기지 않게

        public static RustyJointSettings Default => new RustyJointSettings
        {
            // 몸통은 뻑뻑하고 크게 걸림 / 말단은 헐겁고 잘 출렁임
            //
            // ★ 수치 근거 — 눈에 보이게 하려면 "고착 임계"가 관건이다.
            //   걷기는 관절이 30~60° 움직이므로 임계가 몇 도면 즉시 풀려 정상처럼 보인다.
            //   임계를 크게 잡아야 "버티는 게 보이다가 툭" 이 성립한다.
            //   강성은 낮춰야 따라잡는 과정이 동작으로 보이고(높으면 순간이동),
            //   감쇠는 임계감쇠(2√k)의 0.3~0.4배로 잡아 확실히 출렁이게 한다.
            stiffBody = 420f, stiffTip = 200f,   // 2√420≈41 · 2√200≈28 (임계감쇠 기준)
            dampBody  = 16f,  dampTip  = 8f,     // 비율 0.39 / 0.28 → 눈에 띄는 오버슛
            stickBody = 22f,  stickTip = 10f,    // ★ 이전 7/2.5 → 3~4배. 확실히 버틴다
            jitter    = 0.45f,
            stickWobble = 0.35f, stickWobbleRate = 1.7f,
            // 풀린 뒤 이 각도 안으로 따라잡으면 다시 고착 → 걷는 동안 툭·툭 반복된다.
            // 이전 0.6°는 너무 작아 한 번 풀리면 계속 풀린 채(= 흐물흐물)였다.
            releaseEnd  = 3f, releaseVel = 60f,
            // 몹 평소 이동은 2 m/s대, 돌진은 훨씬 빠르다(AIConfig 기준).
            // 0.5 이하 = 사실상 정지(공격·조준 중) → 효과 없음. 1.8 이상이면 완전히 뻑뻑.
            moveMin = 0.5f, moveFull = 1.8f, gateRate = 3.5f,
        };
    }
}
