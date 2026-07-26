using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// [HUD 캔버스, 2026-07-22] 게이지 눈금 스프라이트를 코드로 굽는다.
    ///
    /// 아트 PNG에 구워진 눈금을 쓰지 않는 이유: 그건 "특정 수치로 채워진 한 장"이라 값이 바뀌면
    /// 못 쓴다. 대신 <b>가득 찬 눈금 한 벌</b>을 흰색으로 만들어 두고, UGUI Image의 Filled 모드로
    /// 잘라 쓴다 — 트랙(어두운 색)과 채움(밝은 색)이 같은 스프라이트를 색만 바꿔 공유하므로
    /// 눈금이 어긋날 수가 없다. 흰색으로 굽는 건 Image.color 틴트를 그대로 곱하기 위해서다.
    ///
    /// <para>아래 형상값은 원본 아트(Hudframe_source.png)를 픽셀 스캔해서 실측한 것이다 —
    /// 칸 수·피치·기울기를 아트와 맞춰야 우리가 그린 게이지가 하우징 안에 "원래 거기 있던 것처럼"
    /// 앉는다. 특히 칸이 <b>오른쪽으로 기운 평행사변형</b>이라는 게 이 HUD의 인상을 크게 좌우한다.</para>
    ///
    /// <para><b>글로우</b>: 아트의 게이지는 강하게 빛난다. 같은 형상을 블러한 장을 채움 밑에 깔아
    /// 재현한다. 블러가 번질 여백이 필요해서 스프라이트는 형상보다 조금 크게 굽고, 배치하는 쪽도
    /// 같은 여백만큼 넓은 사각형을 쓴다(<see cref="BarPad"/> / <see cref="RingOuterFrac"/>).</para>
    /// </summary>
    public static class HudSprites
    {
        // ── 가로 게이지 실측 형상 ────────────────────────────────────────
        /// <summary>칸이 놓인 가로 폭(기울기 제외). 원본 x 167~410.</summary>
        public const float BarSpanW = 244f;
        public const float BarSpanH = 18f;
        public const int   BarSegments = 16;
        public const float BarSegW = 12f;
        public const float BarPitch = 15.4f;
        /// <summary>위로 갈수록 칸이 오른쪽으로 밀리는 양(px). 평행사변형 기울기.</summary>
        public const float BarSlant = 5f;
        /// <summary>글로우가 번질 여백(px). 배치 사각형도 사방으로 이만큼 넓힌다.</summary>
        public const float BarPad = 5f;

        // ── 다이얼 링 실측 형상 ──────────────────────────────────────────
        public const int   RingSegments = 32;
        public const float RingGapDeg = 2.8f;
        /// <summary>바깥 반지름 대비 안쪽 구멍. 원본 링 슬롯 62~97px.</summary>
        public const float RingInnerFrac = 0.64f;
        /// <summary>텍스처 반폭 대비 링 바깥 반지름. 나머지는 글로우가 번질 여백.</summary>
        public const float RingOuterFrac = 0.86f;

        const int BarScale = 4;      // 원본 1px당 텍스처 픽셀
        const int RingSize = 512;
        // 번짐 폭(텍스처 px). 아트의 글로우가 칸 바깥으로 대략 5원본px 퍼지는 것에 맞춘다 —
        // 바는 원본 1px = 4텍스처px, 링은 약 2.3텍스처px라 값이 다르다.
        const float GlowBlurBar = 20f, GlowBlurRing = 16f;

        static Sprite _bar, _barGlow, _ring, _ringGlow;

        /// <summary>가로 눈금 한 벌. 왼쪽부터 차도록 Filled Horizontal로 쓴다.</summary>
        public static Sprite Bar(bool glow)
        {
            if (glow) return _barGlow != null ? _barGlow : _barGlow = BuildBar(true);
            return _bar != null ? _bar : _bar = BuildBar(false);
        }

        /// <summary>원형 눈금 한 벌. 12시부터 시계방향으로 Filled Radial360으로 쓴다.</summary>
        public static Sprite Ring(bool glow)
        {
            if (glow) return _ringGlow != null ? _ringGlow : _ringGlow = BuildRing(true);
            return _ring != null ? _ring : _ring = BuildRing(false);
        }

        /// <summary>
        /// 게이지 채움 비율(0~1)을 Image.fillAmount로 옮긴다. 스프라이트에 글로우 여백이 붙어
        /// 있어서 그냥 넣으면 여백만큼 어긋난다 — 여백을 빼고 칸 영역에만 매핑한다.
        /// </summary>
        public static float BarFillAmount(float fill01)
        {
            float total = BarSpanW + BarSlant + BarPad * 2f;
            return (BarPad + Mathf.Clamp01(fill01) * BarSpanW) / total;
        }

        // ── 굽기 ─────────────────────────────────────────────────────────
        static Sprite BuildBar(bool glow)
        {
            int w = Mathf.RoundToInt((BarSpanW + BarSlant + BarPad * 2f) * BarScale);
            int h = Mathf.RoundToInt((BarSpanH + BarPad * 2f) * BarScale);
            float pad = BarPad * BarScale;
            float segW = BarSegW * BarScale, pitch = BarPitch * BarScale, slant = BarSlant * BarScale;
            float bodyH = BarSpanH * BarScale;

            var a = new float[w * h];
            for (int y = 0; y < h; y++)
            {
                float v = (y - pad) / bodyH;              // 칸 영역 안에서의 세로 위치(0=아래, 1=위)
                float vEdge = Mathf.Min(y - pad, pad + bodyH - y);
                if (vEdge <= -1f) continue;
                for (int x = 0; x < w; x++)
                {
                    // 위로 갈수록 오른쪽으로 밀린 자리를 기준으로 칸을 자른다(평행사변형).
                    float u = x - pad - Mathf.Clamp01(v) * slant;
                    int i = Mathf.FloorToInt(u / pitch);
                    if (i < 0 || i >= BarSegments) continue;
                    float local = u - i * pitch;
                    float hEdge = Mathf.Min(local, segW - local);
                    if (hEdge <= 0f) continue;
                    a[y * w + x] = Mathf.Clamp01(Mathf.Min(hEdge, vEdge) / 1.6f);
                }
            }

            if (glow) a = Blur(a, w, h, GlowBlurBar);
            return ToSprite(a, w, h);
        }

        static Sprite BuildRing(bool glow)
        {
            const int n = RingSize;
            float half = n * 0.5f;
            float outer = half * RingOuterFrac;
            float inner = outer * RingInnerFrac;
            float stepDeg = 360f / RingSegments;
            float halfGap = RingGapDeg * 0.5f;

            var a = new float[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = x + 0.5f - half, dy = y + 0.5f - half;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float radial = Mathf.Min(outer - r, r - inner);
                if (radial <= 0f) continue;

                // 12시 기준 시계방향 각도 — Filled Radial360(origin=Top, clockwise)과 같은 기준.
                float deg = Mathf.Atan2(dx, dy) * Mathf.Rad2Deg;
                if (deg < 0f) deg += 360f;
                float inStep = deg % stepDeg;
                float toGap = Mathf.Min(inStep - halfGap, (stepDeg - halfGap) - inStep);
                if (toGap <= 0f) continue;
                // 각도 거리를 호 길이(px)로 바꿔야 안쪽·바깥쪽 간격 폭이 같아 보인다.
                float angular = toGap * Mathf.Deg2Rad * r;

                a[y * n + x] = Mathf.Clamp01(Mathf.Min(radial, angular) / 1.6f);
            }

            if (glow) a = Blur(a, n, n, GlowBlurRing);
            return ToSprite(a, n, n);
        }

        /// <summary>알파 채널만 흐린다(형상은 흰색이라 RGB는 건드릴 게 없다). 박스 블러 2회 =
        /// 가우시안 근사 — 게이지 하나짜리라 에디터에서 한 번 굽는 비용은 무시해도 된다.</summary>
        static float[] Blur(float[] src, int w, int h, float radius)
        {
            int r = Mathf.Max(1, Mathf.RoundToInt(radius * 0.5f));
            var tmp = new float[w * h];
            var dst = new float[w * h];
            for (int pass = 0; pass < 2; pass++)
            {
                BlurAxis(src, tmp, w, h, r, true);
                BlurAxis(tmp, dst, w, h, r, false);
                src = dst;
                dst = new float[w * h];
            }
            // 두 번 흐리면 봉우리가 낮아진다 — 글로우로 보이게 다시 들어올린다.
            float peak = 0f;
            for (int i = 0; i < src.Length; i++) peak = Mathf.Max(peak, src[i]);
            if (peak > 1e-4f)
                for (int i = 0; i < src.Length; i++) src[i] = Mathf.Clamp01(src[i] / peak);
            return src;
        }

        static void BlurAxis(float[] src, float[] dst, int w, int h, int r, bool horizontal)
        {
            int outer = horizontal ? h : w, inner = horizontal ? w : h;
            for (int o = 0; o < outer; o++)
            {
                float sum = 0f;
                for (int i = -r; i <= r; i++) sum += Sample(src, w, h, o, i, horizontal);
                float inv = 1f / (r * 2 + 1);
                for (int i = 0; i < inner; i++)
                {
                    dst[horizontal ? o * w + i : i * w + o] = sum * inv;
                    sum += Sample(src, w, h, o, i + r + 1, horizontal) - Sample(src, w, h, o, i - r, horizontal);
                }
            }
        }

        static float Sample(float[] src, int w, int h, int o, int i, bool horizontal)
        {
            if (horizontal) return i < 0 || i >= w ? 0f : src[o * w + i];
            return i < 0 || i >= h ? 0f : src[i * w + o];
        }

        static Sprite ToSprite(float[] alpha, int w, int h)
        {
            var tex = new Texture2D(w, h, TextureFormat.ARGB32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            var px = new Color32[w * h];
            for (int i = 0; i < px.Length; i++)
                px[i] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha[i]) * 255f));
            tex.SetPixels32(px);
            tex.Apply();

            var s = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
            s.hideFlags = HideFlags.HideAndDontSave;
            return s;
        }
    }
}
