using System.Collections.Generic;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 아레나 조명 연출. 아레나 루트(WaveRunner 옆)에 붙인다.
    ///
    /// 상태는 새로 만들지 않고 <see cref="WaveRunner.CurrentState"/>를 <b>읽기만</b> 한다
    /// (다른 세션이 소유한 파일을 수정하지 않아 충돌이 나지 않는다).
    ///   Idle·WaitStart      → 평시
    ///   Spawning·Watching   → 전투
    ///   Done                → 클리어
    ///
    /// 대상은 <b>자기 하위</b>만 모으므로, 프리팹을 여러 개 이어 붙여 맵을 만들어도
    /// 각 아레나가 자기 조명만 제어한다.
    ///
    /// 머티리얼은 원본을 직접 건드리지 않는다 — 원본 머티리얼마다 인스턴스를 하나씩 만들어
    /// 그 아레나의 조명들이 공유한다(원본 에셋 안전 + 드로우콜 안 늘어남).
    /// </summary>
    [DisallowMultipleComponent]
    public class ArenaLights : MonoBehaviour
    {
        public enum Mood { Idle, Combat, Cleared }

        [System.Serializable]
        public struct Preset
        {
            [Tooltip("Light 컴포넌트 색")]
            public Color lightColor;
            [Tooltip("Light 세기. 0이면 꺼진 것처럼 보인다")]
            public float lightIntensity;
            [ColorUsage(true, true)]
            [Tooltip("Emissive 머티리얼 색(HDR). 1을 넘겨야 Bloom이 번진다")]
            public Color emission;
        }

        [Header("상태 소스")]
        [Tooltip("비우면 같은 오브젝트·부모·자식에서 자동으로 찾는다")]
        public WaveRunner runner;
        [Tooltip("끄면 runner를 무시하고 아래 manualMood를 쓴다(연출 테스트용)")]
        public bool useRunner = true;
        public Mood manualMood = Mood.Idle;

        [Header("프리셋")]
        // 기본: 소환 중=빨강(combat) / 그 외=하양(idle). 우클릭 컨텍스트 메뉴로 언제든 이 기본으로 리셋 가능.
        public Preset idle    = new Preset { lightColor = new Color(1f, 1f, 1f),      lightIntensity = 2f,
                                             emission = new Color(2f, 2f, 2f) };            // 하양
        public Preset combat  = new Preset { lightColor = new Color(1f, 0.25f, 0.2f),  lightIntensity = 3f,
                                             emission = new Color(3f, 0.1f, 0.1f) };        // 빨강
        public Preset cleared = new Preset { lightColor = new Color(0.3f, 0.6f, 1f),   lightIntensity = 2.2f,
                                             emission = new Color(0.1f, 0.6f, 3f) };        // 파랑

        [Header("전환")]
        [Tooltip("0이면 즉시 전환. 값이 클수록 천천히 물든다")]
        public float fadeSeconds = 0.6f;

        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        Light[] lights = new Light[0];
        Material[] emissiveMats = new Material[0];
        Preset shown;
        bool ready;

        void Awake()
        {
            if (runner == null)
            {
                runner = GetComponent<WaveRunner>()
                      ?? GetComponentInParent<WaveRunner>()
                      ?? GetComponentInChildren<WaveRunner>(true);
            }
            Collect();
            shown = PresetOf(CurrentMood());
            Apply(shown);
            ready = true;
        }

        void Update()
        {
            if (!ready) return;
            Preset target = PresetOf(CurrentMood());

            // 지수 감쇠 보간 — fadeSeconds가 0이면 즉시
            float k = fadeSeconds <= 0f ? 1f : Mathf.Clamp01(Time.deltaTime / fadeSeconds);
            shown.lightColor     = Color.Lerp(shown.lightColor, target.lightColor, k);
            shown.lightIntensity = Mathf.Lerp(shown.lightIntensity, target.lightIntensity, k);
            shown.emission       = Color.Lerp(shown.emission, target.emission, k);
            Apply(shown);
        }

        /// <summary>현재 분위기. runner가 없거나 useRunner가 꺼져 있으면 manualMood.</summary>
        public Mood CurrentMood()
        {
            if (!useRunner || runner == null) return manualMood;
            // 몹 소환(Spawning) 중에만 빨강(Combat), 그 외(WaitStart·Watching·Done·Idle) 전부 하양(Idle).
            return runner.CurrentState == WaveRunner.State.Spawning ? Mood.Combat : Mood.Idle;
        }

        /// <summary>
        /// 프리셋을 "소환=빨강 / 그 외=하양" 기본으로 되돌린다.
        /// 이미 배치된 인스턴스·프리팹은 직렬화된 옛 값을 갖고 있으므로, 컴포넌트 우클릭 → 이 메뉴로 한 번에 맞춘다.
        /// (프리팹에서 리셋하면 마스터 씬의 모든 인스턴스에 전파된다.)
        /// </summary>
        [ContextMenu("프리셋 → 소환=빨강 / 그 외=하양")]
        void ResetWhiteRed()
        {
            idle    = new Preset { lightColor = new Color(1f, 1f, 1f),     lightIntensity = 2f, emission = new Color(2f, 2f, 2f) };
            combat  = new Preset { lightColor = new Color(1f, 0.25f, 0.2f), lightIntensity = 3f, emission = new Color(3f, 0.1f, 0.1f) };
#if UNITY_EDITOR
            if (!Application.isPlaying) UnityEditor.EditorUtility.SetDirty(this);
#endif
            if (ready) { shown = PresetOf(CurrentMood()); Apply(shown); }
        }

        Preset PresetOf(Mood m)
        {
            switch (m)
            {
                case Mood.Combat:  return combat;
                case Mood.Cleared: return cleared;
                default:           return idle;
            }
        }

        // ── 수집 ──
        /// <summary>자기 하위의 Light와 Emission 켜진 렌더러를 모으고, 머티리얼 인스턴스를 만든다.</summary>
        void Collect()
        {
            var ls = new List<Light>();
            foreach (var l in GetComponentsInChildren<Light>(true))
                if (l.type != LightType.Directional) ls.Add(l);   // 방향광은 씬 전체용이라 제외
            lights = ls.ToArray();

            var made = new Dictionary<Material, Material>();
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                Material[] mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    Material sm = mats[i];
                    if (sm == null) continue;
                    if (!sm.HasProperty(EmissionId)) continue;
                    if (!sm.IsKeywordEnabled("_EMISSION")) continue;   // Emission 안 쓰는 머티리얼은 건너뜀

                    if (!made.TryGetValue(sm, out Material inst))
                    {
                        inst = new Material(sm) { name = sm.name + " (arena instance)" };
                        made[sm] = inst;
                    }
                    mats[i] = inst;
                    changed = true;
                }
                if (changed) r.sharedMaterials = mats;
            }

            emissiveMats = new Material[made.Count];
            made.Values.CopyTo(emissiveMats, 0);

            if (lights.Length == 0 && emissiveMats.Length == 0)
                Debug.LogWarning($"[ArenaLights] {name}: 제어할 조명이 없습니다. " +
                                 "하위에 Light 컴포넌트가 있거나 Emission 켜진 머티리얼이 있어야 합니다.");
        }

        void Apply(Preset p)
        {
            foreach (var l in lights)
            {
                if (l == null) continue;
                l.color = p.lightColor;
                l.intensity = p.lightIntensity;
            }
            foreach (var m in emissiveMats)
            {
                if (m == null) continue;
                m.SetColor(EmissionId, p.emission);
            }
        }

        void OnDestroy()
        {
            foreach (var m in emissiveMats)   // 만든 인스턴스 정리(원본 에셋은 그대로)
                if (m != null) Destroy(m);
        }

        // ── 테스트용 ──
        [ContextMenu("평시로")]    void TestIdle()    { useRunner = false; manualMood = Mood.Idle; }
        [ContextMenu("전투로")]    void TestCombat()  { useRunner = false; manualMood = Mood.Combat; }
        [ContextMenu("클리어로")]  void TestCleared() { useRunner = false; manualMood = Mood.Cleared; }
        [ContextMenu("웨이브 상태 따르기")] void TestAuto() { useRunner = true; }
    }
}
