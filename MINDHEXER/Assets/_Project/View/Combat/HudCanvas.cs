using UnityEngine;
using UnityEngine.UI;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// [HUD 캔버스, 2026-07-22] 게임 HUD. UGUI 캔버스를 <b>코드로 조립</b>한다(프리팹·씬 세팅 없음 —
    /// MapBuilder·CombatHudBoot과 같은 결).
    ///
    /// 아트 PNG(Resources/HudFrame)는 <b>껍데기 전용</b>이다. 구워져 있던 눈금과 "62%"는
    /// HudFrameBaker가 파내서 빈 슬롯으로 만들어 두었고, 그 자리에 실제 수치를 그리는 게 여기다.
    /// 눈금 형상은 <see cref="HudSprites"/>가 아트 실측값(칸 16개·기울기 5px 등)대로 굽는다.
    ///
    /// <para><b>정렬 원리</b>: 모든 요소의 RectTransform 앵커를 <i>원본 아트 픽셀 좌표의 비율</i>로
    /// 잡고 offset을 0으로 둔다. 프레임 Image도 화면 전체에 늘어나므로, 화면비가 어떻든 프레임과
    /// 게이지가 <b>똑같이</b> 늘어나 항상 맞물린다(다이얼이 타원이 되면 아트의 원도 같이 타원이 된다).</para>
    /// </summary>
    public class HudCanvas : MonoBehaviour
    {
        public static HudCanvas Instance { get; private set; }

        /// <summary>Resources 안의 프레임 껍데기 텍스처 이름. 없으면 게이지만 뜬다.</summary>
        public const string FrameResource = "HudFrame";

        /// <summary>[다이얼 3분할, 2026-07-22] 예지 게이지 링 아트. 아트가 360° 전체 링으로 와서
        /// 코드로 굽던 눈금(<see cref="HudSprites.Ring"/>) 대신 이걸 Radial360으로 채운다.
        /// 없으면 예전처럼 코드 링으로 자동 폴백한다.</summary>
        public const string DialRingResource = "HudDialRing";

        /// <summary>합성된 프레임에서 다이얼이 차지하는 사각형(원본 아트 픽셀).
        /// HudFrameBaker.DialDest와 <b>같은 값</b>이어야 링이 베젤과 동심원으로 맞는다.</summary>
        const float DialRectX = 1360f, DialRectY = 71f, DialRectSize = 244f;
        /// <summary>다이얼 사각형 대비 가운데 초 표시가 들어갈 비율. 아트의 안쪽 원판은 반지름
        /// 비율 0.57까지지만, 그 안쪽에 장식 고리가 한 겹 더 있어 0.46까지만 쓴다.</summary>
        const float DialTextFrac = 0.46f;

        /// <summary>[피드백, 2026-07-22] HUD 전체 확대 배율. 아트의 테두리가 화면 가장자리에서
        /// 살짝 안쪽으로 떠 보인다는 피드백 — 1보다 크게 잡으면 바깥 여백이 화면 밖으로 밀려나며
        /// 테두리가 화면 모서리에 딱 붙는다.
        ///
        /// <para><b>프레임만 키우면 안 된다</b> — 게이지 채움·다이얼 링은 아트에 파인 슬롯 위에
        /// 얹히는 별개 오브젝트라, 프레임만 키우면 그 자리에서 어긋난다. 그래서 프레임과 게이지를
        /// 같은 부모(<c>content</c>)에 넣고 그 부모를 통째로 확대한다.</para></summary>
        /// <para>1.06까지 올려보니 위아래 테두리 장식이 절반쯤 잘려나갔다 — 모서리 장식이
        /// 살아있으면서 테두리가 화면에 닿는 선이 1.035다. 더 키우려면 잘림을 감수할 것.</para>
        const float FrameOverscan = 1.035f;

        // ── 원본 아트 좌표계 (HudFrameBaker의 실측값과 같이 간다) ────────
        const float SrcW = 1672f, SrcH = 941f;
        const float BarX = 167f;              // 칸이 시작하는 x(바닥 행 기준)
        const float BarHealthY = 713f;
        const float BarDashY   = 771f;
        const float BarLungeY  = 831f;
        const float DialCx = 1482f, DialCy = 193f, DialOuterR = 97f;

        // ── 색 (원본 아트에서 직접 샘플링) ───────────────────────────────
        static readonly Color HealthLit = new Color(0.976f, 0.294f, 0.118f);   // #F94B1E
        static readonly Color DashLit   = new Color(0.357f, 0.878f, 0.988f);   // #5BE0FC
        static readonly Color LungeLit  = new Color(0.035f, 0.910f, 0.976f);   // #09E8F9
        static readonly Color RingLit   = new Color(0.447f, 0.910f, 0.996f);   // #72E8FE
        /// <summary>빈 칸. 아트의 회색(#444)보다 조금 죽여서 채움과 대비를 준다.</summary>
        static readonly Color TrackDim = new Color(0.16f, 0.17f, 0.19f, 0.85f);
        /// <summary>체력이 바닥일 때. 붉은 계열 안에서 더 위험해 보이는 쪽으로 민다.</summary>
        static readonly Color HealthCritical = new Color(1f, 0.13f, 0.06f);

        const float LowHealthFraction = 0.34f;      // 이 아래면 경고색으로 물든다
        const float CriticalHealthFraction = 0.16f; // 이 아래면 맥박까지 뛴다
        const float GlowAlpha = 0.42f;              // 채움 밑에 깔리는 글로우 세기
        const float FillLerpSpeed = 14f;            // 게이지가 목표치로 붙는 속도(1/초)
        const float FlashDecay = 3.4f;              // 번쩍임이 사그라드는 속도

        Canvas canvas;
        GameObject root;
        Gauge health, dash, lunge, dial;
        Text dialText;
        RectTransform dialTextRect;

        /// <summary>게이지 한 줄. 트랙·글로우·채움·번쩍임 네 겹을 한 묶음으로 다룬다.</summary>
        class Gauge
        {
            public Image glow, fill, flash;
            public float shown = -1f;   // 화면에 실제로 그려지는 값(목표치로 부드럽게 따라간다)
            public float flashAmount;   // 0~1, 소모·피격 순간 1로 튀고 감쇠
            public bool radial;

            public void Set(float value01, float dt)
            {
                if (shown < 0f) shown = value01;
                // 갑자기 튀지 않게 목표치로 붙인다 — 대시 충전처럼 연속으로 차는 값이
                // 프레임마다 딱딱 끊겨 보이는 걸 막는다.
                shown = Mathf.Lerp(shown, value01, 1f - Mathf.Exp(-FillLerpSpeed * dt));
                if (Mathf.Abs(shown - value01) < 0.0015f) shown = value01;

                float amount = radial ? shown : HudSprites.BarFillAmount(shown);
                fill.fillAmount = amount;
                glow.fillAmount = amount;
                flash.fillAmount = amount;

                flashAmount = Mathf.Max(0f, flashAmount - FlashDecay * dt);
                var fc = flash.color;
                flash.color = new Color(fc.r, fc.g, fc.b, flashAmount * 0.75f);
            }

            public void Tint(Color lit, float glowScale)
            {
                fill.color = lit;
                glow.color = new Color(lit.r, lit.g, lit.b, GlowAlpha * glowScale);
            }

            public void Punch() => flashAmount = 1f;
        }

        // 직전 프레임 값 — 변화를 감지해 번쩍임을 터뜨린다.
        int lastHp = int.MinValue, lastDashCharges = int.MinValue, lastLungeStacks = int.MinValue;
        bool wasFull;
        float readyPulse;
        int lastTenths = -1;

        void Awake()
        {
            Instance = this;
            Build();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void LateUpdate()
        {
            // Main.Instance를 프레임 안에서 여러 번 다시 읽지 않는다 — 씬 전환·재생 종료처럼
            // 도중에 Main이 사라지는 순간에 "앞에선 통과했는데 뒤에서 null"이 되어 NRE가 났다.
            Main main = Main.Instance;
            bool show = !UiVisibility.Skip && main != null;
            if (root.activeSelf != show) root.SetActive(show);
            if (!show) return;

            // 예측이 timeScale을 소유하므로(슬로모 중에도 HUD는 실시간으로 움직여야 한다)
            // 연출 타이밍은 전부 unscaled로 센다.
            float dt = Time.unscaledDeltaTime;

            ref readonly PlayerCombatState c = ref main.World.player.combat;
            ref readonly PlayerSim p = ref main.World.player;

            UpdateHealth(in c, dt);
            UpdateDash(in p, dt);
            UpdateLunge(in c, dt);
            UpdateDial(main.PredictionCharge01, dt);
        }

        // ── 게이지별 갱신 ────────────────────────────────────────────────
        void UpdateHealth(in PlayerCombatState c, float dt)
        {
            float f = Mathf.Clamp01(c.hp / (float)CombatConfig.PlayerMaxHp);
            if (c.hp < lastHp) health.Punch();          // 피격 순간 흰 번쩍임
            lastHp = c.hp;

            // 낮아질수록 붉은 쪽으로 물들고, 위험 수위 아래에선 맥박이 뛴다.
            float danger = 1f - Mathf.InverseLerp(CriticalHealthFraction, LowHealthFraction, f);
            var col = Color.Lerp(HealthLit, HealthCritical, danger);
            float pulse = f <= CriticalHealthFraction
                ? 0.72f + 0.55f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 4.2f))
                : 1f;
            health.Tint(col, pulse);
            health.Set(f, dt);
        }

        void UpdateDash(in PlayerSim p, float dt)
        {
            int max = Mathf.Max(1, SimConfig.DashMaxCharges);
            float v = p.dashCharges;
            // 충전 중인 다음 스택의 진행분까지 이어 붙여야 칸이 "차오르는" 게 보인다.
            if (p.dashCharges < max && p.dashRecharge > 0 && SimConfig.DashRechargeTicks > 0)
                v += 1f - Mathf.Clamp01(p.dashRecharge / (float)SimConfig.DashRechargeTicks);

            if (p.dashCharges != lastDashCharges && lastDashCharges != int.MinValue) dash.Punch();
            lastDashCharges = p.dashCharges;

            // 한 번도 못 쓰는 상태(0스택)면 죽여서 "지금은 안 된다"를 색으로 알린다.
            dash.Tint(DashLit, p.dashCharges > 0 ? 1f : 0.25f);
            dash.Set(Mathf.Clamp01(v / max), dt);
        }

        void UpdateLunge(in PlayerCombatState c, float dt)
        {
            int max = Mathf.Max(1, CombatConfig.LungeMaxStacks);
            float v = c.lungeStacks;
            if (c.lungeCooldown > 0 && CombatConfig.LungeCooldownTicks > 0 && c.lungeStacks < max)
                v += 1f - Mathf.Clamp01(c.lungeCooldown / (float)CombatConfig.LungeCooldownTicks);

            if (c.lungeStacks != lastLungeStacks && lastLungeStacks != int.MinValue) lunge.Punch();
            lastLungeStacks = c.lungeStacks;

            lunge.Tint(LungeLit, c.lungeStacks > 0 ? 1f : 0.25f);
            lunge.Set(Mathf.Clamp01(v / max), dt);
        }

        void UpdateDial(float charge01, float dt)
        {
            float charge = Mathf.Clamp01(charge01);
            bool usable = charge >= PredictionConfig.ChargeMinToUse;
            bool full = charge >= 1f;

            if (full && !wasFull) { dial.Punch(); readyPulse = 1f; }   // 가득 찬 순간 한 번 번쩍
            wasFull = full;

            // READY — 다 찼으면 링이 천천히 숨쉬듯 밝아졌다 어두워진다. 숫자만 보고 있지 않아도
            // 시야 구석에서 "쓸 수 있다"가 읽히게 하는 게 목적이라, 색이 아니라 밝기로 준다.
            float breathe = full ? 1f + 0.45f * Mathf.Sin(Time.unscaledTime * 3.1f) : 1f;
            readyPulse = Mathf.Max(0f, readyPulse - dt * 1.6f);
            float glowScale = (usable ? 1f : 0.22f) * breathe + readyPulse * 0.8f;

            var col = usable ? RingLit : new Color(RingLit.r * 0.5f, RingLit.g * 0.5f, RingLit.b * 0.55f);
            if (full) col = Color.Lerp(col, Color.white, 0.18f + 0.14f * Mathf.Sin(Time.unscaledTime * 3.1f));
            dial.Tint(col, glowScale);
            dial.Set(charge, dt);

            // 숫자는 %가 아니라 "몇 초를 내다보는가" — 플레이어가 실제로 판단하는 값이다.
            // (게이지를 아껴 길게 볼지, 짧게 자주 쓸지가 이 숫자로 드러난다.)
            int tenths = Mathf.RoundToInt(PredictionConfig.ChargeToSeconds(charge) * 10f);
            if (tenths != lastTenths) { lastTenths = tenths; dialText.text = SecondsText(tenths); }
            dialText.color = usable ? Color.Lerp(col, Color.white, 0.25f)
                                    : new Color(col.r, col.g, col.b, 0.55f);

            // 다 찼을 때만 숫자가 아주 살짝 커진다 — 크기 변화는 밝기보다 주변시에 잘 걸린다.
            float scale = full ? 1f + 0.035f * Mathf.Sin(Time.unscaledTime * 3.1f) : 1f;
            dialTextRect.localScale = new Vector3(scale, scale, 1f);
        }

        /// <summary>
        /// "3.2s" — 소수 한 자리. 단위는 작게 붙여 아트의 "62%" 조판(큰 숫자 + 작은 단위)을 따른다.
        /// 매 프레임 문자열을 새로 엮으면 GC가 튀므로 0.1초 단위로 미리 구워 둔다.
        /// </summary>
        static string SecondsText(int tenths)
        {
            if (SecondsCache == null)
            {
                SecondsCache = new string[MaxTenths + 1];
                for (int i = 0; i <= MaxTenths; i++)
                    SecondsCache[i] = (i / 10) + "." + (i % 10) + "<size=22><color=#7FD4E8>s</color></size>";
            }
            return SecondsCache[Mathf.Clamp(tenths, 0, MaxTenths)];
        }

        const int MaxTenths = 60;   // 6.0초까지 — Min/MaxDurationSeconds(1~5초)에 여유를 둔 상한
        static string[] SecondsCache;

        // ── 조립 ─────────────────────────────────────────────────────────
        void Build()
        {
            canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            // GraphicRaycaster는 일부러 붙이지 않는다 — HUD가 마우스 클릭을 먹으면 안 된다.
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(SrcW, SrcH);
            scaler.matchWidthOrHeight = 0.5f;   // 폰트 크기가 가로·세로 어느 한쪽에만 끌려가지 않게

            root = NewRect("Root", transform).gameObject;
            var rootRt = (RectTransform)root.transform;
            Stretch(rootRt);

            // 프레임과 그 위에 얹히는 것들을 한 부모에 묶어 통째로 확대한다(FrameOverscan).
            // 중심 기준 스케일이라 화면 중앙은 그대로고 바깥 여백만 화면 밖으로 밀려난다.
            var content = NewRect("Content", rootRt);
            Stretch(content);
            content.localScale = new Vector3(FrameOverscan, FrameOverscan, 1f);

            var frame = Resources.Load<Sprite>(FrameResource);
            if (frame != null)
            {
                var img = NewImage("Frame", content);
                Stretch(img.rectTransform);
                img.sprite = frame;
            }
            else
            {
                Debug.LogWarning($"[HUD] Resources/{FrameResource} 가 없어 프레임 없이 게이지만 표시합니다. " +
                                 "메뉴 Precog → HUD 프레임 굽기 를 실행하세요.");
            }

            health = BuildBar(content, "Health", BarHealthY, HealthLit);
            dash   = BuildBar(content, "Dash",   BarDashY,   DashLit);
            lunge  = BuildBar(content, "Lunge",  BarLungeY,  LungeLit);
            BuildDial(content);
        }

        /// <summary>
        /// 트랙·글로우·채움·번쩍임이 <b>같은 눈금 스프라이트</b>를 색만 바꿔 공유한다.
        /// 겹치는 순서가 곧 보이는 순서다 — 트랙 위에 글로우가 번지고 그 위에 채움이 또렷하게 앉는다.
        /// </summary>
        Gauge BuildBar(RectTransform parent, string name, float srcTop, Color lit)
        {
            var group = NewRect(name, parent);
            // 글로우가 번질 여백만큼 사방을 넓힌 사각형을 쓴다(스프라이트도 같은 여백으로 구워져 있다).
            SetSrcRect(group,
                BarX - HudSprites.BarPad,
                srcTop - HudSprites.BarPad,
                HudSprites.BarSpanW + HudSprites.BarSlant + HudSprites.BarPad * 2f,
                HudSprites.BarSpanH + HudSprites.BarPad * 2f);

            var track = NewImage("Track", group);
            Stretch(track.rectTransform);
            track.sprite = HudSprites.Bar(false);
            track.color = TrackDim;

            var g = new Gauge
            {
                glow  = NewFilled("Glow",  group, HudSprites.Bar(true),  Image.FillMethod.Horizontal),
                fill  = NewFilled("Fill",  group, HudSprites.Bar(false), Image.FillMethod.Horizontal),
                flash = NewFilled("Flash", group, HudSprites.Bar(false), Image.FillMethod.Horizontal),
            };
            g.flash.color = new Color(1f, 1f, 1f, 0f);
            g.Tint(lit, 1f);
            return g;
        }

        void BuildDial(RectTransform parent)
        {
            var group = NewRect("Prediction", parent);
            // [다이얼 3분할, 2026-07-22] 아트 링이 있으면 프레임에 합성된 베젤·원판과 같은
            // 사각형에 그대로 얹는다(동심원이 자동으로 맞는다). 없으면 예전처럼 코드로 구운
            // 링을 쓰고, 그 스프라이트는 글로우 여백만큼 크게 구워져 있어 사각형도 더 크다.
            Sprite artRing = Resources.Load<Sprite>(DialRingResource);
            if (artRing != null)
            {
                SetSrcRect(group, DialRectX, DialRectY, DialRectSize, DialRectSize);
            }
            else
            {
                float half = DialOuterR / HudSprites.RingOuterFrac;
                SetSrcRect(group, DialCx - half, DialCy - half, half * 2f, half * 2f);
            }

            Sprite ringSharp = artRing != null ? artRing : HudSprites.Ring(false);
            Sprite ringGlow  = artRing != null ? artRing : HudSprites.Ring(true);

            var track = NewImage("Track", group);
            Stretch(track.rectTransform);
            track.sprite = ringSharp;
            track.color = TrackDim;

            dial = new Gauge
            {
                radial = true,
                glow  = NewFilled("Glow",  group, ringGlow,  Image.FillMethod.Radial360),
                fill  = NewFilled("Fill",  group, ringSharp, Image.FillMethod.Radial360),
                flash = NewFilled("Flash", group, ringSharp, Image.FillMethod.Radial360),
            };
            dial.flash.color = new Color(1f, 1f, 1f, 0f);
            dial.Tint(RingLit, 1f);

            dialTextRect = NewRect("Seconds", group);
            // 안쪽 원판(아트가 살려둔 부분) 안에 들어가도록 링 안쪽 반지름보다 조금 작게.
            float t = artRing != null
                ? DialTextFrac
                : HudSprites.RingOuterFrac * HudSprites.RingInnerFrac * 0.95f;
            dialTextRect.anchorMin = new Vector2(0.5f - t * 0.5f, 0.5f - t * 0.5f);
            dialTextRect.anchorMax = new Vector2(0.5f + t * 0.5f, 0.5f + t * 0.5f);
            dialTextRect.offsetMin = Vector2.zero;
            dialTextRect.offsetMax = Vector2.zero;

            dialText = dialTextRect.gameObject.AddComponent<Text>();
            dialText.raycastTarget = false;
            dialText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            dialText.alignment = TextAnchor.MiddleCenter;
            dialText.supportRichText = true;
            dialText.fontStyle = FontStyle.Bold;
            // 캔버스가 원본 해상도 기준으로 스케일되므로 폰트 크기도 원본 픽셀로 잡으면 된다.
            dialText.fontSize = 44;
            dialText.color = RingLit;
            dialText.text = "0.0s";
        }

        // ── UGUI 유틸 ────────────────────────────────────────────────────
        static Image NewFilled(string name, Transform parent, Sprite sprite, Image.FillMethod method)
        {
            var img = NewImage(name, parent);
            Stretch(img.rectTransform);
            img.sprite = sprite;
            img.type = Image.Type.Filled;
            img.fillMethod = method;
            img.fillOrigin = method == Image.FillMethod.Radial360
                ? (int)Image.Origin360.Top
                : (int)Image.OriginHorizontal.Left;
            img.fillClockwise = true;
            img.fillAmount = 0f;
            return img;
        }

        static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        static Image NewImage(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.raycastTarget = false;
            return img;
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        /// <summary>원본 아트 픽셀 좌표(좌상단 기준)를 앵커 비율로 옮긴다 — 프레임과 같이 늘어난다.</summary>
        static void SetSrcRect(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = new Vector2(x / SrcW, 1f - (y + h) / SrcH);
            rt.anchorMax = new Vector2((x + w) / SrcW, 1f - y / SrcH);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }

    /// <summary>Play 시 HUD 자동 생성. 어느 쪽을 띄울지는 <see cref="HudStyle"/>가 정한다.</summary>
    public static class HudCanvasBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            // VR: ScreenSpaceOverlay HUD는 스테레오에서 올바로 렌더되지 않는다. milestone에선 띄우지 않는다.
            //     (정식 World-Space HUD 전환은 기기 검증 후 별도 단계)
            if (VrMode.Enabled) return;

            // [홀로그램 HUD, 2026-07-22] 코드로 그리는 HoloHud가 기본. 아트 프레임 HUD로
            // 되돌리려면 HudStyle.UseHologram = false.
            if (HudStyle.UseHologram)
            {
                if (Object.FindFirstObjectByType<HoloHud>() == null)
                    new GameObject("[HoloHud]").AddComponent<HoloHud>();
                return;
            }
            if (Object.FindFirstObjectByType<HudCanvas>() != null) return;
            new GameObject("[HudCanvas]").AddComponent<HudCanvas>();
        }
    }
}
