using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 스폰 순간 마커(배관) 큐브가 **출구 방향(로컬 +Z)으로 꿀렁** 늘어났다 돌아오는 연출.
    /// View 전용 — 스케일만 만지므로 sim·결정론·충돌에 영향 없다.
    /// (나중에 실제 배관 에셋 애니메이션으로 교체할 자리.)
    /// </summary>
    [DisallowMultipleComponent]
    public class SpawnPipeFx : MonoBehaviour
    {
        [Tooltip("최대로 늘어나는 비율(1.5 = 최대 2.5배).")]
        public float amount = 1.5f;

        [Tooltip("연출 길이(초).")]
        public float duration = 0.55f;

        [Tooltip("꿀렁이는 횟수(진동 수). 클수록 잘게 떨린다.")]
        public float wobbles = 2f;

        Vector3 baseScale;
        bool    captured;
        float   t = -1f;   // 음수 = 재생 중 아님

        /// <summary>이 마커에 연출 재생(컴포넌트가 없으면 자동 부착).</summary>
        public static void Play(Transform marker)
        {
            if (marker == null) return;
            SpawnPipeFx fx = marker.GetComponent<SpawnPipeFx>();
            if (fx == null) fx = marker.gameObject.AddComponent<SpawnPipeFx>();
            fx.Trigger();
        }

        public void Trigger()
        {
            if (!captured) { baseScale = transform.localScale; captured = true; }
            t = 0f;
        }

        void Update()
        {
            if (t < 0f) return;

            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / Mathf.Max(0.01f, duration));

            // 감쇠 사인 = 튀어나갔다 들어왔다 하며 점점 잦아드는 "꿀렁꿀렁"
            float k = 1f + amount * Mathf.Sin(u * Mathf.PI * 2f * wobbles) * (1f - u);

            Vector3 s = baseScale;
            s.z = baseScale.z * k;    // 출구 방향(로컬 Z)으로만 늘어남
            transform.localScale = s;

            if (u >= 1f) { transform.localScale = baseScale; t = -1f; }
        }

        void OnDisable()
        {
            if (captured) transform.localScale = baseScale;   // 도중에 꺼져도 원복
            t = -1f;
        }
    }
}
