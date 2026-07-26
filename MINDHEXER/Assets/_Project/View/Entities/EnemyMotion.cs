using UnityEngine;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// 몹 절차 애니메이션 — 클립 위에 얹는 레이어.
    ///
    /// 몹마다 컴포넌트를 붙이지 않는다. EntityViews가 이미 배열을 도는 구조라
    /// 개체 하나당 이 구조체 하나를 들고 같은 루프에서 갱신한다(MonoBehaviour 수백 개 방지).
    ///
    /// 1단계 = 시선 추적만. 피격·경직·호흡은 상의 후 추가한다.
    ///
    /// 실행 순서: Animator가 클립을 적용한 뒤(LateUpdate) 본 회전을 가산해야 하므로
    /// EntityViews.Sync는 Main의 LateUpdate 경로에서 불려야 한다(현재 Update면 Animator가 덮어쓴다).
    /// </summary>
    public struct EnemyMotion
    {
        // 캐시된 본 (없으면 null — 그 몹은 시선 추적을 건너뛴다)
        public Transform head, spine2, spine1;
        public Quaternion headRest, spine2Rest, spine1Rest;   // 임포트 시 로컬 회전(기준)
        public bool bound;

        // 현재 적용 중인 각도(도) — 각속도 제한을 위해 상태로 들고 있는다
        public float curYaw, curPitch;

        /// <summary>모델 계층에서 머리·척추 본을 찾아 캐시한다. 개체 생성 시 1회.</summary>
        public void Bind(Transform root)
        {
            bound = true;
            head = spine2 = spine1 = null;
            if (root == null) return;

            // 이름 규칙이 모델마다 조금씩 다르다(Head / mixamorig:Head / Bip01 Head …).
            // 정확히 일치가 아니라 "포함"으로 찾되, 가장 먼저 나온 것을 쓴다.
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                string n = t.name;
                if (head   == null && Has(n, "head")   && !Has(n, "headfront") && !Has(n, "top")) head = t;
                else if (spine2 == null && (Has(n, "spine02") || Has(n, "spine2"))) spine2 = t;
                else if (spine1 == null && (Has(n, "spine01") || Has(n, "spine1"))) spine1 = t;
            }
            // Spine이 하나뿐인 리그면 그것을 spine2로 쓴다
            if (spine2 == null && spine1 == null)
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    if (Has(t.name, "spine")) { spine2 = t; break; }

            if (head   != null) headRest   = head.localRotation;
            if (spine2 != null) spine2Rest = spine2.localRotation;
            if (spine1 != null) spine1Rest = spine1.localRotation;
        }

        static bool Has(string s, string k) => s.IndexOf(k, System.StringComparison.OrdinalIgnoreCase) >= 0;

        public bool HasHead => head != null;

        /// <summary>
        /// 플레이어를 향해 머리·척추를 돌린다. 클립이 적용된 뒤 호출해야 한다.
        /// bodyYaw = 뷰가 이미 적용한 몸통 방향(도). 목표각은 몸통 기준 상대각이다.
        /// </summary>
        public void ApplyLookAt(Vector3 enemyPos, float bodyYaw, Vector3 targetPos,
                                in EnemyLookSettings s, float dt)
        {
            if (head == null && spine2 == null && spine1 == null) return;

            // ── 목표 상대각 ──
            // ★ 시작점은 발밑이 아니라 <b>머리 본의 실제 위치</b>여야 한다.
            //   발에서 재면 상하각이 항상 위를 향해 고개를 젖히게 된다(눈이 안 마주침).
            Vector3 eye = head != null ? head.position
                        : spine2 != null ? spine2.position
                        : enemyPos;
            Vector3 to = targetPos - eye;
            float wantYaw = 0f, wantPitch = 0f;
            if (to.sqrMagnitude > 1e-4f)
            {
                float absYaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
                wantYaw = Mathf.DeltaAngle(bodyYaw, absYaw);           // 몸통 기준 좌우
                float horiz = new Vector3(to.x, 0f, to.z).magnitude;
                wantPitch = -Mathf.Atan2(to.y, Mathf.Max(0.01f, horiz)) * Mathf.Rad2Deg;  // 위를 보면 음수
            }

            // 머리가 도달할 수 있는 전체 한계(로봇 목이라 크게 잡는다). 넘으면 그 한계에서 대기.
            wantYaw   = Mathf.Clamp(wantYaw,   -s.maxYaw,   s.maxYaw);
            wantPitch = Mathf.Clamp(wantPitch, -s.maxPitch, s.maxPitch);

            // 시선을 끌 상황이면 목표를 0으로 (정면 복귀)
            if (s.weight <= 0.001f) { wantYaw = 0f; wantPitch = 0f; }

            // ── 각속도 제한 ──
            float step = Mathf.Max(1f, s.turnSpeed) * dt;
            curYaw   = Mathf.MoveTowards(curYaw,   wantYaw,   step);
            curPitch = Mathf.MoveTowards(curPitch, wantPitch, step);

            float w = Mathf.Clamp01(s.weight);
            float yaw = curYaw * w, pitch = curPitch * w;

            // ── 2단 분산(로봇 목) ──
            //   ① 몸통(척추)은 편한 범위(torsoMax)까지만 따라 돈다 — 크게 잡으면 코르크스크루처럼 꼬인다.
            //   ② 머리가 나머지 극단 각을 단독으로 커버 → 목만 빙 도는 느낌.
            float torsoYaw   = Mathf.Clamp(yaw,   -s.torsoMaxYaw,   s.torsoMaxYaw);
            float torsoPitch = Mathf.Clamp(pitch, -s.torsoMaxPitch, s.torsoMaxPitch);

            // 척추 둘이 torso 몫을 나눠 가진다(위 척추 spine2가 더 많이). 합이 정확히 torso가 되게 정규화.
            float denom = s.spine2Share + s.spine1Share;
            float up = denom > 1e-4f ? s.spine2Share / denom : 0.6f;
            Rot(spine1, torsoYaw * (1f - up), torsoPitch * (1f - up));
            Rot(spine2, torsoYaw * up,        torsoPitch * up);
            // 머리는 척추가 이미 torso만큼 돌았으니, 전체각까지의 나머지만 더한다.
            Rot(head, yaw - torsoYaw, pitch - torsoPitch);
        }

        /// <summary>클립 결과 위에 상대 오프셋을 곱해 더한다(덮지 않고 가산).</summary>
        static void Rot(Transform t, float yaw, float pitch)
        {
            if (t == null || (Mathf.Abs(yaw) < 1e-4f && Mathf.Abs(pitch) < 1e-4f)) return;
            t.localRotation = t.localRotation * Quaternion.Euler(pitch, yaw, 0f);
        }

        /// <summary>즉시 정면으로(스폰·부활 등).</summary>
        public void ResetLook() { curYaw = 0f; curPitch = 0f; }
    }

    /// <summary>시선 추적 튜닝값. 몹 종류별로 다르게 줄 수 있다.</summary>
    [System.Serializable]
    public struct EnemyLookSettings
    {
        public float weight;         // 0=끔, 1=최대
        public float maxYaw;         // 머리가 도달 가능한 좌우 전체 한계(도). 로봇이라 크게.
        public float maxPitch;       // 상하 전체 한계(도)
        public float torsoMaxYaw;    // 몸통(척추)이 따라 도는 좌우 한계 — 넘는 각은 머리가 단독 처리
        public float torsoMaxPitch;  // 몸통이 따라 젖히는 상하 한계
        public float turnSpeed;      // 각속도 제한(도/초)
        public float spine2Share, spine1Share;   // torso 몫을 위/아래 척추가 나누는 비율

        public static EnemyLookSettings Default => new EnemyLookSettings
        {
            weight = 1f,
            maxYaw = 180f,          // 목이 거의 뒤까지 돈다(로봇)
            maxPitch = 80f,         // 점프한 플레이어도 올려다봄
            torsoMaxYaw = 40f,      // 몸통은 40°까지만 — 그 이상은 머리가 단독으로
            torsoMaxPitch = 25f,
            turnSpeed = 400f,       // 빠르게 추적(로봇식 스냅 허용)
            spine2Share = 0.6f, spine1Share = 0.4f,
        };
    }
}
