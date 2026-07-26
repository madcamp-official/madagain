using UnityEngine;
using UnityEngine.UI;
using Game.Sim;

namespace Game.View
{
    // >>> [홀로그램 HUD, 2026-07-22] "에셋 쓰니까 짜친다 — 아이언맨 홀로그램처럼 코드로"라는
    // 피드백. 생성형 아트 PNG(HudFrame/HudDialRing)를 한 장도 쓰지 않고, 전부 정점 메시로
    // 그린다. 아트 기반 HUD(HudCanvas)는 지우지 않고 남겨뒀다 — HudStyle.UseHologram 하나로
    // 갈아끼운다.
    //
    // 홀로그램 느낌을 만드는 규칙 세 가지:
    //  1. <b>가산 합성</b>(HoloUI.shader) — 겹칠수록 밝아진다. 선 하나를 굵기·투명도가 다른
    //     세 겹으로 깔면 그것만으로 빛나 보인다(GlowLayers).
    //  2. <b>테두리가 아니라 브래킷</b> — 사각형을 다 두르지 않고 모서리만 'ㄱ'자로 집는다.
    //     화면을 가두지 않으면서 "투사된 계기판"으로 읽힌다.
    //  3. <b>미세한 불안정함</b> — 아주 약한 깜빡임과 훑고 지나가는 스캔선. 완전히 고정된
    //     그림은 스티커처럼 보이고, 살짝 흔들리면 공중에 떠 있는 빛으로 보인다.

    /// <summary>어떤 HUD를 띄울지. 아트 프레임으로 되돌리려면 false.</summary>
    public static class HudStyle
    {
        public static bool UseHologram = true;
    }

    /// <summary>
    /// 코드로만 그리는 홀로그램 HUD. 체력/대시/런지는 하우징 없는 순수 세그먼트 바,
    /// 예지는 눈금 링, 화면 가장자리는 모서리 브래킷.
    /// </summary>
    public class HoloHud : MonoBehaviour
    {
        public static HoloHud Instance { get; private set; }

        // ── 색 ───────────────────────────────────────────────────────────
        // [팔레트 개정, 2026-07-23] 축을 <b>두 개로만</b> 줄인다 — 시안 = 자원(체력·대시),
        // 앰버 = 능력(우클릭). 단색 홀로그램은 정보가 뭉개지기 쉬워서, 세 번째 색인 적색은
        // 오직 "체력 1칸" 위험 상태에서만 등장한다. 그래야 붉은색이 경보로 읽힌다.
        static readonly Color Cyan   = new Color(0.42f, 0.92f, 1f);
        static readonly Color Health = new Color(0.35f, 0.90f, 1f);
        static readonly Color Dash   = new Color(0.38f, 0.86f, 1f);
        static readonly Color Lunge  = new Color(1f, 0.79f, 0.42f);
        static readonly Color Danger = new Color(1f, 0.30f, 0.37f);

        // ── 배치 (기준 해상도 1920x1080의 픽셀, 좌하단 원점) ─────────────
        // 목업(1280x720)을 1.5배 한 값. 자동 계산하지 말고 실측 상수로 박아둔다.
        const float RefW = 1920f, RefH = 1080f;
        const float BarX = 72f;                        // 세 바 공통 왼쪽 끝
        const float HealthY = 228f, HealthW = 483f, HealthH = 45f;
        const float MinorY  = 138f, MinorW  = 246f, MinorH  = 24f;   // 대시
        const float LungeY  = 72f;                                   // 우클릭(같은 폭·높이)
        const float LabelSize = 15f;

        // 예지 링 — 우상단 앵커. 화면비가 바뀌어도 모서리에서 같은 거리를 유지한다.
        const float DialR = 81f;                       // 바깥 반지름(눈금은 이 안쪽)
        const float DialMarginX = 177f, DialMarginY = 189f;
        const float DialLabelR = 110f;                 // 초 라벨이 놓이는 반지름

        Canvas canvas;
        GameObject root;
        HoloFrame frame;
        HoloScan scan;
        HoloBar health, dash, lunge;
        HoloArc dial;
        Text dialText;
        RectTransform dialTextRect;
        Text healthValue;

        // 피격 팝업 — 좌하단 게이지는 전투 중 시선이 안 가는 자리라, 깎인 순간만 화면 한복판에
        // 숫자를 띄운다. 조준선은 가리지 않게 중앙에서 아래로 내려 잡는다.
        Text hurtText;
        RectTransform hurtRect;
        float hurtT;
        const float HurtShowTime = 1.1f;
        static readonly Vector2 HurtBase = new Vector2(0f, -168f);

        // 표시값은 목표치로 부드럽게 따라간다 — 프레임마다 딱딱 끊기지 않게.
        float shownHealth = -1f, shownDash = -1f, shownLunge = -1f, shownDial = -1f;
        const float FillLerpSpeed = 14f;

        int lastHp = int.MinValue, lastDashCharges = int.MinValue, lastLungeStacks = int.MinValue;
        bool wasFull;
        int lastTenths = -1, lastHpShown = -1;

        void Awake()
        {
            Instance = this;
            Build();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ── 조립 ─────────────────────────────────────────────────────────
        void Build()
        {
            canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(RefW, RefH);
            scaler.matchWidthOrHeight = 0.5f;

            root = NewRect("Root", transform).gameObject;
            var rootRt = (RectTransform)root.transform;
            Stretch(rootRt);

            frame = NewGraphic<HoloFrame>("Frame", rootRt);
            Stretch(frame.rectTransform);
            frame.color = Cyan;

            scan = NewGraphic<HoloScan>("Scan", rootRt);
            Stretch(scan.rectTransform);
            scan.color = Cyan;

            // 칸 수는 시뮬 상한과 그대로 맞물린다 — 체력 3, 대시 2, 런지 2.
            health = NewBar("Health", rootRt, HealthY, HealthW, HealthH, Health, CombatConfig.PlayerMaxHp);
            dash   = NewBar("Dash",   rootRt, MinorY,  MinorW,  MinorH,  Dash,  SimConfig.DashMaxCharges);
            lunge  = NewBar("Lunge",  rootRt, LungeY,  MinorW,  MinorH,  Lunge, CombatConfig.LungeMaxStacks);

            NewLabel("VITALITY", "",      rootRt, HealthY, HealthW, HealthH, Cyan,  LabelSize);
            NewLabel("DASH",     "SHIFT", rootRt, MinorY,  MinorW,  MinorH,  Cyan,  LabelSize - 2f);
            NewLabel("LUNGE",    "RMB",   rootRt, LungeY,  MinorW,  MinorH,  Lunge, LabelSize - 2f);

            // 체력은 칸으로도 읽히지만, 남은 칸 수를 숫자로 한 번 더 준다("02 / 03").
            healthValue = NewText("HealthValue", rootRt, 26, TextAnchor.LowerRight);
            SetRect((RectTransform)healthValue.transform,
                BarX + HealthW - 200f, HealthY + HealthH + 4f, 200f, 30f);
            healthValue.color = new Color(Health.r, Health.g, Health.b, 0.9f);

            BuildDial(rootRt);
            BuildHurt(rootRt);
        }

        void BuildHurt(RectTransform parent)
        {
            hurtText = NewText("HurtPopup", parent, 64, TextAnchor.MiddleCenter);
            hurtRect = (RectTransform)hurtText.transform;
            hurtRect.anchorMin = hurtRect.anchorMax = new Vector2(0.5f, 0.5f);
            hurtRect.pivot = new Vector2(0.5f, 0.5f);
            hurtRect.sizeDelta = new Vector2(640f, 170f);
            hurtRect.anchoredPosition = HurtBase;
            hurtText.color = Danger;
            hurtText.gameObject.SetActive(false);
        }

        /// <summary>체력이 깎인 순간 호출. 감소량과 남은 체력을 화면 중앙에 띄운다.</summary>
        public void ShowHurt(int amount, int hpAfter)
        {
            if (hurtText == null || amount <= 0) return;
            hurtT = HurtShowTime;
            hurtText.text = "-" + amount +
                "\n<size=22><color=#FF9AA2>VITALITY  " + Two(Mathf.Max(0, hpAfter)) +
                " / " + Two(CombatConfig.PlayerMaxHp) + "</color></size>";
            hurtText.gameObject.SetActive(true);
            health.Punch();
        }

        HoloBar NewBar(string name, RectTransform parent, float y, float w, float h, Color c, int cells)
        {
            var bar = NewGraphic<HoloBar>(name, parent);
            SetRect(bar.rectTransform, BarX, y, w, h);
            bar.color = c;
            bar.cells = Mathf.Max(1, cells);
            return bar;
        }

        /// <summary>바 위에 붙는 라벨 한 줄. 왼쪽은 이름, 오른쪽은 조작키 힌트.</summary>
        void NewLabel(string text, string key, RectTransform parent, float y, float w, float h, Color c, float size)
        {
            var t = NewText(text + "Label", parent, size, TextAnchor.LowerLeft);
            SetRect((RectTransform)t.transform, BarX, y + h + 4f, 300f, 22f);
            t.text = text;
            t.color = new Color(c.r, c.g, c.b, 0.85f);

            if (string.IsNullOrEmpty(key)) return;
            var k = NewText(text + "Key", parent, size - 2f, TextAnchor.LowerRight);
            SetRect((RectTransform)k.transform, BarX + w - 200f, y + h + 4f, 200f, 22f);
            k.text = key;
            k.color = new Color(c.r, c.g, c.b, 0.45f);
        }

        void BuildDial(RectTransform parent)
        {
            dial = NewGraphic<HoloArc>("Prediction", parent);
            var rt = DialAnchored(dial.rectTransform, Vector2.zero, new Vector2(DialR * 2f, DialR * 2f));
            dial.color = Cyan;
            dial.MinUsable = PredictionConfig.ChargeMinToUse;

            dialTextRect = (RectTransform)NewText("Seconds", parent, 40, TextAnchor.MiddleCenter).transform;
            dialText = dialTextRect.GetComponent<Text>();
            DialAnchored(dialTextRect, Vector2.zero, new Vector2(DialR * 1.6f, DialR * 0.9f));
            dialText.color = Cyan;

            // 숫자 아래 작은 첨자 — 이 링이 "초"를 재고 있다는 걸 한 번 못박아 둔다.
            var unit = NewText("HorizonLabel", parent, 11, TextAnchor.MiddleCenter);
            DialAnchored((RectTransform)unit.transform, new Vector2(0f, -36f), new Vector2(140f, 18f));
            unit.text = "H O R I Z O N";
            unit.color = new Color(Cyan.r, Cyan.g, Cyan.b, 0.45f);

            // 이름표는 링 왼쪽 — 우상단 모서리 밖으로 글자가 밀려나지 않게 안쪽으로 눕힌다.
            var label = NewText("PredLabel", parent, 16, TextAnchor.MiddleRight);
            var lrt = DialAnchored((RectTransform)label.transform, new Vector2(-117f, 0f), new Vector2(300f, 24f));
            lrt.pivot = new Vector2(1f, 0.5f);
            label.text = "P R E C O G";
            label.color = new Color(Cyan.r, Cyan.g, Cyan.b, 0.7f);

            // 초 눈금 라벨. 게이지 0%가 곧 1초라(ChargeToSeconds) 한 바퀴는 1→5초이고,
            // 90도가 정확히 1초다 — 12시=5초(최대), 3·6·9시가 2·3·4초.
            DialTick("5", 0f, DialLabelR);
            DialTick("2", DialLabelR, 0f);
            DialTick("3", 0f, -DialLabelR);
            DialTick("4", -DialLabelR, 0f);
        }

        void DialTick(string s, float dx, float dy)
        {
            var t = NewText("Tick" + s, (RectTransform)root.transform, 14, TextAnchor.MiddleCenter);
            DialAnchored((RectTransform)t.transform, new Vector2(dx, dy), new Vector2(28f, 20f));
            t.text = s;
            t.color = new Color(Cyan.r, Cyan.g, Cyan.b, 0.5f);
        }

        /// <summary>링 중심을 기준으로 오프셋 배치. 앵커는 화면 우상단에 고정된다.</summary>
        static RectTransform DialAnchored(RectTransform rt, Vector2 offset, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(-DialMarginX + offset.x, -DialMarginY + offset.y);
            rt.sizeDelta = size;
            return rt;
        }

        // ── 갱신 ─────────────────────────────────────────────────────────
        void LateUpdate()
        {
            Main main = Main.Instance;
            bool show = !UiVisibility.Skip && main != null;
            if (root.activeSelf != show) root.SetActive(show);
            if (!show) return;

            // 예측이 timeScale을 소유하므로 HUD 연출은 전부 실시간(unscaled)으로 센다.
            float dt = Time.unscaledDeltaTime;
            float now = Time.unscaledTime;

            ref readonly PlayerCombatState c = ref main.World.player.combat;
            ref readonly PlayerSim p = ref main.World.player;

            UpdateHealth(in c, dt, now);
            UpdateDash(in p, dt);
            UpdateLunge(in c, dt);
            UpdateDial(main.PredictionCharge01, dt, now);
            UpdateHurtPopup(dt);

            // 프레임·스캔선은 값과 무관하게 계속 살아 움직인다.
            frame.Flicker = 0.92f + 0.08f * Mathf.Sin(now * 11.3f) * Mathf.Sin(now * 3.7f);
            frame.SetVerticesDirty();
            scan.Progress = Mathf.Repeat(now * 0.22f, 1f);
            scan.SetVerticesDirty();
        }

        static float Approach(ref float shown, float target, float dt)
        {
            if (shown < 0f) shown = target;
            shown = Mathf.Lerp(shown, target, 1f - Mathf.Exp(-FillLerpSpeed * dt));
            if (Mathf.Abs(shown - target) < 0.0015f) shown = target;
            return shown;
        }

        void UpdateHealth(in PlayerCombatState c, float dt, float now)
        {
            float f = Mathf.Clamp01(c.hp / (float)CombatConfig.PlayerMaxHp);
            if (c.hp < lastHp) health.Punch();
            lastHp = c.hp;

            if (c.hp != lastHpShown)
            {
                lastHpShown = c.hp;
                healthValue.text = Two(Mathf.Max(0, c.hp)) +
                    "<size=17><color=#7FD4E8> / " + Two(CombatConfig.PlayerMaxHp) + "</color></size>";
            }

            // 마지막 한 칸에서만 적색으로 갈아타고 맥박이 뛴다 — 그 전까지는 시안을 유지해야
            // 붉은색이 "경보"로 읽힌다. 칸이 3개뿐이라 비율이 아니라 칸 수로 판정한다.
            bool critical = c.hp <= 1;
            health.color = critical ? Danger : Health;
            health.Intensity = critical ? 0.75f + 0.5f * Mathf.Abs(Mathf.Sin(now * 4.2f)) : 1f;
            health.Value = Approach(ref shownHealth, f, dt);
            health.Tick(dt);
        }

        /// <summary>
        /// 뜰 때 크게 튀고, 천천히 떠오르며, 뒤늦게 사라진다 — 앞부분에서 눈을 끌고
        /// 뒷부분에서 조용히 비켜준다. 알파는 리치텍스트 색과 곱해지므로 한 번만 만지면 된다.
        /// </summary>
        void UpdateHurtPopup(float dt)
        {
            if (hurtT <= 0f) return;
            hurtT -= dt;
            if (hurtT <= 0f) { hurtText.gameObject.SetActive(false); return; }

            float t = 1f - hurtT / HurtShowTime;                         // 0→1 진행
            float fade = t < 0.55f ? 1f : 1f - (t - 0.55f) / 0.45f;
            float pop  = t < 0.16f ? Mathf.Lerp(1.35f, 1f, t / 0.16f) : 1f;

            hurtText.color = new Color(Danger.r, Danger.g, Danger.b, fade);
            hurtRect.anchoredPosition = HurtBase + new Vector2(0f, 34f * (1f - Mathf.Pow(1f - t, 3f)));
            hurtRect.localScale = new Vector3(pop, pop, 1f);
        }

        void UpdateDash(in PlayerSim p, float dt)
        {
            int max = Mathf.Max(1, SimConfig.DashMaxCharges);
            float v = p.dashCharges;
            if (p.dashCharges < max && p.dashRecharge > 0 && SimConfig.DashRechargeTicks > 0)
                v += 1f - Mathf.Clamp01(p.dashRecharge / (float)SimConfig.DashRechargeTicks);

            if (p.dashCharges != lastDashCharges && lastDashCharges != int.MinValue) dash.Punch();
            lastDashCharges = p.dashCharges;

            dash.Intensity = p.dashCharges > 0 ? 1f : 0.3f;
            dash.Value = Approach(ref shownDash, Mathf.Clamp01(v / max), dt);
            dash.Tick(dt);
        }

        void UpdateLunge(in PlayerCombatState c, float dt)
        {
            int max = Mathf.Max(1, CombatConfig.LungeMaxStacks);
            float v = c.lungeStacks;
            if (c.lungeCooldown > 0 && CombatConfig.LungeCooldownTicks > 0 && c.lungeStacks < max)
                v += 1f - Mathf.Clamp01(c.lungeCooldown / (float)CombatConfig.LungeCooldownTicks);

            if (c.lungeStacks != lastLungeStacks && lastLungeStacks != int.MinValue) lunge.Punch();
            lastLungeStacks = c.lungeStacks;

            lunge.Intensity = c.lungeStacks > 0 ? 1f : 0.3f;
            lunge.Value = Approach(ref shownLunge, Mathf.Clamp01(v / max), dt);
            lunge.Tick(dt);
        }

        void UpdateDial(float charge01, float dt, float now)
        {
            float charge = Mathf.Clamp01(charge01);
            bool usable = charge >= PredictionConfig.ChargeMinToUse;
            bool full = charge >= 1f;

            if (full && !wasFull) dial.Punch();
            wasFull = full;

            // 다 찼으면 링이 숨쉬듯 밝아진다 — 시야 구석에서도 "쓸 수 있다"가 읽히게.
            float breathe = full ? 1f + 0.4f * Mathf.Sin(now * 3.1f) : 1f;
            dial.Intensity = (usable ? 1f : 0.28f) * breathe;
            dial.Value = Approach(ref shownDial, charge, dt);
            dial.Sweep = Mathf.Repeat(now * 0.35f, 1f);   // 훑고 도는 스캔 눈금
            dial.Tick(dt);

            int tenths = Mathf.RoundToInt(PredictionConfig.ChargeToSeconds(charge) * 10f);
            if (tenths != lastTenths) { lastTenths = tenths; dialText.text = SecondsText(tenths); }
            dialText.color = usable
                ? Color.Lerp(Cyan, Color.white, full ? 0.4f : 0.15f)
                : new Color(Cyan.r, Cyan.g, Cyan.b, 0.5f);

            float s = full ? 1f + 0.04f * Mathf.Sin(now * 3.1f) : 1f;
            dialTextRect.localScale = new Vector3(s, s, 1f);
        }

        static string Two(int n) => n < 10 ? "0" + n : n.ToString();

        const int MaxTenths = 60;
        static string[] SecondsCache;

        static string SecondsText(int tenths)
        {
            if (SecondsCache == null)
            {
                SecondsCache = new string[MaxTenths + 1];
                for (int i = 0; i <= MaxTenths; i++)
                    SecondsCache[i] = (i / 10) + "." + (i % 10) + "<size=20><color=#7FD4E8>s</color></size>";
            }
            return SecondsCache[Mathf.Clamp(tenths, 0, MaxTenths)];
        }

        // ── UGUI 유틸 ────────────────────────────────────────────────────
        static T NewGraphic<T>(string name, Transform parent) where T : Graphic
        {
            // ★ [버그 수정, 2026-07-23] CanvasRenderer를 <b>명시적으로</b> 넣는다.
            //   Graphic에 [RequireComponent(typeof(CanvasRenderer))]가 걸려 있어도, 타입을
            //   나열하는 이 GameObject 생성자 경로는 그 처리를 <b>건너뛴다</b>. 빌트인 Text·Image는
            //   붙는데 우리가 만든 파생 클래스(HoloFrame·HoloBar·HoloArc·HoloScan)는 안 붙었고,
            //   그래서 <b>홀로그램 HUD의 게이지·링이 지금까지 한 번도 그려지지 않았다</b>
            //   — 라벨(Text)만 떠서 "글자만 있고 막대가 없는" 화면이 됐다. 에러가 안 나므로
            //   조용히 안 보일 뿐이었다.
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(T));
            go.transform.SetParent(parent, false);
            var g = go.GetComponent<T>();
            g.raycastTarget = false;
            g.material = HoloGraphic.HoloMaterial;
            return g;
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

        /// <summary>기준 해상도 픽셀(좌하단 원점)로 배치한다.</summary>
        static void SetRect(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(w, h);
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    /// <summary>가산 합성 재질과 정점 유틸을 공유하는 베이스.</summary>
    public abstract class HoloGraphic : MaskableGraphic
    {
        static Material cached;
        static bool tried;

        /// <summary>Resources/HoloUI.shader로 만든 가산 재질. 없으면 null(기본 UI 재질로 폴백).</summary>
        public static Material HoloMaterial
        {
            get
            {
                if (tried) return cached;
                tried = true;
                var shader = Resources.Load<Shader>("HoloUI");
                if (shader == null)
                {
                    Debug.LogWarning("[HoloHud] Resources/HoloUI.shader 를 못 찾아 기본 UI 재질로 그립니다 " +
                                     "(가산 글로우 없음).");
                    return null;
                }
                cached = new Material(shader) { name = "HoloUI (runtime)", hideFlags = HideFlags.DontSave };
                return cached;
            }
        }

        /// <summary>겹쳐 그리는 글로우 겹 수(코어 + 바깥으로 번지는 겹).</summary>
        protected const int GlowLayers = 3;

        /// <summary>같은 선을 굵기·투명도를 달리해 여러 겹 그린다 — 가산이라 이것만으로 빛난다.</summary>
        protected static void GlowRect(VertexHelper vh, float x, float y, float w, float h, Color c, float slant = 0f)
        {
            for (int i = 0; i < GlowLayers; i++)
            {
                float k = i == 0 ? 0f : i * 1.9f;              // 바깥으로 번지는 양(px)
                float a = i == 0 ? c.a : c.a * (0.3f / i);     // 번질수록 옅게
                Quad(vh, x - k, y - k, w + k * 2f, h + k * 2f,
                    new Color(c.r, c.g, c.b, a), slant);
            }
        }

        protected static void Quad(VertexHelper vh, float x, float y, float w, float h, Color c, float slant = 0f)
        {
            int i = vh.currentVertCount;
            var v = UIVertex.simpleVert;
            v.color = c;
            v.position = new Vector3(x, y);                    vh.AddVert(v);
            v.position = new Vector3(x + slant, y + h);        vh.AddVert(v);
            v.position = new Vector3(x + slant + w, y + h);    vh.AddVert(v);
            v.position = new Vector3(x + w, y);                vh.AddVert(v);
            vh.AddTriangle(i, i + 1, i + 2);
            vh.AddTriangle(i, i + 2, i + 3);
        }

        /// <summary>네 점을 직접 주는 사각형(링 눈금처럼 회전한 도형용).</summary>
        protected static void Quad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c2, Vector2 d, Color c)
        {
            int i = vh.currentVertCount;
            var v = UIVertex.simpleVert;
            v.color = c;
            v.position = a;  vh.AddVert(v);
            v.position = b;  vh.AddVert(v);
            v.position = c2; vh.AddVert(v);
            v.position = d;  vh.AddVert(v);
            vh.AddTriangle(i, i + 1, i + 2);
            vh.AddTriangle(i, i + 2, i + 3);
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    /// <summary>
    /// 화면 모서리 브래킷. 사각형을 다 두르지 않고 네 모서리만 'ㄱ'자로 집는다 —
    /// 시야를 가두지 않으면서 "투사된 계기판" 인상을 준다.
    /// </summary>
    public class HoloFrame : HoloGraphic
    {
        public float Flicker = 1f;

        const float Margin = 26f;      // 화면 가장자리에서 띄우는 거리
        const float Arm = 210f;        // 브래킷 팔 길이
        const float Thin = 2f;         // 선 두께
        const float InnerGap = 9f;     // 두 번째(안쪽) 선까지 간격
        const float InnerArm = 84f;    // 안쪽 짧은 선 길이
        const float EdgeTick = 12f;    // 변 중앙 눈금 길이

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = GetPixelAdjustedRect();
            float x0 = r.xMin + Margin, x1 = r.xMax - Margin;
            float y0 = r.yMin + Margin, y1 = r.yMax - Margin;
            Color c = new Color(color.r, color.g, color.b, color.a * Flicker);
            Color dim = new Color(c.r, c.g, c.b, c.a * 0.45f);

            // 모서리 4개 — (부호로 방향만 뒤집어 같은 코드를 재사용)
            Bracket(vh, x0, y0,  1f,  1f, c, dim);
            Bracket(vh, x1, y0, -1f,  1f, c, dim);
            Bracket(vh, x0, y1,  1f, -1f, c, dim);
            Bracket(vh, x1, y1, -1f, -1f, c, dim);

            // 변 중앙 눈금 — 상하좌우 각 3개씩, 가운데가 길다.
            for (int i = -1; i <= 1; i++)
            {
                float t = i * 46f;
                float len = i == 0 ? EdgeTick * 1.9f : EdgeTick;
                float cx = (x0 + x1) * 0.5f + t, cy = (y0 + y1) * 0.5f + t;
                GlowRect(vh, cx, y0, Thin, len, dim);
                GlowRect(vh, cx, y1 - len, Thin, len, dim);
                GlowRect(vh, x0, cy, len, Thin, dim);
                GlowRect(vh, x1 - len, cy, len, Thin, dim);
            }
        }

        /// <summary>모서리 하나. sx/sy는 안쪽으로 향하는 부호(+1/-1).</summary>
        static void Bracket(VertexHelper vh, float px, float py, float sx, float sy, Color c, Color dim)
        {
            // 바깥 'ㄱ'자
            GlowRect(vh, px + (sx > 0 ? 0f : -Arm), py, Arm, Thin, c);
            GlowRect(vh, px, py + (sy > 0 ? 0f : -Arm), Thin, Arm, c);
            // 안쪽 짧은 선 한 겹 더 — 두 줄이 되면 단번에 "계기판"처럼 읽힌다.
            float ix = px + sx * InnerGap, iy = py + sy * InnerGap;
            GlowRect(vh, ix + (sx > 0 ? 0f : -InnerArm), iy, InnerArm, Thin * 0.7f, dim);
            GlowRect(vh, ix, iy + (sy > 0 ? 0f : -InnerArm), Thin * 0.7f, InnerArm, dim);
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    /// <summary>화면을 아래에서 위로 훑고 지나가는 아주 옅은 스캔선.</summary>
    public class HoloScan : HoloGraphic
    {
        public float Progress;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = GetPixelAdjustedRect();
            float y = Mathf.Lerp(r.yMin, r.yMax, Progress);
            // 가장자리에서 서서히 사라지게 — 위아래 끝에서 튀어나오는 티를 없앤다.
            float edge = Mathf.Sin(Progress * Mathf.PI);
            Color c = new Color(color.r, color.g, color.b, 0.05f * edge);
            Quad(vh, r.xMin, y, r.width, 1.5f, c);
            Quad(vh, r.xMin, y - 26f, r.width, 26f, new Color(c.r, c.g, c.b, c.a * 0.35f));
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    /// <summary>
    /// 하우징 없는 순수 홀로그램 바. 시뮬 상한과 같은 수의 <b>칸</b>으로 나뉘며, 칸 하나가
    /// 자원 하나다(체력 3, 대시 2, 런지 2). 빈 칸은 지우지 않고 껍데기로 남긴다 —
    /// 최대치가 형태로 남아야 "3칸짜리 체력"이라는 리듬이 읽힌다.
    /// </summary>
    public class HoloBar : HoloGraphic
    {
        public float Value;        // 0~1
        public float Intensity = 1f;
        public int cells = 3;

        float flash;

        public void Punch() => flash = 1f;

        public void Tick(float dt)
        {
            flash = Mathf.Max(0f, flash - dt * 3.4f);
            SetVerticesDirty();
        }

        const float SlantRatio = 0.25f;   // 높이 대비 위쪽이 오른쪽으로 밀리는 양(≈14도)
        const float GapRatio = 0.073f;    // 칸 피치 대비 사이 간격
        const float Line = 1.8f;          // 껍데기 외곽선 두께

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = GetPixelAdjustedRect();
            int n = Mathf.Max(1, cells);
            float pitch = r.width / n;
            float w = pitch * (1f - GapRatio);
            float h = r.height;
            float s = h * SlantRatio;

            Color lit  = new Color(color.r, color.g, color.b, color.a * Intensity);
            Color husk = new Color(color.r, color.g, color.b, 0.09f * Intensity);
            Color edge = new Color(color.r, color.g, color.b, 0.45f * Intensity);

            for (int i = 0; i < n; i++)
            {
                float x = r.xMin + i * pitch;
                float k = Mathf.Clamp01((Value - i / (float)n) * n);

                // 껍데기 먼저 — 차 있든 비었든 칸의 자리는 항상 그려진다.
                Quad(vh, x, r.yMin, w, h, husk, s);
                Outline(vh, x, r.yMin, w, h, s, Line, k > 0f ? lit : edge);

                if (k <= 0.001f) continue;
                GlowRect(vh, x, r.yMin, w * k, h, lit, s);

                // 차오르는 중인 칸에만 밝은 선두를 세운다 — 눈이 "지금 어디까지"를 잡는 점.
                if (k < 0.999f)
                    GlowRect(vh, x + w * k - 1.5f, r.yMin - 3f, 3f, h + 6f,
                        new Color(1f, 1f, 1f, 0.6f * Intensity), s);
            }

            // 피격·소모 순간의 흰 번쩍임
            if (flash > 0.001f)
                Quad(vh, r.xMin, r.yMin, r.width, h,
                    new Color(1f, 1f, 1f, flash * 0.5f), s);

            // 바 아래 기준선 — 게이지가 비어도 자리를 잃지 않게.
            Quad(vh, r.xMin, r.yMin - 7f, r.width, 1f,
                new Color(color.r, color.g, color.b, 0.16f));
        }

        /// <summary>평행사변형 한 칸의 테두리. 세로변은 본체와 같은 각도로 눕는다.</summary>
        static void Outline(VertexHelper vh, float x, float y, float w, float h, float s, float t, Color c)
        {
            Quad(vh, x, y, w, t, c);                    // 아래
            Quad(vh, x + s, y + h - t, w, t, c);        // 위
            Quad(vh, x, y, t, h, c, s);                 // 왼쪽(기울어진 세로변)
            Quad(vh, x + w - t, y, t, h, c, s);         // 오른쪽
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    /// <summary>
    /// 예지 게이지 링(도넛). 12시에서 시계방향으로 한 바퀴가 예측 지평 1→5초이고,
    /// <b>90도가 정확히 1초</b>다. 그래서 각도만 봐도 "몇 초짜리를 볼 수 있나"가 읽힌다.
    /// 채움 끝에는 밝은 캐럿이 서고, 그 위를 스캔 눈금이 훑는다.
    /// </summary>
    public class HoloArc : HoloGraphic
    {
        public float Value;
        public float Intensity = 1f;
        public float Sweep;
        /// <summary>이 아래로는 F가 안 먹는 구간. 트랙을 더 죽여 "아직 못 쓴다"를 보여준다.</summary>
        public float MinUsable;

        float flash;

        public void Punch() => flash = 1f;

        public void Tick(float dt)
        {
            flash = Mathf.Max(0f, flash - dt * 2.2f);
            SetVerticesDirty();
        }

        const int Cells = 4;              // 1초짜리 칸(1→5초)
        const int PerCell = 9;            // 칸 하나를 이루는 눈금 수
        const int Ticks = Cells * PerCell;
        const float InnerFrac = 0.74f;    // 눈금 안쪽 반지름 비율
        const float OuterFrac = 0.96f;
        const float TickGapDeg = 1.2f;
        const float CellGapDeg = 4.5f;    // 초 경계에서 더 크게 벌린다

        /// <summary>지평이 길수록 따뜻해진다 — 시선을 안 줘도 주변시로 "길게 볼 수 있다"가 잡힌다.</summary>
        static readonly Color Warm = new Color(1f, 0.92f, 0.72f);

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = GetPixelAdjustedRect();
            Vector2 c0 = r.center;
            float half = Mathf.Min(r.width, r.height) * 0.5f;
            float ri = half * InnerFrac, ro = half * OuterFrac;

            float step = 360f / Ticks;
            for (int i = 0; i < Ticks; i++)
            {
                float t0 = i / (float)Ticks;
                float k = Mathf.Clamp01((Value - t0) * Ticks);

                Color baseC = Color.Lerp(color, Warm, t0 * 0.55f);
                Color c;
                if (k <= 0f)
                {
                    // 꺼진 눈금 — 못 쓰는 구간은 한 단계 더 죽인다.
                    float a = t0 < MinUsable ? 0.045f : 0.10f;
                    c = new Color(baseC.r, baseC.g, baseC.b, a * Intensity);
                }
                else
                {
                    c = new Color(baseC.r, baseC.g, baseC.b, color.a * Intensity);
                    if (k < 1f) c.a *= 0.35f + 0.65f * k;
                }

                // 스캔 눈금이 지나가는 자리는 잠깐 밝아진다.
                float d = Mathf.Abs(Mathf.DeltaAngle(t0 * 360f, Sweep * 360f));
                if (d < 26f) c.a += (1f - d / 26f) * 0.35f * Intensity;

                // 초 경계에 붙은 눈금은 그쪽 변만 더 물러나 컷처럼 보인다.
                float g0 = (i % PerCell == 0 ? CellGapDeg : TickGapDeg) * 0.5f;
                float g1 = ((i + 1) % PerCell == 0 ? CellGapDeg : TickGapDeg) * 0.5f;
                RadialQuad(vh, c0, ri, ro, i * step + g0, (i + 1) * step - g1, c);
                if (k > 0f) RadialQuad(vh, c0, ri - 3f, ro + 3f, i * step + g0, (i + 1) * step - g1,
                    new Color(c.r, c.g, c.b, c.a * 0.22f));   // 번짐 한 겹
            }

            // 바깥 얇은 원 — 눈금이 다 꺼져도 링의 자리가 남는다.
            Ring(vh, c0, half, 1.2f, new Color(color.r, color.g, color.b, 0.18f));

            // 초 경계 바깥 눈금 — 링 밖에 놓인 초 라벨(2·3·4·5)과 짝을 맞춘다.
            for (int q = 0; q < Cells; q++)
                RadialQuad(vh, c0, ro + 5f, ro + 13f, q * 90f - 0.7f, q * 90f + 0.7f,
                    new Color(color.r, color.g, color.b, 0.5f * Intensity));

            // 채움 끝 캐럿 — 링을 가로질러 삐져나오는 흰 선. 정확한 현재값을 집어준다.
            if (Value > 0.002f && Value < 0.998f)
            {
                float a = Value * 360f;
                RadialQuad(vh, c0, ri - 7f, ro + 7f, a - 0.9f, a + 0.9f,
                    new Color(1f, 0.98f, 0.9f, 0.85f * Intensity));
            }

            if (flash > 0.001f)
                Ring(vh, c0, half * 0.985f, 4f, new Color(1f, 1f, 1f, flash * 0.45f));
        }

        /// <summary>12시 기준 시계방향 각도(도)로 잘라낸 부채꼴 조각.</summary>
        static void RadialQuad(VertexHelper vh, Vector2 c0, float ri, float ro, float a0, float a1, Color c)
        {
            Vector2 d0 = Dir(a0), d1 = Dir(a1);
            Quad(vh, c0 + d0 * ri, c0 + d0 * ro, c0 + d1 * ro, c0 + d1 * ri, c);
        }

        static void Ring(VertexHelper vh, Vector2 c0, float radius, float thick, Color c)
        {
            const int steps = 72;
            for (int i = 0; i < steps; i++)
            {
                float a0 = i * 360f / steps, a1 = (i + 1) * 360f / steps;
                RadialQuad(vh, c0, radius - thick * 0.5f, radius + thick * 0.5f, a0, a1, c);
            }
        }

        static Vector2 Dir(float deg)
        {
            float rad = deg * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(rad), Mathf.Cos(rad));   // 12시에서 시계방향
        }
    }
    // <<< [홀로그램 HUD 끝]
}
