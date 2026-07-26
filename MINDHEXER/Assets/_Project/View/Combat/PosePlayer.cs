using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 포즈 JSON들을 순서대로 이어 재생한다(개발용 미리보기).
    /// 클립을 굽기 전에 "이 포즈들이 이어지면 어떤 동작인가"를 콘솔에서 바로 확인하는 용도.
    ///
    /// 재생 중에는 자세를 건드리는 것들(Animator·HandIK·FingerPoser)을 잠시 꺼서
    /// 서로 덮어쓰지 않게 한다. 정지하면 원래대로 복구.
    ///
    /// ※ Assets 폴더에서 직접 읽으므로 에디터 전용이다(빌드에선 동작하지 않음).
    /// </summary>
    public class PosePlayer : MonoBehaviour
    {
        public const string PoseDir = "Assets/_Project/Poses";

        public static PosePlayer Instance { get; private set; }

        Transform root;
        readonly List<PoseFile> seq = new List<PoseFile>();
        float elapsed;
        bool  playing, loop;

        // ── 튜닝 (F2 패널에서 실시간 조절) ──
        [Header("재생 타이밍")]
        [Tooltip("포즈 하나 넘어가는 시간(초)")]
        public float segTime = 0.2f;
        [Tooltip("마지막 포즈에 도달했을 때 멈춰 있는 시간(초) — 임팩트 정지")]
        public float holdLastPose = 0.12f;

        [Header("포즈 사이 이징")]
        [Tooltip("켜면 포즈 사이를 스프링(탄성)으로, 끄면 선형으로 잇는다")]
        public bool springBetweenPoses = true;
        [Tooltip("감쇠 — 클수록 출렁임이 빨리 잦아듦")]
        public float springDamp = 6f;
        [Tooltip("진동수 — 클수록 빠르게 튕김")]
        public float springFreq = 18f;

        [Header("복귀")]
        [Tooltip("끝나면 이 이름의 포즈로 복귀(시퀀스 끝에 자동 추가). 이징은 다른 구간과 동일")]
        public string basePoseName = "기본포즈";
        [Tooltip("재생 중이 아닐 때(평상시) 기본포즈를 유지한다")]
        public bool holdBaseWhenIdle = true;
        [Tooltip("기본포즈로 돌아갈 때 블렌드 없이 뚝 끊는다(순간이동)")]
        public bool snapReturn = true;

        bool idleHoldOn = true;   // Release()로 꺼짐 — 다음 재생 때 다시 켜진다
        bool idleApplied;         // 기본포즈를 이미 적용했는가(매 프레임 재적용 방지)
        PoseFile baseCache;

        [Header("세그먼트별 시간 (F3 패널이 편집. 비어있으면 segTime 사용)")]
        public List<float> segTimes = new List<float>();

        // ── 구간별 이징 ──
        public const int EzLinear = 0, EzIn = 1, EzOut = 2, EzInOut = 3, EzSpring = 4, EzSnap = 5;
        public static readonly string[] EaseNames = { "선형", "이즈인", "이즈아웃", "인아웃", "스프링", "계단" };

        [Header("세그먼트별 이징 (F3 패널이 편집)")]
        public List<int>   segEases  = new List<int>();
        public List<float> segPowers = new List<float>();   // 가속 강도(지수)
        public List<float> segDamps  = new List<float>();
        public List<float> segFreqs  = new List<float>();

        /// <summary>구간 리스트 길이를 맞추고 빠진 칸은 현재 전역값으로 채운다.</summary>
        public void EnsureSegLists(int n)
        {
            while (segTimes.Count  < n) segTimes.Add(segTime);
            while (segEases.Count  < n) segEases.Add(springBetweenPoses ? EzSpring : EzLinear);
            while (segPowers.Count < n) segPowers.Add(2f);
            while (segDamps.Count  < n) segDamps.Add(springDamp);
            while (segFreqs.Count  < n) segFreqs.Add(springFreq);
        }

        public int   GetEase(int i)  => i >= 0 && i < segEases.Count  ? segEases[i]  : (springBetweenPoses ? EzSpring : EzLinear);
        public float GetPower(int i) => i >= 0 && i < segPowers.Count ? segPowers[i] : 2f;
        public float GetDamp(int i)  => i >= 0 && i < segDamps.Count  ? segDamps[i]  : springDamp;
        public float GetFreq(int i)  => i >= 0 && i < segFreqs.Count  ? segFreqs[i]  : springFreq;

        public void SetEase(int i, int v)    { EnsureSegLists(i + 1); segEases[i]  = v; }
        public void SetPower(int i, float v) { EnsureSegLists(i + 1); segPowers[i] = v; }
        public void SetDamp(int i, float v)  { EnsureSegLists(i + 1); segDamps[i]  = v; }
        public void SetFreq(int i, float v)  { EnsureSegLists(i + 1); segFreqs[i]  = v; }

        /// <summary>구간 i의 이징을 적용해 0..1을 변형한다.</summary>
        public float EaseSeg(int i, float x) => Ease(GetEase(i), x, GetPower(i), GetDamp(i), GetFreq(i));

        /// <summary>이징 곡선. power는 가속 강도(클수록 급격), damp·freq는 스프링 전용.</summary>
        public static float Ease(int type, float x, float power, float damp, float freq)
        {
            x = Mathf.Clamp01(x);
            float p = Mathf.Max(1f, power);
            switch (type)
            {
                case EzIn:     return Mathf.Pow(x, p);                       // 천천히 → 빠르게
                case EzOut:    return 1f - Mathf.Pow(1f - x, p);             // 빠르게 → 천천히
                case EzInOut:  return x < 0.5f ? 0.5f * Mathf.Pow(2f * x, p)
                                               : 1f - 0.5f * Mathf.Pow(2f * (1f - x), p);
                case EzSpring: return SpringCurve(x, damp, freq);            // 지나쳤다 정착(오버슛)
                case EzSnap:   return x >= 1f ? 1f : 0f;                     // 계단 — 끝에서 뚝
                default:       return x;                                     // 선형
            }
        }

        /// <summary>감쇠 스프링. 1을 살짝 넘었다가 되돌아와 정확히 1에 정착한다.</summary>
        public static float SpringCurve(float x, float damp, float freq)
        {
            float d = Mathf.Max(0.1f, damp);
            float w = Mathf.Max(0.1f, freq);
            float v   = 1f - Mathf.Exp(-d * x) * Mathf.Cos(w * x);
            float end = 1f - Mathf.Exp(-d)     * Mathf.Cos(w);
            return v / Mathf.Max(0.0001f, end);
        }

        /// <summary>현재 시퀀스의 포즈 이름들(기본포즈 포함, F3 표시용).</summary>
        public List<string> SeqNames { get; private set; } = new List<string>();

        /// <summary>재생 중인가 — ViewmodelMotion이 루트를 양보할지 판단용.</summary>
        public bool IsPlaying => playing;

        /// <summary>지금 재생 중인 시퀀스가 이 접두어로 시작하는가(기본포즈 복귀 전까지 true).
        /// sim의 lungePhase는 0틱이라 금방 끝나지만, 포즈 애니메이션은 그 뒤로도 이어진다.
        /// 칼 단면 클리핑처럼 "애니메이션이 끝날 때까지" 유지할 것들이 이걸 본다.</summary>
        public bool IsPlayingPrefix(string prefix)
        {
            if (!playing || string.IsNullOrEmpty(prefix) || lastNames == null) return false;
            foreach (var n in lastNames) if (n.StartsWith(prefix)) return true;
            return false;
        }

        // 서로 다른 애니메이션 경계 = 순간이동(블렌드 없이 뚝 끊김) 세그먼트
        readonly HashSet<int> snapSegs = new HashSet<int>();
        /// <summary>이 세그먼트가 순간이동 경계인가(F3 표시용).</summary>
        public bool IsSnapSeg(int i) => snapSegs.Contains(i);

        // 마지막 재생 인자("다시 재생"용)
        List<string> lastNames;
        List<int>    lastSnaps;
        bool         lastLoop;
        string       lastKey = "";   // 시퀀스가 바뀌면 segTimes 리셋 판단용

        // 재생 중 잠시 끈 것들
        Animator            anim;
        bool                animWas;
        readonly List<Behaviour> suspended = new List<Behaviour>();

        void Awake() { Instance = this; }

        Transform Root()
        {
            if (root != null) return root;
            var main = Main.Instance;
            var cam  = main != null ? main.Cam : Camera.main;
            if (cam != null)
            {
                var t0 = cam.transform.Find("KatanaViewmodel");
                if (t0 != null) root = t0;
                else if (cam.transform.childCount > 0) root = cam.transform.GetChild(0);
            }
            if (root == null)
            {
                var go = GameObject.Find("KatanaViewmodel");
                if (go != null) root = go.transform;
            }
            return root;
        }

        /// <summary>뷰모델이 새로 만들어졌을 때 캐시된 참조를 버린다.</summary>
        public void ForgetRoot()
        {
            root = null;
            anim = null;
            suspended.Clear();
            idleApplied = false;
        }

        // ── 파일 ──
        public static List<string> ListPoses()
        {
            var names = new List<string>();
            if (!Directory.Exists(PoseDir)) return names;
            foreach (var f in Directory.GetFiles(PoseDir, "pose_*.json"))
                names.Add(Path.GetFileNameWithoutExtension(f).Substring("pose_".Length));
            names.Sort();
            return names;
        }

        static PoseFile Load(string name)
        {
            string f = Path.Combine(PoseDir, $"pose_{name}.json");
            if (!File.Exists(f)) return null;
            return JsonUtility.FromJson<PoseFile>(File.ReadAllText(f, System.Text.Encoding.UTF8));
        }

        // ── 타이밍 프로파일 (애니메이션별 튜닝값을 파일로 보존) ──
        public static string TimingPath => Path.Combine(PoseDir, "timing.json");

        /// <summary>현재 시퀀스의 프로파일 키(F3 표시·저장용).</summary>
        public string CurrentKey { get; private set; } = "";

        static PoseTimingFile timingCache;

        static PoseTimingFile Timings()
        {
            if (timingCache != null) return timingCache;
            if (File.Exists(TimingPath))
            {
                try { timingCache = JsonUtility.FromJson<PoseTimingFile>(File.ReadAllText(TimingPath, System.Text.Encoding.UTF8)); }
                catch { timingCache = null; }
            }
            if (timingCache == null) timingCache = new PoseTimingFile();
            if (timingCache.items == null) timingCache.items = new List<PoseTiming>();
            return timingCache;
        }

        static PoseTiming FindTiming(string key)
        {
            foreach (var t in Timings().items) if (t.key == key) return t;
            return null;
        }

        /// <summary>현재 튜닝값을 이 시퀀스의 프로파일로 저장한다. 다음 재생부터 자동 적용.</summary>
        public bool SaveTiming()
        {
            if (string.IsNullOrEmpty(CurrentKey)) return false;
            var t = FindTiming(CurrentKey);
            if (t == null) { t = new PoseTiming { key = CurrentKey }; Timings().items.Add(t); }
            t.segTimes   = segTimes.ToArray();
            t.hold       = holdLastPose;
            t.spring     = springBetweenPoses;
            t.springDamp = springDamp;
            t.springFreq = springFreq;
            t.snapReturn = snapReturn;
            t.eases      = segEases.ToArray();       // 구간별 이징까지 보존
            t.powers     = segPowers.ToArray();
            t.damps      = segDamps.ToArray();
            t.freqs      = segFreqs.ToArray();
            try
            {
                Directory.CreateDirectory(PoseDir);
                File.WriteAllText(TimingPath, JsonUtility.ToJson(Timings(), true), System.Text.Encoding.UTF8);
                return true;
            }
            catch (System.Exception e) { Debug.LogWarning("[PosePlayer] 타이밍 저장 실패: " + e.Message); return false; }
        }

        /// <summary>이 시퀀스에 저장된 프로파일이 있으면 불러와 적용한다.</summary>
        bool ApplyTiming(string key, int segCount)
        {
            var t = FindTiming(key);
            if (t == null || t.segTimes == null) return false;
            segTimes.Clear();
            for (int i = 0; i < segCount; i++)
                segTimes.Add(i < t.segTimes.Length ? t.segTimes[i] : segTime);
            holdLastPose       = t.hold;
            springBetweenPoses = t.spring;
            springDamp         = t.springDamp > 0f ? t.springDamp : springDamp;
            springFreq         = t.springFreq > 0f ? t.springFreq : springFreq;
            snapReturn         = t.snapReturn;

            // 구간별 이징 — 저장돼 있으면 그대로, 없으면 전역값으로 채운다(구버전 파일 호환)
            segEases.Clear(); segPowers.Clear(); segDamps.Clear(); segFreqs.Clear();
            for (int i = 0; i < segCount; i++)
            {
                segEases.Add (t.eases  != null && i < t.eases.Length  ? t.eases[i]  : (t.spring ? EzSpring : EzLinear));
                segPowers.Add(t.powers != null && i < t.powers.Length ? t.powers[i] : 2f);
                segDamps.Add (t.damps  != null && i < t.damps.Length  ? t.damps[i]  : springDamp);
                segFreqs.Add (t.freqs  != null && i < t.freqs.Length  ? t.freqs[i]  : springFreq);
            }
            return true;
        }

        /// <summary>이 시퀀스에 저장된 프로파일이 있는가(F3 표시용).</summary>
        public bool HasSavedTiming() => !string.IsNullOrEmpty(CurrentKey) && FindTiming(CurrentKey) != null;

        /// <summary>디스크에서 타이밍을 다시 읽는다(외부 편집 반영).</summary>
        public static void ReloadTimings() => timingCache = null;

        // ── 적용 ──
        public static void ApplyPose(Transform root, PoseFile pf)
        {
            if (root == null || pf == null) return;
            if (pf.rootPos != null && pf.rootPos.Length == 3)
            {
                root.localPosition = PoseMath.ToV3(pf.rootPos);
                if (pf.rootQuat  != null && pf.rootQuat.Length  == 4) root.localRotation = PoseMath.ToQ(pf.rootQuat);
                if (pf.rootScale != null && pf.rootScale.Length == 3) root.localScale    = PoseMath.ToV3(pf.rootScale);
            }
            if (pf.bones != null)
                foreach (var b in pf.bones)
                {
                    var t = root.Find(b.path);
                    if (t != null) t.localRotation = PoseMath.ToQ(b.quat);
                }
            if (pf.objects != null)
                foreach (var o in pf.objects)
                {
                    var t = root.Find(o.path);
                    if (t == null) continue;
                    if (o.pos   != null && o.pos.Length   == 3) t.localPosition = PoseMath.ToV3(o.pos);
                    if (o.quat  != null && o.quat.Length  == 4) t.localRotation = PoseMath.ToQ(o.quat);
                    if (o.scale != null && o.scale.Length == 3) t.localScale    = PoseMath.ToV3(o.scale);
                }
        }

        /// <summary>포즈 하나를 즉시 적용. 성공 시 true.</summary>
        public bool ApplySingle(string name)
        {
            var r = Root(); if (r == null) return false;
            var pf = Load(name); if (pf == null) return false;
            Suspend();                    // 다른 것이 덮어쓰지 않게
            ApplyPose(r, pf);
            return true;
        }

        int returnSegIndex = int.MaxValue;   // 이 세그먼트부터 = 기본포즈 복귀 구간(여기만 스프링)

        /// <summary>마지막 재생 시퀀스 요약(콘솔 표시용).</summary>
        public string SequenceSummary { get; private set; } = "";

        /// <summary>현재 시퀀스의 총 재생 시간(초) — 순간이동 구간은 0으로 친다.</summary>
        public float CurrentTotalTime
        {
            get
            {
                float t = Mathf.Max(0f, holdLastPose);
                int last = segTimes.Count - 1;
                for (int i = 0; i < segTimes.Count; i++)
                {
                    if (snapSegs.Contains(i)) continue;
                    if (snapReturn && i == last) continue;
                    t += Mathf.Max(0.02f, segTimes[i]);
                }
                return t;
            }
        }

        /// <summary>접두어 하나 재생.</summary>
        public int Play(string prefix, float dur, bool doLoop) => Play(new[] { prefix }, dur, doLoop);

        /// <summary>여러 접두어를 이어서 재생(콤보). 예: [slash1_, slash2_].
        /// 서로 다른 접두어의 경계는 블렌드 없이 뚝 끊긴다(순간이동).</summary>
        public int Play(string[] prefixes, float dur, bool doLoop)
        {
            segTime = Mathf.Max(0.05f, dur);
            var names = new List<string>();
            var snaps = new List<int>();      // 이 인덱스로 "들어가는" 세그먼트가 경계
            var all = ListPoses();
            foreach (var prefix in prefixes)
            {
                bool first = true;
                foreach (var n in all)
                    if (n.StartsWith(prefix))
                    {
                        // 새 애니메이션의 첫 포즈로 넘어가는 구간 = 순간이동
                        if (first && names.Count > 0) snaps.Add(names.Count - 1);
                        names.Add(n);
                        first = false;
                    }
            }
            return PlayNames(names, doLoop, snaps);
        }

        /// <summary>정확한 포즈 이름들을 순서대로 재생(중간 건너뛰기 가능).
        /// snapAt에 든 세그먼트 인덱스는 블렌드 없이 즉시 다음 포즈로 스냅한다.</summary>
        public int PlayNames(List<string> names, bool doLoop, List<int> snapAt = null)
        {
            var r = Root(); if (r == null) return 0;
            seq.Clear();
            var used = new List<string>();
            foreach (var n in names)
            {
                var pf = Load(n);
                if (pf != null) { seq.Add(pf); used.Add(n); }
            }
            if (seq.Count < 2) { seq.Clear(); return 0; }

            snapSegs.Clear();
            if (snapAt != null) foreach (var i in snapAt) snapSegs.Add(i);

            // 재생 후 기본포즈 복귀
            var basePf = Load(basePoseName);
            if (basePf != null && used[used.Count - 1] != basePoseName)
            {
                seq.Add(basePf);
                used.Add(basePoseName);
                returnSegIndex = seq.Count - 2;
            }
            else returnSegIndex = int.MaxValue;

            // 세그먼트별 시간 — ①저장된 프로파일 ②같은 시퀀스면 튜닝값 유지 ③없으면 기본값
            string key = string.Join("|", used);
            int segCount = seq.Count - 1;
            CurrentKey = key;
            if (key != lastKey || segTimes.Count != segCount)
            {
                if (!ApplyTiming(key, segCount))
                {
                    segTimes.Clear(); segEases.Clear(); segPowers.Clear(); segDamps.Clear(); segFreqs.Clear();
                }
            }
            EnsureSegLists(segCount);
            lastKey = key;
            SeqNames = used;

            float total = holdLastPose;
            foreach (var s in segTimes) total += Mathf.Max(0.05f, s);
            SequenceSummary = string.Join(" → ", used) + $"  (총 {total:0.00}초)";

            lastNames = new List<string>(names);
            lastSnaps = snapAt != null ? new List<int>(snapAt) : null;
            lastLoop = doLoop;
            loop = doLoop; elapsed = 0f; playing = true;
            idleHoldOn = true; idleApplied = false;   // 끝나면 다시 기본포즈로
            Suspend();
            return seq.Count;
        }

        /// <summary>마지막 재생을 현재 튜닝값(세그먼트별 시간 포함)으로 다시 재생.</summary>
        public bool Replay()
        {
            if (lastNames == null || lastNames.Count == 0) return false;
            return PlayNames(lastNames, lastLoop, lastSnaps) >= 2;
        }

        /// <summary>재생 중지 — 평상시(기본포즈)로 돌아간다. 컴포넌트는 계속 잠들어 있다.</summary>
        public void Stop()
        {
            playing = false;
            seq.Clear();
            idleApplied = false;      // 다음 프레임에 기본포즈 재적용
        }

        /// <summary>완전 해제 — 기본포즈 유지를 끄고 Animator·IK·SwordView를 원래대로 돌려준다.</summary>
        public void Release()
        {
            playing = false;
            seq.Clear();
            idleHoldOn = false;
            idleApplied = false;
            Restore();
        }

        /// <summary>자세를 건드리는 컴포넌트를 잠시 끈다(중복 제어 방지).</summary>
        void Suspend(bool includeFingers = false, bool includeIK = false)
        {
            var r = Root(); if (r == null) return;
            if (anim == null) { anim = r.GetComponent<Animator>(); if (anim != null) animWas = anim.enabled; }
            if (anim != null) anim.enabled = false;

            if (suspended.Count == 0)
            {
                // ★ IK·손가락은 재생 중에도 켜 둔다.
                //   포즈는 IK를 켠 상태로 만들었으므로 IK가 돌아야 저작 당시와 같은 그림이 나온다.
                //   (기본자세도 하나의 애니메이션이라 여기서 IK를 끄면 안 된다)
                //   이 인자들은 특수한 경우를 위해 남겨둔 것이고, 평시엔 둘 다 false다.
                if (includeIK)
                    foreach (var ik in r.GetComponentsInChildren<HandIK>(true))
                        if (ik.enabled) { ik.enabled = false; suspended.Add(ik); }
                if (includeFingers)
                    foreach (var fp in r.GetComponentsInChildren<FingerPoser>(true))
                        if (fp.enabled) { fp.enabled = false; suspended.Add(fp); }
            }
            // ★ SwordView는 끄지 않는다 — 타격음·상태 전이 감지가 계속 돌아야 실제 게임에서 쓸 수 있다.
            //   대신 IsDriving을 보고 자세 구동(DriveFromSim)만 건너뛰게 한다.
            IsDriving = true;
        }

        /// <summary>포즈가 뷰모델 자세를 쥐고 있는가. SwordView는 이때 Animator 구동을 건너뛴다.</summary>
        public bool IsDriving { get; private set; }

        void Restore()
        {
            IsDriving = false;
            if (anim != null) anim.enabled = animWas;
            foreach (var b in suspended) if (b != null) b.enabled = true;
            suspended.Clear();
        }

        void LateUpdate()
        {
            // 평상시 — 재생 중이 아니면 기본포즈를 유지한다(한 번만 적용하고 잠들어 있음)
            if (!playing || seq.Count < 2)
            {
                if (holdBaseWhenIdle && idleHoldOn && !idleApplied)
                {
                    var r0 = Root(); if (r0 == null) return;      // 뷰모델이 아직 없으면 다음 프레임에 재시도
                    if (baseCache == null) baseCache = Load(basePoseName);
                    if (baseCache == null) { idleApplied = true; return; }   // 기본포즈 파일 없음 — 재시도 중단
                    Suspend();   // IK·손가락은 계속 돈다(포즈를 IK 켠 상태로 만들었으므로)
                    ApplyPose(r0, baseCache);
                    // 방금 적용한 손가락을 FingerPoser의 기준으로 삼는다 — 안 그러면 제 rest로 되돌려버린다.
                    foreach (var fp in r0.GetComponentsInChildren<FingerPoser>(true)) fp.Rebuild();
                    // 이 자세를 절차 모션의 기준점으로 — 안 그러면 옛 기준 위에 오프셋이 얹혀 튄다.
                    if (ViewmodelMotion.Instance != null) ViewmodelMotion.Instance.RecaptureBase();
                    idleApplied = true;
                }
                return;
            }
            idleApplied = false;

            var r = Root(); if (r == null) { Stop(); return; }

            // 타임라인: [내용 세그먼트들(각자 시간)] → [마지막 포즈 정지(hold)] → [기본포즈 복귀(스프링)]
            bool hasReturn  = returnSegIndex != int.MaxValue;
            int segCount    = seq.Count - 1;
            int contentSegs = hasReturn ? returnSegIndex : segCount;

            // 순간이동 구간은 시간 0 — 블렌드 없이 다음 포즈로 뚝 끊긴다.
            // (애니메이션 경계 + snapReturn이면 기본포즈 복귀 구간까지)
            System.Func<int, float> segT = (i) =>
                snapSegs.Contains(i)                        ? 0f
                : (snapReturn && i == returnSegIndex)       ? 0f
                : i < segTimes.Count ? Mathf.Max(0.02f, segTimes[i]) : Mathf.Max(0.02f, segTime);

            float aEnd = 0f;
            for (int k = 0; k < contentSegs; k++) aEnd += segT(k);          // 내용 구간 끝
            float hEnd  = aEnd + Mathf.Max(0f, holdLastPose);               // 정지 구간 끝
            float total = hEnd + (hasReturn ? segT(contentSegs) : 0f);      // 전체

            elapsed += Time.deltaTime;
            if (elapsed >= total)
            {
                if (loop) elapsed -= total;
                else { elapsed = total; playing = false; }
            }

            if (elapsed < aEnd)
            {
                // 내용 구간 — 세그먼트별 시간으로 진행. 스프링 또는 선형
                float acc = 0f; int i = 0; float raw = 0f;
                for (int k = 0; k < contentSegs; k++)
                {
                    float st = segT(k);
                    if (elapsed < acc + st) { i = k; raw = (elapsed - acc) / st; break; }
                    acc += st;
                    i = k; raw = 1f;
                }
                Blend(r, seq[i], seq[i + 1], EaseSeg(i, raw));
            }
            else if (elapsed < hEnd || !hasReturn)
            {
                // 마지막 포즈에서 정지(임팩트 홀드)
                Blend(r, seq[contentSegs], seq[contentSegs], 0f);
            }
            else
            {
                // 기본포즈 복귀
                float rt = segT(contentSegs);
                if (rt <= 0f)
                {
                    // 순간이동 — 블렌드 없이 기본포즈로 뚝
                    Blend(r, seq[returnSegIndex + 1], seq[returnSegIndex + 1], 0f);
                }
                else
                {
                    float raw = Mathf.Clamp01((elapsed - hEnd) / rt);
                    Blend(r, seq[returnSegIndex], seq[returnSegIndex + 1], EaseSeg(returnSegIndex, raw));
                }
            }
        }

        static void Blend(Transform root, PoseFile a, PoseFile b, float u)
        {
            // 루트 배치도 보간(양쪽에 저장돼 있을 때)
            // Unclamped — 스프링 오버슛(u>1)이 실제 포즈에 반영되게
            if (a.rootPos != null && a.rootPos.Length == 3 && b.rootPos != null && b.rootPos.Length == 3)
            {
                root.localPosition = Vector3.LerpUnclamped(PoseMath.ToV3(a.rootPos), PoseMath.ToV3(b.rootPos), u);
                if (a.rootQuat != null && b.rootQuat != null)
                    root.localRotation = Quaternion.SlerpUnclamped(PoseMath.ToQ(a.rootQuat), PoseMath.ToQ(b.rootQuat), u);
            }
            if (a.bones != null)
                for (int k = 0; k < a.bones.Length; k++)
                {
                    var t = root.Find(a.bones[k].path);
                    if (t == null) continue;
                    Quaternion qa = PoseMath.ToQ(a.bones[k].quat);
                    Quaternion qb = FindQ(b.bones, a.bones[k].path, qa);
                    if (Quaternion.Dot(qa, qb) < 0f) qb = new Quaternion(-qb.x, -qb.y, -qb.z, -qb.w);   // 최단 경로
                    t.localRotation = Quaternion.SlerpUnclamped(qa, qb, u);
                }
            if (a.objects != null)
                for (int k = 0; k < a.objects.Length; k++)
                {
                    var o = a.objects[k];
                    var t = root.Find(o.path);
                    if (t == null) continue;
                    var ob = FindObj(b.objects, o.path);
                    Quaternion qa = PoseMath.ToQ(o.quat);
                    Quaternion qb = ob != null ? PoseMath.ToQ(ob.quat) : qa;
                    if (Quaternion.Dot(qa, qb) < 0f) qb = new Quaternion(-qb.x, -qb.y, -qb.z, -qb.w);
                    Vector3 pa = PoseMath.ToV3(o.pos), pb = ob != null ? PoseMath.ToV3(ob.pos) : pa;
                    t.localRotation = Quaternion.SlerpUnclamped(qa, qb, u);
                    t.localPosition = Vector3.LerpUnclamped(pa, pb, u);
                }
        }

        static Quaternion FindQ(PoseBone[] arr, string path, Quaternion fallback)
        {
            if (arr != null)
                foreach (var b in arr) if (b.path == path) return PoseMath.ToQ(b.quat);
            return fallback;
        }

        static PoseObject FindObj(PoseObject[] arr, string path)
        {
            if (arr != null)
                foreach (var o in arr) if (o.path == path) return o;
            return null;
        }
    }

    /// <summary>Play 시 자동 부착.</summary>
    public static class PosePlayerBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Object.FindFirstObjectByType<PosePlayer>() == null)
                new GameObject("[PosePlayer]").AddComponent<PosePlayer>();
        }
    }
}
