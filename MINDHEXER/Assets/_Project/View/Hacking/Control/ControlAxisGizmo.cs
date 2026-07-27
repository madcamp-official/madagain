using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 조종 축 방향 표시 — 장악(파랑)한 대상을 바라보는 동안, 그 대상이 어느 방향으로 움직이는지
    /// 슬롯별로 <b>색이 다른 막대</b>로 보여준다. 슬롯1(좌/우클릭) vs 슬롯2(Shift+좌/우클릭) 구분용.
    ///
    /// ※ 가늘고 긴 직육면체는 <b>임시 표현</b>이다. 화살표 메시·셰이더 연출로 교체 대상(§7).
    /// </summary>
    [DisallowMultipleComponent]
    public class ControlAxisGizmo : MonoBehaviour
    {
        [Tooltip("슬롯1 축(좌클릭 −/우클릭 +) 색.")]
        public Color slot0Color = new Color(0.4f, 1f, 0.3f, 1f);

        [Tooltip("슬롯2 축(Shift+좌클릭 +/Shift+우클릭 −) 색.")]
        public Color slot1Color = new Color(1f, 0.6f, 0.2f, 1f);

        [Tooltip("막대 길이(m).")]
        public float barLength = 3f;

        [Tooltip("막대 두께(m).")]
        public float barThickness = 0.08f;

        IExternalControl _control;
        Hackable _hackable;
        Transform[] _bars;

        void Awake()
        {
            _hackable = GetComponent<Hackable>();
            _control = GetComponent<IExternalControl>();
            if (_control == null) { enabled = false; return; }

            _bars = new Transform[_control.AxisCount];
            for (int i = 0; i < _bars.Length; i++) _bars[i] = MakeBar(i);
            SetVisible(false);
        }

        Transform MakeBar(int slot)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = $"[AxisBar{slot}]";

            // 조준 Raycast를 가로채면 대상 조준이 깨진다 — 콜라이더 반드시 제거.
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);

            var t = go.transform;
            t.SetParent(transform, false);

            var r = go.GetComponent<Renderer>();
            var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            mat.color = slot == 0 ? slot0Color : slot1Color;
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return t;
        }

        void SetVisible(bool on)
        {
            if (_bars == null) return;
            foreach (var b in _bars) if (b != null) b.gameObject.SetActive(on);
        }

        void LateUpdate()
        {
            // 조종 중이면 항상 표시 — 시선과 무관하게 조종되므로(도주하며 조종, §2.5).
            bool show = _hackable != null && _hackable.captureState == CaptureState.Captured;
            SetVisible(show);
            if (!show) return;

            for (int i = 0; i < _bars.Length; i++)
            {
                Vector3 axis = _control.AxisWorld(i);
                if (axis.sqrMagnitude < 0.0001f) continue;

                _bars[i].position = transform.position;
                _bars[i].rotation = Quaternion.LookRotation(axis);            // 로컬 +Z = 축 방향
                _bars[i].localScale = new Vector3(barThickness, barThickness, barLength);
            }
        }
    }
}
