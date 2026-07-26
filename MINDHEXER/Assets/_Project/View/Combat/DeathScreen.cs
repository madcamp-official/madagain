using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

namespace Game.View
{
    // >>> [사망 화면, 2026-07-23] hp가 0이 되어도 아무 일도 일어나지 않던 걸 막는다.
    //
    // sim은 피해를 받아 hp를 0에서 <b>멈출 뿐</b>이다(CombatResolve). 죽음의 처리는 전부
    // 여기, 뷰에 있다 — sim을 건드리지 않으므로 결정론·예측 재생과 섞이지 않는다.
    //
    // 정지는 <b>timeScale로 하지 않는다</b>. timeScale은 예측 컨트롤러가 매 프레임 덮어쓰는
    // 값이라 여기서 0을 넣으면 서로 싸운다. 타이틀 화면과 같은 방법으로 Main을 끈다 —
    // 그러면 입력·sim 전진·1인칭 카메라가 한꺼번에 멎고, 연출(파티클·시체)은 계속 돈다.
    //
    // 조립 방식은 TitleScreen과 같다: 프리팹·씬 세팅 없이 코드로만, 아트 PNG 없이.

    /// <summary>
    /// 죽으면 화면이 암전되며 "YOU DIE"가 뜬다. ENTER로 그 자리에서 다시 시작.
    /// 키보드 전용이라 EventSystem·커서가 필요 없다(FPS의 마우스 잠금과 싸우지 않는다).
    /// </summary>
    public class DeathScreen : MonoBehaviour
    {
        /// <summary>죽어 있는 동안 켜진다. 다른 UI가 "지금 화면은 내 것이 아니다"를 알 수 있게.</summary>
        public static bool Active { get; private set; }

        // ── 타이밍(전부 unscaled — timeScale이 얼마든 같은 속도로 흐른다) ──
        const float VeilFade  = 1.10f;   // 암전에 걸리는 시간
        const float TitleAt   = 0.85f;   // 글자가 들어오기 시작하는 시각
        const float TitleFade = 0.70f;
        const float HintAt    = 1.90f;
        const float HintFade  = 0.50f;
        const float ReviveFade = 0.35f;  // 재시작할 때 장막이 걷히는 시간

        /// <summary>피의 잔광. HUD 체력색(#F94B1E)을 그대로 쓴다 — 죽음도 같은 팔레트 안에 남는다.</summary>
        static readonly Color Ember = new Color(0.976f, 0.478f, 0.106f);

        const float RefW = 1920f, RefH = 1080f;
        /// <summary>글자 사이를 벌린다 — Legacy Text엔 자간이 없어 공백으로 준다(타이틀과 같은 규칙).</summary>
        const string DieText = "Y O U   D I E";

        Canvas canvas;
        CanvasScaler scaler;
        Image veil;
        Image bar;            // 글자를 가로지르는 베인 자국
        Text die, dieEcho, hint;

        bool dead, reviving;
        float t;              // 죽은 뒤 흐른 시간(unscaled)

        void Update()
        {
            var main = Main.Instance;
            if (main == null) return;

            if (!dead)
            {
                // 죽음을 <b>실제 월드에서만</b> 본다. 예측 미리보기는 별도 월드를 굴리므로
                // 여기 hp에 영향을 주지 않는다(그래서 따로 걸러낼 필요가 없다).
                if (main.enabled && main.PlayerDead) Die(main);
                return;
            }

            t += Time.unscaledDeltaTime;

            if (reviving)
            {
                float k = Mathf.Clamp01(t / ReviveFade);
                SetVeil(1f - k);
                SetAlpha(die, 0f); SetAlpha(dieEcho, 0f); SetAlpha(hint, 0f);
                SetBar(0f, 0f);
                if (k >= 1f) Revive(main);
                return;
            }

            Animate();

            // 글자가 다 뜨기 전에 누르는 건 무시한다 — 죽자마자 튀는 손가락에 재시작이 먹히면
            // "무슨 일이 있었는지" 볼 새가 없다.
            if (t < HintAt) return;
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame
                || kb.spaceKey.wasPressedThisFrame)
            {
                reviving = true;
                t = 0f;
            }
        }

        void Die(Main main)
        {
            dead = true;
            Active = true;
            t = 0f;

            Build();

            // 타이틀과 같은 방법 — Main을 끄면 입력·sim·1인칭 카메라가 한꺼번에 멎는다.
            // (Main.OnDisable이 timeScale도 1로 되돌린다)
            main.enabled = false;
            UiVisibility.SuppressedByMenu = true;   // 체력 0짜리 HUD가 장막 뒤에 남지 않게
            HitStop.FrozenTicks = 0;
            CombatAudio.PlayerHurt();
        }

        void Revive(Main main)
        {
            Active = false;
            dead = reviving = false;
            UiVisibility.SuppressedByMenu = false;
            main.RestartRun();
            main.enabled = true;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            // 화면 부품은 버린다 — 다음 죽음에 새로 짓는다(매번 짓는 비용은 무시할 만하고,
            // 살아 있는 동안 빈 캔버스가 남아 다른 UI 위에 얹히는 쪽이 더 성가시다).
            // ★ 스케일러를 <b>먼저</b> 지운다 — CanvasScaler는 Canvas를 RequireComponent로 물고
            //   있어서, 캔버스부터 지우려 하면 "의존 컴포넌트가 있다"며 거부당한다.
            Destroy(scaler);
            Destroy(canvas);
            foreach (Transform c in transform) Destroy(c.gameObject);
            canvas = null; scaler = null; veil = null; bar = null; die = dieEcho = hint = null;
        }

        void Animate()
        {
            // 암전은 뒤로 갈수록 빨라진다(ease-in) — 처음 한 박자는 죽는 장면이 보이고,
            // 그 다음에 훅 꺼진다. 선형으로 하면 그냥 조명이 꺼지는 것처럼 밋밋하다.
            float v = Mathf.Clamp01(t / VeilFade);
            SetVeil(v * v);

            float k = Mathf.Clamp01((t - TitleAt) / TitleFade);
            k = 1f - (1f - k) * (1f - k);                     // ease-out — 스며들듯 떠오른다
            SetAlpha(die, k);
            // 잔상은 본체보다 옅게, 옆으로 살짝 밀려 떨린다 — 예지 잔상과 같은 어법이지만
            // 여기선 청록이 아니라 불씨색이다(미래가 아니라 끝난 것이므로).
            float jitter = Mathf.Sin(t * 7.3f) * 2.5f + Mathf.Sin(t * 2.1f) * 1.5f;
            ((RectTransform)dieEcho.transform).anchoredPosition = new Vector2(jitter, 0f);
            SetAlpha(dieEcho, k * 0.45f);

            // 베인 자국이 글자를 가로지르며 벌어진다.
            SetBar(k, k);

            SetAlpha(hint, Mathf.Clamp01((t - HintAt) / HintFade) * 0.40f);
        }

        void SetVeil(float a)
        {
            if (veil != null) veil.color = new Color(0f, 0f, 0f, Mathf.Clamp01(a));
        }

        void SetBar(float width01, float alpha)
        {
            if (bar == null) return;
            var rt = bar.rectTransform;
            rt.sizeDelta = new Vector2(760f * width01, 2f);
            bar.color = new Color(Ember.r, Ember.g, Ember.b, alpha * 0.55f);
        }

        static void SetAlpha(Graphic g, float a)
        {
            if (g == null) return;
            var c = g.color;
            g.color = new Color(c.r, c.g, c.b, a);
        }

        // ── 조립 ─────────────────────────────────────────────────────────
        void Build()
        {
            canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 240;   // 타이틀(200)·등장 컷신(220)보다 위 — 무엇도 뚫고 나오지 않게
            scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(RefW, RefH);
            scaler.matchWidthOrHeight = 0.5f;

            veil = NewImage("Veil");
            Stretch(veil.rectTransform);
            veil.color = new Color(0f, 0f, 0f, 0f);

            // 잔상을 먼저 깔고 그 위에 본체(그려지는 순서 = 겹치는 순서).
            dieEcho = NewText("DieEcho", 122, TextAnchor.MiddleCenter);
            SetRectC((RectTransform)dieEcho.transform, 0f, 40f, 1600f, 180f);
            dieEcho.text = DieText;
            dieEcho.color = new Color(Ember.r, Ember.g, Ember.b, 0f);

            die = NewText("Die", 122, TextAnchor.MiddleCenter);
            SetRectC((RectTransform)die.transform, 0f, 40f, 1600f, 180f);
            die.text = DieText;
            die.color = new Color(0.92f, 0.90f, 0.89f, 0f);

            bar = NewImage("Bar");
            SetRectC(bar.rectTransform, 0f, -48f, 0f, 2f);
            bar.color = new Color(Ember.r, Ember.g, Ember.b, 0f);

            hint = NewText("Hint", 20, TextAnchor.MiddleCenter);
            SetRectC((RectTransform)hint.transform, 0f, -140f, 1200f, 28f);
            hint.text = "ENTER  다 시  시 작";
            hint.color = new Color(1f, 1f, 1f, 0f);
        }

        // ── UGUI 유틸 (TitleScreen과 같은 것들) ─────────────────────────
        Image NewImage(string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(transform, false);
            var img = go.GetComponent<Image>();
            img.raycastTarget = false;
            return img;
        }

        Text NewText(string name, float size, TextAnchor anchor)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(transform, false);
            var t = go.GetComponent<Text>();
            t.raycastTarget = false;
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.fontSize = Mathf.RoundToInt(size);
            t.fontStyle = FontStyle.Bold;
            t.alignment = anchor;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        /// <summary>화면 <b>중앙</b> 기준 오프셋으로 배치(해상도가 달라도 가운데를 지킨다).</summary>
        static void SetRectC(RectTransform rt, float dx, float dy, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(dx, dy);
            rt.sizeDelta = new Vector2(w, h);
        }
    }

    /// <summary>Play 시 자동 부착(PlayerHitFeedbackBoot 등과 같은 자리·같은 방식).</summary>
    public static class DeathScreenBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Object.FindFirstObjectByType<DeathScreen>() == null)
                new GameObject("[DeathScreen]").AddComponent<DeathScreen>();
        }
    }
}
