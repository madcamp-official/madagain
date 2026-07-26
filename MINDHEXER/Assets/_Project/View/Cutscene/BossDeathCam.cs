using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 보스 죽음 연출용 <b>아레나 전경 카메라 구도 마커</b>. 순수 authoring 데이터다 —
    /// 위치·회전은 이 오브젝트의 Transform, 화각은 <see cref="fov"/>. 런타임 로직은 없다(마커일 뿐).
    /// <see cref="BossDeathDirector"/>가 보스 사망 때 이 포즈·FOV로 카메라를 옮긴다.
    ///
    /// 구도는 <b>씬 뷰에서 직접</b> 잡는다: 오브젝트를 옮기고 FOV를 조절하면 프러스텀 기즈모가
    /// 그 화면 범위를 보여준다. 다 맞추면 씬을 저장하면 된다(씬에 직렬화됨).
    /// 에디터 메뉴: <c>Tools ▸ 보스 죽음 카메라 설정</c> / <c>… 죽음 카메라로 씬뷰 정렬</c>.
    /// </summary>
    [DisallowMultipleComponent]
    public class BossDeathCam : MonoBehaviour
    {
        [Tooltip("전경 카메라 화각(도).")]
        public float fov = 50f;
        [Tooltip("프러스텀 기즈모 길이(m) — 구도 확인용, 연출엔 영향 없음.")]
        public float gizmoFar = 40f;

        /// <summary>씬에 배치된 죽음 카메라 마커(비활성 포함). 없으면 null.</summary>
        public static BossDeathCam Find()
            => Object.FindFirstObjectByType<BossDeathCam>(FindObjectsInactive.Include);

        void OnDrawGizmos()
        {
            Matrix4x4 old = Gizmos.matrix;
            Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
            Gizmos.color = new Color(1f, 0.35f, 0.1f, 0.9f);
            // +Z(카메라 전방)로 열리는 프러스텀. near 0.3, far gizmoFar, 16:9.
            Gizmos.DrawFrustum(Vector3.zero, fov, gizmoFar, 0.3f, 16f / 9f);
            Gizmos.matrix = old;
            Gizmos.color = new Color(1f, 0.5f, 0.2f, 1f);
            Gizmos.DrawSphere(transform.position, 0.35f);
        }
    }
}
