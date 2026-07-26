using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Game.EditorTools
{
    /// <summary>
    /// [HUD 프레임, 2026-07-22] 생성형 아트로 뽑은 HUD 프레임 PNG를 게임에서 쓸 오버레이로 굽는다.
    /// 아트를 다시 뽑아도 메뉴 한 번이면 되도록 파이프라인으로 만들어 둔다.
    ///
    /// 원본이 그대로는 못 쓰이는 이유 두 가지:
    /// <list type="number">
    /// <item><b>알파가 없다.</b> 24bpp RGB — "투명" 체크무늬가 알파가 아니라 흰 픽셀로 그려져 있어서
    ///   그냥 깔면 화면이 흰 종이로 덮인다. 바깥에서 흘러들어오는 흰 영역만 투명으로 만든다
    ///   (플러드 필). 단순 밝기 임계값으로 하면 어두운 패널 위의 <i>흰 글자</i>까지 뚫려버린다 —
    ///   글자는 바깥과 연결돼 있지 않으므로 플러드 필은 건드리지 않는다.</item>
    /// <item><b>게이지가 특정 수치로 구워져 있다.</b> HEALTH 14칸·"62%"는 값이 바뀌면 못 쓴다.
    ///   눈금 자리를 파내 빈 슬롯으로 만들고, 실제 게이지는 Game.View.HudCanvas가 그 자리에 그린다.</item>
    /// </list>
    ///
    /// <para><b>왜 좌표를 자동 검출하지 않는가</b>: 처음엔 색·밝기로 게이지를 찾게 만들었는데,
    /// 생성형 아트라 링이 완벽한 동심원이 아니고 아이콘이 바 하우징에 딱 붙어 있는 등 규칙성이
    /// 없어서 휴리스틱이 계속 빗나갔다(배경 흰색을 켜진 눈금으로 세거나, 62%만 찬 링의 경계
    /// 상자에서 중심이 왼쪽으로 밀리는 식). 픽셀 스캔으로 <b>한 번 실측한 값을 아래에 박아두는
    /// 편</b>이 정확하고 읽기도 쉽다. 아트가 바뀌면 이 숫자만 다시 재면 된다.</para>
    /// </summary>
    public static class HudFrameBaker
    {
        // [아트 2차 납품, 2026-07-22] 처음 원본(Hudframe_source.png)은 알파가 없는 24bpp라
        // 흰 배경을 플러드 필로 뚫어야 했다. 새로 뽑은 아트는 배경이 진짜 투명(32bpp)이라
        // 그 단계가 통째로 필요 없다 — 아래 KeyOutBackground는 알파가 없을 때만 돈다.
        const string SourcePath = "Assets/_Project/Art/UI/Source/HudFrame_border_only.png";
        const string OutputPath = "Assets/_Project/Art/UI/Resources/HudFrame.png";

        // [아트 2차 납품, 2026-07-22] 새 아트는 <b>레이어가 분리돼</b> 왔다 — 테두리 한 장 +
        // 게이지 묶음·예지 다이얼·조준점 세 장. 분리 레이어는 각자 캔버스가 다르고(1204x631 등)
        // 원본 1672x941 안에서의 위치 정보가 없어서, 아래 실측 사각형에 맞춰 다시 합성한다.
        // 합성 후 좌표계가 1차 아트와 같아지므로 HudCanvas·아래 파내기 상수를 그대로 쓸 수 있다.
        //
        // dest는 <b>불투명 픽셀의 경계 상자</b> 기준이다(캔버스 여백은 무시하고 내용물만 맞춘다).
        // 값은 1차 아트(Hudframe_source.png)에서 같은 덩어리의 경계 상자를 실측한 것 —
        // 아트를 다시 뽑아 비율이 달라지면 여기부터 다시 잰다.
        struct Layer
        {
            public string path;
            public RectInt dest;   // 1672x941 좌표계(좌상단 원점)에서 내용물이 차지할 사각형
            /// <summary>레이어 캔버스에서 잘라 쓸 사각형. 비워두면(width 0) 불투명 경계 상자를
            /// 자동으로 쓴다. <b>여러 레이어가 서로 정렬돼야 할 때는 반드시 지정한다</b> —
            /// 각자의 경계 상자에 맞추면 레이어마다 배율이 달라져 어긋난다.</summary>
            public RectInt src;
        }

        // [다이얼 3분할, 2026-07-22] 예지 다이얼이 세 장으로 다시 왔다 — 바깥 베젤 / 게이지 링 /
        // 안쪽 원판. 셋은 같은 1062x1051 캔버스에 정렬돼 있으므로 <b>같은 src 사각형</b>으로
        // 잘라야 겹쳤을 때 동심원이 유지된다(= 바깥 베젤의 불투명 경계 상자).
        static readonly RectInt DialSrc = new RectInt(8, 8, 1046, 1035);
        /// <summary>합성 결과에서 다이얼이 차지하는 사각형. HudCanvas.DialRect와 같이 간다.</summary>
        static readonly RectInt DialDest = new RectInt(1360, 71, 244, 244);

        const string DialRingSource = "Assets/_Project/Art/UI/Source/HUD_2_gauge_fill_1.png";
        /// <summary>게이지 링은 프레임에 굽지 않는다 — 실시간으로 차오르는 부분이라 별도
        /// 스프라이트로 뽑아 HudCanvas가 Radial360으로 채운다.</summary>
        const string DialRingOutput = "Assets/_Project/Art/UI/Resources/HudDialRing.png";

        static readonly Layer[] Layers =
        {
            new Layer   // 좌하단 HEALTH/DASH/RIGHT CLICK 묶음
            {
                path = "Assets/_Project/Art/UI/Source/StatusBars_transparent_1.png",
                dest = new RectInt(97, 688, 328, 172),
            },
            new Layer   // 다이얼 바깥 베젤
            {
                path = "Assets/_Project/Art/UI/Source/HUD_1_outer_1.png",
                dest = DialDest, src = DialSrc,
            },
            new Layer   // 다이얼 안쪽 원판(가운데 초 표시가 올라간다)
            {
                path = "Assets/_Project/Art/UI/Source/HUD_3_inner_1.png",
                dest = DialDest, src = DialSrc,
            },
            // [피드백, 2026-07-22] 화면 중앙 조준점은 뺀다 — 시야 한가운데를 가리는 게 거슬린다는
            // 피드백. 아트(Crosshair_transparent_1.png)는 Source에 그대로 두었으니 되살리려면
            // 아래 줄의 주석만 풀면 된다.
            // new Layer { path = "Assets/_Project/Art/UI/Source/Crosshair_transparent_1.png",
            //             dest = new RectInt(760, 416, 150, 119) },
        };

        /// <summary>[피드백 번복, 2026-07-22] 화면을 두르는 테두리를 살릴지. 예전엔 거슬린다고
        /// 잘라냈지만(아래 KeepRegions), 테두리까지 포함된 아트를 새로 뽑아와서 이번엔 살린다.
        /// false로 두면 예전처럼 정보 덩어리(게이지·다이얼·조준점)만 남는다.</summary>
        // const가 아니라 static readonly로 둔다 — const면 컴파일러가 분기를 접어버려서
        // "도달 불가 코드" 경고가 뜬다(되돌릴 수 있게 남겨둔 코드인데 경고로 보이면 곤란하다).
        static readonly bool KeepBorder = true;

        /// <summary>구워진 게이지 링·"62%"를 파낼지. 다이얼이 레이어로 분리돼 온 3차 아트부터는
        /// 파낼 게 없다(false). 통짜 다이얼 아트로 되돌릴 때만 true.</summary>
        static readonly bool EraseBakedDial = false;

        /// <summary>원본 크기. 아래 좌표는 전부 이 좌표계(좌상단 원점)의 실측값이다.</summary>
        const int SrcW = 1672, SrcH = 941;

        // ── 실측값 (Hudframe_source.png, 2026-07-22) ─────────────────────
        // 가로 게이지 3개: 칸은 오른쪽으로 기운 평행사변형이고, 위로 갈수록 5px 밀린다.
        // 칸 16개 · 피치 15.25px · 칸 폭 12px. 바닥 행 기준 x 167~410.
        static readonly (int top, int height)[] BarBands =
        {
            (713, 17),   // HEALTH
            (771, 18),   // DASH
            (831, 17),   // RIGHT CLICK
        };
        const int BarLeft = 167, BarRight = 410, BarSlant = 5;

        // PREDICTION 다이얼: 링은 12시에서 시계방향으로 찬다(원본 225° ≈ 62%로 확인).
        const int DialCx = 1482, DialCy = 193;
        const int RingInner = 58, RingOuter = 100;  // 파낼 고리 — 여기에 우리 링이 들어간다
        const int PlateRadius = 46;                 // "62%"를 덮을 안쪽 원판
        /// <summary>구워진 링의 청록 글로우는 고리 밖 베젤까지 번져 있다. 이 반경 안에서
        /// 잔재를 반대편(빈 칸 쪽) 색으로 덮어 "왼쪽은 꺼졌는데 오른쪽만 푸른" 티를 없앤다.</summary>
        const int GlowCleanupRadius = 122;

        /// <summary>
        /// [피드백, 2026-07-22] 화면을 두르는 프레임 테두리가 플레이 중에 너무 거슬린다는 피드백 —
        /// 실제로 정보를 담은 덩어리(게이지 묶음·PREDICTION 다이얼·조준점)만 남기고 나머지는
        /// 전부 지운다. 테두리·상단 나침반·"UNIT ID" 명판은 장식이라 정보 손실이 없다.
        ///
        /// 각 덩어리는 원본에서 이미 투명 배경에 둘러싸여 있으므로, 넉넉한 사각형으로 잘라도
        /// 잘린 티가 나지 않는다 — 사각형이 덩어리를 완전히 품기만 하면 된다.
        /// </summary>
        static readonly RectInt[] KeepRegions =
        {
            // 경계는 실측으로 딱 맞춘다 — 넉넉하게 잡으면 테두리 조각이 같이 남는다
            // (왼쪽 x91~94의 세로 획, 오른쪽 x1597~1604의 모서리 조각이 실제로 남았었다).
            new RectInt(96, 678, 350, 195),      // 좌하단 게이지 묶음(아이콘·라벨·바 3개)
            new RectInt(1365, 46, 229, 268),     // 우상단 PREDICTION 라벨 + 다이얼
            new RectInt(758, 398, 158, 156),     // 화면 중앙 조준점
        };

        // 배경 판정 — 이 값보다 밝은(모든 채널) 픽셀만 플러드 필이 통과한다.
        const int BackgroundMin = 232;
        // 플러드로 지워진 영역 안에서 이 밝기까지는 부분 알파로 남겨 경계를 부드럽게 한다.
        const int FeatherFloor = 244;

        [MenuItem("Precog/HUD 프레임 굽기 (알파 + 게이지 파내기)")]
        public static void Bake()
        {
            byte[] raw;
            try { raw = File.ReadAllBytes(SourcePath); }
            catch (IOException e)
            {
                Debug.LogError($"[HUD] 원본을 못 읽었습니다: {SourcePath}\n{e.Message}");
                return;
            }

            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!tex.LoadImage(raw))
            {
                Debug.LogError($"[HUD] PNG 디코딩 실패: {SourcePath}");
                return;
            }

            int w = tex.width, h = tex.height;
            if (w != SrcW || h != SrcH)
            {
                Debug.LogError($"[HUD] 원본 크기가 {w}x{h}인데 실측 좌표는 {SrcW}x{SrcH} 기준입니다. " +
                               "아트를 다시 뽑았다면 HudFrameBaker의 실측 상수부터 다시 재세요.");
                Object.DestroyImmediate(tex);
                return;
            }

            Color32[] px = tex.GetPixels32();
            // 원본이 이미 투명 배경이면 플러드 필을 돌리면 안 된다 — 투명 픽셀의 RGB는 의미
            // 없는 잔여값이라, 밝기 기준으로 번지게 두면 엉뚱한 데를 뚫는다.
            if (!HasAlpha(px))
            {
                KeyOutBackground(px, w, h);
                Debug.Log("[HUD] 원본에 알파가 없어 흰 배경 플러드 필을 수행했습니다.");
            }
            foreach (var layer in Layers) Composite(px, w, h, layer);
            foreach (var band in BarBands) EraseBar(px, w, h, band.top, band.height);
            // [다이얼 3분할, 2026-07-22] 다이얼은 이제 파낼 게 없다 — 게이지 링이 아예 별도
            // 레이어로 분리돼 와서, 합성하는 두 장(베젤·원판)에는 구워진 수치가 없다.
            // 1차/2차 아트로 되돌릴 때만 EraseDial을 다시 켠다.
            if (EraseBakedDial) EraseDial(px, w, h);
            if (!KeepBorder) KeepOnlyPanels(px, w, h);

            tex.SetPixels32(px);
            tex.Apply();

            Directory.CreateDirectory(Path.GetDirectoryName(OutputPath));
            File.WriteAllBytes(OutputPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(OutputPath, ImportAssetOptions.ForceUpdate);
            ApplyImportSettings(OutputPath);
            Debug.Log($"[HUD] {OutputPath} 완료 — 배경 투명화 + 게이지 슬롯 파내기.");

            ExportDialRing();
        }

        // ── 게이지 링 스프라이트 ─────────────────────────────────────────
        /// <summary>
        /// 다이얼 게이지 링을 <b>프레임과 같은 사각형 기준</b>으로 잘라 별도 스프라이트로 뽑는다.
        /// 자르는 사각형이 베젤·원판과 같으므로(<see cref="DialSrc"/>), HudCanvas가 이 스프라이트를
        /// 다이얼 자리에 그대로 늘리기만 하면 합성된 베젤·원판과 동심원이 정확히 맞는다.
        /// 축소는 하지 않는다 — 런타임 스프라이트라 원본 해상도를 그대로 살려 4K에서도 또렷하다.
        /// </summary>
        static void ExportDialRing()
        {
            byte[] raw;
            try { raw = File.ReadAllBytes(DialRingSource); }
            catch (IOException e)
            {
                Debug.LogError($"[HUD] 게이지 링 원본을 못 읽었습니다: {DialRingSource}\n{e.Message}");
                return;
            }

            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!tex.LoadImage(raw))
            {
                Debug.LogError($"[HUD] PNG 디코딩 실패: {DialRingSource}");
                Object.DestroyImmediate(tex);
                return;
            }

            int lw = tex.width, lh = tex.height;
            Color32[] lp = tex.GetPixels32();
            Object.DestroyImmediate(tex);

            var cropped = new Texture2D(DialSrc.width, DialSrc.height, TextureFormat.RGBA32, false);
            var outPx = new Color32[DialSrc.width * DialSrc.height];
            for (int y = 0; y < DialSrc.height; y++)
            for (int x = 0; x < DialSrc.width; x++)
            {
                int sx = DialSrc.xMin + x, sy = DialSrc.yMin + y;
                outPx[(DialSrc.height - 1 - y) * DialSrc.width + x] =
                    sx < 0 || sy < 0 || sx >= lw || sy >= lh
                        ? new Color32(0, 0, 0, 0)
                        : Get(lp, lw, lh, sx, sy);
            }
            cropped.SetPixels32(outPx);
            cropped.Apply();

            Directory.CreateDirectory(Path.GetDirectoryName(DialRingOutput));
            File.WriteAllBytes(DialRingOutput, cropped.EncodeToPNG());
            Object.DestroyImmediate(cropped);
            AssetDatabase.ImportAsset(DialRingOutput, ImportAssetOptions.ForceUpdate);
            ApplyImportSettings(DialRingOutput);
            Debug.Log($"[HUD] {DialRingOutput} 완료 — 게이지 링 스프라이트({DialSrc.width}x{DialSrc.height}).");
        }

        // ── 레이어 합성 ──────────────────────────────────────────────────
        /// <summary>
        /// 분리 레이어 한 장을 지정한 사각형에 맞춰 축소해 베이스 위에 알파 블렌딩한다.
        /// 레이어 캔버스에는 여백이 있으므로 <b>불투명 경계 상자</b>를 먼저 구해 그것만 쓴다 —
        /// 여백을 포함해 맞추면 아트마다 여백 크기가 달라 위치가 어긋난다.
        /// </summary>
        static void Composite(Color32[] basePx, int w, int h, Layer layer)
        {
            byte[] raw;
            try { raw = File.ReadAllBytes(layer.path); }
            catch (IOException e)
            {
                Debug.LogError($"[HUD] 레이어를 못 읽었습니다: {layer.path}\n{e.Message}");
                return;
            }

            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!tex.LoadImage(raw))
            {
                Debug.LogError($"[HUD] PNG 디코딩 실패: {layer.path}");
                return;
            }

            int lw = tex.width, lh = tex.height;
            Color32[] lp = tex.GetPixels32();
            Object.DestroyImmediate(tex);

            RectInt src = layer.src;
            if (src.width <= 0 && !OpaqueBounds(lp, lw, lh, out src))
            {
                Debug.LogWarning($"[HUD] 레이어가 통째로 투명합니다: {layer.path}");
                return;
            }

            // dest 픽셀마다 src에서 역으로 샘플링한다(축소라 area 평균이 정석이지만, 원본이
            // 3~4배 크기라 바이리니어로도 계단이 눈에 띄지 않는다).
            for (int dy = 0; dy < layer.dest.height; dy++)
            for (int dx = 0; dx < layer.dest.width; dx++)
            {
                int x = layer.dest.xMin + dx, y = layer.dest.yMin + dy;
                if (x < 0 || y < 0 || x >= w || y >= h) continue;

                float u = src.xMin + (dx + 0.5f) / layer.dest.width * src.width - 0.5f;
                float v = src.yMin + (dy + 0.5f) / layer.dest.height * src.height - 0.5f;
                Color over = SampleBilinear(lp, lw, lh, u, v);
                if (over.a <= 0.002f) continue;

                Color under = Get(basePx, w, h, x, y);
                // 스트레이트 알파 over 합성. 아래(테두리)가 비어 있는 자리가 대부분이라
                // 알파도 함께 합성해야 가장자리 반투명이 살아난다.
                float a = over.a + under.a * (1f - over.a);
                Color rgb = a <= 0.0001f ? Color.clear
                    : (over * over.a + under * under.a * (1f - over.a)) / a;
                Set(basePx, w, h, x, y, new Color(rgb.r, rgb.g, rgb.b, a));
            }
        }

        /// <summary>알파가 있는 픽셀들의 경계 상자(레이어 캔버스 좌상단 원점).</summary>
        static bool OpaqueBounds(Color32[] px, int w, int h, out RectInt bounds)
        {
            int minX = w, minY = h, maxX = -1, maxY = -1;
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (Get(px, w, h, x, y).a <= 8) continue;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
            bounds = new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
            return maxX >= 0;
        }

        static Color SampleBilinear(Color32[] px, int w, int h, float x, float y)
        {
            int x0 = Mathf.Clamp(Mathf.FloorToInt(x), 0, w - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(y), 0, h - 1);
            int x1 = Mathf.Min(x0 + 1, w - 1), y1 = Mathf.Min(y0 + 1, h - 1);
            float fx = Mathf.Clamp01(x - x0), fy = Mathf.Clamp01(y - y0);
            Color c00 = Get(px, w, h, x0, y0), c10 = Get(px, w, h, x1, y0);
            Color c01 = Get(px, w, h, x0, y1), c11 = Get(px, w, h, x1, y1);
            return Color.Lerp(Color.Lerp(c00, c10, fx), Color.Lerp(c01, c11, fx), fy);
        }

        /// <summary>원본이 이미 투명 배경을 갖고 있는가(= 알파 채널이 실제로 쓰였는가).</summary>
        static bool HasAlpha(Color32[] px)
        {
            for (int i = 0; i < px.Length; i++)
                if (px[i].a < 250) return true;
            return false;
        }

        // ── 배경 키잉 ────────────────────────────────────────────────────
        /// <summary>
        /// 테두리에서 출발해 밝은 픽셀만 타고 번지는 플러드 필. 바깥과 이어진 흰 영역만
        /// 투명해지므로, 어두운 패널 안에 갇힌 흰 글자("UNIT ID" 등)는 그대로 남는다.
        /// </summary>
        static void KeyOutBackground(Color32[] px, int w, int h)
        {
            var seen = new bool[w * h];
            var stack = new Stack<int>(w * h / 4);

            for (int x = 0; x < w; x++) { Push(stack, seen, px, w, x, 0); Push(stack, seen, px, w, x, h - 1); }
            for (int y = 0; y < h; y++) { Push(stack, seen, px, w, 0, y); Push(stack, seen, px, w, w - 1, y); }

            while (stack.Count > 0)
            {
                int i = stack.Pop();
                int x = i % w, y = i / w;

                // 가장자리 안티에일리어싱을 살리려고, 완전히 흰 픽셀만 알파 0으로 만들고
                // 살짝 어두운 픽셀은 그 어두운 만큼 남겨 경계를 부드럽게 한다.
                Color32 c = px[i];
                int luma = Mathf.Min(c.r, Mathf.Min(c.g, c.b));
                px[i] = new Color32(c.r, c.g, c.b, (byte)Mathf.Clamp(
                    Mathf.RoundToInt((FeatherFloor - luma) * 255f / (FeatherFloor - BackgroundMin)), 0, 255));

                if (x > 0)     Push(stack, seen, px, w, x - 1, y);
                if (x < w - 1) Push(stack, seen, px, w, x + 1, y);
                if (y > 0)     Push(stack, seen, px, w, x, y - 1);
                if (y < h - 1) Push(stack, seen, px, w, x, y + 1);
            }
        }

        static void Push(Stack<int> stack, bool[] seen, Color32[] px, int w, int x, int y)
        {
            int i = y * w + x;
            if (seen[i]) return;
            Color32 c = px[i];
            if (c.r < BackgroundMin || c.g < BackgroundMin || c.b < BackgroundMin) return;
            seen[i] = true;
            stack.Push(i);
        }

        /// <summary><see cref="KeepRegions"/> 밖을 전부 투명하게 지운다 — 화면 테두리 제거.</summary>
        static void KeepOnlyPanels(Color32[] px, int w, int h)
        {
            var keep = new bool[w * h];
            foreach (var r in KeepRegions)
            {
                int x0 = Mathf.Max(0, r.xMin), x1 = Mathf.Min(w - 1, r.xMax);
                int y0 = Mathf.Max(0, r.yMin), y1 = Mathf.Min(h - 1, r.yMax);
                for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                    keep[(h - 1 - y) * w + x] = true;
            }
            for (int i = 0; i < px.Length; i++)
                if (!keep[i]) px[i] = new Color32(0, 0, 0, 0);
        }

        // ── 게이지 파내기 ────────────────────────────────────────────────
        /// <summary>
        /// 칸이 놓인 띠를 통째로 비운다. 칸이 기울어 있어 위로 갈수록 오른쪽으로 밀리므로
        /// 오른쪽 끝에 기울기만큼 여유를 준다 — 대신 하우징 끝 캡(x≈419)은 건드리지 않는다.
        /// </summary>
        static void EraseBar(Color32[] px, int w, int h, int top, int height)
        {
            for (int y = top - 1; y <= top + height + 1; y++)
            for (int x = BarLeft - 2; x <= BarRight + BarSlant + 2; x++)
            {
                if (x < 0 || y < 0 || x >= w || y >= h) continue;
                Set(px, w, h, x, y, new Color32(0, 0, 0, 0));
            }
        }

        /// <summary>
        /// 링 눈금은 파내고(우리 링이 들어갈 자리), 바깥 베젤과 안쪽 원판은 아트가 예쁘게 그려둔
        /// 부분이라 살린다. 구워진 "62%"만 원판 색으로 덮어 지운다 — 글자 픽셀만 골라 지우면
        /// 안티에일리어싱 테두리가 유령처럼 남아서, 원판 전체를 다시 칠하는 편이 깨끗하다.
        /// </summary>
        static void EraseDial(Color32[] px, int w, int h)
        {
            // 원판 대표색 — 글자가 없는 아래쪽 호에서 여러 점 평균. 한 점만 찍으면 글자 경계에
            // 걸려 색이 튄다.
            int pr = 0, pg = 0, pb = 0, pn = 0;
            for (int a = 100; a <= 260; a += 10)
            {
                float rad = a * Mathf.Deg2Rad;
                int sx = DialCx + Mathf.RoundToInt(Mathf.Sin(rad) * PlateRadius * 0.85f);
                int sy = DialCy - Mathf.RoundToInt(Mathf.Cos(rad) * PlateRadius * 0.85f);
                if (sx < 0 || sy < 0 || sx >= w || sy >= h) continue;
                Color32 c = Get(px, w, h, sx, sy);
                pr += c.r; pg += c.g; pb += c.b; pn++;
            }
            var plate = pn > 0 ? new Color32((byte)(pr / pn), (byte)(pg / pn), (byte)(pb / pn), 255)
                               : new Color32(8, 10, 12, 255);

            for (int y = DialCy - GlowCleanupRadius; y <= DialCy + GlowCleanupRadius; y++)
            for (int x = DialCx - GlowCleanupRadius; x <= DialCx + GlowCleanupRadius; x++)
            {
                if (x < 0 || y < 0 || x >= w || y >= h) continue;
                float dx = x - DialCx, dy = y - DialCy;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                if (dist > GlowCleanupRadius) continue;

                if (dist >= RingInner && dist <= RingOuter)
                {
                    Set(px, w, h, x, y, new Color32(0, 0, 0, 0));
                }
                else if (dist < PlateRadius)
                {
                    // 가장자리로 갈수록 살짝 어둡게 — 평평하게 칠하면 원판이 스티커처럼 뜬다.
                    float t = dist / PlateRadius;
                    float k = Mathf.Lerp(1.35f, 0.75f, t * t);
                    Set(px, w, h, x, y, Scale(plate, k));
                }
                else if (HasGlow(Get(px, w, h, x, y)))
                {
                    // 고리 밖 베젤·안쪽 장식 고리에 번진 청록 글로우. 원본 링이 62%만 차 있어서
                    // 그대로 두면 오른쪽만 푸르게 빛나는 채로 굳는다 — 좌우 대칭인 점을 이용해
                    // 반대편(빈 칸 쪽) 픽셀로 덮는다.
                    Color32 mirror = Get(px, w, h, Mathf.Clamp(DialCx * 2 - x, 0, w - 1), y);
                    Set(px, w, h, x, y, HasGlow(mirror) ? Scale(plate, 1.6f) : mirror);
                }
            }
        }

        /// <summary>구워진 링에서 번져 나온 청록 글로우인가. 옅은 잔재까지 잡도록 느슨하게 본다.</summary>
        static bool HasGlow(Color32 c) => c.a > 8 && c.b > 46 && c.b > c.r * 1.35f && c.g > c.r * 1.1f;

        static Color32 Scale(Color32 c, float k) => new Color32(
            (byte)Mathf.Clamp(Mathf.RoundToInt(c.r * k), 0, 255),
            (byte)Mathf.Clamp(Mathf.RoundToInt(c.g * k), 0, 255),
            (byte)Mathf.Clamp(Mathf.RoundToInt(c.b * k), 0, 255), 255);

        // ── 픽셀 유틸 (좌상단 원점 ↔ Unity의 좌하단 원점) ────────────────
        static Color32 Get(Color32[] px, int w, int h, int x, int y) => px[(h - 1 - y) * w + x];
        static void Set(Color32[] px, int w, int h, int x, int y, Color32 c) => px[(h - 1 - y) * w + x] = c;

        static void ApplyImportSettings(string path)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer == null) return;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.Uncompressed;   // 얇은 선이 뭉개지면 안 된다
            importer.SaveAndReimport();
        }
    }
}
