using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 손가락 포즈를 슬라이더 몇 개로 조절한다. ★ 1인칭 화면을 보며 실시간 튜닝하는 용도.
    ///
    /// [ExecuteAlways]라 <b>Play를 누르지 않아도</b> 에디터에서 값을 바꾸는 즉시 Game 뷰에 반영된다.
    /// 뼈 40개를 하나씩 돌릴 필요 없이 손가락별 슬라이더로 굽힘 정도만 조절하면 된다.
    ///
    /// 동작: 기준 포즈(rest)를 캡처해 두고, 매 LateUpdate에 rest 기준으로 각도를 더한다.
    ///   → 누적되지 않고, Animator가 클립 포즈를 쓴 뒤에 얹히므로 additive로 동작한다.
    ///
    /// 리그마다 손가락이 굽는 축이 달라서 <see cref="curlAxis"/>를 노출했다.
    /// 엉뚱한 방향으로 꺾이면 축만 바꾸면 된다(대개 (0,0,1) 또는 (1,0,0)).
    /// </summary>
    [ExecuteAlways]
    public class FingerPoser : MonoBehaviour
    {
        [Header("대상")]
        [Tooltip("손 뼈(예: mixamorig:RightHand). 비우면 자기 자신부터 이름으로 찾음")]
        public Transform handRoot;

        [Header("굽힘 (0=편 손, 1=꽉 쥠)")]
        [Range(0f, 1f)] public float grip;       // 전체 동시
        [Range(0f, 1f)] public float thumb;      // 손가락별 추가분
        [Range(0f, 1f)] public float index;
        [Range(0f, 1f)] public float middle;
        [Range(0f, 1f)] public float ring;
        [Range(0f, 1f)] public float pinky;

        [Header("각도 설정")]
        [Tooltip("굽는 축(로컬). 엉뚱하게 꺾이면 이걸 바꾸십시오")]
        public Vector3 curlAxis = new Vector3(0f, 0f, 1f);
        [Tooltip("굽힘 1.0일 때 마디당 각도(도)")]
        public float maxCurlDeg = 70f;
        [Tooltip("마디별 비중 (뿌리·중간·끝)")]
        public Vector3 jointWeights = new Vector3(1f, 1f, 0.85f);
        [Tooltip("엄지는 방향이 달라 따로 배율")]
        public float thumbScale = 0.6f;

        [Header("벌림")]
        [Range(-1f, 1f)] public float spread;
        public Vector3 spreadAxis = new Vector3(0f, 1f, 0f);
        public float maxSpreadDeg = 12f;

        [Header("절차 그립 (타격 순간 확 쥐었다 풀림)")]
        [Tooltip("클수록 빨리 조여지고 빨리 풀림")]
        public float gripStiff = 200f;
        public float gripDamp  = 16f;
        [Tooltip("현재 스프링 값(읽기용). PulseGrip으로 튕긴다")]
        public float gripSpring;
        float gripVel;

        [Header("지속 그립 (달리기·공중 등 상태에 따라 계속 유지되는 쥠)")]
        [Tooltip("ViewmodelMotion이 매 프레임 밀어넣는 값. 부드럽게 추적된다")]
        public float sustainTarget;
        [Tooltip("지속 그립 추적 속도 (클수록 빨리 반응)")]
        public float sustainSpeed = 8f;
        public float sustainGrip;      // 실제 적용되는 값(읽기용)

        public static FingerPoser Instance { get; private set; }   // 콘솔 진입점

        /// <summary>타격 순간처럼 확 쥐었다 풀리게 한다. amount = 세기(0.2~1 권장).</summary>
        public void PulseGrip(float amount) => gripVel += amount * gripStiff * 0.05f;

        /// <summary>달리기·공중처럼 "그 상태인 동안 계속" 쥐고 있게 한다.</summary>
        public void SetSustain(float amount) => sustainTarget = Mathf.Clamp01(amount);

        /// <summary>절차 그립을 즉시 0으로.</summary>
        public void ResetGrip() { gripSpring = 0f; gripVel = 0f; sustainTarget = 0f; sustainGrip = 0f; }

        static readonly string[] FingerKeys = { "Thumb", "Index", "Middle", "Ring", "Pinky" };
        const int Joints = 3;   // 1·2·3 마디(4는 끝점이라 회전 불필요)

        Transform[,] chain;        // [손가락, 마디]
        Quaternion[,] rest;        // 기준 포즈
        bool ready;

        void OnEnable()  { Instance = this; Rebuild(); }
        void OnValidate(){ if (!ready) Rebuild(); }

        /// <summary>뼈를 다시 찾고 현재 포즈를 기준으로 캡처.</summary>
        [ContextMenu("본 다시 찾기 + 기준 포즈 캡처")]
        public void Rebuild()
        {
            Transform root = handRoot != null ? handRoot : transform;
            chain = new Transform[FingerKeys.Length, Joints];
            rest  = new Quaternion[FingerKeys.Length, Joints];

            var all = root.GetComponentsInChildren<Transform>(true);
            int found = 0;
            for (int f = 0; f < FingerKeys.Length; f++)
                for (int j = 0; j < Joints; j++)
                {
                    string key = FingerKeys[f];
                    string want = (j + 1).ToString();
                    foreach (var t in all)
                    {
                        string n = t.name;
                        if (n.IndexOf(key, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                        if (!n.EndsWith(want)) continue;
                        chain[f, j] = t;
                        rest[f, j]  = t.localRotation;
                        found++;
                        break;
                    }
                }
            ready = found > 0;
            if (!ready) Debug.LogWarning($"[FingerPoser] 손가락 뼈를 못 찾았습니다. handRoot를 손 뼈로 지정하십시오. (검사 대상 {all.Length}개)");
        }

        /// <summary>현재 화면의 포즈를 새 기준으로 삼는다(그립 포즈를 잡은 뒤 누르십시오).</summary>
        [ContextMenu("현재 포즈를 기준으로 고정")]
        public void CaptureRest()
        {
            if (!ready) { Rebuild(); return; }
            for (int f = 0; f < FingerKeys.Length; f++)
                for (int j = 0; j < Joints; j++)
                    if (chain[f, j] != null) rest[f, j] = chain[f, j].localRotation;
            grip = thumb = index = middle = ring = pinky = 0f;
            spread = 0f;
        }

        // ── 프리셋 저장/복원 (기본 그립) ──
        /// <summary>기준 포즈를 1차원 배열로 내보낸다(5손가락 × 3마디 = 15).</summary>
        public Quaternion[] ExportRest()
        {
            if (!ready) Rebuild();
            var outArr = new Quaternion[FingerKeys.Length * Joints];
            for (int f = 0; f < FingerKeys.Length; f++)
                for (int j = 0; j < Joints; j++)
                    outArr[f * Joints + j] = rest != null ? rest[f, j] : Quaternion.identity;
            return outArr;
        }

        /// <summary>기준 포즈를 되돌리고, 뼈도 즉시 그 포즈로 세팅한다.</summary>
        public void ImportRest(Quaternion[] data)
        {
            if (data == null || data.Length < FingerKeys.Length * Joints) return;
            if (!ready) Rebuild();
            for (int f = 0; f < FingerKeys.Length; f++)
                for (int j = 0; j < Joints; j++)
                {
                    rest[f, j] = data[f * Joints + j];
                    if (chain[f, j] != null) chain[f, j].localRotation = rest[f, j];
                }
            grip = thumb = index = middle = ring = pinky = 0f;
            spread = 0f;
            ResetGrip();
        }

        /// <summary>손가락별 굽힘 값(전체 grip + 개별 추가분).</summary>
        float CurlOf(int finger)
        {
            float extra = finger switch
            {
                0 => thumb, 1 => index, 2 => middle, 3 => ring, _ => pinky
            };
            return Mathf.Clamp01(grip + extra + gripSpring + sustainGrip);   // 절차 그립(순간+지속)을 더함
        }

        void LateUpdate()
        {
            if (!ready) return;

            // 절차 그립 스프링(목표 0으로 감쇠) — PulseGrip으로 튕기면 확 쥐었다 풀린다
            float dt = Application.isPlaying ? Time.deltaTime : 0f;
            if (dt > 0f)
            {
                gripVel += (-gripStiff * gripSpring - gripDamp * gripVel) * dt;
                gripSpring += gripVel * dt;
                // 지속 그립은 목표를 향해 부드럽게 추적(달리기 시작/정지에 뚝 끊기지 않게)
                sustainGrip = Mathf.Lerp(sustainGrip, sustainTarget, 1f - Mathf.Exp(-sustainSpeed * dt));
            }
            Vector3 axis = curlAxis.sqrMagnitude < 1e-6f ? Vector3.forward : curlAxis.normalized;
            Vector3 sAxis = spreadAxis.sqrMagnitude < 1e-6f ? Vector3.up : spreadAxis.normalized;

            for (int f = 0; f < FingerKeys.Length; f++)
            {
                float curl = CurlOf(f);
                if (f == 0) curl *= thumbScale;                  // 엄지는 방향이 달라 약하게
                // 손가락을 부채꼴로 벌림(검지=+, 소지=-)
                float spreadT = (f - 2f) / 2f;                   // -1 … +1
                float spreadDeg = spread * maxSpreadDeg * spreadT;

                for (int j = 0; j < Joints; j++)
                {
                    Transform t = chain[f, j];
                    if (t == null) continue;

                    float w = j == 0 ? jointWeights.x : j == 1 ? jointWeights.y : jointWeights.z;
                    Quaternion q = Quaternion.AngleAxis(curl * maxCurlDeg * w, axis);
                    if (j == 0 && Mathf.Abs(spreadDeg) > 0.01f)   // 벌림은 뿌리 마디에만
                        q = Quaternion.AngleAxis(spreadDeg, sAxis) * q;

                    t.localRotation = rest[f, j] * q;             // 누적 없이 항상 기준에서 계산
                }
            }
        }
    }
}
