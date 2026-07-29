using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 손가락 포즈를 슬라이더 몇 개로 조절한다. (Precog에서 포팅, 변경 없음)
    ///
    /// [ExecuteAlways]라 Play를 누르지 않아도 에디터에서 값을 바꾸는 즉시 반영된다.
    /// 뼈를 하나씩 돌릴 필요 없이 손가락별 슬라이더로 굽힘 정도만 조절하면 된다.
    ///
    /// 동작: 기준 포즈(rest)를 캡처해 두고, 매 LateUpdate에 rest 기준으로 각도를 더한다(additive).
    ///
    /// 리그마다 손가락이 굽는 축이 달라서 curlAxis를 노출했다. 엉뚱한 방향으로 꺾이면 축만 바꾸면 된다.
    /// 뼈 탐색은 이름에 "Thumb/Index/Middle/Ring/Pinky"가 포함되고 "1"/"2"/"3"으로 끝나는지로 판정하므로,
    /// 우리 리그(R_Thumb1 등)에도 그대로 맞는다.
    /// </summary>
    [ExecuteAlways]
    public class FingerPoser : MonoBehaviour
    {
        [Header("대상")]
        [Tooltip("손 뼈. 비우면 자기 자신부터 이름으로 찾음")]
        public Transform handRoot;

        [Header("굽힘 (0=편 손, 1=꽉 쥠)")]
        [Range(0f, 1f)] public float grip;
        [Range(0f, 1f)] public float thumb;
        [Range(0f, 1f)] public float index;
        [Range(0f, 1f)] public float middle;
        [Range(0f, 1f)] public float ring;
        [Range(0f, 1f)] public float pinky;

        [Header("각도 설정")]
        [Tooltip("굽는 축(로컬). 엉뚱하게 꺾이면 이걸 바꾸십시오")]
        public Vector3 curlAxis = new Vector3(0f, 0f, 1f);
        public float maxCurlDeg = 70f;
        public Vector3 jointWeights = new Vector3(1f, 1f, 0.85f);
        public float thumbScale = 0.6f;

        [Header("벌림")]
        [Range(-1f, 1f)] public float spread;
        public Vector3 spreadAxis = new Vector3(0f, 1f, 0f);
        public float maxSpreadDeg = 12f;

        [Header("절차 그립 (순간 — 확 쥐었다 풀림)")]
        public float gripStiff = 200f;
        public float gripDamp  = 16f;
        public float gripSpring;
        float gripVel;

        [Header("지속 그립 (조준·상호작용 등 상태에 따라 유지)")]
        [Tooltip("ViewmodelMotion이 매 프레임 밀어넣는 값. 부드럽게 추적된다")]
        public float sustainTarget;
        public float sustainSpeed = 8f;
        public float sustainGrip;

        public static FingerPoser Instance { get; private set; }

        public void PulseGrip(float amount) => gripVel += amount * gripStiff * 0.05f;
        public void SetSustain(float amount) => sustainTarget = Mathf.Clamp01(amount);
        public void ResetGrip() { gripSpring = 0f; gripVel = 0f; sustainTarget = 0f; sustainGrip = 0f; }

        static readonly string[] FingerKeys = { "Thumb", "Index", "Middle", "Ring", "Pinky" };
        const int Joints = 3;

        Transform[,] chain;
        Quaternion[,] rest;
        bool ready;

        void OnEnable()  { Instance = this; Rebuild(); }
        void OnValidate(){ if (!ready) Rebuild(); }

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

        public Quaternion[] ExportRest()
        {
            if (!ready) Rebuild();
            var outArr = new Quaternion[FingerKeys.Length * Joints];
            for (int f = 0; f < FingerKeys.Length; f++)
                for (int j = 0; j < Joints; j++)
                    outArr[f * Joints + j] = rest != null ? rest[f, j] : Quaternion.identity;
            return outArr;
        }

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

        float CurlOf(int finger)
        {
            float extra = finger switch
            {
                0 => thumb, 1 => index, 2 => middle, 3 => ring, _ => pinky
            };
            return Mathf.Clamp01(grip + extra + gripSpring + sustainGrip);
        }

        void LateUpdate()
        {
            if (!ready) return;

            float dt = Application.isPlaying ? Time.deltaTime : 0f;
            if (dt > 0f)
            {
                gripVel += (-gripStiff * gripSpring - gripDamp * gripVel) * dt;
                gripSpring += gripVel * dt;
                sustainGrip = Mathf.Lerp(sustainGrip, sustainTarget, 1f - Mathf.Exp(-sustainSpeed * dt));
            }
            Vector3 axis = curlAxis.sqrMagnitude < 1e-6f ? Vector3.forward : curlAxis.normalized;
            Vector3 sAxis = spreadAxis.sqrMagnitude < 1e-6f ? Vector3.up : spreadAxis.normalized;

            for (int f = 0; f < FingerKeys.Length; f++)
            {
                float curl = CurlOf(f);
                if (f == 0) curl *= thumbScale;
                float spreadT = (f - 2f) / 2f;
                float spreadDeg = spread * maxSpreadDeg * spreadT;

                for (int j = 0; j < Joints; j++)
                {
                    Transform t = chain[f, j];
                    if (t == null) continue;

                    float w = j == 0 ? jointWeights.x : j == 1 ? jointWeights.y : jointWeights.z;
                    Quaternion q = Quaternion.AngleAxis(curl * maxCurlDeg * w, axis);
                    if (j == 0 && Mathf.Abs(spreadDeg) > 0.01f)
                        q = Quaternion.AngleAxis(spreadDeg, sAxis) * q;

                    t.localRotation = rest[f, j] * q;
                }
            }
        }
    }
}
