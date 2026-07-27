using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 입력 컨텍스트 상태기계(뼈대). 해킹 이벤트에 따라
    /// Player ↔ Hacking ↔ ExternalControl / ViewEntry 를 전환한다.
    /// 실제 게임플레이(조종·빙의 로직)는 아직 없음 — 컨텍스트 전환 골격만. (기초_설계안 §2.5·§6.5)
    /// </summary>
    public class HackContext : MonoBehaviour
    {
        public ControlContext Current { get; private set; } = ControlContext.Player;
        public Hackable ActiveTarget { get; private set; }

        ControlContext _prevBeforeHacking = ControlContext.Player;

        /// <summary>Space 홀드로 해킹 시작(대상 조준됨).</summary>
        public void BeginHacking(Hackable target)
        {
            if (target == null) return;
            _prevBeforeHacking = Current;
            ActiveTarget = target;
            Set(ControlContext.Hacking);
        }

        /// <summary>패턴 성공 → 대상 종류에 따라 분기. (§2.5 상태기계)</summary>
        public void OnPatternSucceeded()
        {
            if (ActiveTarget == null) { Set(ControlContext.Player); return; }

            switch (ActiveTarget.controlType)
            {
                // 외부 조종은 컨텍스트 전환이 없다 — §6.5 "1회 장악": 대상이 파랑으로 남고
                // 플레이어는 본체 그대로 그 대상을 바라보며 조종한다.
                case ControlType.ExternalControl: Set(ControlContext.Player); break;
                case ControlType.ViewEntry:       Set(ControlContext.ViewEntry); break;
                case ControlType.Stun:            Set(ControlContext.Player); break; // 스턴은 컨텍스트 전환 없음
            }

            var response = ActiveTarget.GetComponent<IHackResponse>();
            if (response != null) response.OnHackSucceeded();
        }

        /// <summary>패턴 실패·취소 → 직전 컨텍스트로 복귀.</summary>
        public void OnPatternCancelled()
        {
            Set(_prevBeforeHacking);
        }

        /// <summary>Q — 본체로 복귀·조종 해제.</summary>
        public void ReturnToBody()
        {
            ActiveTarget = null;
            Set(ControlContext.Player);
        }

        void Set(ControlContext c) { Current = c; }
    }
}
