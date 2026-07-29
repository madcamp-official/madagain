using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 조종 실 — 플레이어(손)와 조종 중인 대상을 실시간으로 잇는 선. (기초_설계안 §6.2 마리오네트)
    /// 외부 조종은 빙의와 달리 실이 계속 연결된 채 유지되며, 그 연결이 곧 "지금 이걸 조종 중"이라는 신호다.
    ///
    /// ※ 길이 가변 직육면체는 <b>임시 표현</b>이다. 실 메시·트레일·셰이더로 교체 대상(§2.2·§7).
    /// </summary>
    public class ControlTether : MonoBehaviour
    {
        [Tooltip("해킹 시도 중(패턴 그리는 중) 실 색 — 초록(§7 아직 안 먹음).")]
        public Color hackingColor = new Color(0.4f, 1f, 0.3f, 1f);

        [Tooltip("조종 중(장악 성공) 실 색 — 파랑(§7 내 것).")]
        public Color controlColor = new Color(0.3f, 0.8f, 1f, 1f);

        [Tooltip("실 두께(m).")]
        public float thickness = 0.05f;

        [Tooltip("실이 시작하는 지점의 카메라 기준 오프셋(오른손 위 펫 거미 자리, §2.6).\n" +
                 "★ 거미가 씬에 있으면 originOverride가 이 값을 대신한다 — 이건 거미가 없을 때의 임시 자리다.")]
        public Vector3 handOffset = new Vector3(0.35f, -0.35f, 0.5f);

        [Tooltip("실이 실제로 나오는 지점(거미 방적돌기). SpiderRig가 손목에 있을 때만 채워 넣는다.\n" +
                 "거미가 대상으로 날아가 붙은 뒤에는 비워야 한다 — 안 그러면 실 길이가 0이 된다.\n" +
                 "비어 있으면 from + handOffset 을 쓴다(기존 동작).")]
        public Transform originOverride;

        Transform _bar;
        Material _mat;

        /// <summary>지금 실이 보이는가. 펫 거미가 "발사 자세"를 잡는 신호로 쓴다(§2.6).</summary>
        public bool Active { get; private set; }

        /// <summary>실이 시작하는 지점(월드). 거미가 여기서 실을 뽑는다.</summary>
        public Vector3 StartPoint { get; private set; }

        /// <summary>실 끝(대상) 지점(월드). 거미가 이쪽으로 엉덩이를 겨눈다.</summary>
        public Vector3 EndPoint { get; private set; }

        void EnsureBar()
        {
            if (_bar != null) return;

            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "[TetherBar]";

            // 조준 Raycast를 가로채면 안 된다 — 콜라이더 제거.
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);

            var r = go.GetComponent<Renderer>();
            _mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            r.sharedMaterial = _mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            _bar = go.transform;
            _bar.gameObject.SetActive(false);
        }

        /// <summary>매 프레임 호출. target이 null이면 실을 감춘다. captured=true면 파랑, false면 초록.</summary>
        public void UpdateTether(Transform from, Transform target, bool captured)
        {
            EnsureBar();

            if (from == null || target == null) { _bar.gameObject.SetActive(false); Active = false; return; }
            _mat.color = captured ? controlColor : hackingColor;

            Vector3 a = originOverride != null ? originOverride.position : from.TransformPoint(handOffset);
            Vector3 b = target.position;
            Vector3 d = b - a;
            float len = d.magnitude;
            if (len < 0.01f) { _bar.gameObject.SetActive(false); Active = false; return; }

            Active = true;
            StartPoint = a;
            EndPoint = b;

            _bar.gameObject.SetActive(true);
            _bar.position = a + d * 0.5f;
            _bar.rotation = Quaternion.LookRotation(d);           // 로컬 +Z를 대상 쪽으로
            _bar.localScale = new Vector3(thickness, thickness, len);
        }
    }
}
