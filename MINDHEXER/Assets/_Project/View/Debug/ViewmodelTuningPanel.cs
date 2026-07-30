using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.View
{
    /// <summary>
    /// F6 1인칭 손·거미 위치 조정 패널.
    ///
    /// <para><b>왜 필요한가</b> — 손 자세와 거미가 얹히는 자리는 계산으로 정할 수 없다.
    /// 모델마다 손 뼈가 보는 축이 다르고(설계 §4.3), 거미 크기·다리 길이에 따라 얹히는 높이가
    /// 달라진다. <b>보면서 밀어 맞추는 수밖에 없다.</b></para>
    ///
    /// <para>조정 대상은 이미 있는 컴포넌트의 필드다 — 이 패널은 값을 들고 있지 않는다.
    /// 저장하면 <see cref="FilePath"/>에 JSON으로 남고, 다음 Play에서 <b>자동으로 불러온다.</b>
    /// (Play를 끄면 리그가 사라지므로 저장하지 않으면 전부 날아간다.)</para>
    ///
    /// <para>F1 이동 · F2 조명 · F3 포즈 · F4 포즈시퀀스 · F5 글리치가 이미 쓰이고 있어 F6이다.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public class ViewmodelTuningPanel : MonoBehaviour
    {
        const float PanelWidth = 440f;

        bool _open;
        Vector2 _scroll;
        bool _secHand = true, _secSpider = true, _secLegs, _secCam;

        CursorLockMode _prevLock;
        FirstPersonPlayer _fpp;
        bool _prevLookFrozen;

        MantleRig _mantle;
        SpiderRig _spider;
        SpiderLegs _legs;
        ViewmodelCamera _vmCam;

        public static string FilePath => "Assets/_Project/Poses/viewmodel_tuning.json";

        [System.Serializable]
        class Saved
        {
            public Vector3 idleLocalR, idleLocalL, idleEulerR, idleEulerL;
            public float idleWeight, idleGripR, idleGripL;
            public MantleRig.IdleFingerPose idleFingerR, idleFingerL;
            public Vector3 handEulerR, handEulerL;
            public float gazeAmount, gazeMaxDeg;
            public float anchorRadius, anchorSpread;
            public float nearClip, vrNearClip;
            public bool captured;
        }

        void Awake() { _fpp = FindFirstObjectByType<FirstPersonPlayer>(); }

        void Start() { FindTargets(); Load(); }

        void FindTargets()
        {
            if (_mantle == null) _mantle = FindFirstObjectByType<MantleRig>();
            if (_spider == null) _spider = FindFirstObjectByType<SpiderRig>();
            if (_legs   == null) _legs   = FindFirstObjectByType<SpiderLegs>();
            if (_vmCam  == null) _vmCam  = FindFirstObjectByType<ViewmodelCamera>();
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null || !kb.f6Key.wasPressedThisFrame) return;

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

            GUILayout.BeginArea(new Rect(Screen.width - PanelWidth - 12f, 12f, PanelWidth, Screen.height - 24f), GUI.skin.box);
            GUILayout.Label("<b>1인칭 손 · 거미 조정 (F6)</b>", Rich());

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("저장")) Save();
            if (GUILayout.Button("불러오기")) Load();
            if (GUILayout.Button("대상 다시 찾기")) FindTargets();
            GUILayout.EndHorizontal();

            _scroll = GUILayout.BeginScrollView(_scroll);
            DrawHand();
            DrawSpider();
            DrawLegs();
            DrawCam();
            GUILayout.EndScrollView();

            GUILayout.Label($"<size=10>{FilePath}</size>", Rich());
            GUILayout.EndArea();
        }

        // ── 손 ──────────────────────────────────────────────────────────
        void DrawHand()
        {
            if (!Section("오른손 · 왼손 (매사냥 자세)", ref _secHand)) return;
            if (_mantle == null) { Info("MantleRig가 없다."); return; }

            _mantle.driveIdlePose = GUILayout.Toggle(_mantle.driveIdlePose, " 기본 자세를 IK로 잡기 (끄면 모델 T포즈)");
            _mantle.idleWeight = F("IK 가중치", _mantle.idleWeight, 0f, 1f);

            GUILayout.Label("<b>오른손 — 오른쪽 아래에서 들어와 손등이 보인다</b>", Rich());
            _mantle.idleLocalR = V3("  위치", _mantle.idleLocalR, -0.8f, 0.8f);
            _mantle.idleEulerR = V3("  회전", _mantle.idleEulerR, -180f, 180f);
            _mantle.idleGripR  = F("  공통 말림", _mantle.idleGripR, 0f, 1f);
            Fingers("  R", _mantle.idleFingerR);

            GUILayout.Label("<b>왼손 — 평소엔 화면 밖</b>", Rich());
            _mantle.idleLocalL = V3("  위치", _mantle.idleLocalL, -0.8f, 0.8f);
            _mantle.idleEulerL = V3("  회전", _mantle.idleEulerL, -180f, 180f);
            _mantle.idleGripL  = F("  공통 말림", _mantle.idleGripL, 0f, 1f);
            Fingers("  L", _mantle.idleFingerL);

            GUILayout.Label("<b>손 뼈 축 보정 (등반에도 함께 적용)</b>", Rich());
            _mantle.handEulerR = V3("  R 보정", _mantle.handEulerR, -180f, 180f);
            _mantle.handEulerL = V3("  L 보정", _mantle.handEulerL, -180f, 180f);

            Info("손이 뒤집혀 보이면 <b>회전</b>이 아니라 <b>축 보정</b>을 만지십시오 — 리그마다 손 뼈가 보는 축이 다릅니다.");
        }

        // ── 거미 ────────────────────────────────────────────────────────
        void DrawSpider()
        {
            if (!Section("거미 — 응시", ref _secSpider)) return;
            if (_spider == null) { Info("SpiderRig가 없다."); return; }

            _spider.gazeAmount = F("응시 세기", _spider.gazeAmount, 0f, 1f);
            _spider.gazeMaxDeg = F("최대 각도(°)", _spider.gazeMaxDeg, 0f, 120f);
            _spider.stabilize  = GUILayout.Toggle(_spider.stabilize, " 수평 안정화 (독수리)");

            if (_spider.perchAnchor != null)
            {
                GUILayout.Label("<b>얹히는 자리 (손목 기준 로컬)</b>", Rich());
                Vector3 p = _spider.perchAnchor.localPosition;
                Vector3 np = V3("  위치", p, -0.3f, 0.3f);
                if (np != p) _spider.perchAnchor.localPosition = np;
            }
            else Info("perchAnchor가 비어 있다 — SpiderRig가 R_Hand 아래에 자동 생성한다.");

            Info("각도를 넘으면 거기서 멈춥니다. 그 '못 따라오는' 느낌이 오히려 생물처럼 보입니다.");
        }

        // ── 다리 ────────────────────────────────────────────────────────
        void DrawLegs()
        {
            if (!Section("거미 다리 — 팔에 붙는 지점", ref _secLegs)) return;
            if (_legs == null) { Info("SpiderLegs가 없다."); return; }

            _legs.weight       = F("다리 IK 가중치", _legs.weight, 0f, 1f);
            _legs.anchorRadius = F("팔 둘레 반지름(m)", _legs.anchorRadius, 0.005f, 0.12f);
            _legs.anchorSpread = F("팔 축 간격(m)",    _legs.anchorSpread, 0.005f, 0.2f);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("앵커 다시 만들기")) _legs.CreateAnchors();
            if (GUILayout.Button("길이 다시 측정")) _legs.Measure();
            GUILayout.EndHorizontal();

            Info("반지름·간격을 바꾼 뒤에는 <b>앵커 다시 만들기</b>를 눌러야 반영됩니다.");
        }

        // ── 카메라 ──────────────────────────────────────────────────────
        void DrawCam()
        {
            if (!Section("근평면 (손가락 잘림)", ref _secCam)) return;
            if (_vmCam == null) { Info("ViewmodelCamera가 없다."); return; }

            Info(_vmCam.Status);
            _vmCam.nearClip   = F("PC 오버레이 근평면", _vmCam.nearClip,   0.005f, 0.3f);
            _vmCam.vrNearClip = F("VR 메인 근평면",     _vmCam.vrNearClip, 0.01f,  0.3f);
            _vmCam.vrFarClip  = F("VR 원평면",          _vmCam.vrFarClip,  50f,    1000f);

            Info("손가락이 잘리면 근평면을 낮추고, z-fighting이 보이면 <b>VR 원평면</b>을 줄이십시오 " +
                 "— 정밀도는 근/원 <b>비율</b>이 지배합니다.");
            if (GUILayout.Button("뷰모델 카메라 값 저장")) _vmCam.Save();
        }

        // ── 저장 ────────────────────────────────────────────────────────
        public bool Save()
        {
            FindTargets();
            var s = new Saved { captured = true };
            if (_mantle != null)
            {
                s.idleLocalR = _mantle.idleLocalR; s.idleLocalL = _mantle.idleLocalL;
                s.idleEulerR = _mantle.idleEulerR; s.idleEulerL = _mantle.idleEulerL;
                s.idleWeight = _mantle.idleWeight;
                s.idleGripR = _mantle.idleGripR;   s.idleGripL = _mantle.idleGripL;
                s.idleFingerR = _mantle.idleFingerR; s.idleFingerL = _mantle.idleFingerL;
                s.handEulerR = _mantle.handEulerR; s.handEulerL = _mantle.handEulerL;
            }
            if (_spider != null) { s.gazeAmount = _spider.gazeAmount; s.gazeMaxDeg = _spider.gazeMaxDeg; }
            if (_legs != null)   { s.anchorRadius = _legs.anchorRadius; s.anchorSpread = _legs.anchorSpread; }
            if (_vmCam != null)  { s.nearClip = _vmCam.nearClip; s.vrNearClip = _vmCam.vrNearClip; }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, JsonUtility.ToJson(s, true), System.Text.Encoding.UTF8);
                Debug.Log("[F6] 저장 → " + FilePath);
                return true;
            }
            catch (System.Exception e) { Debug.LogWarning("[F6] 저장 실패: " + e.Message); return false; }
        }

        public bool Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return false;
                var s = JsonUtility.FromJson<Saved>(File.ReadAllText(FilePath, System.Text.Encoding.UTF8));
                if (s == null || !s.captured) return false;

                if (_mantle != null)
                {
                    _mantle.idleLocalR = s.idleLocalR; _mantle.idleLocalL = s.idleLocalL;
                    _mantle.idleEulerR = s.idleEulerR; _mantle.idleEulerL = s.idleEulerL;
                    _mantle.idleWeight = s.idleWeight;
                    _mantle.idleGripR = s.idleGripR;   _mantle.idleGripL = s.idleGripL;
                    // 옛 저장 파일엔 손가락별 값이 없다 — null이면 코드 기본값을 유지한다.
                    if (s.idleFingerR != null) _mantle.idleFingerR = s.idleFingerR;
                    if (s.idleFingerL != null) _mantle.idleFingerL = s.idleFingerL;
                    _mantle.handEulerR = s.handEulerR; _mantle.handEulerL = s.handEulerL;
                }
                if (_spider != null) { _spider.gazeAmount = s.gazeAmount; _spider.gazeMaxDeg = s.gazeMaxDeg; }
                if (_legs != null && s.anchorRadius > 0f)
                {
                    _legs.anchorRadius = s.anchorRadius; _legs.anchorSpread = s.anchorSpread;
                    _legs.CreateAnchors();
                }
                if (_vmCam != null && s.nearClip > 0f) { _vmCam.nearClip = s.nearClip; _vmCam.vrNearClip = s.vrNearClip; }
                Debug.Log("[F6] 불러옴");
                return true;
            }
            catch { return false; }
        }

        // ── GUI 헬퍼 ────────────────────────────────────────────────────

        static GUIStyle _rich;
        static GUIStyle Rich()
        {
            if (_rich == null) _rich = new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true };
            return _rich;
        }

        /// <summary>
        /// 손가락별 <b>추가</b> 말림. 공통 말림에 더해지므로, 가장 펴진 손가락은 0으로 두고
        /// 나머지를 올린다. 다섯이 같으면 집게처럼 보인다.
        /// </summary>
        static void Fingers(string prefix, MantleRig.IdleFingerPose p)
        {
            if (p == null) return;
            p.index  = F(prefix + " 검지", p.index,  0f, 1f);
            p.middle = F(prefix + " 중지", p.middle, 0f, 1f);
            p.ring   = F(prefix + " 약지", p.ring,   0f, 1f);
            p.pinky  = F(prefix + " 소지", p.pinky,  0f, 1f);
            p.thumb  = F(prefix + " 엄지", p.thumb,  0f, 1f);
            p.spread = F(prefix + " 벌림", p.spread, -1f, 1f);
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
            GUILayout.Label(label, Rich(), GUILayout.Width(160f));
            GUILayout.Label(v.ToString("0.###"), Rich(), GUILayout.Width(52f));
            float r = GUILayout.HorizontalSlider(v, min, max);
            GUILayout.EndHorizontal();
            return r;
        }

        static Vector3 V3(string label, Vector3 v, float min, float max)
        {
            GUILayout.Label(label, Rich());
            return new Vector3(
                F("    X", v.x, min, max),
                F("    Y", v.y, min, max),
                F("    Z", v.z, min, max));
        }
    }

    /// <summary>Play 시작 시 자동 부착 — 씬에 오브젝트를 놓지 않아도 F6이 동작한다.</summary>
    public static class ViewmodelTuningPanelBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Object.FindFirstObjectByType<ViewmodelTuningPanel>() == null)
                new GameObject("[ViewmodelTuningPanel]").AddComponent<ViewmodelTuningPanel>();
        }
    }
}
