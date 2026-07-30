using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 프레스 단면(Head)에 닿으면 즉사 — 압사의 최소 버전.
    ///
    /// <para>대상은 셋으로 갈린다:
    /// <list type="bullet">
    ///  <item><b>순찰 중인 경비병</b> — 자기 콜라이더가 그대로 걸린다. <see cref="GuardDestruction"/> 파괴.</item>
    ///  <item><b>빙의 중인 경비병</b> — 빙의 중엔 경비병 자신의 콜라이더가 꺼져 있다(<see cref="ViewEntryController"/>).
    ///        실제로 그 자리에서 물리적으로 움직이는 건 <b>플레이어 리그의 CharacterController</b>이므로
    ///        그쪽이 걸린다. 경비병을 파괴하고 <see cref="ViewEntryController.KillPossessedTarget"/>으로
    ///        본체에 돌려보낸다 — 게임오버는 아니다.</item>
    ///  <item><b>본체(빙의 중이 아닌 플레이어)</b> — 같은 CharacterController가 걸리지만 이번엔
    ///        빙의 중이 아니므로 진짜 사망 = <see cref="GameOverManager"/> 트리거.</item>
    /// </list></para>
    ///
    /// <para><b>트리거 콜백을 안 쓰는 이유</b>: <c>OnTriggerEnter</c>는 양쪽 중 한쪽에라도
    /// <see cref="Rigidbody"/>가 있어야 발동하는데, 프레스 <c>Head</c>(<see cref="TelescopingActuator"/>가
    /// <c>localPosition</c>을 직접 옮김)도 경비병(<see cref="GuardPatrol"/>이 <c>transform.position</c>을
    /// 직접 옮김)도 Rigidbody가 없다. 그래서 매 프레임 <see cref="Physics.OverlapBox"/>로 직접 겹침을
    /// 잰다 — Rigidbody 배선을 신경 쓸 필요가 없어 훨씬 확실하다.</para>
    ///
    /// <para>기존 <c>Head BoxCollider</c>(비트리거, 실제 충돌용)는 그대로 두고 건드리지 않는다 —
    /// 이 컴포넌트는 그 콜라이더의 바운즈만 읽어 별도로 검사할 뿐이다.</para>
    ///
    /// <para><b>본체 셸</b>(<see cref="ViewEntryController.Shell"/>)에는 콜라이더를 붙이지 않는다 —
    /// 붙이면 조준 레이(<see cref="HackDriver.FindAimedHackable"/>)를 가로막거나(레이어로 뺄 수는
    /// 있지만 <c>ClimbLedge</c>·<c>DevConsole</c>은 <c>~0</c> 마스크라 그래도 걸림), 되돌아올 때
    /// 리그가 셸 위치로 정확히 겹치며 텔레포트하는 순간 CC 겹침-밀어내기가 발생한다(<see cref="ViewEntryController"/>
    /// 의 <c>ExitBody</c> 참조). 그래서 셸은 <b>위치만</b> 직접 재서 검사한다 — 물리 엔진에 아예
    /// 발을 안 담그므로 위 문제들과 완전히 무관하다.</para>
    /// </summary>
    public class PressCrusher : MonoBehaviour
    {
        [Tooltip("압사 판정 기준 콜라이더. 비우면 자기 자신에서 찾는다(프레스 Head에 붙이면 됨).")]
        public Collider face;

        [Tooltip("판정 레이어.")]
        public LayerMask mask = ~0;

        void Awake()
        {
            if (face == null) face = GetComponent<Collider>();
            if (face == null) Debug.LogError($"[압사] {name}: 판정할 콜라이더가 없습니다.", this);
        }

        void Update()
        {
            if (face == null) return;

            Vector3 worldCenter;
            Vector3 halfExtents;
            Quaternion rot;

            var box = face as BoxCollider;
            if (box != null)
            {
                // BoxCollider면 회전까지 반영한 정확한 오리엔티드 박스로 검사한다.
                worldCenter = box.transform.TransformPoint(box.center);
                halfExtents = Vector3.Scale(box.size, box.transform.lossyScale) * 0.5f;
                rot = box.transform.rotation;
            }
            else
            {
                // 다른 콜라이더 타입 폴백 — 월드 AABB(회전 없음)로 근사한다.
                Bounds b = face.bounds;
                worldCenter = b.center;
                halfExtents = b.extents;
                rot = Quaternion.identity;
            }

            Collider[] hits = Physics.OverlapBox(worldCenter, halfExtents, rot, mask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hits.Length; i++)
            {
                if (hits[i] == face) continue;

                // 순찰 중인 경비병 — 자기 콜라이더로 바로 걸린다.
                var d = hits[i].GetComponentInParent<GuardDestruction>();
                if (d != null && !d.Destroyed) { d.Destruct(Vector3.zero); continue; }

                // 플레이어 리그(빙의 중엔 경비병 대신 이쪽이 물리적으로 여기 있다).
                var fpp = hits[i].GetComponentInParent<FirstPersonPlayer>();
                if (fpp == null) continue;

                var vec = ViewEntryController.Current;
                if (vec != null && vec.Active && vec.AllowsMove)
                    vec.KillPossessedTarget();   // 빙의 중인 경비병이 죽음 — 본체로 복귀, 게임오버 아님
                else
                    GameOverManager.Trigger("프레스에 압사");   // 본체가 직접 닿음 — 진짜 사망
            }

            CheckShell(worldCenter, halfExtents, rot);
        }

        /// <summary>본체 셸은 콜라이더가 없으므로 물리 쿼리에 안 걸린다 — 위치만 박스 안에 있는지
        /// 직접 잰다. 셸은 빙의 중에만 존재하고, 셸이 깔린다는 건 "떠나 있는 동안 본체가 죽었다" =
        /// 게임오버다(경비병이 죽는 것과 달리 되돌아갈 곳이 없다).</summary>
        void CheckShell(Vector3 worldCenter, Vector3 halfExtents, Quaternion rot)
        {
            var vec = ViewEntryController.Current;
            Transform shell = vec != null ? vec.Shell : null;
            if (shell == null) return;

            Vector3 local = Quaternion.Inverse(rot) * (shell.position - worldCenter);
            if (Mathf.Abs(local.x) <= halfExtents.x && Mathf.Abs(local.y) <= halfExtents.y && Mathf.Abs(local.z) <= halfExtents.z)
                GameOverManager.Trigger("본체가 프레스에 압사");
        }
    }
}
