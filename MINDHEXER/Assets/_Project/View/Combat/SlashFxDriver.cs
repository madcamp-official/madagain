using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 베기 이펙트(SwordSlash)를 공격에 맞춰 띄운다. 각도·위치·타이밍을 F4에서 조절한다.
    ///
    /// 카메라 기준 로컬로 배치하므로 어디를 보든 화면상 각도가 일정하다.
    ///   pitch = 위아래로 눕히기 · yaw = 좌우로 돌리기 · roll = 화면 안에서의 사선 각도
    ///
    /// PoseCombatDriver가 공격 포즈를 재생할 때 함께 호출한다.
    /// </summary>
    public class SlashFxDriver : MonoBehaviour
    {
        public static SlashFxDriver Instance { get; private set; }

        [System.Serializable]
        public class Slot
        {
            public string name = "평타1";
            [Tooltip("SwordSlash.Spawn 종류 — 1 / 2 / t")]
            public string which = "1";
            [Tooltip("카메라 기준 위치(m) — x 우, y 상, z 앞")]
            public Vector3 offset = new Vector3(0f, -0.05f, 2.2f);
            [Tooltip("화면 안 사선 각도(도) — 이게 베는 방향")]
            public float roll = 35f;
            [Tooltip("위아래 눕히기(도)")]
            public float pitch;
            [Tooltip("좌우 돌리기(도)")]
            public float yaw;
            [Tooltip("전체 크기 배수")]
            public float scale = 1f;
            [Tooltip("공격 시작 후 이만큼 뒤에 뜬다(초) — 칼이 지나가는 순간에 맞춘다")]
            public float delay = 0.06f;
            [Tooltip("이 슬롯 사용")]
            public bool enabled = true;
            [Tooltip("같이 재생할 포즈 시퀀스 접두어 — 배치를 실제 동작과 함께 보기 위함")]
            public string posePrefix = "slash1_";

            // ── 방사형 버스트 (겐지식 — 피격 지점에서 사방으로 퍼지는 참격) ──
            [Tooltip("이 슬롯을 궤적이 아니라 방사형 버스트로 쓴다")]
            public bool  burst;
            [Tooltip("갈래 수")]
            public int   burstCount = 7;
            [Tooltip("퍼지는 각도 범위(도) — 360이면 완전 방사")]
            public float burstSpread = 360f;
            [Tooltip("각 갈래 각도에 섞는 난수(도)")]
            public float burstJitter = 12f;
            [Tooltip("갈래별 길이 편차(0=균일, 0.5면 ±50%)")]
            public float burstLengthVary = 0.35f;
            [Tooltip("갈래별 크기 편차")]
            public float burstScaleVary = 0.25f;
            [Tooltip("갈래가 순차로 터지는 간격(초) — 0이면 동시")]
            public float burstStagger = 0.012f;

            // ── 추종 (고속 이동 시 이펙트가 뒤로 밀리는 문제) ──
            [Tooltip("0 월드 고정 · 1 카메라 고정 · 2 지연 추종 · 3 단계 전환")]
            public int   follow = SlashFollow.Soft;
            [Tooltip("지연 추종 — 클수록 빨리 따라붙는다")]
            public float followSpeed = 8f;
            [Tooltip("지연 추종 시 회전도 따라갈지")]
            public bool  followRotation = true;
            [Tooltip("단계 전환 — 이 시간 동안 붙어 있다가 월드에 놓는다(초)")]
            public float attachTime = 0.09f;
            [Tooltip("뷰모델 레이어로 그린다 — 벽·적을 뚫고 항상 위에 보인다")]
            public bool  onViewmodelLayer = true;

            // ── 형태 ──
            public float length       = 9f;      // 베는 선 길이
            public float radiusWide   = 0.85f;   // 칼날 방향 반경(넓은 축)
            public float radiusThick  = 0.30f;   // 두께 방향 반경(짧은 축)
            public float taperPower   = 1.4f;    // 끝으로 갈수록 뾰족해지는 정도
            public int   layerCount   = 4;       // 중첩 셸 수
            public float innerShellScale = 0.28f;// 안쪽 셸 크기 비율
            public float layerFalloff = 0.55f;   // 바깥 셸이 옅어지는 정도

            // ── 타이밍 ──
            public float revealTime = 0.05f;     // 그어지는 시간
            public float holdTime   = 0.04f;     // 유지
            public float fadeTime   = 0.20f;     // 사라짐
            public float revealSoft = 0.12f;     // 그어지는 경계 부드러움
            public float tailFade   = 0.45f;     // 꼬리 흐림

            // ── 색 ──
            public Color colorLow  = new Color(0.12f, 1.0f, 0.10f, 1f);   // 옅은 바깥
            public Color colorMid  = new Color(1.6f, 1.15f, 0.05f, 1f);   // 중간
            public Color colorHigh = new Color(3.2f, 3.2f, 2.6f, 1f);     // 밝은 코어
            public float ramp1 = 0.18f, ramp2 = 0.55f, ramp3 = 0.85f;     // 색 전환 지점
            public float intensity = 0.80f;
            public float contrast  = 1.35f;
            [Tooltip("0 가산(빛남) · 1 알파(검정 가능) · 2 곱셈(가장 검음)")]
            public int   blendMode = SwordSlash.BlendAdd;

            // ── 노이즈 ──
            public float   noiseAmount  = 0.5f;
            public Vector2 noiseTile1   = new Vector2(3f, 0.35f);
            public Vector2 noiseScroll1 = new Vector2(-0.8f, 0.05f);
            public Vector2 noiseTile2   = new Vector2(7f, 0.5f);
            public Vector2 noiseScroll2 = new Vector2(-1.5f, -0.04f);

            /// <summary>이 슬롯의 비주얼 값을 실제 이펙트에 씌운다.
            /// SwordSlash는 형태가 바뀌면 스스로 메시를 다시 만들고 셰이더 값은 매 프레임 반영하므로,
            /// 생성 뒤에 덮어써도 즉시 적용된다.</summary>
            public void ApplyTo(SwordSlash fx)
            {
                if (fx == null) return;
                fx.length = length; fx.radiusWide = radiusWide; fx.radiusThick = radiusThick;
                fx.taperPower = taperPower;
                fx.layerFalloff = layerFalloff;
                fx.revealTime = revealTime; fx.holdTime = holdTime; fx.fadeTime = fadeTime;
                fx.revealSoft = revealSoft; fx.tailFade = tailFade;
                fx.colorLow = colorLow; fx.colorMid = colorMid; fx.colorHigh = colorHigh;
                fx.ramp1 = ramp1; fx.ramp2 = ramp2; fx.ramp3 = ramp3;
                fx.intensity = intensity; fx.contrast = contrast;
                fx.blendMode = blendMode;
                fx.noiseAmount = noiseAmount;
                fx.noiseTile1 = noiseTile1; fx.noiseScroll1 = noiseScroll1;
                fx.noiseTile2 = noiseTile2; fx.noiseScroll2 = noiseScroll2;
                // layerCount·innerShellScale은 Awake에서 셸을 만들 때만 쓰이므로
                // 생성 뒤 바꿔도 반영되지 않는다 → 재생성이 필요하다(패널이 안내).
            }

            /// <summary>셸 구조는 생성 시점에만 반영된다 — 값이 바뀌었는지 판단용.</summary>
            public bool ShellChanged(SwordSlash fx) =>
                fx != null && (fx.layerCount != layerCount ||
                               !Mathf.Approximately(fx.innerShellScale, innerShellScale));
        }

        [Tooltip("공격 종류별 설정")]
        public Slot[] slots =
        {
            new Slot { name = "평타1",  which = "1", roll =  35f, posePrefix = "slash1_" },
            new Slot { name = "평타2",  which = "2", roll = -35f, posePrefix = "slash2_" },
            // 찌르기는 확정 피격이라 궤적이 어색하다 — 기본으로 끄고 피격 버스트에 맡긴다
            new Slot { name = "찌르기", which = "t", roll =   0f, posePrefix = "thrust1_",
                       offset = new Vector3(0f, -0.02f, 2.6f), scale = 0.9f, delay = 0.04f,
                       enabled = false },
            // 명중 순간 피격 지점에서 사방으로 터지는 참격 — 평타1·2·찌르기 전부 공용
            new Slot { name = "피격", which = "t", posePrefix = "slash1_",
                       burst = true, scale = 0.35f, delay = 0f,
                       length = 5f, radiusWide = 0.5f, radiusThick = 0.22f, taperPower = 1.8f,
                       revealTime = 0.03f, holdTime = 0.02f, fadeTime = 0.14f,
                       follow = SlashFollow.World },
        };

        [Tooltip("이펙트 전체 사용")]
        public bool active = true;

        // ── 저장 (Play 중 바꾼 값은 원래 사라진다 — 파일로 남긴다) ──
        [System.Serializable] class SlotFile { public Slot[] slots; }

        public static string SavePath => "Assets/_Project/Poses/slashfx.json";

        void Awake()
        {
            Instance = this;
            LoadFromDisk();
        }

        /// <summary>현재 값을 파일로. 다음 Play부터 자동 적용된다.</summary>
        public bool Save()
        {
            try
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(SavePath));
                System.IO.File.WriteAllText(SavePath,
                    JsonUtility.ToJson(new SlotFile { slots = slots }, true), System.Text.Encoding.UTF8);
                Debug.Log("[베기 이펙트] 저장: " + SavePath);
                return true;
            }
            catch (System.Exception e) { Debug.LogWarning("[베기 이펙트] 저장 실패: " + e.Message); return false; }
        }

        /// <summary>파일이 있으면 읽어 적용.</summary>
        public bool LoadFromDisk()
        {
            try
            {
                if (!System.IO.File.Exists(SavePath)) return false;
                var f = JsonUtility.FromJson<SlotFile>(System.IO.File.ReadAllText(SavePath, System.Text.Encoding.UTF8));
                if (f == null || f.slots == null || f.slots.Length == 0) return false;
                slots = f.slots;
                return true;
            }
            catch { return false; }
        }

        public Slot Find(string name)
        {
            if (slots == null) return null;
            foreach (var s in slots) if (s.name == name) return s;
            return null;
        }

        /// <summary>슬롯 이름으로 발동(지연 포함).</summary>
        /// <summary>슬롯 이름으로 발동.
        /// delayOverride ≥ 0이면 슬롯 지연 대신 그 값을 쓴다(즉발 판정에서 "선딜 뒤 이펙트"용).
        /// 코루틴이라 도중에 공격이 캔슬돼도 예약된 이펙트는 그대로 나온다.</summary>
        public void Fire(string slotName, float delayOverride = -1f)
        {
            if (!active) return;
            var s = Find(slotName);
            if (s == null || !s.enabled) return;
            float d = delayOverride >= 0f ? delayOverride : s.delay;
            if (d > 0f) StartCoroutine(FireDelayed(s, d));
            else SpawnNow(s);
        }

        System.Collections.IEnumerator FireDelayed(Slot s, float wait)
        {
            if (wait > 0f) yield return new WaitForSeconds(wait);
            SpawnNow(s);
        }

        /// <summary>지연 없이 즉시 생성(F5 미리보기용). hold=true면 사라지지 않고 남는다.</summary>
        public SwordSlash SpawnNow(Slot s, bool hold = false)
        {
            var cam = ResolveCam();
            if (cam == null || s == null) return null;

            Transform ct = cam.transform;
            Vector3 pos = ct.TransformPoint(s.offset);
            // Spawn이 which에 따라 roll을 또 더하므로, 여기서는 pitch·yaw만 주고
            // roll은 생성 뒤에 직접 덮어써 F5 값이 그대로 반영되게 한다.
            Quaternion rot = ct.rotation * Quaternion.Euler(s.pitch, s.yaw, 0f);

            var fx = SwordSlash.Spawn(pos, rot, s.which);
            if (fx == null) return null;
            fx.transform.rotation = rot * Quaternion.Euler(0f, 0f, s.roll);
            fx.transform.localScale = Vector3.one * Mathf.Max(0.05f, s.scale);
            s.ApplyTo(fx);            // Spawn이 which별로 덮어쓴 값을 슬롯 값으로 되돌린다
            fx.hold = hold;
            fx.Refresh();             // ★ 첫 프레임 초록 번쩍 방지 — 즉시 셰이더에 반영

            // 뷰모델 레이어로 올리면 벽·적을 뚫고 항상 위에 그려진다.
            // 셸은 Awake에서 자식으로 만들어지므로 생성 뒤에 입혀야 한다.
            if (s.onViewmodelLayer)
            {
                int vl = LayerMask.NameToLayer(ViewmodelCamera.DefaultLayer);
                if (vl >= 0) ViewmodelCamera.SetLayerRecursive(fx.transform, vl);
            }

            // 미리보기는 패널이 직접 위치를 잡으므로 추종을 걸지 않는다
            if (!hold)
                fx.gameObject.AddComponent<SlashFollow>()
                  .Init(ct, s.follow, s.followSpeed, s.followRotation, s.attachTime);

            return fx;
        }

        // ── 방사형 버스트 ──

        /// <summary>피격 지점(월드)에서 사방으로 참격을 터뜨린다. 겐지식 연출.
        /// 카메라를 향해 정렬하므로 어느 방향에서 봐도 별 모양이 보인다.</summary>
        /// <summary>지정한 월드 위치·회전에 슬롯의 궤적 이펙트를 1회 띄운다(칼끝 단면 등).
        /// 배치 오프셋·추종은 무시하고 넘어온 좌표를 그대로 쓴다.</summary>
        public SwordSlash SpawnAt(string slotName, Vector3 worldPos, Quaternion rot)
        {
            if (!active) return null;
            var s = Find(slotName);
            // enabled는 "공격 시 자동 발동" 여부다. SpawnAt은 명시적 호출(단면 이펙트 등)이라
            // enabled와 무관하게 띄운다 — 찌르기 궤적은 자동으론 꺼두고 여기서만 쓸 수 있게.
            if (s == null) return null;

            var fx = SwordSlash.Spawn(worldPos, rot, s.which);
            if (fx == null) return null;
            fx.transform.SetPositionAndRotation(worldPos, rot * Quaternion.Euler(0f, 0f, s.roll));
            fx.transform.localScale = Vector3.one * Mathf.Max(0.05f, s.scale);
            s.ApplyTo(fx);
            fx.Refresh();
            if (s.onViewmodelLayer)
            {
                int vl = LayerMask.NameToLayer(ViewmodelCamera.DefaultLayer);
                if (vl >= 0) ViewmodelCamera.SetLayerRecursive(fx.transform, vl);
            }
            return fx;
        }

        public void BurstAt(Vector3 worldPos, Slot s = null, float delay = 0f)
        {
            s = s ?? Find("피격");
            if (!active || s == null || !s.enabled) return;
            if (delay > 0f) { StartCoroutine(BurstDelayed(worldPos, s, delay)); return; }
            var cam = ResolveCam(); if (cam == null) return;

            int n = Mathf.Max(1, s.burstCount);
            for (int i = 0; i < n; i++)
            {
                float baseAng = s.burstSpread >= 359f
                    ? i * (360f / n)                                   // 완전 방사 — 균등 분할
                    : -s.burstSpread * 0.5f + (n == 1 ? 0f : s.burstSpread * i / (n - 1));
                float ang = baseAng + Random.Range(-s.burstJitter, s.burstJitter);

                if (s.burstStagger > 0.0001f) StartCoroutine(BurstOne(worldPos, s, ang, i * s.burstStagger));
                else SpawnRay(worldPos, s, ang);
            }
        }

        /// <summary>선딜만큼 기다렸다 터뜨린다. 코루틴이라 도중 캔슬돼도 예약분은 나온다.</summary>
        System.Collections.IEnumerator BurstDelayed(Vector3 pos, Slot s, float wait)
        {
            yield return new WaitForSeconds(wait);
            BurstAt(pos, s);
        }

        System.Collections.IEnumerator BurstOne(Vector3 pos, Slot s, float ang, float wait)
        {
            if (wait > 0f) yield return new WaitForSeconds(wait);
            SpawnRay(pos, s, ang);
        }

        /// <summary>버스트의 갈래 하나. 카메라를 바라보게 세우고 roll로 방향을 준다.</summary>
        void SpawnRay(Vector3 worldPos, Slot s, float angDeg)
        {
            var cam = ResolveCam(); if (cam == null) return;

            // 카메라를 향하도록 정렬 → 화면상에서 별 모양이 된다
            Quaternion face = Quaternion.LookRotation(worldPos - cam.transform.position, cam.transform.up);
            Quaternion rot  = face * Quaternion.Euler(s.pitch, s.yaw, 0f) * Quaternion.Euler(0f, 0f, angDeg);

            var fx = SwordSlash.Spawn(worldPos, rot, s.which);
            if (fx == null) return;
            fx.transform.rotation = rot;

            s.ApplyTo(fx);
            // 갈래마다 길이·크기를 흔들어 규칙적으로 안 보이게
            fx.length *= 1f + Random.Range(-s.burstLengthVary, s.burstLengthVary);
            fx.transform.localScale = Vector3.one *
                Mathf.Max(0.05f, s.scale * (1f + Random.Range(-s.burstScaleVary, s.burstScaleVary)));
            fx.Refresh();

            if (s.onViewmodelLayer)
            {
                int vl = LayerMask.NameToLayer(ViewmodelCamera.DefaultLayer);
                if (vl >= 0) ViewmodelCamera.SetLayerRecursive(fx.transform, vl);
            }
        }

        // ── 포즈와 함께 재생 (배치를 실제 동작 속에서 확인) ──
        [Tooltip("일정 간격으로 반복 재생")]
        public bool  autoRepeat;
        [Tooltip("반복 간격(초) — 0이면 시퀀스 길이에 맞춰 자동")]
        public float repeatInterval;
        float repeatTimer;
        Slot  repeatSlot;

        /// <summary>대응 포즈 시퀀스와 이펙트를 같이 재생한다(이펙트는 슬롯 지연을 지킨다).</summary>
        public bool PlayWithPose(Slot s)
        {
            if (s == null) return false;
            PreviewHide();                       // 고정 미리보기가 떠 있으면 시야를 가린다
            repeatSlot = s;

            var pp = PosePlayer.Instance;
            int n = 0;
            if (pp != null && !string.IsNullOrEmpty(s.posePrefix))
                n = pp.Play(s.posePrefix, pp.segTime, false);

            Fire(s.name);                        // 지연 포함해서 이펙트도 발동
            repeatTimer = 0f;
            return n >= 2;
        }

        /// <summary>반복 간격 — 지정값이 없으면 포즈 길이 + 여유로 잡는다.</summary>
        float RepeatPeriod(Slot s)
        {
            if (repeatInterval > 0.01f) return repeatInterval;
            float fxLen = s.delay + s.revealTime + s.holdTime + s.fadeTime;
            var pp = PosePlayer.Instance;
            float poseLen = pp != null ? pp.CurrentTotalTime : 0f;
            return Mathf.Max(fxLen, poseLen) + 0.35f;
        }

        // ── 튜닝용 고정 미리보기 ──
        SwordSlash preview;
        Slot previewSlot;

        public bool PreviewOn => preview != null;

        /// <summary>사라지지 않는 미리보기를 띄운다(값을 바꾸면 즉시 반영된다).</summary>
        public void PreviewShow(Slot s)
        {
            PreviewHide();
            previewSlot = s;
            preview = SpawnNow(s, true);
        }

        public void PreviewHide()
        {
            if (preview != null) Destroy(preview.gameObject);
            preview = null; previewSlot = null;
        }

        /// <summary>셸 개수처럼 생성 시에만 반영되는 값이 바뀌면 미리보기를 다시 만든다.</summary>
        public void PreviewRebuild()
        {
            if (previewSlot != null) PreviewShow(previewSlot);
        }

        void LateUpdate()
        {
            // 반복 재생
            if (autoRepeat && repeatSlot != null)
            {
                repeatTimer -= Time.deltaTime;
                if (repeatTimer <= 0f)
                {
                    repeatTimer = RepeatPeriod(repeatSlot);
                    var pp = PosePlayer.Instance;
                    if (pp != null && !string.IsNullOrEmpty(repeatSlot.posePrefix))
                        pp.Play(repeatSlot.posePrefix, pp.segTime, false);
                    Fire(repeatSlot.name);
                }
            }

            // 미리보기가 떠 있으면 슬라이더 변경을 매 프레임 반영
            if (preview == null || previewSlot == null) return;
            var cam = ResolveCam();
            if (cam != null)
            {
                Transform ct = cam.transform;
                Quaternion rot = ct.rotation * Quaternion.Euler(previewSlot.pitch, previewSlot.yaw, 0f);
                preview.transform.position = ct.TransformPoint(previewSlot.offset);
                preview.transform.rotation = rot * Quaternion.Euler(0f, 0f, previewSlot.roll);
                preview.transform.localScale = Vector3.one * Mathf.Max(0.05f, previewSlot.scale);
            }
            previewSlot.ApplyTo(preview);
        }

        static Camera ResolveCam()
        {
            var main = Main.Instance;
            var c = main != null ? main.Cam : null;
            return c != null ? c : Camera.main;
        }
    }

    /// <summary>Play 시 자동 부착.</summary>
    public static class SlashFxDriverBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Object.FindFirstObjectByType<SlashFxDriver>() == null)
                new GameObject("[SlashFxDriver]").AddComponent<SlashFxDriver>();
        }
    }
}
