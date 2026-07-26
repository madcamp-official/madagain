using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 해킹 루프 배선. 카메라 중앙 시선 Raycast로 Hackable 조준 → controlType 색 하이라이트 →
    /// Space 홀드로 해킹 시작 → 점 패턴 미니게임(§2.4) → 성공 시 HackContext 컨텍스트 전환 → Q 복귀.
    /// 해킹 중 시점 고정(§2.5). 성공 후 조종/빙의(F3·F4)는 미구현. (기초_설계안 §2.5·§6·§7)
    /// </summary>
    [RequireComponent(typeof(HackContext))]
    public class HackDriver : MonoBehaviour
    {
        [Tooltip("시선 Raycast 기준 카메라. 비우면 Camera.main.")]
        public Camera cam;

        [Tooltip("점 패턴 미니게임(§2.4). 비우면 자동 추가.")]
        public PatternMinigame minigame;

        static readonly Color ExternalColor  = new Color(0.4f, 1f, 0.3f);   // 연두 (외부 조종)
        static readonly Color ViewEntryColor = new Color(0f, 0.8f, 0.85f);  // 청록 (시점 진입)
        static readonly Color StunColor      = new Color(1f, 0.85f, 0.1f);  // 노랑 (보스 스턴)

        /// <summary>입력 출처. 기본 PC(키보드/마우스). VR에선 GameBoot이 네트워크 소스로 교체.</summary>
        public IHexInputSource Source = new PcHexInputSource();
        HackContext _ctx;
        FreeLookController _freeLook;   // 해킹 중 시점 고정용(§2.5)
        bool _lookWasEnabled;
        Hackable _highlighted;
        MaterialPropertyBlock _mpb;
        int _baseColorId;

        void Awake()
        {
            _ctx = GetComponent<HackContext>();
            if (cam == null) cam = Camera.main;
            if (minigame == null) minigame = GetComponent<PatternMinigame>() ?? gameObject.AddComponent<PatternMinigame>();
            if (minigame.ui == null) minigame.ui = GetComponent<PatternUI>() ?? gameObject.AddComponent<PatternUI>();
            _freeLook = GetComponentInParent<FreeLookController>();
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
                        aimed.captureState = CaptureState.Hacking;
                        minigame.Begin(aimed.PatternLineCount);
                        FreezeLook(true);
                        Debug.Log($"[Hack] 시작: {aimed.kind} ({aimed.controlType}, {aimed.PatternLineCount}선)");
                    }
                    break;

                case ControlContext.Hacking:
                    PatternState st = minigame.Tick(input.strokeDir, input.hackHeld);
                    if (st == PatternState.Succeeded) OnPatternDone(true);
                    else if (st == PatternState.Failed || st == PatternState.Cancelled) OnPatternDone(false);
                    break;
            }

            if (input.returnToBody && _ctx.Current != ControlContext.Player)
            {
                _ctx.ReturnToBody();
                Debug.Log("[Hack] 복귀(Q) → Player");
            }
        }

        void OnPatternDone(bool success)
        {
            Hackable target = _ctx.ActiveTarget;
            FreezeLook(false);
            if (success)
            {
                if (target != null)
                    target.captureState = target.controlType == ControlType.ExternalControl
                        ? CaptureState.Captured : CaptureState.None;
                _ctx.OnPatternSucceeded();
                Debug.Log($"[Hack] 성공 → 컨텍스트 = {_ctx.Current}");
            }
            else
            {
                if (target != null) target.captureState = CaptureState.None;
                _ctx.OnPatternCancelled();
                Debug.Log($"[Hack] 실패/취소 → 컨텍스트 = {_ctx.Current}");
            }
        }

        // 해킹 중 마우스가 패턴을 그리므로 시점(FreeLook)을 잠깐 멈춘다(§2.5). 끝나면 이전 상태 복원.
        void FreezeLook(bool freeze)
        {
            if (_freeLook == null) return;
            if (freeze) { _lookWasEnabled = _freeLook.lookEnabled; _freeLook.lookEnabled = false; }
            else _freeLook.lookEnabled = _lookWasEnabled;
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
