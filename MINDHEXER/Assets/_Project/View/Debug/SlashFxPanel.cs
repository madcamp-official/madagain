using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.View
{
    /// <summary>
    /// F5 — 베기 이펙트 상세 튜닝. 배치·형태·색·노이즈·타이밍을 공격 종류별로 전부 조절한다.
    ///
    /// [고정 미리보기]를 켜면 사라지지 않는 이펙트가 떠 있고, 슬라이더를 움직이는 대로
    /// 즉시 반영된다(SwordSlash가 형태 변경 시 메시를 다시 만들고 색은 매 프레임 적용).
    /// (F1 전투 · F2 재생/카메라 · F3 시퀀스 · F4 절차 모션 · F5 베기 이펙트)
    /// </summary>
    public class SlashFxPanel : MonoBehaviour
    {
        bool open;
        int  sel, tab;
        Vector2 scroll;
        static readonly string[] Tabs = { "배치", "형태", "색", "노이즈", "타이밍", "버스트" };
        static readonly string[] FollowNames = { "월드 고정", "카메라 고정", "지연 추종", "단계 전환" };

        /// <summary>패널이 열려 있는가(열려 있으면 Main이 플레이어 입력을 막는다).</summary>
        public static bool AnyOpen;

        void OnDisable() { AnyOpen = false; }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.f5Key.wasPressedThisFrame)
            {
                open = !open;
                AnyOpen = open;
                Cursor.lockState = open ? CursorLockMode.None : CursorLockMode.Locked;
                Cursor.visible = open;
                if (!open) { var d = SlashFxDriver.Instance; if (d != null) d.PreviewHide(); }
            }
        }

        void OnGUI()
        {
            if (!open) return;
            var fx = SlashFxDriver.Instance;
            const float W = 430f;
            float H = Mathf.Min(Screen.height - 24f, 620f);
            GUILayout.BeginArea(new Rect(Screen.width - W - 12f, 12f, W, H), GUI.skin.box);
            GUILayout.Label("<b>베기 이펙트 튜닝 (F5)</b>", Rich());

            if (fx == null || fx.slots == null || fx.slots.Length == 0)
            { GUILayout.Label("SlashFxDriver 없음"); GUILayout.EndArea(); return; }

            fx.active = GUILayout.Toggle(fx.active, " 이펙트 사용");

            // 슬롯 선택
            var names = new string[fx.slots.Length];
            for (int i = 0; i < fx.slots.Length; i++) names[i] = fx.slots[i].name;
            int newSel = GUILayout.Toolbar(Mathf.Clamp(sel, 0, names.Length - 1), names);
            if (newSel != sel) { sel = newSel; if (fx.PreviewOn) fx.PreviewShow(fx.slots[sel]); }
            var s = fx.slots[sel];

            // 고정 미리보기 — 켜두고 값을 만지면 실시간으로 바뀐다
            bool wantPrev = GUILayout.Toggle(fx.PreviewOn, " <b>고정 미리보기</b> (안 사라짐 · 값 즉시 반영)", Rich());
            if (wantPrev != fx.PreviewOn) { if (wantPrev) fx.PreviewShow(s); else fx.PreviewHide(); }

            tab = GUILayout.Toolbar(tab, Tabs);
            scroll = GUILayout.BeginScrollView(scroll);

            switch (tab)
            {
                case 0:  // 배치
                    s.enabled = GUILayout.Toggle(s.enabled, " 이 공격에 이펙트 사용");

                    // 같이 재생할 포즈 — 아래 [평타와 함께 재생]이 이걸 쓴다
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("같이 재생할 포즈", GUILayout.Width(110f));
                    var pfx = PosePrefixes();
                    int cur = System.Array.IndexOf(pfx, s.posePrefix);
                    int nw = GUILayout.Toolbar(cur < 0 ? 0 : cur, pfx);
                    if (nw != cur && nw >= 0 && nw < pfx.Length) s.posePrefix = pfx[nw];
                    GUILayout.EndHorizontal();
                    GUILayout.Label("<b>각도</b>", Rich());
                    s.roll  = F("roll — 화면 안 사선", s.roll,  -180f, 180f, 1f, "0.0");
                    s.pitch = F("pitch — 위아래 눕힘", s.pitch, -90f,   90f, 1f, "0.0");
                    s.yaw   = F("yaw — 좌우 돌림",     s.yaw,   -90f,   90f, 1f, "0.0");
                    GUILayout.Label("<b>위치 (카메라 기준)</b>", Rich());
                    s.offset.x = F("x — 좌우", s.offset.x, -3f, 3f, 0.05f, "0.00");
                    s.offset.y = F("y — 상하", s.offset.y, -3f, 3f, 0.05f, "0.00");
                    s.offset.z = F("z — 앞뒤", s.offset.z, 0.2f, 8f, 0.1f, "0.00");
                    s.scale    = F("전체 크기 배수", s.scale, 0.1f, 3f, 0.05f, "0.00");

                    GUILayout.Space(6f);
                    GUILayout.Label("<b>추종 — 빠르게 이동할 때 이펙트 처리</b>", Rich());
                    s.follow = GUILayout.Toolbar(Mathf.Clamp(s.follow, 0, 3), FollowNames);
                    switch (s.follow)
                    {
                        case SlashFollow.World:
                            GUILayout.Label("<size=10>그 자리에 남는다. 이동이 빠르면 뒤로 밀려 보인다.</size>", Rich());
                            break;
                        case SlashFollow.Camera:
                            GUILayout.Label("<size=10>화면상 위치 고정. 절대 안 밀리지만 세계에 박힌 느낌은 없다.</size>", Rich());
                            break;
                        case SlashFollow.Soft:
                            s.followSpeed = F("따라오는 속도", s.followSpeed, 0.5f, 30f, 0.5f, "0.0");
                            s.followRotation = GUILayout.Toggle(s.followRotation, " 회전도 따라감");
                            GUILayout.Label("<size=10>한 박자 늦게 따라온다 — 잔상이 남으면서 화면 밖으로 안 나간다.</size>", Rich());
                            break;
                        default:
                            s.attachTime = F("붙어있는 시간(초)", s.attachTime, 0f, 0.4f, 0.01f, "0.000");
                            GUILayout.Label($"<size=10>{s.attachTime:0.000}초 동안 화면에 붙었다가 월드에 놓는다.\n" +
                                            $"그어짐+유지 = {s.revealTime + s.holdTime:0.000}초에 맞추면 자연스럽다.</size>", Rich());
                            break;
                    }
                    s.onViewmodelLayer = GUILayout.Toggle(s.onViewmodelLayer, " 뷰모델 레이어로 그리기 (벽 뚫림 방지)");
                    break;

                case 1:  // 형태
                    GUILayout.Label("<b>굵기·길이</b>", Rich());
                    s.length      = F("길이",            s.length,      1f,   25f,  0.5f,  "0.0");
                    s.radiusWide  = F("굵기 — 칼날 방향", s.radiusWide,  0.05f, 3f,  0.05f, "0.00");
                    s.radiusThick = F("굵기 — 두께 방향", s.radiusThick, 0.02f, 2f,  0.02f, "0.00");
                    s.taperPower  = F("끝 뾰족함",        s.taperPower,  0.3f,  5f,  0.1f,  "0.0");
                    GUILayout.Label("<size=10>넓은 축 = 베는 면 / 두께 축 = 옆에서 본 굵기</size>", Rich());

                    GUILayout.Space(4f);
                    GUILayout.Label("<b>중첩 셸 (속이 찬 빛 덩어리)</b>", Rich());
                    int lc = Mathf.RoundToInt(F("셸 개수", s.layerCount, 1f, 6f, 1f, "0"));
                    s.innerShellScale = F("안쪽 셸 크기", s.innerShellScale, 0.05f, 1f, 0.02f, "0.00");
                    s.layerFalloff    = F("바깥 옅어짐",   s.layerFalloff,    0f,    1f, 0.05f, "0.00");
                    if (lc != s.layerCount)
                    {
                        s.layerCount = lc;
                        if (fx.PreviewOn) fx.PreviewRebuild();   // 셸 구조는 생성 시에만 반영됨
                    }
                    GUILayout.Label("<size=10><color=#ffb060>셸 개수·안쪽 크기는 생성 시에만 반영</color> — 미리보기가 자동 재생성됩니다</size>", Rich());
                    break;

                case 2:  // 색
                    GUILayout.Label("<b>합성 방식</b>", Rich());
                    s.blendMode = GUILayout.Toolbar(Mathf.Clamp(s.blendMode, 0, 2), SwordSlash.BlendNames);
                    GUILayout.Label(s.blendMode == SwordSlash.BlendAdd
                        ? "<size=10>더하기만 한다 — <color=#ffb060>검정(0,0,0)은 투명이 되어 안 보인다.</color> 빛나는 검기용.</size>"
                        : s.blendMode == SwordSlash.BlendAlpha
                        ? "<size=10>배경을 덮는다 — <b>검정이 검정으로 찍힌다.</b> 밝은 색도 가능.</size>"
                        : "<size=10>화면을 깎아낸다 — <b>가장 새까맣다.</b> 흰색(1,1,1)은 아무 변화가 없다.</size>", Rich());
                    if (s.blendMode != SwordSlash.BlendAdd && s.intensity > 1.2f)
                        GUILayout.Label("<size=10><color=#ffb060>검정을 쓰려면 밝기를 1 이하로 낮추십시오(지금 " + s.intensity.ToString("0.00") + ").</color></size>", Rich());

                    GUILayout.Space(4f);
                    GUILayout.Label("<b>색 (HDR — 1을 넘으면 블룸이 터진다)</b>", Rich());
                    s.colorHigh = ColorRow("코어(가장 밝음)", s.colorHigh);
                    s.colorMid  = ColorRow("중간",            s.colorMid);
                    s.colorLow  = ColorRow("바깥(헤일로)",    s.colorLow);
                    GUILayout.Space(4f);
                    GUILayout.Label("<b>색 전환 지점</b>", Rich());
                    s.ramp1 = F("바깥 → 중간", s.ramp1, 0f, 1f, 0.02f, "0.00");
                    s.ramp2 = F("중간 → 코어", s.ramp2, 0f, 1f, 0.02f, "0.00");
                    s.ramp3 = F("코어 포화",   s.ramp3, 0f, 1f, 0.02f, "0.00");
                    GUILayout.Space(4f);
                    s.intensity = F("밝기",   s.intensity, 0f,   8f, 0.05f, "0.00");
                    s.contrast  = F("대비",   s.contrast,  0.2f, 6f, 0.05f, "0.00");
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("흰색")) { Preset(s, new Color(3.2f,3.2f,3.2f), new Color(1.7f,1.7f,1.8f), new Color(0.5f,0.55f,0.7f)); s.blendMode = SwordSlash.BlendAdd; s.intensity = 0.8f; }
                    if (GUILayout.Button("초록")) { Preset(s, new Color(3.2f,3.2f,2.6f), new Color(1.6f,1.15f,0.05f), new Color(0.12f,1f,0.1f)); s.blendMode = SwordSlash.BlendAdd; s.intensity = 0.8f; }
                    if (GUILayout.Button("붉음")) { Preset(s, new Color(3.2f,2.6f,2.4f), new Color(2.2f,0.5f,0.15f), new Color(1.0f,0.1f,0.06f)); s.blendMode = SwordSlash.BlendAdd; s.intensity = 0.8f; }
                    if (GUILayout.Button("푸름")) { Preset(s, new Color(3.0f,3.2f,3.2f), new Color(0.5f,1.4f,2.2f), new Color(0.08f,0.4f,1.2f)); s.blendMode = SwordSlash.BlendAdd; s.intensity = 0.8f; }
                    GUILayout.EndHorizontal();

                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("<b>순수 검정</b>", Rich()))
                    {   // 곱셈 — 화면을 완전히 깎아낸다
                        Preset(s, Color.black, Color.black, Color.black);
                        s.blendMode = SwordSlash.BlendMul; s.intensity = 1f; s.contrast = 1.35f;
                    }
                    if (GUILayout.Button("검정+흰 심지"))
                    {   // 알파 — 코어만 희고 나머지는 새까맣게
                        Preset(s, new Color(1f,1f,1f), new Color(0.06f,0.06f,0.08f), Color.black);
                        s.blendMode = SwordSlash.BlendAlpha; s.intensity = 1f;
                        s.ramp1 = 0.30f; s.ramp2 = 0.80f; s.ramp3 = 0.93f;
                    }
                    GUILayout.EndHorizontal();
                    break;

                case 3:  // 노이즈
                    s.noiseAmount = F("노이즈 양", s.noiseAmount, 0f, 1f, 0.05f, "0.00");
                    GUILayout.Label("<b>레이어 1 (굵은 결)</b>", Rich());
                    s.noiseTile1.x   = F("  타일 U", s.noiseTile1.x,   0.2f, 20f, 0.2f, "0.0");
                    s.noiseTile1.y   = F("  타일 V", s.noiseTile1.y,   0.05f, 4f, 0.05f, "0.00");
                    s.noiseScroll1.x = F("  흐름 U", s.noiseScroll1.x, -4f,  4f,  0.1f, "0.00");
                    s.noiseScroll1.y = F("  흐름 V", s.noiseScroll1.y, -2f,  2f,  0.05f,"0.00");
                    GUILayout.Label("<b>레이어 2 (잔결)</b>", Rich());
                    s.noiseTile2.x   = F("  타일 U", s.noiseTile2.x,   0.2f, 20f, 0.2f, "0.0");
                    s.noiseTile2.y   = F("  타일 V", s.noiseTile2.y,   0.05f, 4f, 0.05f, "0.00");
                    s.noiseScroll2.x = F("  흐름 U", s.noiseScroll2.x, -4f,  4f,  0.1f, "0.00");
                    s.noiseScroll2.y = F("  흐름 V", s.noiseScroll2.y, -2f,  2f,  0.05f,"0.00");
                    break;

                case 5:  // 버스트
                    s.burst = GUILayout.Toggle(s.burst, " <b>방사형 버스트로 사용</b> (겐지식 피격 연출)", Rich());
                    if (!s.burst)
                        GUILayout.Label("<size=10>끄면 이 슬롯은 일반 궤적으로 동작합니다.</size>", Rich());
                    else
                    {
                        s.burstCount      = Mathf.RoundToInt(F("갈래 수", s.burstCount, 1f, 24f, 1f, "0"));
                        s.burstSpread     = F("퍼짐 각도(360=완전방사)", s.burstSpread, 20f, 360f, 5f, "0");
                        s.burstJitter     = F("각도 흔들기", s.burstJitter, 0f, 45f, 1f, "0.0");
                        s.burstLengthVary = F("길이 편차",   s.burstLengthVary, 0f, 0.8f, 0.05f, "0.00");
                        s.burstScaleVary  = F("크기 편차",   s.burstScaleVary,  0f, 0.8f, 0.05f, "0.00");
                        s.burstStagger    = F("순차 간격(초)", s.burstStagger, 0f, 0.08f, 0.002f, "0.000");
                        GUILayout.Label("<size=10>갈래는 카메라를 향해 정렬되므로 어느 방향에서 봐도 별 모양이 됩니다.\n" +
                                        "길이·크기 편차를 주면 규칙적으로 안 보입니다.</size>", Rich());

                        GUILayout.Space(4f);
                        if (GUILayout.Button("<b>버스트 터뜨려 보기</b>", Rich(), GUILayout.Height(26f)))
                        {
                            var c = Camera.main;
                            if (c != null) fx.BurstAt(c.transform.position + c.transform.forward * 2.4f, s);
                        }
                    }
                    break;

                default: // 타이밍
                    s.delay      = F("발동 지연(초)", s.delay,      0f,    0.5f, 0.01f, "0.000");
                    GUILayout.Space(4f);
                    s.revealTime = F("그어짐(초)",   s.revealTime, 0.005f, 0.5f, 0.005f,"0.000");
                    s.holdTime   = F("유지(초)",     s.holdTime,   0f,     0.5f, 0.005f,"0.000");
                    s.fadeTime   = F("사라짐(초)",   s.fadeTime,   0.01f,  1f,   0.01f, "0.000");
                    GUILayout.Space(4f);
                    s.revealSoft = F("그어짐 경계 부드러움", s.revealSoft, 0.001f, 1f, 0.01f, "0.000");
                    s.tailFade   = F("꼬리 흐림",           s.tailFade,   0f,     1f, 0.02f, "0.00");
                    GUILayout.Label($"<size=11>총 지속 {s.revealTime + s.holdTime + s.fadeTime:0.000}초</size>", Rich());
                    break;
            }

            GUILayout.EndScrollView();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("<b>저장</b>", Rich(), GUILayout.Height(26f))) fx.Save();
            if (GUILayout.Button("불러오기", GUILayout.Width(80f), GUILayout.Height(26f)))
            { Debug.Log(fx.LoadFromDisk() ? "[F5] 저장값 불러옴" : "[F5] 저장 파일 없음"); if (fx.PreviewOn) fx.PreviewRebuild(); }
            GUILayout.EndHorizontal();

            // ── 실제 동작과 함께 보기 ──
            GUILayout.BeginHorizontal();
            if (GUILayout.Button($"<b>▶ {s.posePrefix} 평타와 함께 재생</b>", Rich(), GUILayout.Height(28f)))
                if (!fx.PlayWithPose(s))
                    Debug.LogWarning($"[F5] '{s.posePrefix}*' 포즈가 2개 미만입니다 — 이펙트만 재생됩니다.");
            bool rep = GUILayout.Toggle(fx.autoRepeat, " 반복", GUI.skin.button, GUILayout.Width(60f), GUILayout.Height(28f));
            if (rep != fx.autoRepeat) { fx.autoRepeat = rep; if (rep) fx.PlayWithPose(s); }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("이펙트만", GUILayout.Height(24f))) fx.SpawnNow(s);
            if (GUILayout.Button("각도 0", GUILayout.Width(70f), GUILayout.Height(24f))) s.roll = s.pitch = s.yaw = 0f;
            if (GUILayout.Button("정지", GUILayout.Width(60f), GUILayout.Height(24f)))
            { fx.autoRepeat = false; var pp = PosePlayer.Instance; if (pp != null) pp.Stop(); }
            GUILayout.EndHorizontal();

            GUILayout.Label("<size=10><b>저장</b>을 눌러야 남습니다 → Poses/slashfx.json (다음 Play에 자동 적용)</size>", Rich());
            GUILayout.EndArea();
        }

        /// <summary>저장된 포즈에서 "이름_숫자" 형태의 접두어를 모은다(slash1_, thrust1_ …).</summary>
        static string[] PosePrefixes()
        {
            var set = new System.Collections.Generic.List<string>();
            foreach (var n in PosePlayer.ListPoses())
            {
                int u = n.LastIndexOf('_');
                if (u <= 0) continue;
                string p = n.Substring(0, u + 1);
                if (!set.Contains(p)) set.Add(p);
            }
            if (set.Count == 0) set.Add("slash1_");
            return set.ToArray();
        }

        static void Preset(SlashFxDriver.Slot s, Color high, Color mid, Color low)
        { s.colorHigh = high; s.colorMid = mid; s.colorLow = low; }

        /// <summary>HDR 색 한 줄 — R/G/B를 0~4 범위로(1 초과 = 블룸).</summary>
        static Color ColorRow(string label, Color c)
        {
            GUILayout.Label($"  {label}", Rich());
            c.r = F("   R", c.r, 0f, 4f, 0.05f, "0.00");
            c.g = F("   G", c.g, 0f, 4f, 0.05f, "0.00");
            c.b = F("   B", c.b, 0f, 4f, 0.05f, "0.00");
            return c;
        }

        static float F(string label, float v, float min, float max, float step, string fmt)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{label}: {v.ToString(fmt)}", GUILayout.Width(185f));
            if (GUILayout.Button("−", GUILayout.Width(22f))) v -= step;
            v = GUILayout.HorizontalSlider(v, min, max);
            if (GUILayout.Button("+", GUILayout.Width(22f))) v += step;
            GUILayout.EndHorizontal();
            return Mathf.Clamp(v, min, max);
        }

        static GUIStyle Rich() => new GUIStyle(GUI.skin.label) { richText = true };
    }

    /// <summary>Play 시 자동 부착.</summary>
    public static class SlashFxPanelBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Object.FindFirstObjectByType<SlashFxPanel>() == null)
                new GameObject("[SlashFxPanel]").AddComponent<SlashFxPanel>();
        }
    }
}
