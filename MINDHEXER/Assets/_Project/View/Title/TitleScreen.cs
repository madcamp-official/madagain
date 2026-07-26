using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

namespace Game.View
{
    // >>> [타이틀 화면, 2026-07-23] 게임이 아레나로 곧장 떨어지던 걸 앞에 한 겹 세운다.
    //
    // 만드는 방식은 HoloHud와 <b>같은 규칙</b>이다 — 프리팹·씬 세팅 없이 코드로만 조립하고,
    // 생성형 아트 PNG를 한 장도 쓰지 않고 전부 정점 메시로 그린다.
    //
    // <b>레이아웃은 고스트러너 계열</b>(레퍼런스 지정)이다: 화면 한가운데 큰 워드마크, 그 아래
    // 가운데 정렬된 모서리 깎은 버튼 기둥, 선택된 것만 주황으로 꽉 차고 글자가 어두워진다.
    // 예전 좌측 정렬 계기판 배치는 버렸다.
    //
    // 그 위에 이 게임만의 것을 둘 얹는다:
    //  1. <b>칼금</b> — 워드마크를 PRE|COG로 가르는 세로 베기. 고스트러너가 로고 가운데 카타나를
    //     세워 둔 자리인데, 우리 쪽에선 "현재와 미래를 가르는 칼"이라는 뜻이 그대로 붙는다.
    //     부팅할 때 이 칼금이 먼저 그어지고 그 자리에서 글자가 열린다.
    //  2. <b>예지 잔상</b> — 워드마크 뒤로 청록 잔상이 앞서 흘렀다 되돌아온다. 인게임에서
    //     미래를 고스트로 보여주는 것과 같은 규칙이라, 첫 화면에서 이미 장르가 읽힌다.
    //
    // 게임 정지는 <b>timeScale로 하지 않는다</b>. timeScale은 예측 컨트롤러가 매 프레임
    // 덮어쓰는 값이라 여기서 건드리면 서로 싸운다. 대신 Main 컴포넌트를 끈다.
    //
    // ★ 끄는 시점이 중요하다 — <b>Main.Start가 돌기 전</b>이어야 한다. Start는 런타임 NavMesh를
    //   굽느라 몇 초가 걸리고, 그동안 화면이 새까맣다(예전엔 그 대기를 다 맞고 나서야 타이틀이
    //   떴다). Awake에서 AutoBoot.EnsureMain()으로 Main을 <b>확보하면서 동시에</b> 꺼 두면
    //   Start 자체가 미뤄지고, 타이틀이 첫 프레임부터 뜬다. 굽기는 "새 게임"을 누른 뒤로 밀린다.

    /// <summary>타이틀 화면을 띄울지. 전투·포즈 작업 중엔 false로 두면 곧장 플레이로 들어간다.</summary>
    public static class TitleStyle
    {
        public static bool ShowOnPlay = true;
    }

    /// <summary>
    /// 시작 화면. 키보드 전용(↑↓ 선택 · ENTER 실행 · ESC 뒤로)이라 EventSystem·레이캐스터가
    /// 필요 없다 — 커서를 띄우지 않으므로 FPS의 마우스 잠금과도 싸우지 않는다.
    /// </summary>
    public class TitleScreen : MonoBehaviour
    {
        public static TitleScreen Instance { get; private set; }

        /// <summary>튜닝 패널(F7)이 구도를 실시간으로 만질 때 쓴다.</summary>
        public TitleActor Actor => actor;

        // ── 색 ───────────────────────────────────────────────────────────
        /// <summary>선택된 버튼을 꽉 채우는 주황. 레퍼런스의 강조색을 HUD 체력색(#F94B1E)
        /// 쪽으로 당겨 게임 팔레트 안에 남긴다.</summary>
        static readonly Color Ember = new Color(0.976f, 0.478f, 0.106f);
        static readonly Color Cyan  = new Color(0.42f, 0.92f, 1f);
        /// <summary>주황 위에 얹히는 글자. 검정이 아니라 아주 어두운 청록 — 순검정은 주황 위에서
        /// 구멍처럼 뚫려 보인다.</summary>
        static readonly Color OnEmber = new Color(0.043f, 0.055f, 0.063f);

        const float RefW = 1920f, RefH = 1080f;

        // ── 배치 ─────────────────────────────────────────────────────────
        // 왼쪽 레일(워드마크·메뉴) + 오른쪽 주인공. 레퍼런스의 무게중심을 그대로 옮긴 것이다 —
        // 글자를 다 왼쪽에 몰아야 오른쪽에 선 사람과 자리를 다투지 않는다.
        const float RailX = 300f;                     // 왼쪽 정렬 기준선(메뉴 전용)
        /// <summary>[피드백, 2026-07-23] 워드마크는 <b>화면 정중앙</b>. 메뉴만 왼쪽에 남는다 —
        /// 레퍼런스가 로고를 가운데 두고 캐릭터가 그 오른쪽을 파고드는 구성이다.</summary>
        const float LogoCx = 960f, LogoCy = 700f;
        const float LogoSize = 138f;
        const float SlashY0 = 596f, SlashY1 = 806f;   // 칼금이 지나는 세로 구간
        const float BtnW = 400f, BtnH = 50f;
        const float BtnCx = RailX + BtnW * 0.5f;      // 버튼 기둥은 레일에 왼쪽 끝을 맞춘다
        const float BtnTopCy = 415f;                  // 첫 버튼의 중심 y
        const float BtnStep = 62f;
        const float HintCy = 150f;

        /// <summary>주인공을 띄우는 자리(렌더 텍스처 크기와 비율이 같아야 안 찌그러진다).</summary>
        /// <summary>렌더 텍스처가 2:3이므로 여기도 2:3을 지켜야 안 찌그러진다(768×1152).</summary>
        const float ActorCx = 1400f, ActorCy = 560f, ActorW = 920f, ActorH = 1380f;

        /// <summary>[피드백, 2026-07-23] 예지 잔상 세기 — "최대로". 미리보기 슬라이더 상한값.</summary>
        const float EchoGain = 2.5f;

        const float FadeIn = 1.25f;
        const float FadeOut = 0.32f;

        enum Item { NewGame, Controls, Quit }
        static readonly string[] ItemLabel = { "새 게임", "조 작", "종 료" };

        /// <summary>맵 스냅샷. Precog → 타이틀 배경 굽기 로 다시 찍는다.</summary>
        public const string BackdropResource = "TitleBackdrop";

        Canvas canvas;
        RectTransform root;
        Image veil;
        RawImage bgImage, actorImage;
        RawImage[] actorEchoes;   // 상시 잔상(본체 뒤)
        RawImage[] actorSlices;   // 지지직 띠(본체 위)
        float[] sliceShift;
        float glitchEndsAt, nextGlitchAt, glitchStrength;
        TitleActor actor;

        /// <summary>지지직 띠 개수. 많을수록 잘게 찢기지만, 7 정도면 눈은 "깨졌다"로 읽는다.</summary>
        const int GlitchBands = 7;
        /// <summary>글리치 사이 간격(초) 하한·상한 — 규칙적이면 장식이 되고, 불규칙해야 고장으로 읽힌다.</summary>
        const float GlitchGapMin = 1.6f, GlitchGapMax = 4.5f;
        /// <summary>한 번 터질 때 지속(초). 길면 고장난 화면이 되고, 짧아야 "지지직"이 된다.</summary>
        const float GlitchHoldMin = 0.06f, GlitchHoldMax = 0.20f;
        /// <summary>띠가 밀리는 최대 폭(기준 해상도 px).</summary>
        const float GlitchShiftMax = 46f;
        /// <summary>띠가 다시 뽑히는 주기(초). 매 프레임 바꾸면 지글거리기만 하고 형태가 안 남는다.</summary>
        const float GlitchStutter = 0.045f;
        float lastStutterAt;
        HoloScan scan;
        TitleSlash slash;
        Text logo;
        Text[] echoes;
        TitleButton[] buttons;
        Text[] btnLabels;
        Text hint, loading;
        RectTransform controlsRoot;

        Item selected;
        bool controlsOpen;
        bool closing, launching, started;
        float born, closeStart;
        float flashPhase;
        float[] hi;              // 버튼별 강조량 0~1 (목표치로 부드럽게 따라간다)

        void Awake()
        {
            Instance = this;
            born = Time.unscaledTime;

            // ★ Main.Start가 돌기 전에 재운다 — 위 주석의 "검은 화면" 문제.
            //   EnsureMain은 AutoBoot과 공유하는 진입점이라, 둘 중 누가 먼저 돌든 Main은 하나다.
            var main = AutoBoot.EnsureMain();
            main.enabled = false;

            Build();
            UiVisibility.SuppressedByMenu = true;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            UiVisibility.SuppressedByMenu = false;
            // 주인공은 캔버스 밖 별개 루트라(전용 카메라·조명을 달고 있다) 같이 안 지워진다.
            if (actor != null) Destroy(actor.gameObject);
        }

        void Update()
        {
            // ★ 부팅 시각은 Awake가 아니라 <b>첫 Update</b>에 잡는다. Awake와 첫 프레임 사이에는
            //   도메인 리로드·오디오 합성 같은 게 끼어 몇 초가 지나기도 한다. Awake 기준으로
            //   재면 그 몇 초가 age에 미리 얹혀, 눈에 보이기도 전에 연출이 끝나 있다.
            if (!started) { started = true; born = Time.unscaledTime; }

            float dt = Time.unscaledDeltaTime;
            float age = Time.unscaledTime - born;

            // 메뉴가 떠 있는 동안 게임은 재워 둔다(다른 코드가 켜도 여기서 되돌린다).
            if (!launching && Main.Instance != null && Main.Instance.enabled)
                Main.Instance.enabled = false;

            // 커서 해제도 매 프레임 확인한다 — 메뉴는 키보드 전용이라 커서는 풀되
            // 보이게 하진 않는다(윈도 화살표 하나로 분위기가 다 깨진다).
            // 튜닝 패널(F7)이 열려 있을 땐 예외 — 슬라이더를 잡으려면 커서가 보여야 한다.
            if (!launching && Cursor.lockState != CursorLockMode.None && !TitleTunePanel.Open)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = false;
            }

            // 패널이 열려 있으면 메뉴 키 입력을 멈춘다 — 안 그러면 스크롤하려고 누른
            // ↑↓가 메뉴 선택까지 옮기고, ENTER가 게임을 시작해 버린다.
            if (!TitleTunePanel.Open) ReadKeys();
            Animate(age, dt);

            if (closing)
            {
                float k = Mathf.Clamp01((Time.unscaledTime - closeStart) / FadeOut);
                SetAlpha(k);
                if (k >= 1f) BeginLaunch();
            }
        }

        // ── 입력 ─────────────────────────────────────────────────────────
        void ReadKeys()
        {
            if (closing || launching) return;
            var kb = Keyboard.current;
            if (kb == null) return;

            bool confirm = kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame
                        || kb.spaceKey.wasPressedThisFrame;

            if (controlsOpen)
            {
                if (confirm || kb.escapeKey.wasPressedThisFrame) SetControlsOpen(false);
                return;
            }

            int move = 0;
            if (kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame) move++;
            if (kb.upArrowKey.wasPressedThisFrame   || kb.wKey.wasPressedThisFrame) move--;
            if (move != 0)
            {
                int n = ItemLabel.Length;
                selected = (Item)(((int)selected + move + n) % n);   // 끝에서 반대편으로 감긴다
                buttons[(int)selected].Punch();
                flashPhase = 1f;                                     // 옮길 때마다 잔상이 한 번 튄다
            }

            if (confirm) Activate();

            // 타이틀에서의 ESC는 곧장 종료가 아니라 "종료 항목으로 커서 옮기기"다 —
            // 실수로 한 번 누른 게 게임을 닫으면 안 된다.
            if (kb.escapeKey.wasPressedThisFrame && selected != Item.Quit)
            {
                selected = Item.Quit;
                buttons[(int)selected].Punch();
            }
        }

        void Activate()
        {
            switch (selected)
            {
                case Item.NewGame:
                    closing = true;
                    closeStart = Time.unscaledTime;
                    flashPhase = 1f;
                    break;
                case Item.Controls:
                    SetControlsOpen(true);
                    break;
                case Item.Quit:
#if UNITY_EDITOR
                    UnityEditor.EditorApplication.isPlaying = false;
#else
                    Application.Quit();
#endif
                    break;
            }
        }

        void SetControlsOpen(bool open)
        {
            controlsOpen = open;
            controlsRoot.gameObject.SetActive(open);
            flashPhase = 1f;
        }

        /// <summary>
        /// 메뉴가 다 걷힌 뒤. <b>여기서 곧장 Main을 켜지 않는다</b> — 켜는 순간 Start가 NavMesh를
        /// 굽느라 프레임이 몇 초 멈추는데, 그 전에 "생성 중" 글자가 화면에 한 번은 그려져야
        /// 멈춘 게 고장으로 안 보인다. 그래서 글자만 띄우고 실제 기동은 다음 프레임으로 미룬다.
        /// </summary>
        void BeginLaunch()
        {
            if (launching) return;
            launching = true;
            loading.gameObject.SetActive(true);
        }

        void LateUpdate()
        {
            // BeginLaunch가 켠 "생성 중"이 이 프레임에 그려졌다. 이제 실제로 깨운다.
            if (!launching || !loading.gameObject.activeSelf) return;
            if (Time.unscaledTime - closeStart < FadeOut + 0.05f) return;   // 최소 한 프레임 보장

            UiVisibility.SuppressedByMenu = false;
            if (Main.Instance != null) Main.Instance.enabled = true;
            else AutoBoot.EnsureMain().enabled = true;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            // [등장 컷신, 2026-07-23] 메뉴가 걷힌 자리를 히어로 랜딩이 이어받는다.
            // ★ Main을 켠 <b>직후</b>에 세운다 — 컷신이 Awake에서 곧바로 조작을 잠그므로
            //   Main.Start(NavMesh 굽기)가 도는 동안 입력이 새지 않고, 굽는 몇 초도
            //   컷신의 검은 장막이 "생성 중" 글자와 함께 덮는다.
            if (IntroStyle.PlayOnNewGame) IntroCutscene.Play();

            Destroy(gameObject);
        }

        // ── 연출 ─────────────────────────────────────────────────────────
        void Animate(float age, float dt)
        {
            scan.Progress = Mathf.Repeat(age * 0.24f, 1f);
            scan.SetVerticesDirty();

            // 칼금이 먼저 그어지고, 그 자리에서 글자가 열린다.
            slash.Reveal = Mathf.Clamp01((age - 0.08f) / 0.34f);
            slash.Sweep = Mathf.Repeat(age * 0.5f, 1f);
            slash.Flash = flashPhase;
            slash.SetVerticesDirty();

            // 예지 잔상 — 평소엔 느리게 흔들리다가 flashPhase가 서면 크게 앞서 나갔다 되돌아온다.
            flashPhase = Mathf.Max(0f, flashPhase - dt * 2.6f);
            float burst = flashPhase * flashPhase;
            for (int i = 0; i < echoes.Length; i++)
            {
                float lead = i + 1;
                float wob = Mathf.Sin(age * (0.9f + 0.23f * lead) + lead * 1.7f);
                float dx = (wob * (3f + 2.5f * lead) + burst * 30f * lead) * EchoGain;
                float dy = Mathf.Sin(age * (0.6f + 0.11f * lead)) * 1.6f * lead * EchoGain;
                // 기준 위치에 흔들림을 더한다 — dx/dy만 넣으면 워드마크가 화면 좌하단으로 날아간다.
                ((RectTransform)echoes[i].transform).anchoredPosition =
                    new Vector2(LogoCx + dx, LogoCy + dy);
                echoes[i].color = new Color(Cyan.r, Cyan.g, Cyan.b,
                    Mathf.Min(0.55f, (0.18f / lead) * (1f + burst * 2.4f) * EchoGain));
            }
            logo.color = Color.Lerp(Color.white, Cyan, 0.06f + 0.30f * burst);

            AnimateActorGhosts(age, burst);

            // 버튼 강조는 목표치로 붙인다 — 즉시 바뀌면 딱딱 끊기고, 느리면 끌린다.
            for (int i = 0; i < buttons.Length; i++)
            {
                float want = i == (int)selected ? 1f : 0f;
                hi[i] = Mathf.Lerp(hi[i], want, 1f - Mathf.Exp(-20f * dt));
                buttons[i].Highlight = hi[i];
                buttons[i].Tick(dt);
                btnLabels[i].color = Color.Lerp(new Color(1f, 1f, 1f, 0.62f), OnEmber, hi[i]);
            }

            // ★ "다 떴으면 건너뛴다"는 최적화를 하지 않는다. 예전엔 age가 페이드 창을 넘으면
            //   여기서 조기 반환했는데, 그러면 <b>알파를 한 번도 못 쓴 채</b> 창이 지나가는
            //   경우가 생긴다 — 그러면 화면이 통째로 투명해서 아무것도 안 보인다.
            //   CanvasGroup 몇 개 쓰는 비용은 무시할 만하고, 상태가 항상 값과 일치하는 쪽이 옳다.
            if (!closing) SetAlpha(0f);
        }

        /// <summary>
        /// 주인공 주변의 예지 잔상과 지지직.
        ///
        /// <para>둘 다 <b>같은 렌더 텍스처를 다시 그리는 것</b>이라 3D 렌더 비용이 늘지 않는다.
        /// 잔상은 본체 뒤에 청록으로 깔려 늘 흐르고, 지지직은 본체를 가로로 자른 띠를
        /// 순간적으로 좌우로 밀어 만든다 — 규칙적으로 터지면 장식이 되므로 간격도 세기도
        /// 매번 다시 뽑는다.</para>
        /// </summary>
        void AnimateActorGhosts(float age, float burst)
        {
            if (actor == null || actorEchoes == null) return;

            for (int i = 0; i < actorEchoes.Length; i++)
            {
                float lead = i + 1;
                float wob = Mathf.Sin(age * (0.7f + 0.19f * lead) + lead * 2.3f);
                float dx = (wob * (5f + 4f * lead) + burst * 26f * lead) * EchoGain;
                float dy = Mathf.Cos(age * (0.5f + 0.13f * lead)) * 2.2f * lead;
                ((RectTransform)actorEchoes[i].transform).anchoredPosition =
                    new Vector2(ActorCx + dx, ActorCy + dy);
                actorEchoes[i].color = new Color(Cyan.r, Cyan.g, Cyan.b,
                    Mathf.Min(0.5f, (0.16f / lead) * (1f + burst * 2f) * EchoGain));
            }

            if (actorSlices == null) return;
            float now = Time.unscaledTime;

            // 다음 글리치 예약 — 첫 프레임엔 nextGlitchAt이 0이라 바로 한 번 터진다.
            if (now >= nextGlitchAt && now >= glitchEndsAt)
            {
                glitchEndsAt = now + Random.Range(GlitchHoldMin, GlitchHoldMax);
                nextGlitchAt = glitchEndsAt + Random.Range(GlitchGapMin, GlitchGapMax);
                glitchStrength = Random.Range(0.45f, 1f);
            }

            bool on = now < glitchEndsAt;
            // 띠를 매 프레임 새로 뽑으면 지글거리기만 한다 — 몇 프레임 유지해야 형태가 남는다.
            if (on && now - lastStutterAt >= GlitchStutter)
            {
                lastStutterAt = now;
                for (int i = 0; i < sliceShift.Length; i++)
                    sliceShift[i] = Random.Range(-1f, 1f) * GlitchShiftMax * glitchStrength;
            }

            float bandH = ActorH / GlitchBands;
            for (int i = 0; i < actorSlices.Length; i++)
            {
                float shift = on ? sliceShift[i] : 0f;
                var rt = (RectTransform)actorSlices[i].transform;
                rt.anchoredPosition = new Vector2(ActorCx + shift,
                    ActorCy - ActorH * 0.5f + bandH * (i + 0.5f));
                // 밀린 띠만 보인다 — 안 밀린 띠는 본체와 겹쳐 있어 켜 봐야 두꺼워지기만 한다.
                float a = on ? Mathf.Min(0.85f, Mathf.Abs(shift) / GlitchShiftMax) : 0f;
                // 밀린 방향에 따라 청록/주황으로 갈라 색수차처럼 보이게 한다.
                Color c = shift >= 0f ? Cyan : Ember;
                actorSlices[i].color = new Color(c.r, c.g, c.b, a);
            }
        }

        /// <summary>부팅 페이드(closeK=0) ↔ 종료 페이드(closeK=1)를 한 곳에서 처리한다.</summary>
        void SetAlpha(float closeK)
        {
            // 플레이 중에 스크립트를 고치면 도메인 리로드가 일어나 직렬화 안 되는 필드가 날아간다
            // (fades는 CanvasGroup 참조를 담은 private 구조체 배열이라 그 대상이다).
            // 개발 중에만 생기는 상황이지만, 그때 NRE가 매 프레임 쏟아지면 콘솔이 못 쓰게 된다.
            if (fades == null) return;

            float age = Time.unscaledTime - born;
            for (int i = 0; i < fades.Length; i++)
            {
                Fade f = fades[i];
                float inK = Mathf.Clamp01((age - f.delay) / f.dur);
                inK = 1f - (1f - inK) * (1f - inK);              // ease-out — 튀지 않고 스며든다
                f.group.alpha = inK * (1f - closeK);
            }
            // 배경 장막은 마지막까지 남는다 — 먼저 걷히면 지형이 훤히 드러나 UI가 붕 뜬다.
            var vc = veil.color;
            veil.color = new Color(vc.r, vc.g, vc.b, VeilAlpha * (1f - closeK * closeK));
        }

        struct Fade { public CanvasGroup group; public float delay, dur; }
        Fade[] fades;

        // ── 조립 ─────────────────────────────────────────────────────────
        /// <summary>씬 지형이 실루엣으로만 비치는 세기. 레퍼런스처럼 배경이 살아 있되
        /// 흰 워드마크와 다투지는 않는 선.</summary>
        const float VeilAlpha = 0.74f;

        void Build()
        {
            canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200;   // HUD(100)보다 위
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(RefW, RefH);
            scaler.matchWidthOrHeight = 0.5f;

            root = NewRect("Root", transform);
            Stretch(root);

            // ── 배경 = 맵 스냅샷 ──
            // 실시간 씬을 비추지 않고 구워 둔 사진을 쓴다. 카메라가 어디를 보고 있든 구도가
            // 고정되고, 아레나를 켜지 않아도 되므로 첫 프레임부터 뜬다.
            // Sprite가 아니라 Texture2D + RawImage인 이유: PNG의 기본 임포트 타입이 Texture라,
            // Sprite로 바꾸는 임포트 설정 없이 그대로 읽히는 쪽이 손이 덜 간다.
            var bgTex = Resources.Load<Texture2D>(BackdropResource);
            if (bgTex != null)
            {
                bgImage = NewRaw("Backdrop", root);
                bgImage.texture = bgTex;
                // 사진 비율과 화면 비율이 다를 때 늘어나지 않게 — 넘치는 쪽을 잘라낸다.
                CoverFit(bgImage.rectTransform, (float)bgTex.width / bgTex.height);
            }
            else
            {
                Debug.LogWarning($"[타이틀] Resources/{BackdropResource} 가 없어 배경 없이 표시합니다.");
            }

            // 장막 — 알파 블렌드여야 하므로 홀로 재질(가산)을 쓰지 않는다.
            veil = NewImage("Veil", root);
            Stretch(veil.rectTransform);
            veil.color = new Color(0.012f, 0.016f, 0.021f, VeilAlpha);

            // ── 주인공 ──
            // 장막 <b>위</b>에 온다 — 아래 두면 사람까지 같이 어두워져 배경에 묻는다.
            actor = TitleActor.Spawn();
            if (actor != null)
            {
                // 상시 잔상 — 본체 <b>뒤</b>에 깔린다(그리는 순서 = 겹치는 순서). 워드마크의
                // 예지 잔상과 같은 규칙이라, 사람과 글자가 한 화면 안에서 같은 말을 한다.
                actorEchoes = new RawImage[2];
                for (int i = actorEchoes.Length - 1; i >= 0; i--)
                {
                    actorEchoes[i] = NewRaw("ActorEcho" + i, root);
                    SetRectC(actorEchoes[i].rectTransform, ActorCx, ActorCy, ActorW, ActorH);
                    actorEchoes[i].texture = actor.Target;
                }

                actorImage = NewRaw("Actor", root);
                SetRectC(actorImage.rectTransform, ActorCx, ActorCy, ActorW, ActorH);
                actorImage.texture = actor.Target;

                // 지지직 — 본체를 가로로 자른 띠들. 평소엔 본체와 <b>정확히 겹쳐</b> 안 보이다가,
                // 글리치 순간에만 띠마다 다른 양으로 좌우로 밀린다(데이터모싱 어법).
                // uvRect로 같은 렌더 텍스처의 다른 구간을 보여 주므로 추가 렌더 비용이 없다.
                actorSlices = new RawImage[GlitchBands];
                sliceShift = new float[GlitchBands];
                for (int i = 0; i < GlitchBands; i++)
                {
                    float h = 1f / GlitchBands;
                    var img = NewRaw("ActorSlice" + i, root);
                    img.texture = actor.Target;
                    img.uvRect = new Rect(0f, i * h, 1f, h);
                    SetRectC(img.rectTransform, ActorCx,
                        ActorCy - ActorH * 0.5f + ActorH * (i + 0.5f) * h, ActorW, ActorH * h);
                    img.color = new Color(1f, 1f, 1f, 0f);   // 평소엔 완전히 투명
                    actorSlices[i] = img;
                }
            }

            scan = NewGraphic<HoloScan>("Scan", root);
            Stretch(scan.rectTransform);
            // HoloScan은 알파를 자기가 정하므로(0.05 고정) 색 자체를 죽여서 세기를 낮춘다.
            // 가산이라 어두운 배경 위에서는 원색 그대로면 띠가 너무 또렷하게 지나간다.
            scan.color = new Color(Cyan.r * 0.45f, Cyan.g * 0.45f, Cyan.b * 0.45f, 1f);

            // ── 워드마크 ──
            // 잔상을 먼저 깔고 그 위에 본체(그려지는 순서 = 겹치는 순서).
            echoes = new Text[2];
            for (int i = echoes.Length - 1; i >= 0; i--)
            {
                echoes[i] = NewText("Echo" + i, root, LogoSize, TextAnchor.MiddleCenter);
                SetRectC((RectTransform)echoes[i].transform, LogoCx, LogoCy, 1600f, 200f);
                echoes[i].text = LogoText;
            }
            logo = NewText("Logo", root, LogoSize, TextAnchor.MiddleCenter);
            SetRectC((RectTransform)logo.transform, LogoCx, LogoCy, 1600f, 200f);
            logo.text = LogoText;

            slash = NewGraphic<TitleSlash>("Slash", root);
            SetRectC(slash.rectTransform, LogoCx, (SlashY0 + SlashY1) * 0.5f, 260f, SlashY1 - SlashY0);
            slash.color = Color.white;

            // ── 버튼 기둥 ──
            buttons = new TitleButton[ItemLabel.Length];
            btnLabels = new Text[ItemLabel.Length];
            hi = new float[ItemLabel.Length];
            for (int i = 0; i < ItemLabel.Length; i++)
            {
                float cy = BtnTopCy - i * BtnStep;
                buttons[i] = NewGraphic<TitleButton>("Btn" + i, root, false);   // 채움 위에 글자가 얹혀야 한다
                SetRectC(buttons[i].rectTransform, BtnCx, cy, BtnW, BtnH);
                buttons[i].color = Ember;

                // 라벨은 버튼의 자식이 아니라 형제로 둔다 — 자식이면 버튼의 가산 재질이
                // 글자에까지 상속돼 어두운 글자가 안 나온다(가산은 어둡게 만들 수 없다).
                btnLabels[i] = NewText("BtnLabel" + i, root, 23, TextAnchor.MiddleCenter);
                SetRectC((RectTransform)btnLabels[i].transform, BtnCx, cy, BtnW, BtnH);
                btnLabels[i].text = ItemLabel[i];
            }
            hi[0] = 1f;

            hint = NewText("Hint", root, 18, TextAnchor.MiddleLeft);
            SetRectC((RectTransform)hint.transform, RailX + 600f, HintCy, 1200f, 28f);
            hint.text = "↑ ↓  선택        ENTER  실행        ESC  뒤로";
            hint.color = new Color(1f, 1f, 1f, 0.34f);

            var build = NewText("Build", root, 15, TextAnchor.MiddleRight);
            SetRectC((RectTransform)build.transform, RefW - 190f - 300f, 92f, 600f, 24f);
            build.text = "PROTOTYPE BUILD";
            build.color = new Color(Cyan.r, Cyan.g, Cyan.b, 0.22f);

            // "생성 중" — 새 게임을 누른 뒤 NavMesh를 굽는 동안 화면에 남는 유일한 글자.
            loading = NewText("Loading", root, 22, TextAnchor.MiddleCenter);
            SetRectC((RectTransform)loading.transform, LogoCx, 540f, 1200f, 34f);
            loading.text = "아 레 나  생 성  중";
            loading.color = new Color(Cyan.r, Cyan.g, Cyan.b, 0.85f);
            loading.gameObject.SetActive(false);

            BuildControls();

            // 부팅 순서 — 칼금이 먼저 그어지고, 워드마크가 열리고, 버튼이 한 줄씩 들어온다.
            // 전부 동시에 뜨면 그림 한 장이고, 순서가 있으면 켜지는 장치가 된다.
            // 배경·주인공은 목록에 조건부로 들어간다(에셋이 없으면 없는 채로 성립해야 한다).
            var list = new System.Collections.Generic.List<Fade>();
            if (bgImage != null) list.Add(Fade_(bgImage, 0.00f, 0.45f));
            if (actorImage != null) list.Add(Fade_(actorImage, 0.18f, 0.50f));
            if (actorEchoes != null)
                foreach (var e in actorEchoes) list.Add(Fade_(e, 0.18f, 0.50f));
            if (actorSlices != null)
                foreach (var s in actorSlices) list.Add(Fade_(s, 0.18f, 0.50f));
            list.AddRange(new[]
            {
                Fade_(scan,  0.00f, 0.50f),
                Fade_(slash, 0.06f, 0.20f),
                Fade_(logo,  0.30f, 0.40f),
                Fade_(echoes[0], 0.30f, 0.40f),
                Fade_(echoes[1], 0.30f, 0.40f),
                Fade_(buttons[0], 0.62f, 0.26f), Fade_(btnLabels[0], 0.62f, 0.26f),
                Fade_(buttons[1], 0.70f, 0.26f), Fade_(btnLabels[1], 0.70f, 0.26f),
                Fade_(buttons[2], 0.78f, 0.26f), Fade_(btnLabels[2], 0.78f, 0.26f),
                Fade_(hint,  0.90f, 0.30f),
                Fade_(build, 0.90f, 0.30f),
            });
            fades = list.ToArray();
            SetAlpha(0f);
        }

        /// <summary>글자 사이를 벌린다 — Legacy Text엔 자간이 없어서 공백으로 준다.
        /// 붙여 쓰면 덩어리로 보이고, 벌리면 각인처럼 읽힌다. 가운데 넓은 틈은 칼금 자리다.</summary>
        const string LogoText = "P R E   C O G";

        /// <summary>조작 안내 — 시작 화면에서 나가지 않고 그 자리에 겹쳐 연다.</summary>
        void BuildControls()
        {
            controlsRoot = NewRect("Controls", root);
            controlsRoot.anchorMin = controlsRoot.anchorMax = new Vector2(0.5f, 0.5f);
            controlsRoot.pivot = new Vector2(0.5f, 0.5f);
            controlsRoot.sizeDelta = new Vector2(880f, 540f);
            controlsRoot.anchoredPosition = Vector2.zero;

            var panel = NewGraphic<TitlePanel>("Panel", controlsRoot);
            Stretch(panel.rectTransform);
            panel.color = Cyan;

            var head = NewText("Head", controlsRoot, 26, TextAnchor.UpperLeft);
            SetRectTL(head, 52f, 38f, 500f, 36f);
            head.text = "조 작";
            head.color = Color.white;

            // 왼쪽 = 키, 오른쪽 = 하는 일. 나눠야 눈이 키만 훑고 지나갈 수 있다.
            string[,] rows =
            {
                { "W A S D",      "이동" },
                { "마우스",        "시점" },
                { "SPACE",        "점프 · 공중에서 한 번 더" },
                { "SHIFT + 방향",  "4방향 대시" },
                { "좌클릭",        "평타" },
                { "우클릭",        "타깃 런지" },
                { "F",            "예지 — 미래를 미리 본다" },
                { "ESC",          "커서 해제" },
            };
            for (int i = 0; i < rows.GetLength(0); i++)
            {
                float y = 108f + i * 48f;
                var k = NewText("Key" + i, controlsRoot, 20, TextAnchor.UpperLeft);
                SetRectTL(k, 52f, y, 300f, 28f);
                k.text = rows[i, 0];
                k.color = Ember;

                var v = NewText("Val" + i, controlsRoot, 20, TextAnchor.UpperLeft);
                SetRectTL(v, 330f, y, 520f, 28f);
                v.text = rows[i, 1];
                v.color = new Color(1f, 1f, 1f, 0.72f);
            }

            var back = NewText("Back", controlsRoot, 16, TextAnchor.UpperLeft);
            SetRectTL(back, 52f, 500f, 500f, 24f);
            back.text = "ESC  돌아가기";
            back.color = new Color(1f, 1f, 1f, 0.34f);

            controlsRoot.gameObject.SetActive(false);
        }

        Fade Fade_(Graphic g, float delay, float dur)
        {
            // ★ 여기서 <c>??</c>를 쓰면 안 된다. 유니티는 없는 컴포넌트에 대해 <b>가짜 null</b>
            // (== 만 오버로드된 껍데기)을 돌려주는데, C#의 ?? 는 그 오버로드를 거치지 않고
            // 진짜 null만 본다. 그래서 껍데기를 "있다"고 판단해 AddComponent를 건너뛰고,
            // 나중에 alpha를 쓰는 순간 MissingComponentException으로 터진다.
            var go = g.gameObject;
            if (!go.TryGetComponent(out CanvasGroup cg)) cg = go.AddComponent<CanvasGroup>();
            return new Fade { group = cg, delay = delay, dur = dur };
        }

        // ── UGUI 유틸 (HoloHud와 같은 것들) ──────────────────────────────
        static T NewGraphic<T>(string name, Transform parent) where T : Graphic
        {
            return NewGraphic<T>(name, parent, true);
        }

        /// <summary>
        /// <paramref name="additive"/>=false면 기본 UI 재질로 그린다.
        ///
        /// <para>★ 가산 재질(HoloUI)은 셰이더 큐가 <b>Overlay</b>라, 계층 순서와 무관하게
        /// 모든 Text(큐 Transparent)보다 <b>나중에</b> 그려진다. 버튼처럼 "채움 위에 글자가
        /// 얹혀야 하는" 것에 가산을 쓰면 주황 채움이 글자를 덮어버린다 — 선택된 항목의 라벨만
        /// 사라지는 증상이 이것이었다. 채움이 필요한 것은 알파 블렌드로 그려야 순서가 지켜진다.</para>
        /// </summary>
        static T NewGraphic<T>(string name, Transform parent, bool additive) where T : Graphic
        {
            // ★ CanvasRenderer를 <b>명시적으로</b> 넣는다. Graphic에는 [RequireComponent]가 걸려
            //   있지만, GameObject 생성자에 타입을 나열하는 이 경로는 그 처리를 <b>건너뛴다</b>.
            //   Text·Image 같은 빌트인은 어찌어찌 붙지만 우리가 만든 Graphic 파생 클래스는
            //   안 붙고, 그러면 메시를 아무리 만들어도 그릴 대상이 없어 <b>조용히 안 보인다</b>
            //   (에러도 안 난다 — 그래서 찾는 데 오래 걸린다).
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(T));
            go.transform.SetParent(parent, false);
            var g = go.GetComponent<T>();
            g.raycastTarget = false;
            if (additive) g.material = HoloGraphic.HoloMaterial;
            return g;
        }

        static Image NewImage(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.raycastTarget = false;
            return img;
        }

        static RawImage NewRaw(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<RawImage>();
            img.raycastTarget = false;
            return img;
        }

        /// <summary>
        /// 화면을 꽉 채우되 <b>비율은 지킨다</b> — 넘치는 쪽이 잘려나간다(CSS의 cover와 같다).
        /// 그냥 늘리면 21:9 모니터에서 배경 사진이 옆으로 퍼져 뭉개진다.
        /// </summary>
        static void CoverFit(RectTransform rt, float srcAspect)
        {
            Stretch(rt);
            float screenAspect = (float)Screen.width / Mathf.Max(1, Screen.height);
            if (screenAspect > srcAspect)
            {
                // 화면이 더 넓다 → 폭을 맞추고 위아래를 넘긴다.
                float over = (screenAspect / srcAspect - 1f) * 0.5f;
                rt.anchorMin = new Vector2(0f, -over);
                rt.anchorMax = new Vector2(1f, 1f + over);
            }
            else
            {
                float over = (srcAspect / screenAspect - 1f) * 0.5f;
                rt.anchorMin = new Vector2(-over, 0f);
                rt.anchorMax = new Vector2(1f + over, 1f);
            }
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        static Text NewText(string name, Transform parent, float size, TextAnchor anchor)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.raycastTarget = false;
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.fontSize = Mathf.RoundToInt(size);
            t.fontStyle = FontStyle.Bold;
            t.alignment = anchor;
            t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        /// <summary>기준 해상도 픽셀(좌하단 원점)의 <b>중심</b> 좌표로 배치.</summary>
        static void SetRectC(RectTransform rt, float cx, float cy, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(cx, cy);
            rt.sizeDelta = new Vector2(w, h);
        }

        /// <summary>패널 안쪽 배치 — 패널 좌상단 원점(y가 아래로 자란다).</summary>
        static void SetRectTL(Graphic g, float x, float y, float w, float h)
        {
            var rt = (RectTransform)g.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    /// <summary>
    /// 워드마크를 PRE|COG로 가르는 세로 베기. 레퍼런스가 로고 한가운데 카타나를 세워 둔 자리인데,
    /// 우리는 칼 자체를 그리지 않고 <b>베인 자국</b>만 남긴다 — 아트 없이 정점만으로 되고,
    /// "미래를 미리 갈라 본다"는 게임의 말과도 맞는다.
    /// </summary>
    public class TitleSlash : HoloGraphic
    {
        /// <summary>0~1. 부팅할 때 위에서 아래로 그어지는 진행도.</summary>
        public float Reveal = 1f;
        /// <summary>0~1로 계속 도는 위상 — 밝은 점이 칼금을 타고 흐른다.</summary>
        public float Sweep;
        /// <summary>선택을 옮긴 순간 한 번 밝아진다.</summary>
        public float Flash;

        const float Tilt = 16f;      // 위끝이 오른쪽으로 밀린 정도(px) — 살짝 기울어야 벤 자국이 된다
        const float Core = 2.6f;     // 칼금 두께

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = GetPixelAdjustedRect();
            if (Reveal <= 0.001f) return;

            float cx = r.center.x;
            float yTop = r.yMax, yBot = Mathf.Lerp(r.yMax, r.yMin, Reveal);
            float a = color.a * (0.85f + 0.15f * Flash);

            // 본 칼금 — 위에서 아래로 그어진 만큼만 그린다.
            float tTop = (yTop - r.yMin) / r.height, tBot = (yBot - r.yMin) / r.height;
            Vector2 pTop = new Vector2(cx + Tilt * (tTop - 0.5f) * 2f, yTop);
            Vector2 pBot = new Vector2(cx + Tilt * (tBot - 0.5f) * 2f, yBot);
            for (int i = 0; i < GlowLayers; i++)
            {
                float w = Core + i * 5.5f;
                float al = i == 0 ? a : a * (0.26f / i);
                Line(vh, pTop, pBot, w, new Color(color.r, color.g, color.b, al));
            }

            // 그어지는 끝점의 불꽃 — 아직 진행 중일 때만.
            if (Reveal < 0.999f)
                Line(vh, pBot + Vector2.up * 26f, pBot, Core * 2.4f,
                    new Color(1f, 1f, 1f, a * 0.9f));

            // 속도선 — 칼금 양옆으로 짧은 가로선 몇 개. 레퍼런스의 잔선을 최소한으로 옮긴 것.
            float reach = Reveal;
            for (int i = 0; i < 5; i++)
            {
                float t = (i + 1) / 6f;
                float y = Mathf.Lerp(r.yMin, r.yMax, t);
                if (y < yBot) continue;
                float len = (18f + 46f * Mathf.Abs(Mathf.Sin(t * 3.1f))) * reach;
                float al = a * 0.22f * (1f - Mathf.Abs(t - 0.5f) * 1.4f);
                if (al <= 0.002f) continue;
                float x = cx + Tilt * (t - 0.5f) * 2f;
                Quad(vh, x - 8f - len, y, len, 1.4f, new Color(color.r, color.g, color.b, al));
                Quad(vh, x + 8f, y, len * 0.7f, 1.4f, new Color(color.r, color.g, color.b, al));
            }

            // 칼금을 타고 흐르는 밝은 점 — 정지 이미지가 아니라 살아 있는 자국으로 읽히게 한다.
            float sy = Mathf.Lerp(r.yMax, r.yMin, Sweep);
            if (sy >= yBot)
            {
                float st = (sy - r.yMin) / r.height;
                float sx = cx + Tilt * (st - 0.5f) * 2f;
                float fade = 1f - Sweep;
                Quad(vh, sx - Core, sy - 14f, Core * 2f, 28f,
                    new Color(1f, 1f, 1f, 0.5f * fade * fade * Reveal));
            }
        }

        /// <summary>두 점을 잇는 두께 있는 선.</summary>
        internal static void Line(VertexHelper vh, Vector2 a, Vector2 b, float thick, Color c)
        {
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < 0.0001f) return;
            d /= len;
            Vector2 n = new Vector2(-d.y, d.x) * (thick * 0.5f);
            Quad(vh, a - n, a + n, b + n, b - n, c);
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    /// <summary>
    /// 모서리를 깎은 메뉴 버튼. 선택되면 주황으로 꽉 차고(그 위 글자는 어두워진다),
    /// 아니면 얇은 윤곽선만 남는다 — 레퍼런스의 버튼 어법 그대로다.
    /// </summary>
    public class TitleButton : HoloGraphic
    {
        /// <summary>0~1. 선택 강조량(바깥에서 부드럽게 몰아준다).</summary>
        public float Highlight;

        float punch;

        public void Punch() => punch = 1f;

        public void Tick(float dt)
        {
            punch = Mathf.Max(0f, punch - dt * 4.5f);
            SetVerticesDirty();
        }

        const float Chamfer = 16f;   // 좌우 끝을 깎는 폭
        const float Edge = 1.7f;     // 윤곽선 두께

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = GetPixelAdjustedRect();
            float x0 = r.xMin, x1 = r.xMax, y0 = r.yMin, y1 = r.yMax, ym = r.center.y;
            float c = Mathf.Min(Chamfer, r.width * 0.25f);

            var p = new Vector2[6];
            p[0] = new Vector2(x0 + c, y0);
            p[1] = new Vector2(x1 - c, y0);
            p[2] = new Vector2(x1, ym);
            p[3] = new Vector2(x1 - c, y1);
            p[4] = new Vector2(x0 + c, y1);
            p[5] = new Vector2(x0, ym);

            // 채움 — 가운데 사각형 + 양끝 삼각형(꼭짓점을 겹쳐 준 사각형).
            float fill = Highlight;
            if (fill > 0.002f)
            {
                var fc = new Color(color.r, color.g, color.b, fill);
                Quad(vh, x0 + c, y0, r.width - c * 2f, r.height, fc);
                Quad(vh, p[5], p[4], p[0], p[5], fc);
                Quad(vh, p[2], p[1], p[3], p[2], fc);
            }

            // 윤곽선 — 선택되지 않았을 땐 흰 선만 남고, 선택되면 주황 쪽으로 물든다.
            Color line = Color.Lerp(new Color(1f, 1f, 1f, 0.55f),
                                    new Color(color.r, color.g, color.b, 0.95f), fill);
            line.a *= 0.85f + 0.15f * punch;
            float t = Edge + punch * 1.4f;
            for (int i = 0; i < 6; i++)
                TitleSlash.Line(vh, p[i], p[(i + 1) % 6], t, line);

            // 선택된 버튼은 좌우에 짧은 꼬리를 단다 — 기둥 안에서 어느 칸인지 주변시로도 잡힌다.
            if (fill > 0.02f)
            {
                var tc = new Color(color.r, color.g, color.b, 0.5f * fill);
                Quad(vh, x0 - 26f, ym - 1f, 18f, 2f, tc);
                Quad(vh, x1 + 8f, ym - 1f, 18f, 2f, tc);
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    /// <summary>조작 안내가 앉는 판. 테두리를 다 두르지 않고 모서리만 집는다(HUD와 같은 규칙).</summary>
    public class TitlePanel : HoloGraphic
    {
        const float Arm = 54f;
        const float Thin = 2f;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = GetPixelAdjustedRect();

            // 아주 옅은 바닥 — 뒤의 지형을 한 겹 눌러 글자가 읽히게 한다.
            // (가산 재질이라 '어둡게'가 안 되므로, 대신 청록을 아주 얇게 깐다)
            Quad(vh, r.xMin, r.yMin, r.width, r.height,
                new Color(color.r * 0.12f, color.g * 0.14f, color.b * 0.18f, 0.10f));

            Color c = new Color(color.r, color.g, color.b, color.a * 0.8f);
            Corner(vh, r.xMin, r.yMin,  1f,  1f, c);
            Corner(vh, r.xMax, r.yMin, -1f,  1f, c);
            Corner(vh, r.xMin, r.yMax,  1f, -1f, c);
            Corner(vh, r.xMax, r.yMax, -1f, -1f, c);

            float y = r.yMax - 78f;
            Quad(vh, r.xMin + 52f, y, r.width - 104f, 1f,
                new Color(color.r, color.g, color.b, color.a * 0.28f));
        }

        static void Corner(VertexHelper vh, float px, float py, float sx, float sy, Color c)
        {
            GlowRect(vh, px + (sx > 0 ? 0f : -Arm), py + (sy > 0 ? 0f : -Thin), Arm, Thin, c);
            GlowRect(vh, px + (sx > 0 ? 0f : -Thin), py + (sy > 0 ? 0f : -Arm), Thin, Arm, c);
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    /// <summary>
    /// Play 시 타이틀 자동 생성. HudCanvasBoot·CombatHudBoot과 같은 자리에서 같은 방식으로 뜬다.
    ///
    /// <para><b>Main이 씬에 있는지 검사하지 않는다</b> — 어느 씬에도 <c>Main</c>은 놓여 있지 않고,
    /// <see cref="AutoBoot"/>이 여기와 <b>같은</b> AfterSceneLoad 콜백에서 만들어 붙인다. 두 콜백의
    /// 순서는 유니티가 보장하지 않으므로 여기서 Main을 찾아 조건을 걸면 실행할 때마다 뜨거나
    /// 안 뜨거나 한다. 대신 <see cref="TitleScreen.Awake"/>가 <c>AutoBoot.EnsureMain()</c>으로
    /// 순서와 무관하게 Main을 확보하면서 동시에 재운다.</para>
    /// </summary>
    public static class TitleScreenBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (VrMode.Enabled) return;   // VR: PC용 타이틀(ScreenSpace UI)을 띄우지 않고 Main이 게임을 바로 시작
            if (!TitleStyle.ShowOnPlay) return;
            if (UnityEngine.Object.FindFirstObjectByType<TitleScreen>() != null) return;
            new GameObject("[TitleScreen]").AddComponent<TitleScreen>();
        }
    }
}
