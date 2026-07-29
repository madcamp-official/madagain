using System.Collections.Generic;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 이 라이더 위에 서 있는 것을 함께 실어 나른다. 발판·바닥 역할을 하는 라이더에 붙인다.
    ///
    /// <para><b>왜 부모 지정(SetParent)이 아닌가</b> — 플레이어를 계층에 끼워 넣으면 VR 리그·
    /// <see cref="AutoTraversal"/>·<see cref="MantleRig"/>가 쓰는 좌표계가 흔들린다. 대신 세트의
    /// 월드 이동량만큼 <c>CharacterController.Move</c>를 한 번 더 불러 준다 — 계층은 그대로 두고
    /// 결과만 같게 만드는 방식이라 다른 시스템과 안 싸운다.</para>
    ///
    /// <para><see cref="RailSet"/>이 자기 이동 직후 <see cref="Carry"/>를 부른다. 중첩 세트 안의
    /// 발판은 그 안쪽 세트가 나르므로(소유자 판정) 이중으로 밀리지 않는다.</para>
    ///
    /// <para>⚠️ 현재는 <c>CharacterController</c>만 대상이다. 경비병(NavMeshAgent) 탑승은 미구현.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public class RailPlatform : MonoBehaviour
    {
        [Tooltip("발 밑 이 거리 안에서 이 발판이 잡히면 '올라타 있다'로 본다(m).")]
        public float probeDistance = 0.35f;

        RailSet _owner;
        bool _ownerCached;

        /// <summary>이 발판을 나르는 세트 = 가장 가까운 조상 RailSet.</summary>
        public RailSet Owner
        {
            get
            {
                if (!_ownerCached) { _owner = GetComponentInParent<RailSet>(); _ownerCached = true; }
                return _owner;
            }
        }

        // 씬의 CharacterController 목록. 매 프레임 찾으면 비싸서 주기적으로만 갱신한다.
        static readonly List<CharacterController> Riders = new List<CharacterController>();
        static float _nextScan;

        // 도메인 리로드를 끈 환경에서 이전 플레이의 파괴된 참조가 남지 않게 한다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void ResetStatics()
        {
            Riders.Clear();
            _nextScan = 0f;
        }

        /// <summary>세트가 움직인 만큼 위에 탄 것들을 같이 옮긴다.</summary>
        public void Carry(Vector3 delta)
        {
            RefreshRiders();

            for (int i = 0; i < Riders.Count; i++)
            {
                var cc = Riders[i];
                if (cc == null || !cc.enabled || !cc.gameObject.activeInHierarchy) continue;
                if (!StandingOnMe(cc)) continue;
                cc.Move(delta);
                // "내가 걸은 건지 밀린 건지" 구분용 화면 지연 — 레일·피스톤·프레서 등 강제 이동의
                // 유일한 관문이 여기라, 여기 한 곳만 호출하면 전부에 적용된다(MotionFeel.OnCarried 참조).
                if (cc.TryGetComponent(out MotionFeel feel)) feel.OnCarried(delta);
            }
        }

        bool StandingOnMe(CharacterController cc)
        {
            float r = Mathf.Max(0.01f, cc.radius * 0.9f);
            Vector3 bottomSphere = cc.transform.TransformPoint(cc.center)
                                 + Vector3.up * (-cc.height * 0.5f + cc.radius);

            if (!Physics.SphereCast(bottomSphere, r, Vector3.down, out RaycastHit hit,
                                    probeDistance, ~0, QueryTriggerInteraction.Ignore))
                return false;

            return hit.collider != null && hit.collider.transform.IsChildOf(transform);
        }

        static void RefreshRiders()
        {
            if (Time.unscaledTime < _nextScan) return;
            _nextScan = Time.unscaledTime + 1f;

            Riders.Clear();
            Riders.AddRange(Object.FindObjectsByType<CharacterController>(FindObjectsSortMode.None));
        }
    }
}
