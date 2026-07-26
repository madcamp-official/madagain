using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 베기 이펙트가 카메라를 어떻게 따라올지 정한다.
    ///
    /// 고속 이동 게임에서 이펙트를 월드에 고정하면, 사라지는 동안 플레이어가 앞질러 가
    /// 이펙트가 화면 뒤로 밀리거나 앞으로 튀어나온 것처럼 보인다.
    ///
    ///   월드 고정 : 그 자리에 남는다(기존 동작)
    ///   카메라 고정: 카메라 자식이 되어 화면상 위치가 고정된다
    ///   지연 추종 : 한 박자 늦게 따라온다 — 잔상이 남으면서도 화면 밖으로 안 나간다
    ///   단계 전환 : 그어지는 동안은 붙어 있다가, 이후 월드에 놓아 잔상을 세계에 남긴다
    ///
    /// 카메라 고정·단계 전환은 <b>부모로 붙여</b> 처리한다. LateUpdate 순서에 의존하지 않아
    /// (Cinemachine Brain이 언제 카메라를 옮기든) 떨림이 생기지 않는다.
    /// </summary>
    public class SlashFollow : MonoBehaviour
    {
        public const int World = 0, Camera = 1, Soft = 2, Staged = 3;

        public Transform cam;
        public int   mode = Soft;
        public float speed = 8f;          // 지연 추종 따라오는 속도
        public bool  followRotation = true;
        public float attachTime = 0.09f;  // 단계 전환에서 붙어 있는 시간

        Vector3    localPos;              // 카메라 기준 목표 위치
        Quaternion localRot;              // 카메라 기준 목표 회전
        float age;
        bool  detached;

        /// <summary>스폰 직후 호출 — 현재 월드 포즈를 카메라 기준으로 기록하고 모드를 건다.</summary>
        public void Init(Transform camera, int followMode, float followSpeed, bool followRot, float attach)
        {
            cam = camera;
            mode = followMode;
            speed = followSpeed;
            followRotation = followRot;
            attachTime = attach;

            if (cam == null) { mode = World; return; }
            localPos = cam.InverseTransformPoint(transform.position);
            localRot = Quaternion.Inverse(cam.rotation) * transform.rotation;

            // 붙이는 모드는 부모 지정으로 끝 — 매 프레임 계산도, 순서 의존도 없다
            if (mode == Camera || mode == Staged)
                transform.SetParent(cam, true);
        }

        void LateUpdate()
        {
            if (cam == null || mode == World || mode == Camera) return;

            age += Time.deltaTime;

            if (mode == Staged)
            {
                // 붙어 있다가 시간이 지나면 월드에 놓는다(월드 포즈는 그대로 유지)
                if (!detached && age >= attachTime)
                {
                    transform.SetParent(null, true);
                    detached = true;
                }
                return;
            }

            // 지연 추종 — 프레임률에 무관한 지수 감쇠로 목표를 쫓는다
            Vector3    tp = cam.TransformPoint(localPos);
            Quaternion tr = cam.rotation * localRot;
            float k = 1f - Mathf.Exp(-Mathf.Max(0.1f, speed) * Time.deltaTime);
            transform.position = Vector3.Lerp(transform.position, tp, k);
            if (followRotation) transform.rotation = Quaternion.Slerp(transform.rotation, tr, k);
        }
    }
}
