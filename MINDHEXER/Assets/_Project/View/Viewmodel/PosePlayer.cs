using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 포즈 JSON들을 순서대로 이어 재생한다(개발용 미리보기). (Precog에서 포팅)
    /// 클립을 굽기 전에 "이 포즈들이 이어지면 어떤 동작인가"를 바로 확인하는 용도.
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

        [Header("재생 타이밍")]
        [Tooltip("포즈 하나 넘어가는 시간(초)")]
        public float segTime = 0.2f;
        [Tooltip("마지막 포즈에 도달했을 때 멈춰 있는 시간(초) — 임팩트 정지")]
        public float holdLastPose = 0.12f;

        [Header("포즈 사이 이징")]
        public bool springBetweenPoses = true;
        public float springDamp = 6f;
        public float springFreq = 18f;

        [Header("복귀")]
        [Tooltip("끝나면 이 이름의 포즈로 복귀(시퀀스 끝에 자동 추가)")]
        public string basePoseName = "기본포즈";
        public bool holdBaseWhenIdle = true;
        public bool snapReturn = true;

        bool idleHoldOn = true;
        bool idleApplied;
        PoseFile baseCache;

        [Header("세그먼트별 시간 (편집 도구가 채움. 비어있으면 segTime 사용)")]
        public List<float> segTimes = new List<float>();

        public const int EzLinear = 0, EzIn = 1, EzOut = 2, EzInOut = 3, EzSpring = 4, EzSnap = 5;
        public static readonly string[] EaseNames = { "선형", "이즈인", "이즈아웃", "인아웃", "스프링", "계단" };

        public List<int>   segEases  = new List<int>();
        public List<float> segPowers = new List<float>();
        public List<float> segDamps  = new List<float>();
        public List<float> segFreqs  = new List<float>();

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

        public float EaseSeg(int i, float x) => Ease(GetEase(i), x, GetPower(i), GetDamp(i), GetFreq(i));

        public static float Ease(int type, float x, float power, float damp, float freq)
        {
            x = Mathf.Clamp01(x);
            float p = Mathf.Max(1f, power);
            switch (type)
            {
                case EzIn:     return Mathf.Pow(x, p);
                case EzOut:    return 1f - Mathf.Pow(1f - x, p);
                case EzInOut:  return x < 0.5f ? 0.5f * Mathf.Pow(2f * x, p)
                                               : 1f - 0.5f * Mathf.Pow(2f * (1f - x), p);
                case EzSpring: return SpringCurve(x, damp, freq);
                case EzSnap:   return x >= 1f ? 1f : 0f;
                default:       return x;
            }
        }

        public static float SpringCurve(float x, float damp, float freq)
        {
            float d = Mathf.Max(0.1f, damp);
            float w = Mathf.Max(0.1f, freq);
            float v   = 1f - Mathf.Exp(-d * x) * Mathf.Cos(w * x);
            float end = 1f - Mathf.Exp(-d)     * Mathf.Cos(w);
            return v / Mathf.Max(0.0001f, end);
        }

        public List<string> SeqNames { get; private set; } = new List<string>();

        /// <summary>재생 중인가 — ViewmodelMotion이 루트를 양보할지 판단용.</summary>
        public bool IsPlaying => playing;

        /// <summary>지금 재생 중인 시퀀스가 이 접두어로 시작하는가(기본포즈 복귀 전까지 true).</summary>
        public bool IsPlayingPrefix(string prefix)
        {
            if (!playing || string.IsNullOrEmpty(prefix) || lastNames == null) return false;
            foreach (var n in lastNames) if (n.StartsWith(prefix)) return true;
            return false;
        }

        readonly HashSet<int> snapSegs = new HashSet<int>();
        public bool IsSnapSeg(int i) => snapSegs.Contains(i);

        List<string> lastNames;
        List<int>    lastSnaps;
        bool         lastLoop;
        string       lastKey = "";

        Animator            anim;
        bool                animWas;
        readonly List<Behaviour> suspended = new List<Behaviour>();

        void Awake() { Instance = this; }

        Transform Root()
        {
            if (root != null) return root;
            var cam = Camera.main;
            if (cam != null)
            {
                var t0 = cam.transform.Find(ViewmodelCamera.ViewmodelRootName);
                if (t0 != null) root = t0;
                else if (cam.transform.childCount > 0) root = cam.transform.GetChild(0);
            }
            if (root == null)
            {
                var go = GameObject.Find(ViewmodelCamera.ViewmodelRootName);
                if (go != null) root = go.transform;
            }
            return root;
        }

        public void ForgetRoot()
        {
            root = null;
            anim = null;
            suspended.Clear();
            idleApplied = false;
        }

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

        public static string TimingPath => Path.Combine(PoseDir, "timing.json");
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
            t.eases      = segEases.ToArray();
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

        public bool HasSavedTiming() => !string.IsNullOrEmpty(CurrentKey) && FindTiming(CurrentKey) != null;
        public static void ReloadTimings() => timingCache = null;

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

        public bool ApplySingle(string name)
        {
            var r = Root(); if (r == null) return false;
            var pf = Load(name); if (pf == null) return false;
            Suspend();
            ApplyPose(r, pf);
            return true;
        }

        int returnSegIndex = int.MaxValue;
        public string SequenceSummary { get; private set; } = "";

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

        public int Play(string prefix, float dur, bool doLoop) => Play(new[] { prefix }, dur, doLoop);

        public int Play(string[] prefixes, float dur, bool doLoop)
        {
            segTime = Mathf.Max(0.05f, dur);
            var names = new List<string>();
            var snaps = new List<int>();
            var all = ListPoses();
            foreach (var prefix in prefixes)
            {
                bool first = true;
                foreach (var n in all)
                    if (n.StartsWith(prefix))
                    {
                        if (first && names.Count > 0) snaps.Add(names.Count - 1);
                        names.Add(n);
                        first = false;
                    }
            }
            return PlayNames(names, doLoop, snaps);
        }

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

            var basePf = Load(basePoseName);
            if (basePf != null && used[used.Count - 1] != basePoseName)
            {
                seq.Add(basePf);
                used.Add(basePoseName);
                returnSegIndex = seq.Count - 2;
            }
            else returnSegIndex = int.MaxValue;

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
            idleHoldOn = true; idleApplied = false;
            Suspend();
            return seq.Count;
        }

        public bool Replay()
        {
            if (lastNames == null || lastNames.Count == 0) return false;
            return PlayNames(lastNames, lastLoop, lastSnaps) >= 2;
        }

        public void Stop()
        {
            playing = false;
            seq.Clear();
            idleApplied = false;
        }

        public void Release()
        {
            playing = false;
            seq.Clear();
            idleHoldOn = false;
            idleApplied = false;
            Restore();
        }

        void Suspend(bool includeFingers = false, bool includeIK = false)
        {
            var r = Root(); if (r == null) return;
            if (anim == null) { anim = r.GetComponent<Animator>(); if (anim != null) animWas = anim.enabled; }
            if (anim != null) anim.enabled = false;

            if (suspended.Count == 0)
            {
                // IK·손가락은 재생 중에도 켜 둔다 — 포즈는 IK를 켠 상태로 만들었으므로 그래야 저작 당시와
                // 같은 그림이 나온다. includeIK/includeFingers는 특수한 경우를 위한 것이고 평시엔 false다.
                if (includeIK)
                    foreach (var ik in r.GetComponentsInChildren<HandIK>(true))
                        if (ik.enabled) { ik.enabled = false; suspended.Add(ik); }
                if (includeFingers)
                    foreach (var fp in r.GetComponentsInChildren<FingerPoser>(true))
                        if (fp.enabled) { fp.enabled = false; suspended.Add(fp); }
            }
            // 이 컴포넌트가 자세를 쥐고 있는 동안, 절차 모션(ViewmodelMotion) 등 다른 구동자는
            // IsDriving을 보고 자세 구동을 건너뛰어야 서로 안 싸운다.
            IsDriving = true;
        }

        /// <summary>포즈가 뷰모델 자세를 쥐고 있는가. 다른 절차 구동자는 이때 자세 쓰기를 건너뛰어야 한다.</summary>
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
            if (!playing || seq.Count < 2)
            {
                if (holdBaseWhenIdle && idleHoldOn && !idleApplied)
                {
                    var r0 = Root(); if (r0 == null) return;
                    if (baseCache == null) baseCache = Load(basePoseName);
                    if (baseCache == null) { idleApplied = true; return; }
                    Suspend();
                    ApplyPose(r0, baseCache);
                    foreach (var fp in r0.GetComponentsInChildren<FingerPoser>(true)) fp.Rebuild();
                    if (ViewmodelMotion.Instance != null) ViewmodelMotion.Instance.RecaptureBase();
                    idleApplied = true;
                }
                return;
            }
            idleApplied = false;

            var r = Root(); if (r == null) { Stop(); return; }

            bool hasReturn  = returnSegIndex != int.MaxValue;
            int segCount    = seq.Count - 1;
            int contentSegs = hasReturn ? returnSegIndex : segCount;

            System.Func<int, float> segT = (i) =>
                snapSegs.Contains(i)                        ? 0f
                : (snapReturn && i == returnSegIndex)       ? 0f
                : i < segTimes.Count ? Mathf.Max(0.02f, segTimes[i]) : Mathf.Max(0.02f, segTime);

            float aEnd = 0f;
            for (int k = 0; k < contentSegs; k++) aEnd += segT(k);
            float hEnd  = aEnd + Mathf.Max(0f, holdLastPose);
            float total = hEnd + (hasReturn ? segT(contentSegs) : 0f);

            elapsed += Time.deltaTime;
            if (elapsed >= total)
            {
                if (loop) elapsed -= total;
                else { elapsed = total; playing = false; }
            }

            if (elapsed < aEnd)
            {
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
                Blend(r, seq[contentSegs], seq[contentSegs], 0f);
            }
            else
            {
                float rt = segT(contentSegs);
                if (rt <= 0f)
                {
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
                    if (Quaternion.Dot(qa, qb) < 0f) qb = new Quaternion(-qb.x, -qb.y, -qb.z, -qb.w);
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
