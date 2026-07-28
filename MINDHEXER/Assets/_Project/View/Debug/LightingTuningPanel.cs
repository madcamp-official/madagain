using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.View
{
    /// <summary>
    /// F2 조명·흑백 튜닝 패널 — 환경광·반사·조명·채도·블룸·재질을 Play 중 실시간 조정한다.
    ///
    /// <para><b>왜 필요한가</b>: "빛을 없앤다"를 끝까지 밀면 화면이 그냥 검정이 된다.
    /// 특히 metallic이 높은 표면은 albedo를 거의 안 쓰고 <b>환경 반사</b>에서 밝기를 얻으므로,
    /// ambient·reflection을 0으로 만들면 직접광이 닿아도 검게 남는다.
    /// 어디까지 죽여야 "어둡되 형태가 읽히는" 상태가 되는지는 <b>눈으로 보며 정할 수밖에 없다.</b></para>
    ///
    /// <para><b>개발자 도구</b>(OnGUI) — PC 튜닝 전용. VR 빌드에선 보이지 않는다.
    /// 값이 확정되면 씬/머티리얼에 굳히는 것을 전제로 한다.</para>
    ///
    /// <para>패널을 열면 진입 시점의 값을 스냅샷으로 잡아두고, '되돌리기'로 언제든 복원한다.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public class LightingTuningPanel : MonoBehaviour
    {
        const float PanelWidth = 430f;

        bool _open;
        Vector2 _scroll;
        bool _secEnv = true, _secLight = true, _secGrade, _secMat;

        CursorLockMode _prevLock;
        FirstPersonPlayer _fpp;
        bool _prevLookFrozen;

        // 씬에서 찾아 쓰는 대상들 (없으면 해당 섹션만 비활성)
        Volume _volume;
        ColorAdjustments _grade;
        Bloom _bloom;
        Material _grungeMat;

        static string FilePath => Path.Combine(Application.persistentDataPath, "lighting_tuning.json");

        // ── 진입 시점 스냅샷 (되돌리기용) ──────────────────────────────
        [System.Serializable]
        class EnvSnapshot
        {
            public int ambientMode;
            public Color ambientLight, ambientSky, ambientEquator, ambientGround;
            public float ambientIntensity, reflectionIntensity;
            public bool captured;
        }
        EnvSnapshot _snap = new EnvSnapshot();

        void Awake()
        {
            _fpp = UnityEngine.Object.FindFirstObjectByType<FirstPersonPlayer>();
        }

        void Start()
        {
            FindTargets();
            CaptureEnv();
        }

        void FindTargets()
        {
            if (_volume == null)
            {
                var vols = UnityEngine.Object.FindObjectsByType<Volume>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                foreach (var v in vols) { if (v.isGlobal && v.sharedProfile != null) { _volume = v; break; } }
            }
            if (_volume != null && _volume.sharedProfile != null)
            {
                _volume.sharedProfile.TryGet(out _grade);
                _volume.sharedProfile.TryGet(out _bloom);
            }
            if (_grungeMat == null)
            {
                // 절차 재질을 쓰는 렌더러에서 공유 머티리얼을 집는다.
                var rends = UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                foreach (var r in rends)
                {
                    var m = r.sharedMaterial;
                    if (m != null && m.shader != null && m.shader.name.Contains("TriplanarGrunge")) { _grungeMat = m; break; }
                }
            }
        }

        void CaptureEnv()
        {
            if (_snap.captured) return;
            _snap.ambientMode = (int)RenderSettings.ambientMode;
            _snap.ambientLight = RenderSettings.ambientLight;
            _snap.ambientSky = RenderSettings.ambientSkyColor;
            _snap.ambientEquator = RenderSettings.ambientEquatorColor;
            _snap.ambientGround = RenderSettings.ambientGroundColor;
            _snap.ambientIntensity = RenderSettings.ambientIntensity;
            _snap.reflectionIntensity = RenderSettings.reflectionIntensity;
            _snap.captured = true;
        }

        void RestoreEnv()
        {
            if (!_snap.captured) return;
            RenderSettings.ambientMode = (AmbientMode)_snap.ambientMode;
            RenderSettings.ambientLight = _snap.ambientLight;
            RenderSettings.ambientSkyColor = _snap.ambientSky;
            RenderSettings.ambientEquatorColor = _snap.ambientEquator;
            RenderSettings.ambientGroundColor = _snap.ambientGround;
            RenderSettings.ambientIntensity = _snap.ambientIntensity;
            RenderSettings.reflectionIntensity = _snap.reflectionIntensity;
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null || !kb.f2Key.wasPressedThisFrame) return;

            _open = !_open;
            if (_open)
            {
                FindTargets();
                _prevLock = Cursor.lockState;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                if (_fpp != null) { _prevLookFrozen = _fpp.LookFrozen; _fpp.LookFrozen = true; }
            }
            else
            {
                Cursor.lockState = _prevLock;
                Cursor.visible = _prevLock != CursorLockMode.Locked;
                if (_fpp != null) _fpp.LookFrozen = _prevLookFrozen;
            }
        }

        void OnGUI()
        {
            if (!_open) return;

            GUILayout.BeginArea(new Rect(12f, 12f, PanelWidth, Screen.height - 24f), GUI.skin.box);
            GUILayout.Label("<b>조명 · 흑백 튜닝 (F2)</b>", Rich());

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("저장")) Save();
            if (GUILayout.Button("불러오기")) Load();
            if (GUILayout.Button("진입시점 복원")) RestoreEnv();
            GUILayout.EndHorizontal();

            _scroll = GUILayout.BeginScrollView(_scroll);

            DrawPresets();
            DrawEnv();
            DrawLights();
            DrawGrade();
            DrawMaterial();

            GUILayout.EndScrollView();
            GUILayout.Label($"<size=10>{FilePath}</size>", Rich());
            GUILayout.EndArea();
        }

        // ── 프리셋 ──────────────────────────────────────────────────────
        void DrawPresets()
        {
            GUILayout.Space(4f);
            GUILayout.Label("<b>프리셋 — 한 번에 비교</b>", Rich());
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("① 완전 소등"))  ApplyPreset(0f, 0f, false);
            if (GUILayout.Button("② 최소 환경광")) ApplyPreset(0.03f, 0.3f, false);
            if (GUILayout.Button("③ 폐공장"))     ApplyPreset(0.10f, 1f, false);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("④ 밝은 확인용")) ApplyPreset(0.25f, 1f, true);
            if (GUILayout.Button("Directional 토글")) ToggleDirectional();
            GUILayout.EndHorizontal();
            Info("① 은 환경광·반사가 0이라 <b>금속이 검게</b> 나온다(정상). ③ 이 기본 목표.");
        }

        void ApplyPreset(float ambient, float reflection, bool directional)
        {
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(ambient, ambient, ambient, 1f);
            RenderSettings.reflectionIntensity = reflection;
            var dl = FindDirectional();
            if (dl != null) dl.gameObject.SetActive(directional);
        }

        void ToggleDirectional()
        {
            var dl = FindDirectional();
            if (dl != null) dl.gameObject.SetActive(!dl.gameObject.activeSelf);
        }

        /// <summary>GameObject.Find는 비활성 오브젝트를 못 찾는다 — 꺼둔 조명도 다시 켤 수 있어야 하므로 Include로 찾는다.</summary>
        static Light FindDirectional()
        {
            var lights = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var l in lights) if (l.type == LightType.Directional) return l;
            return null;
        }

        // ── 환경광 ──────────────────────────────────────────────────────
        void DrawEnv()
        {
            if (!Section("환경광 · 반사", ref _secEnv)) return;

            float a = RenderSettings.ambientLight.grayscale;
            float na = F("환경광 밝기 (무채색)", a, 0f, 0.6f);
            if (!Mathf.Approximately(na, a))
            {
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(na, na, na, 1f);
            }
            RenderSettings.reflectionIntensity = F("반사 강도", RenderSettings.reflectionIntensity, 0f, 1f);
            RenderSettings.ambientIntensity = F("환경광 배율", RenderSettings.ambientIntensity, 0f, 3f);

            Info($"모드={RenderSettings.ambientMode}  Skybox={(RenderSettings.skybox != null ? RenderSettings.skybox.name : "없음")}");
            Info("<b>금속은 반사로 보인다.</b> 반사 강도를 0으로 두면 metallic 표면이 검게 남는다.");
        }

        // ── 조명 ────────────────────────────────────────────────────────
        void DrawLights()
        {
            if (!Section("조명", ref _secLight)) return;

            var lights = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var l in lights)
            {
                GUILayout.Space(2f);
                GUILayout.BeginHorizontal();
                bool on = GUILayout.Toggle(l.gameObject.activeSelf, "", GUILayout.Width(18f));
                if (on != l.gameObject.activeSelf) l.gameObject.SetActive(on);
                GUILayout.Label($"<b>{l.gameObject.name}</b> <size=10>({l.type})</size>", Rich());
                GUILayout.EndHorizontal();
                if (!on) continue;
                l.intensity = F("  세기", l.intensity, 0f, l.type == LightType.Directional ? 5f : 200f);
                if (l.type != LightType.Directional)
                    l.range = F("  범위", l.range, 1f, 120f);
                if (l.type == LightType.Spot)
                    l.spotAngle = F("  각도", l.spotAngle, 5f, 170f);
            }
            if (lights.Length == 0) Info("씬에 라이트가 없다.");
        }

        // ── 색보정 ──────────────────────────────────────────────────────
        void DrawGrade()
        {
            if (!Section("흑백 · 블룸", ref _secGrade)) return;

            if (_volume == null || _grade == null)
            {
                Info("글로벌 Volume/ColorAdjustments를 못 찾았다. [GreyscaleTest] 오브젝트를 확인.");
                if (GUILayout.Button("다시 찾기")) FindTargets();
                return;
            }

            bool volOn = GUILayout.Toggle(_volume.gameObject.activeSelf, " 흑백 Volume 켜기");
            if (volOn != _volume.gameObject.activeSelf) _volume.gameObject.SetActive(volOn);

            _grade.saturation.overrideState = true;
            _grade.saturation.value = F("채도 (-100=흑백)", _grade.saturation.value, -100f, 0f);
            _grade.contrast.overrideState = true;
            _grade.contrast.value = F("대비", _grade.contrast.value, -50f, 60f);
            _grade.postExposure.overrideState = true;
            _grade.postExposure.value = F("노출", _grade.postExposure.value, -3f, 3f);

            if (_bloom != null)
            {
                GUILayout.Space(2f);
                _bloom.threshold.overrideState = true;
                _bloom.threshold.value = F("블룸 문턱 (>1=발광만)", _bloom.threshold.value, 0f, 3f);
                _bloom.intensity.overrideState = true;
                _bloom.intensity.value = F("블룸 세기", _bloom.intensity.value, 0f, 4f);
            }
            Info("문턱을 1 아래로 내리면 <b>일반 밝은 면까지</b> 번져 뿌옇게 된다.");
        }

        // ── 절차 재질 ───────────────────────────────────────────────────
        void DrawMaterial()
        {
            if (!Section("절차 재질 (폐공장)", ref _secMat)) return;

            if (_grungeMat == null)
            {
                Info("TriplanarGrunge 재질을 쓰는 오브젝트를 못 찾았다.");
                if (GUILayout.Button("다시 찾기")) FindTargets();
                return;
            }
            GUILayout.Label($"<size=10>{_grungeMat.name}</size>", Rich());

            SetF("_Metallic",      F("Metallic",        _grungeMat.GetFloat("_Metallic"), 0f, 1f));
            SetF("_Smoothness",    F("Smoothness",      _grungeMat.GetFloat("_Smoothness"), 0f, 1f));
            GUILayout.Space(2f);
            SetF("_NoiseScale",    F("그런지 크기(m)",   _grungeMat.GetFloat("_NoiseScale"), 0.1f, 6f));
            SetF("_GrungeAmount",  F("그런지 세기",      _grungeMat.GetFloat("_GrungeAmount"), 0f, 1f));
            SetF("_GrungeValue",   F("그런지 명도",      _grungeMat.GetFloat("_GrungeValue"), 0f, 1f));
            GUILayout.Space(2f);
            SetF("_RustHeight",    F("녹 시작 높이(Y)",  _grungeMat.GetFloat("_RustHeight"), -5f, 20f));
            SetF("_RustFade",      F("녹 페이드",        _grungeMat.GetFloat("_RustFade"), 0.5f, 20f));
            SetF("_RustAmount",    F("녹 세기",          _grungeMat.GetFloat("_RustAmount"), 0f, 1f));
            GUILayout.Space(2f);
            SetF("_DustAmount",    F("윗면 먼지",        _grungeMat.GetFloat("_DustAmount"), 0f, 1f));
            SetF("_CavityAmount",  F("아랫면 어두움",    _grungeMat.GetFloat("_CavityAmount"), 0f, 1f));
            SetF("_BumpStrength",  F("요철 세기",        _grungeMat.GetFloat("_BumpStrength"), 0f, 2f));
            SetF("_BumpScale",     F("요철 크기",        _grungeMat.GetFloat("_BumpScale"), 0.5f, 20f));

            Info("요철 세기를 <b>0</b>으로 두면 픽셀당 노이즈 4회가 사라져 모바일에서 크게 가벼워진다.");
        }

        void SetF(string prop, float v)
        {
            if (_grungeMat.HasProperty(prop)) _grungeMat.SetFloat(prop, v);
        }

        // ── 저장 / 불러오기 ─────────────────────────────────────────────
        [System.Serializable]
        class Save1
        {
            public float ambient, reflection, saturation, contrast, exposure, bloomThreshold, bloomIntensity;
            public string matJson;
        }

        void Save()
        {
            var s = new Save1();
            s.ambient = RenderSettings.ambientLight.grayscale;
            s.reflection = RenderSettings.reflectionIntensity;
            if (_grade != null) { s.saturation = _grade.saturation.value; s.contrast = _grade.contrast.value; s.exposure = _grade.postExposure.value; }
            if (_bloom != null) { s.bloomThreshold = _bloom.threshold.value; s.bloomIntensity = _bloom.intensity.value; }
            File.WriteAllText(FilePath, JsonUtility.ToJson(s, true));
            Debug.Log("[F2] 저장 -> " + FilePath);
        }

        void Load()
        {
            if (!File.Exists(FilePath)) { Debug.LogWarning("[F2] 저장 파일 없음"); return; }
            var s = JsonUtility.FromJson<Save1>(File.ReadAllText(FilePath));
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(s.ambient, s.ambient, s.ambient, 1f);
            RenderSettings.reflectionIntensity = s.reflection;
            if (_grade != null)
            {
                _grade.saturation.overrideState = true;   _grade.saturation.value = s.saturation;
                _grade.contrast.overrideState = true;     _grade.contrast.value = s.contrast;
                _grade.postExposure.overrideState = true; _grade.postExposure.value = s.exposure;
            }
            if (_bloom != null)
            {
                _bloom.threshold.overrideState = true; _bloom.threshold.value = s.bloomThreshold;
                _bloom.intensity.overrideState = true; _bloom.intensity.value = s.bloomIntensity;
            }
            Debug.Log("[F2] 불러옴");
        }

        // ── GUI 헬퍼 ────────────────────────────────────────────────────

        static GUIStyle _rich;
        static GUIStyle Rich()
        {
            if (_rich == null) _rich = new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true };
            return _rich;
        }

        static bool Section(string title, ref bool open)
        {
            GUILayout.Space(4f);
            open = GUILayout.Toggle(open, (open ? "▼ " : "▶ ") + title, GUI.skin.button);
            return open;
        }

        static void Info(string s) => GUILayout.Label($"<size=11>{s}</size>", Rich());

        static float F(string label, float v, float min, float max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, Rich(), GUILayout.Width(170f));
            GUILayout.Label(v.ToString("0.###"), Rich(), GUILayout.Width(48f));
            float r = GUILayout.HorizontalSlider(v, min, max);
            GUILayout.EndHorizontal();
            return r;
        }
    }

    /// <summary>Play 시작 시 자동 부착 — 씬에 오브젝트를 놓지 않아도 F2가 동작한다.</summary>
    public static class LightingTuningPanelBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (UnityEngine.Object.FindFirstObjectByType<LightingTuningPanel>() == null)
                new GameObject("[LightingTuningPanel]").AddComponent<LightingTuningPanel>();
        }
    }
}
