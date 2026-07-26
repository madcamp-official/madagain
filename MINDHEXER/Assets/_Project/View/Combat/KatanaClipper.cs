using UnityEngine;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// 찌르기로 적에 박힌 만큼 칼끝을 잘라 "관통해 안 보이는" 연출.
    ///
    /// 실제 거리(LungeStopDistance)는 밸런스상 못 줄이므로, 시각적으로만 칼날을 잘라
    /// 적에 박힌 것처럼 보이게 한다. 로봇 칼이라 단면이 잘려도 자연스럽다.
    ///
    /// Precog/KatanaClip 셰이더의 _ClipX(그릴 비율 0~1)를 찌르기 진행도에 맞춰 낮춘다.
    ///   찌르기 아님 → 1(전체) · 박히는 중 → 감소 · 최대 도달 → clipMax.
    ///
    /// ★ 순수 뷰 — Sim/예지에 전혀 영향 없음. 셰이더가 KatanaClip이 아니면 조용히 논다.
    ///
    /// 단면 위치에 찌르기 궤적 이펙트를 띄워 "칼이 파고드는" 인상을 준다.
    /// 찌를 때 화면이 흔들려 단면이 미세하게 움직이므로, 이펙트는 찌르기 시작 순간
    /// 한 번만 그 지점에 띄우고 <b>고정</b>한다(순간이동급이라 위치 차이는 무시할 만하다).
    /// </summary>
    [DefaultExecutionOrder(50)]   // SwordView가 포즈를 적용한 뒤 돈다
    public class KatanaClipper : MonoBehaviour
    {
        public static KatanaClipper Instance { get; private set; }

        [Tooltip("최대로 박혔을 때 남기는 비율 — 0.55면 칼끝 45%가 안 보인다")]
        [Range(0.2f, 1f)] public float clipMax = 0.55f;
        [Tooltip("들어갈 때 속도(초당). 클수록 빨리 잘린다")]
        public float clipInSpeed = 8f;
        [Tooltip("빠질 때 속도(초당). 찌르기 끝나고 원래대로")]
        public float clipOutSpeed = 4f;

        [Tooltip("단면에 궤적 이펙트를 띄운다")]
        public bool  spawnCutFx = true;
        [Tooltip("이펙트를 띄울 슬롯 이름(SlashFxDriver)")]
        public string cutFxSlot = "찌르기";
        [Tooltip("클리핑을 유지할 포즈 접두어 — 이 시퀀스가 재생되는 동안 칼이 잘려 있다")]
        public string thrustPosePrefix = "thrust1_";

        static readonly int IdClip = Shader.PropertyToID("_ClipX");
        static readonly int IdAxis = Shader.PropertyToID("_BladeAxis");
        static readonly int IdMin  = Shader.PropertyToID("_BladeMin");
        static readonly int IdMax  = Shader.PropertyToID("_BladeMax");

        Renderer bladeRend;
        Transform katana;
        MaterialPropertyBlock mpb;
        float clip = 1f;
        float bladeMin, bladeMax;   // 칼 로컬 X 범위(측정)
        bool  measured;

        bool prevStabbing;          // 직전 프레임에 가리는 중이었나(이펙트 1회 발동용)
        SwordSlash cutFx;           // 단면에 띄운 이펙트(고정)

        void Awake() { Instance = this; }

        Renderer FindBlade()
        {
            if (bladeRend != null) return bladeRend;
            var cam = Main.Instance != null ? Main.Instance.Cam : Camera.main;
            if (cam == null) return null;
            katana = null;
            foreach (var t in cam.GetComponentsInChildren<Transform>(true))
                if (t.name == "Katana") { katana = t; break; }
            if (katana == null) { var go = GameObject.Find("Katana"); if (go) katana = go.transform; }
            if (katana == null) return null;
            bladeRend = katana.GetComponentInChildren<Renderer>();
            return bladeRend;
        }

        void MeasureBlade(Renderer r)
        {
            if (measured) return;
            var mf = r.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return;
            Bounds lb = mf.sharedMesh.bounds;
            bladeMin = lb.center.x - lb.extents.x;   // 손잡이 쪽 로컬 X
            bladeMax = lb.center.x + lb.extents.x;   // 칼끝 쪽 로컬 X (측정: 칼날 = 로컬 +X)
            if (mpb == null) mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);
            mpb.SetVector(IdAxis, new Vector4(1f, 0f, 0f, 0f));
            mpb.SetFloat(IdMin, bladeMin);
            mpb.SetFloat(IdMax, bladeMax);
            r.SetPropertyBlock(mpb);
            measured = true;
        }

        /// <summary>현재 clip 비율에 해당하는 단면의 월드 위치.</summary>
        Vector3 CutWorldPos()
        {
            // 로컬 X = min + clip*(max-min) 지점이 단면
            float localX = bladeMin + clip * (bladeMax - bladeMin);
            return katana.TransformPoint(new Vector3(localX, 0f, 0f));
        }

        void LateUpdate()
        {
            var main = Main.Instance;
            if (main == null) return;
            var r = FindBlade();
            if (r == null) return;
            MeasureBlade(r);

            ref readonly PlayerCombatState c = ref main.World.player.combat;
            byte lg = c.lungePhase;

            // ★ sim의 lungePhase(0틱이라 순식간에 끝남)가 아니라, 찌르기 <b>포즈 애니메이션</b>이
            //   기본포즈로 돌아갈 때까지 가린다. 포즈는 thrust1_ 시퀀스가 끝(기본포즈 복귀)나야 풀린다.
            //   찌르기→기본포즈가 계단(순간이동)이라, 풀리는 것도 그 순간 뚝 돌아와 자연스럽다.
            var pp = PosePlayer.Instance;
            bool stabbing = (lg != CombatConfig.LgNone)
                         || (pp != null && pp.IsPlayingPrefix(thrustPosePrefix));

            float target = stabbing ? clipMax : 1f;
            float speed  = stabbing ? clipInSpeed : clipOutSpeed;
            clip = Mathf.MoveTowards(clip, target, speed * Time.deltaTime);

            if (mpb == null) mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);
            mpb.SetFloat(IdClip, clip);
            r.SetPropertyBlock(mpb);

            // ── 단면 이펙트: 찌르기 시작(안 가림→가림 전이) 순간 1회, 그 지점에 고정 ──
            if (spawnCutFx && !prevStabbing && stabbing)
            {
                var fxDrv = SlashFxDriver.Instance;
                if (fxDrv != null && katana != null)
                {
                    // 단면은 clipMax까지 박힐 것이므로 그 최종 지점에 띄운다.
                    float localX = bladeMin + clipMax * (bladeMax - bladeMin);
                    Vector3 pos = katana.TransformPoint(new Vector3(localX, 0f, 0f));
                    Quaternion rot = Quaternion.LookRotation(katana.TransformDirection(Vector3.right), Vector3.up);
                    cutFx = fxDrv.SpawnAt(cutFxSlot, pos, rot);
                }
            }
            prevStabbing = stabbing;
        }

        /// <summary>뷰모델이 새로 만들어지면 참조를 버린다.</summary>
        public void Forget()
        {
            bladeRend = null; katana = null; measured = false; clip = 1f;
            prevStabbing = false;
        }
    }

    /// <summary>Play 시 자동 부착.</summary>
    public static class KatanaClipperBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Object.FindFirstObjectByType<KatanaClipper>() == null)
                new GameObject("[KatanaClipper]").AddComponent<KatanaClipper>();
        }
    }
}
