using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 플레이어 시작 지점. 씬에 빈 오브젝트를 하나 두고 이 컴포넌트를 붙이면 거기서 시작한다.
    /// 오브젝트의 forward(파란 축)가 시작 시 바라보는 방향이 된다.
    ///
    /// 없으면 Main이 카메라 위치 근처로 폴백하는데, 그건 씬 카메라를 옮길 때마다 시작점이
    /// 바뀌고 NavMesh가 비면 (0,0,0)으로 떨어져 버려서 맵 테스트가 불안정해진다.
    /// </summary>
    public class PlayerSpawnPoint : MonoBehaviour
    {
        void OnDrawGizmos()
        {
            Vector3 p = transform.position;
            Gizmos.color = new Color(0.25f, 1f, 0.45f);
            Gizmos.DrawWireSphere(p + Vector3.up * 1.0f, 0.35f);   // 머리
            Gizmos.DrawLine(p, p + Vector3.up * 1.45f);            // 몸
            Gizmos.DrawLine(p, p + Vector3.right * 0.3f);          // 발 표시
            Gizmos.DrawLine(p, p - Vector3.right * 0.3f);

            Gizmos.color = new Color(0.1f, 0.8f, 1f);              // 바라보는 방향
            Vector3 eye = p + Vector3.up * 1.1f;
            Gizmos.DrawLine(eye, eye + transform.forward * 2f);
        }
    }
}
