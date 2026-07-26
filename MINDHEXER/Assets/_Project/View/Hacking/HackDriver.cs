using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 스캐폴딩 배선 데모. 카메라 중앙 시선 Raycast로 Hackable 조준 → controlType 색 하이라이트 →
    /// Space 홀드로 해킹(미니게임 자리표시자: 홀드 지속 시 성공) → HackContext 컨텍스트 전환 → Q 복귀.
    /// 실제 게임플레이(패턴 미니게임·PD 조종·시점 전환)는 아직 없음. (기초_설계안 §2.5·§6·§7)
    /// </summary>
    [RequireComponent(typeof(HackContext))]
    public class HackDriver : MonoBehaviour
    {
        [Tooltip("시선 Raycast 기준 카메라. 비우면 Camera.main.")]
        public Camera cam;

        [Tooltip("미니게임 자리표시자: 이만큼 홀드 지속하면 해킹 성공 처리(초).")]
        public float hackSuccessHold = 0.6f;

        static readonly Color ExternalColor  = new Color(0.4f, 1f, 0.3f);   // 연두 (외부 조종)
        static readonly Color ViewEntryColor = new Color(0f, 0.8f, 0.85f);  // 청록 (시점 진입)
        static readonly Color StunColor      = new Color(1f, 0.85f, 0.1f);  // 노랑 (보스 스턴)

        /// <summary>입력 출처. 기본 PC(키보드/마우스). VR에선 GameBoot이 네트워크 소스로 교체.</summary>
        public IHexInputSource Source = new PcHexInputSource();
        HackContext _ctx;
        Hackable _highlighted;
        MaterialPropertyBlock _mpb;
        int _baseColorId;
        float _hackTimer;

        void Awake()
        {
            _ctx = GetComponent<HackContext>();
            if (cam == null) cam = Camera.main;
            _mpb = new MaterialPropertyBlock();
            _baseColorId = Shader.PropertyToID("_BaseColor");
        }

        void Update()
        {
            HexInput input = Source.Poll(_ctx.Current);

            bool canAim = _ctx.Current == ControlContext.Player || _ctx.Current == ControlContext.ViewEntry;
            Hackable aimed = canAim ? Raycast() : null;
            UpdateHighlight(aimed);

            switch (_ctx.Current)
            {
                case ControlContext.Player:
                case ControlContext.ViewEntry:
                    if (input.hackHeld && aimed != null)
                    {
                        _ctx.BeginHacking(aimed);
                        _hackTimer = 0f;
                        Debug.Log($"[Hack] 시작: {aimed.kind} ({aimed.controlType}, {aimed.PatternLineCount}선)");
                    }
                    break;

                case ControlContext.Hacking:
                    if (input.hackHeld)
                    {
                        _hackTimer += Time.deltaTime;
                        if (_hackTimer >= hackSuccessHold)
                        {
                            _ctx.OnPatternSucceeded();
                            Debug.Log($"[Hack] 성공 → 컨텍스트 = {_ctx.Current}");
                        }
                    }
                    else
                    {
                        _ctx.OnPatternCancelled();
                        Debug.Log($"[Hack] 취소 → 컨텍스트 = {_ctx.Current}");
                    }
                    break;
            }

            if (input.returnToBody && _ctx.Current != ControlContext.Player)
            {
                _ctx.ReturnToBody();
                Debug.Log("[Hack] 복귀(Q) → Player");
            }
        }

        Hackable Raycast()
        {
            if (cam == null) return null;
            var ray = new Ray(cam.transform.position, cam.transform.forward);
            if (Physics.Raycast(ray, out RaycastHit hit, 100f))
            {
                var h = hit.collider.GetComponentInParent<Hackable>();
                if (h != null && hit.distance <= h.hackRange) return h;
            }
            return null;
        }

        void UpdateHighlight(Hackable target)
        {
            if (_highlighted == target) return;
            if (_highlighted != null) SetTint(_highlighted, null);
            _highlighted = target;
            if (_highlighted != null) SetTint(_highlighted, ColorFor(_highlighted.controlType));
        }

        Color ColorFor(ControlType t)
        {
            if (t == ControlType.ViewEntry) return ViewEntryColor;
            if (t == ControlType.Stun) return StunColor;
            return ExternalColor;
        }

        void SetTint(Hackable h, Color? c)
        {
            Renderer[] rends = (h.glowRenderers != null && h.glowRenderers.Length > 0)
                ? h.glowRenderers : h.GetComponentsInChildren<Renderer>();
            foreach (var r in rends)
            {
                if (r == null) continue;
                r.GetPropertyBlock(_mpb);
                if (c.HasValue) _mpb.SetColor(_baseColorId, c.Value);
                else _mpb.Clear();
                r.SetPropertyBlock(_mpb);
            }
        }
    }
}
