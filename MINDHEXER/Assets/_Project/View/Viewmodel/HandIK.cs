using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 2본 IK (어깨-팔꿈치-손목) — 인체 관절 제한 포함. (Precog에서 포팅, 변경 없음 — 무기 의존성 없음)
    ///
    /// 설계 원칙:
    ///   1) <b>절대 해</b>: 매 프레임 목표로부터 팔꿈치 위치를 새로 계산한다(회전 델타 누적 아님).
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
        public Transform upper;
        public Transform lower;
        public Transform end;

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
        [Tooltip("손목을 목표 회전에 맞춤")]
        public bool matchRotation = true;

        [Header("인체 관절 제한 (도)")]
        [Range(0f, 30f)]  public float elbowMinFlex = 3f;
        [Range(60f, 160f)] public float elbowMaxFlex = 145f;
        [Range(30f, 180f)] public float shoulderMaxCone = 135f;
        [Range(10f, 90f)]  public float wristMaxSwing = 70f;
        [Range(10f, 120f)] public float wristMaxTwist = 85f;

        [Header("디버그")]
        public bool drawGizmos = true;

        Quaternion upperRestLocal, lowerRestLocal, endRestLocal;
        Vector3    shoulderRestDirLocal;
        bool captured;

        void OnEnable() { Capture(); }

        [ContextMenu("현재 자세를 기준으로 캡처")]
        public void Capture()
        {
            if (upper == null || lower == null || end == null) { captured = false; return; }
            upperRestLocal = upper.localRotation;
            lowerRestLocal = lower.localRotation;
            endRestLocal   = end.localRotation;
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

            Quaternion upper0 = upper.localRotation, lower0 = lower.localRotation, end0 = end.localRotation;

            Vector3 A = upper.position;
            float lab = Vector3.Distance(upper.position, lower.position);
            float lcb = Vector3.Distance(lower.position, end.position);
            if (lab < 1e-5f || lcb < 1e-5f) return;

            float minFlex = Mathf.Min(elbowMinFlex, elbowMaxFlex);
            float maxFlex = Mathf.Max(elbowMinFlex, elbowMaxFlex);
            float dMax = ReachAtFlex(lab, lcb, minFlex);
            float dMin = ReachAtFlex(lab, lcb, maxFlex);

            Vector3 palmOff = PalmOffset();
            Quaternion handRotFinal = matchRotation ? target.rotation : end.rotation;
            Vector3 wristTarget = target.position - handRotFinal * palmOff;

            Vector3 toT = wristTarget - A;
            float dist = toT.magnitude;
            if (dist < 1e-5f) return;
            Vector3 dirAT = toT / dist;
            dist = Mathf.Clamp(dist, dMin, dMax);

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

            Vector3 poleW = pole != null
                ? pole.position
                : A + (transform.parent != null ? transform.parent.TransformDirection(poleLocalDir) : poleLocalDir);
            Vector3 n = Vector3.Cross(dirAT, poleW - A);
            if (n.sqrMagnitude < 1e-7f)
            {
                n = Vector3.Cross(dirAT, lower.position - A);
                if (n.sqrMagnitude < 1e-7f)
                    n = Vector3.Cross(dirAT, Mathf.Abs(dirAT.y) < 0.9f ? Vector3.up : Vector3.right);
            }
            n.Normalize();

            float cosShoulder = Mathf.Clamp((lab * lab + dist * dist - lcb * lcb) / (2f * lab * dist), -1f, 1f);
            float angShoulder = Mathf.Acos(cosShoulder) * Mathf.Rad2Deg;
            Vector3 dirAB = Quaternion.AngleAxis(angShoulder, n) * dirAT;
            Vector3 B = A + dirAB * lab;
            Vector3 Tc = A + dirAT * dist;

            AimBone(upper, lower.position, B);
            AimBone(lower, end.position,   Tc);

            if (matchRotation)
            {
                end.rotation = target.rotation;
                ClampWrist();
            }

            if (weight < 1f)
            {
                upper.localRotation = Quaternion.Slerp(upper0, upper.localRotation, weight);
                lower.localRotation = Quaternion.Slerp(lower0, lower.localRotation, weight);
                end.localRotation   = Quaternion.Slerp(end0,   end.localRotation,   weight);
            }
        }

        public Vector3 PalmOffset()
        {
            if (palmPoint != null && end != null)
                return Quaternion.Inverse(end.rotation) * (palmPoint.position - end.position);
            return palmOffsetLocal;
        }

        public Vector3 PalmWorld() => end != null ? end.position + end.rotation * PalmOffset() : Vector3.zero;

        [ContextMenu("현재 파지를 그립에 기록 (회전+오프셋)")]
        public void CaptureGripFromHand()
        {
            if (end == null || target == null) { Debug.LogWarning("[HandIK] end 또는 target 이 비어 있습니다."); return; }
#if UNITY_EDITOR
            UnityEditor.Undo.RecordObject(target, "파지 기록");
            UnityEditor.Undo.RecordObject(this,   "파지 기록");
#endif
            Quaternion before = target.rotation;
            target.rotation = end.rotation;
            palmPoint = null;
            palmOffsetLocal = Quaternion.Inverse(end.rotation) * (target.position - end.position);

            float d = palmOffsetLocal.magnitude;
            Debug.Log($"[HandIK] 파지 기록 완료 — 회전 {Quaternion.Angle(before, target.rotation):F1}° 보정, " +
                      $"손바닥 오프셋 {palmOffsetLocal:F4} (손목→손바닥 {d:F3}m)");
        }

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

        static void AimBone(Transform bone, Vector3 childWorldPos, Vector3 desiredChildPos)
        {
            Vector3 cur = childWorldPos - bone.position;
            Vector3 want = desiredChildPos - bone.position;
            if (cur.sqrMagnitude < 1e-10f || want.sqrMagnitude < 1e-10f) return;
            bone.rotation = Quaternion.FromToRotation(cur, want) * bone.rotation;
        }

        static float ReachAtFlex(float lab, float lcb, float flexDeg)
        {
            float interior = (180f - flexDeg) * Mathf.Deg2Rad;
            float d2 = lab * lab + lcb * lcb - 2f * lab * lcb * Mathf.Cos(interior);
            return Mathf.Sqrt(Mathf.Max(d2, 1e-6f));
        }

        void ClampWrist()
        {
            Quaternion local = end.localRotation;
            Quaternion delta = Quaternion.Inverse(endRestLocal) * local;

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

        static Quaternion ClampAngle(Quaternion q, float maxDeg)
        {
            q.ToAngleAxis(out float ang, out Vector3 axis);
            if (float.IsNaN(axis.x) || axis.sqrMagnitude < 1e-8f) return Quaternion.identity;
            if (ang > 180f) ang -= 360f;
            float clamped = Mathf.Clamp(ang, -maxDeg, maxDeg);
            return Quaternion.AngleAxis(clamped, axis.normalized);
        }

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
                Gizmos.color = Color.magenta;
                Vector3 palm = PalmWorld();
                Gizmos.DrawWireSphere(palm, 0.02f);
                Gizmos.DrawLine(end.position, palm);
                Gizmos.color = Color.gray;
                Gizmos.DrawLine(palm, target.position);
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
