using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 스테이지 입구에 보스 머리가 <b>끼는 지점</b>. (보스전_설계 §2 <c>Wedged</c> · §6)
    ///
    /// <para>이 컴포넌트는 <b>상태만</b> 갖는다 — 보스를 붙잡아 두고, 시간을 재고, 부들부들 떨고,
    /// "프레스가 어디까지 내려와야 머리에 닿는가"를 알려 준다. <b>프레스를 직접 만지지 않는다.</b>
    /// 프레스 잠금·플릭 전용 전환·상한 대입은 전부 <see cref="StageEntranceFlow"/>가 소유한다 —
    /// 한 물건을 두 컴포넌트가 만지면 어느 쪽이 마지막에 썼는지로 버그가 난다.</para>
    ///
    /// <para><b>접촉 지점을 마커로 두는 이유</b>: 보스 <see cref="SkinnedMeshRenderer"/> 118개의
    /// <c>localBounds</c>가 전부 <c>size(3,3,3)</c>으로 통일돼 있다(가까이서 컬링돼 사라지는 문제를
    /// 막으려고 그렇게 했다). 그래서 <b>바운즈로는 머리 높이를 잴 수 없다.</b> 대신
    /// <see cref="BossHeadCrush"/>의 자세 캡처 목록에 마커를 하나 끼워 두면, 단계가 올라가 머리가
    /// 납작해질 때 마커도 같이 내려온다 — 프레스가 회차마다 더 깊이 들어가는 것이 공짜로 얻어진다.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public class BossWedgePoint : MonoBehaviour
    {
        [Header("배치")]
        [Tooltip("보스 머리가 와서 멈출 지점. 비우면 이 오브젝트 자신.")]
        public Transform headStop;

        [Header("타이밍")]
        [Tooltip("낑겨 있는 시간(초). 이 안에 프레스로 찍어야 한다. 회차마다 줄이려면 " +
                 "StageEntranceFlow가 배치 시 덮어쓴다(설계 §7).")]
        public float wedgeSeconds = 6f;

        [Header("부들부들 (VR 멀미 — 카메라는 절대 안 흔든다. 보스만 떤다)")]
        [Tooltip("떨림 진폭(월드 m). 보스가 스케일 50이라 작아 보여도 큰 값이다.")]
        public float shakeAmplitude = 0.2f;

        [Tooltip("떨림 빈도(Hz). 높을수록 '부들부들', 낮으면 '흔들흔들'.")]
        public float shakeFrequency = 16f;

        [Tooltip("떨림에 섞을 회전(도). 0이면 평행 이동만.")]
        public float shakeTiltDeg = 0.8f;

        /// <summary>지금 낑겨 있는가.</summary>
        public bool Wedged { get; private set; }

        /// <summary>남은 낑김 시간(초). 낑기지 않았으면 0.</summary>
        public float Remaining { get; private set; }

        /// <summary>낑김 시간이 다 지나 스스로 풀렸다(= 플레이어가 못 찍었다).</summary>
        public event System.Action OnTimedOut;

        /// <summary>보스 머리에서 프레스가 닿을 지점. 단계가 오르면 같이 내려온다(클래스 주석).</summary>
        public Vector3 ContactWorld => _contact != null ? _contact.position : transform.position;

        /// <summary>머리가 멈출 자리.</summary>
        public Transform Stop => headStop != null ? headStop : transform;

        Transform _bossRoot;
        Transform _contact;
        Vector3 _bossHomePos;
        Quaternion _bossHomeRot;
        float _seed;

        /// <summary>
        /// 보스를 이 지점에 물린다. 부르는 쪽이 보스를 이미 자리에 가져다 놨다고 가정한다 —
        /// 이동은 <see cref="BossChase"/>의 몫이고 여기서는 <b>붙잡아 두기만</b> 한다.
        /// </summary>
        /// <param name="bossRoot">떨림을 적용할 보스 루트.</param>
        /// <param name="contactMarker">머리에서 프레스가 닿을 지점. null이면 <see cref="Stop"/>을 쓴다.</param>
        public void Begin(Transform bossRoot, Transform contactMarker)
        {
            if (bossRoot == null) { Debug.LogError($"[낑김] {name}: 보스 루트가 없습니다.", this); return; }

            _bossRoot = bossRoot;
            _contact = contactMarker;

            // 떨림은 홈 자세에 얹는 오프셋이다 — 누적되면 보스가 슬금슬금 떠내려간다.
            _bossHomePos = bossRoot.position;
            _bossHomeRot = bossRoot.rotation;

            // 입구마다 다른 위상으로 떨게 한다(같은 프리팹 4개가 똑같이 떨면 티가 난다).
            _seed = Mathf.Abs(transform.position.x * 0.137f + transform.position.z * 0.311f) % 100f;

            Remaining = wedgeSeconds;
            Wedged = true;
        }

        /// <summary>
        /// 낑김을 끝낸다. 찍혀서 끝났든 시간이 다 됐든 <b>보스 자세는 홈으로 정확히 되돌린다</b> —
        /// 떨림 오프셋이 남으면 다음 상태가 어긋난 자리에서 시작한다.
        /// </summary>
        public void End()
        {
            if (!Wedged) return;
            Wedged = false;
            Remaining = 0f;

            if (_bossRoot != null)
            {
                _bossRoot.position = _bossHomePos;
                _bossRoot.rotation = _bossHomeRot;
            }
            _bossRoot = null;
            _contact = null;
        }

        void LateUpdate()
        {
            if (!Wedged) return;

            Remaining -= Time.deltaTime;
            if (Remaining <= 0f)
            {
                End();
                OnTimedOut?.Invoke();
                return;
            }

            Shake();
        }

        /// <summary>
        /// 홈 자세 + 노이즈. <c>Random</c>이 아니라 <see cref="Mathf.PerlinNoise"/>를 쓴다 —
        /// 프레임마다 독립 난수를 쓰면 60fps에서는 부들부들이지만 30fps로 떨어지면 성격이 확 변한다.
        /// 시간 기반 노이즈는 프레임률과 무관하게 같은 움직임을 낸다.
        /// </summary>
        void Shake()
        {
            if (_bossRoot == null) return;

            float t = (Time.time + _seed) * shakeFrequency;
            Vector3 n = new Vector3(
                Mathf.PerlinNoise(t, 0f) - 0.5f,
                Mathf.PerlinNoise(0f, t) - 0.5f,
                Mathf.PerlinNoise(t, t) - 0.5f) * 2f;

            _bossRoot.position = _bossHomePos + n * shakeAmplitude;
            _bossRoot.rotation = _bossHomeRot * Quaternion.Euler(n * shakeTiltDeg);
        }

        void OnDrawGizmosSelected()
        {
            Transform s = Stop;
            Gizmos.color = new Color(1f, 0.5f, 0.1f, 0.9f);
            Gizmos.DrawWireSphere(s.position, 1f);
            Gizmos.DrawLine(s.position, s.position + Vector3.up * 4f);

            if (Application.isPlaying && Wedged)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawWireSphere(ContactWorld, 0.8f);
            }
        }
    }
}
