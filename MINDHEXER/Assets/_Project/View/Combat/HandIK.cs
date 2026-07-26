using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 2본 IK (어깨-팔꿈치-손목) — 인체 관절 제한 포함.
    ///
    /// 설계 원칙(이전 구현의 실패를 고친 것):
    ///   1) <b>절대 해</b>: 매 프레임 목표로부터 팔꿈치 위치를 새로 계산한다.
    ///      이전엔 현재 자세에 회전 델타를 누적해서 오차가 남고 진동·뒤집힘이 났다.
    ///   2) <b>굽힘 평면을 명시</b>: 팔꿈치가 갈 방향을 폴로 확정한다. 폴이 팔 축과
    ///      평행해지면 자동으로 대체 축을 쓴다(뒤집힘 방지).
    ///   3) <b>인체 관절 제한</b>: 팔꿈치는 경첩(과신전 금지, 최대 굴곡 제한),
    ///      어깨는 원뿔 제한, 손목은 스윙/트위스트 분리 후 각각 제한.
    ///
    /// 실행: LateUpdate — Animator가 클립 포즈를 쓴 뒤에 얹힌다.
    /// [ExecuteAlways] — Play 없이 에디터에서도 즉시 반영.
    /// </summary>
    [ExecuteAlways]
    public class HandIK : MonoBehaviour
    {
        [Header("팔 체인 (위팔 → 아래팔 → 손)")]
        public Transform upper;   // mixamorig:*Arm      — 어깨 관절
        public Transform lower;   // mixamorig:*ForeArm  — 팔꿈치
        public Transform end;     // mixamorig:*Hand     — 손목

        [Header("목표")]
        public Transform target;

        [Header("손바닥 오프셋")]
        [Tooltip("손 뼈(=손목) 기준 손바닥 위치. 이 지점이 목표에 오도록 푼다.\n" +
                 "0이면 손목이 목표에 붙어 손바닥이 그 너머로 삐져나간다.")]
        public Vector3 palmOffsetLocal = Vector3.zero;
        [Tooltip("지정하면 이 Transform 을 손바닥 지점으로 사용(오프셋 자동 계산)")]
        public Transform palmPoint;

        [Header("팔꿈치 방향")]
        [Tooltip("팔꿈치가 향할 기준점. 비우면 아래 방향값을 사용")]
        public Transform pole;
        [Tooltip("폴이 없을 때 쓸 방향(캐릭터 루트 기준). 보통 아래·뒤")]
        public Vector3 poleLocalDir = new Vector3(0f, -1f, -0.6f);

        [Header("가중치")]
        [Range(0f, 1f)] public float weight = 1f;
        [Tooltip("손목을 목표 회전에 맞춤(칼을 제대로 쥐게)")]
        public bool matchRotation = true;

        [Header("인체 관절 제한 (도)")]
        [Tooltip("팔꿈치 최소 굴곡. 0=완전히 편 상태. 과신전 방지를 위해 2~5 권장")]
        [Range(0f, 30f)]  public float elbowMinFlex = 3f;
        [Tooltip("팔꿈치 최대 굴곡. 사람은 약 145°")]
        [Range(60f, 160f)] public float elbowMaxFlex = 145f;
        [Tooltip("위팔이 기준 방향에서 벗어날 수 있는 최대 각(어깨 가동 원뿔)")]
        [Range(30f, 180f)] public float shoulderMaxCone = 135f;
        [Tooltip("손목 꺾임 최대각(굴곡·신전·좌우)")]
        [Range(10f, 90f)]  public float wristMaxSwing = 70f;
        [Tooltip("손목 비틀림 최대각")]
        [Range(10f, 120f)] public float wristMaxTwist = 85f;

        [Header("디버그")]
        public bool drawGizmos = true;

        // ── 기준(rest) 데이터 ──
        Quaternion upperRestLocal, lowerRestLocal, endRestLocal;
        Vector3    shoulderRestDirLocal;   // 위팔 기준 방향(어깨 부모 공간)
        bool captured;

        void OnEnable() { Capture(); }

        /// <summary>현재 자세를 관절 제한의 기준(rest)으로 잡는다.</summary>
        [ContextMenu("현재 자세를 기준으로 캡처")]
        public void Capture()
        {
            if (upper == null || lower == null || end == null) { captured = false; return; }
            upperRestLocal = upper.localRotation;
            lowerRestLocal = lower.localRotation;
            endRestLocal   = end.localRotation;
            // 위팔이 뻗은 방향을 어깨 부모 공간으로 기록(어깨 원뿔 제한의 중심축)
            Vector3 dirW = (lower.position - upper.position).normalized;
            shoulderRestDirLocal = upper.parent != null
                ? upper.parent.InverseTransformDirection(dirW)
                : dirW;
            captured = true;
        }

        void LateUpdate() => Solve();

        void Solve()
        {
            if (weight <= 0f || upper == null || lower == null || end == null || target == null) return;
            if (!captured) Capture();

            // 블렌드용 원본
            Quaternion upper0 = upper.localRotation, lower0 = lower.localRotation, end0 = end.localRotation;

            Vector3 A = upper.position;
            float lab = Vector3.Distance(upper.position, lower.position);   // 위팔 길이
            float lcb = Vector3.Distance(lower.position, end.position);     // 아래팔 길이
            if (lab < 1e-5f || lcb < 1e-5f) return;

            // ── 1) 목표 거리를 "팔꿈치 제한이 허용하는 범위"로 클램프 ──
            //     굴곡각 f 일 때 팔꿈치 내각 = 180-f, 코사인법칙으로 도달거리를 구한다.
            float minFlex = Mathf.Min(elbowMinFlex, elbowMaxFlex);
            float maxFlex = Mathf.Max(elbowMinFlex, elbowMaxFlex);
            float dMax = ReachAtFlex(lab, lcb, minFlex);   // 가장 편 상태 → 가장 멂
            float dMin = ReachAtFlex(lab, lcb, maxFlex);   // 가장 굽힌 상태 → 가장 가까움

            // ★ 손바닥이 목표에 오도록: 손목이 가야 할 지점 = 목표 − (손 회전 × 손바닥오프셋)
            //   손 뼈의 원점은 손목이라, 오프셋 없이 풀면 손목이 그립에 붙고 손바닥이 삐져나간다.
            Vector3 palmOff = PalmOffset();
            Quaternion handRotFinal = matchRotation ? target.rotation : end.rotation;
            Vector3 wristTarget = target.position - handRotFinal * palmOff;

            Vector3 toT = wristTarget - A;
            float dist = toT.magnitude;
            if (dist < 1e-5f) return;
            Vector3 dirAT = toT / dist;
            dist = Mathf.Clamp(dist, dMin, dMax);

            // ── 2) 어깨 원뿔 제한: 위팔이 기준 방향에서 너무 벗어나지 않게 목표 방향을 당김 ──
            Vector3 restDirW = upper.parent != null
                ? upper.parent.TransformDirection(shoulderRestDirLocal).normalized
                : shoulderRestDirLocal;
            float coneAngle = Vector3.Angle(restDirW, dirAT);
            if (coneAngle > shoulderMaxCone)
            {
                Vector3 axis = Vector3.Cross(restDirW, dirAT);
                if (axis.sqrMagnitude > 1e-8f)
                    dirAT = Quaternion.AngleAxis(shoulderMaxCone, axis.normalized) * restDirW;
            }

            // ── 3) 굽힘 평면 결정 (폴). 팔 축과 평행하면 대체 축 사용 → 뒤집힘 방지 ──
            Vector3 poleW = pole != null
                ? pole.position
                : A + (transform.parent != null ? transform.parent.TransformDirection(poleLocalDir) : poleLocalDir);
            Vector3 n = Vector3.Cross(dirAT, poleW - A);
            if (n.sqrMagnitude < 1e-7f)
            {
                // 폴이 팔 축과 겹침 → 현재 팔꿈치가 만드는 평면을 우선 사용
                n = Vector3.Cross(dirAT, lower.position - A);
                if (n.sqrMagnitude < 1e-7f)
                {
                    // 그것도 퇴화 → 임의의 수직 축
                    n = Vector3.Cross(dirAT, Mathf.Abs(dirAT.y) < 0.9f ? Vector3.up : Vector3.right);
                }
            }
            n.Normalize();

            // ── 4) 팔꿈치 위치를 절대 계산 (코사인법칙) ──
            float cosShoulder = Mathf.Clamp((lab * lab + dist * dist - lcb * lcb) / (2f * lab * dist), -1f, 1f);
            float angShoulder = Mathf.Acos(cosShoulder) * Mathf.Rad2Deg;
            Vector3 dirAB = Quaternion.AngleAxis(angShoulder, n) * dirAT;
            Vector3 B = A + dirAB * lab;
            Vector3 Tc = A + dirAT * dist;   // 제한 반영된 최종 손 위치

            // ── 5) 뼈를 목표 방향으로 조준 (절대 방향이라 한 번에 수렴) ──
            AimBone(upper, lower.position, B);   // 위팔: 팔꿈치를 B로
            AimBone(lower, end.position,   Tc);  // 아래팔: 손을 Tc로

            // ── 6) 손목 ──
            if (matchRotation)
            {
                end.rotation = target.rotation;
                ClampWrist();
            }

            // ── 7) weight 블렌드 ──
            if (weight < 1f)
            {
                upper.localRotation = Quaternion.Slerp(upper0, upper.localRotation, weight);
                lower.localRotation = Quaternion.Slerp(lower0, lower.localRotation, weight);
                end.localRotation   = Quaternion.Slerp(end0,   end.localRotation,   weight);
            }
        }

        // ★ 오프셋은 "손 회전 기준 + 월드 길이" 로 다룬다.
        //   InverseTransformPoint/TransformPoint 는 스케일까지 반영하는데, 이 캐릭터는 루트 스케일이
        //   100이라 그걸 쓰면 오프셋이 1/100로 줄어들어 사실상 0이 된다(손이 목표로 끌려가던 원인).
        //   회전만 쓰는 아래 방식은 스케일과 무관하게 항상 일관된다.

        /// <summary>손목 기준 손바닥 오프셋(월드 길이). palmPoint 가 있으면 그걸로 계산.</summary>
        public Vector3 PalmOffset()
        {
            if (palmPoint != null && end != null)
                return Quaternion.Inverse(end.rotation) * (palmPoint.position - end.position);
            return palmOffsetLocal;
        }

        /// <summary>손바닥 지점의 현재 월드 위치(기즈모·검증용).</summary>
        public Vector3 PalmWorld() => end != null ? end.position + end.rotation * PalmOffset() : Vector3.zero;

        /// <summary>
        /// ★ 권장: 지금 손이 칼을 제대로 쥔 상태를 그립에 통째로 기록한다.
        ///   ① 그립 회전 = 현재 손 회전 (월드 기준)  ② 손바닥 오프셋 = 손목→그립 벡터
        ///
        /// 결과: IK를 켜도 화면이 바뀌지 않고(현재 파지를 그대로 기록하므로),
        ///       이후 칼·그립을 움직이면 손이 올바른 각도로 따라온다.
        ///
        /// 순서: IK 끄기 → 손을 제대로 쥐게 맞추기 → 그립을 손바닥 한가운데로 이동 → 이 버튼 → IK 켜기
        /// </summary>
        [ContextMenu("현재 파지를 그립에 기록 (회전+오프셋)")]
        public void CaptureGripFromHand()
        {
            if (end == null || target == null) { Debug.LogWarning("[HandIK] end 또는 target 이 비어 있습니다."); return; }
#if UNITY_EDITOR
            UnityEditor.Undo.RecordObject(target, "파지 기록");
            UnityEditor.Undo.RecordObject(this,   "파지 기록");
#endif
            Quaternion before = target.rotation;
            target.rotation = end.rotation;                     // ① 그립 회전 = 손 회전
            palmPoint = null;
            // ② 손목→그립 (회전 기준, 월드 길이 — 스케일 영향 없음)
            palmOffsetLocal = Quaternion.Inverse(end.rotation) * (target.position - end.position);

            float d = palmOffsetLocal.magnitude;
            Debug.Log($"[HandIK] 파지 기록 완료 — 그립 회전 {Quaternion.Angle(before, target.rotation):F1}° 보정, " +
                      $"손바닥 오프셋 {palmOffsetLocal:F4} (손목→손바닥 {d:F3}m)" +
                      (d < 0.005f ? "\n  ★ 거리가 거의 0입니다. 그립을 손바닥 한가운데로 먼저 옮기십시오." :
                       d > 0.20f  ? "\n  ★ 거리가 너무 큽니다. 그립이 손에서 멀리 떨어져 있습니다." : ""));
        }

        /// <summary>회전은 두고 손바닥 오프셋만 다시 잡을 때.</summary>
        [ContextMenu("손바닥 오프셋만 캡처 (회전 유지)")]
        public void CapturePalmOffset()
        {
            if (end == null || target == null) return;
#if UNITY_EDITOR
            UnityEditor.Undo.RecordObject(this, "오프셋 캡처");
#endif
            palmPoint = null;
            palmOffsetLocal = Quaternion.Inverse(end.rotation) * (target.position - end.position);
            Debug.Log($"[HandIK] 손바닥 오프셋 = {palmOffsetLocal:F4} (손목→그립 {palmOffsetLocal.magnitude:F3}m)");
        }

        /// <summary>뼈의 자식 방향(current)을 desired 로 향하게 회전.</summary>
        static void AimBone(Transform bone, Vector3 childWorldPos, Vector3 desiredChildPos)
        {
            Vector3 cur = childWorldPos - bone.position;
            Vector3 want = desiredChildPos - bone.position;
            if (cur.sqrMagnitude < 1e-10f || want.sqrMagnitude < 1e-10f) return;
            bone.rotation = Quaternion.FromToRotation(cur, want) * bone.rotation;
        }

        /// <summary>굴곡각 f(도)일 때 어깨~손 거리.</summary>
        static float ReachAtFlex(float lab, float lcb, float flexDeg)
        {
            float interior = (180f - flexDeg) * Mathf.Deg2Rad;   // 팔꿈치 내각
            float d2 = lab * lab + lcb * lcb - 2f * lab * lcb * Mathf.Cos(interior);
            return Mathf.Sqrt(Mathf.Max(d2, 1e-6f));
        }

        /// <summary>손목을 인체 범위로 제한. 아래팔 기준 로컬 회전을 스윙/트위스트로 분리해 각각 클램프.</summary>
        void ClampWrist()
        {
            Quaternion local = end.localRotation;                 // 아래팔 기준
            Quaternion delta = Quaternion.Inverse(endRestLocal) * local;   // 기준 자세로부터의 차이

            // 트위스트 축 = 아래팔에서 손으로 뻗는 방향(로컬)
            Vector3 twistAxis = end.parent != null
                ? end.parent.InverseTransformDirection((end.position - lower.position).normalized)
                : Vector3.forward;
            if (twistAxis.sqrMagnitude < 1e-8f) twistAxis = Vector3.forward;
            twistAxis.Normalize();

            SwingTwist(delta, twistAxis, out Quaternion swing, out Quaternion twist);

            swing = ClampAngle(swing, wristMaxSwing);
            twist = ClampAngle(twist, wristMaxTwist);

            end.localRotation = endRestLocal * (swing * twist);
        }

        /// <summary>회전을 지정 축 기준 스윙(축에 수직) + 트위스트(축 둘레)로 분해.</summary>
        static void SwingTwist(Quaternion q, Vector3 axis, out Quaternion swing, out Quaternion twist)
        {
            Vector3 r = new Vector3(q.x, q.y, q.z);
            Vector3 p = Vector3.Project(r, axis);
            twist = new Quaternion(p.x, p.y, p.z, q.w);
            float m = twist.x * twist.x + twist.y * twist.y + twist.z * twist.z + twist.w * twist.w;
            if (m < 1e-8f) twist = Quaternion.identity;
            else { float inv = 1f / Mathf.Sqrt(m); twist = new Quaternion(twist.x * inv, twist.y * inv, twist.z * inv, twist.w * inv); }
            swing = q * Quaternion.Inverse(twist);
        }

        /// <summary>회전 크기를 maxDeg 로 제한.</summary>
        static Quaternion ClampAngle(Quaternion q, float maxDeg)
        {
            q.ToAngleAxis(out float ang, out Vector3 axis);
            if (float.IsNaN(axis.x) || axis.sqrMagnitude < 1e-8f) return Quaternion.identity;
            if (ang > 180f) ang -= 360f;
            float clamped = Mathf.Clamp(ang, -maxDeg, maxDeg);
            return Quaternion.AngleAxis(clamped, axis.normalized);
        }

        /// <summary>목표가 팔 사정거리 밖인가(툴 경고용).</summary>
        public bool IsOutOfReach()
        {
            if (upper == null || lower == null || end == null || target == null) return false;
            float lab = Vector3.Distance(upper.position, lower.position);
            float lcb = Vector3.Distance(lower.position, end.position);
            return Vector3.Distance(upper.position, target.position) > ReachAtFlex(lab, lcb, elbowMinFlex);
        }

        void OnDrawGizmosSelected()
        {
            if (!drawGizmos || upper == null || lower == null || end == null) return;
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(upper.position, lower.position);
            Gizmos.DrawLine(lower.position, end.position);
            Gizmos.DrawWireSphere(lower.position, 0.02f);
            if (target != null)
            {
                Gizmos.color = IsOutOfReach() ? Color.red : Color.green;
                Gizmos.DrawWireSphere(target.position, 0.03f);
                // 손바닥 지점(자홍) — 이 점이 목표(초록)에 겹쳐야 제대로 쥔 것
                Gizmos.color = Color.magenta;
                Vector3 palm = PalmWorld();
                Gizmos.DrawWireSphere(palm, 0.02f);
                Gizmos.DrawLine(end.position, palm);      // 손목 → 손바닥
                Gizmos.color = Color.gray;
                Gizmos.DrawLine(palm, target.position);   // 손바닥 → 목표(짧을수록 정확)
            }
            if (pole != null)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawLine(lower.position, pole.position);
                Gizmos.DrawWireSphere(pole.position, 0.02f);
            }
        }
    }
}
