using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.View
{
    /// <summary>
    /// F8 — 몹 발광·오염 튜닝. 값을 바꾸면 <b>몹 4종 전부에 즉시</b> 반영된다
    /// (머티리얼을 하나씩 인스펙터로 만지지 않아도 되게).
    ///
    /// 저장은 발광·오염을 <b>따로</b> 한다 — 한쪽만 마음에 들 때 다른 쪽을 덮어쓰지 않게.
    /// (F1=전투 · F2=포즈재생 · F3=시퀀스 · F4=절차모션 · F5=베기이펙트 · F6=콤보 · F7=NavMesh · F8=몹 비주얼)
    /// </summary>
    public class EnemyVisualPanel : MonoBehaviour
    {
        bool open;
        int tab;
        Vector2 scroll;
        static readonly string[] Tabs = { "발광", "녹·오염", "반사·빨강" };

        /// <summary>패널이 열려 있는가.</summary>
        public static bool AnyOpen;

        void OnDisable() { AnyOpen = false; }

        void Start()
        {
            // 저장돼 있으면 시작할 때 불러온다
            if (EnemyVisualSave.Exists &&
                EnemyVisualSave.Load(ref EntityViews.Glow, ref EntityViews.Dirt))
                Debug.Log("[F8] 저장된 몹 비주얼 설정을 불러왔습니다.");
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.f8Key.wasPressedThisFrame)
            {
                open = !open;
                AnyOpen = open;
                Cursor.lockState = open ? CursorLockMode.None : CursorLockMode.Locked;
                Cursor.visible = open;
            }
        }

        void OnGUI()
        {
            if (!open) return;
            const float W = 420f;
            float H = Mathf.Min(Screen.height - 24f, 560f);
            GUILayout.BeginArea(new Rect(Screen.width - W - 12f, 12f, W, H), GUI.skin.box);
            GUILayout.Label("<b>몹 비주얼 (F8)</b>  <size=10>발광·녹 — 몹 4종 동시 적용</size>", Rich());

            EntityViews.GlowEnabled = GUILayout.Toggle(EntityViews.GlowEnabled, " 발광·오염 켜기");
            GUILayout.Label($"<size=10>저장 파일: {(EnemyVisualSave.Exists ? "<color=#80e080>있음</color>" : "<color=#c0c0c0>없음</color>")}" +
                            "   Assets/_Project/Poses/enemy_visual.json</size>", Rich());

            tab = GUILayout.Toolbar(tab, Tabs);
            scroll = GUILayout.BeginScrollView(scroll);

            ref var g = ref EntityViews.Glow;
            ref var d = ref EntityViews.Dirt;

            if (tab == 0)
            {
                GUILayout.Label("<b>상태별 강도</b>  <size=10>Bloom threshold 1.05 — 1 이하면 안 번짐</size>", Rich());
                g.baseIntensity   = F("평상시",       g.baseIntensity,   0f, 40f);
                g.windupIntensity = F("공격 예비 ★",  g.windupIntensity, 0f, 80f);
                g.attackIntensity = F("타격 순간",     g.attackIntensity, 0f, 80f);
                g.chargeIntensity = F("돌진",         g.chargeIntensity, 0f, 60f);
                g.aimIntensity    = F("조준",         g.aimIntensity,    0f, 60f);
                g.gloryIntensity  = F("처형 가능",     g.gloryIntensity,  0f, 60f);

                GUILayout.Space(4f);
                GUILayout.Label("<b>색</b>", Rich());
                g.baseColor   = ColorRow("평상시", g.baseColor);
                g.windupColor = ColorRow("예비·공격", g.windupColor);
                g.gloryColor  = ColorRow("처형", g.gloryColor);

                GUILayout.Space(4f);
                GUILayout.Label("<b>반응</b>", Rich());
                g.followSpeed    = F("상태 전이 속도", g.followSpeed,    1f, 40f);
                g.flashIntensity = F("피격 깜빡",     g.flashIntensity, 0f, 80f);
                g.flashDecay     = F("깜빡 감쇠",     g.flashDecay,     1f, 20f);
                g.colorJitter    = F("개체별 색편차",  g.colorJitter,    0f, 0.5f);

                GUILayout.Space(4f);
                GUILayout.Label("<b>원거리 보정</b>  <size=10>어두운 맵에서 멀리 있는 몹이 묻히지 않게</size>", Rich());
                g.distanceBoost = F("가산 배수", g.distanceBoost, 0f, 4f);
                g.boostStart    = F("시작 거리(m)", g.boostStart, 0f, 40f);
                g.boostRange    = F("도달 거리(m)", g.boostRange, 1f, 60f);

                SaveRow("발광", true);
            }
            else if (tab == 1)
            {
                GUILayout.Label("<b>오염 강도</b>", Rich());
                g.grungeScale = F("전체 배수", g.grungeScale, 0f, 1f);
                GUILayout.Label("<size=10>개체마다 난수 편차가 곱해진다 — 같은 몹도 다르게 더럽다</size>", Rich());

                GUILayout.Space(4f);
                GUILayout.Label("<b>녹 색</b>  <size=10>어두운 배경에선 <b>밝은</b> 색이어야 보인다</size>", Rich());
                d.rustColor = ColorRow("넓은 얼룩", d.rustColor);
                d.rustDark  = ColorRow("뭉친 코어", d.rustDark);
                d.rustBoost = F("진하기", d.rustBoost, 0f, 3f);
                d.darken    = F("어둡게(낮게 유지)", d.darken, 0f, 1f);

                GUILayout.Space(4f);
                GUILayout.Label("<b>무늬</b>", Rich());
                d.grungeTexScale = F("얼룩 크기(낮을수록 큼)", d.grungeTexScale, 1f, 30f);
                d.streak         = F("흘러내린 자국", d.streak, 0f, 1f);

                SaveRow("오염", false);
            }
            else
            {
                GUILayout.Label("<b>반사 대비</b>  <size=10>어둠 속에서 색보다 잘 읽히는 신호</size>", Rich());
                d.cleanSmoothness = F("깨끗한 곳 번쩍임", d.cleanSmoothness, 0f, 1f);
                d.rustSmoothness  = F("녹슨 곳 무광도",   d.rustSmoothness,  0f, 1f);
                d.rustMetallic    = F("녹 금속성",        d.rustMetallic,    0f, 1f);
                GUILayout.Label("<size=10>둘 차이가 클수록 어둠에서 잘 드러난다 (예: 0.95 ↔ 0.02)</size>", Rich());

                GUILayout.Space(6f);
                GUILayout.Label("<b>빨강 추출</b>  <size=10>어디까지를 '빨강'으로 보고 빛낼지</size>", Rich());
                d.redThreshold = F("임계값", d.redThreshold, 0f, 1f);
                d.redSoftness  = F("경계 부드러움", d.redSoftness, 0.01f, 0.5f);
                GUILayout.Label("<size=10>임계값을 낮추면 더 넓은 영역이 빛난다(주황·분홍까지)</size>", Rich());

                SaveRow("오염", false);
            }

            GUILayout.EndScrollView();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("<b>전부 저장</b>", Rich(), GUILayout.Height(26f)))
                Debug.Log(EnemyVisualSave.Save(in EntityViews.Glow, in EntityViews.Dirt)
                    ? "[F8] 저장 완료 — " + EnemyVisualSave.Path : "[F8] 저장 실패");
            if (GUILayout.Button("불러오기", GUILayout.Width(80f), GUILayout.Height(26f)))
                Debug.Log(EnemyVisualSave.Load(ref EntityViews.Glow, ref EntityViews.Dirt)
                    ? "[F8] 불러옴" : "[F8] 저장 파일 없음");
            if (GUILayout.Button("기본값", GUILayout.Width(70f), GUILayout.Height(26f)))
            { EntityViews.Glow = EnemyGlowSettings.Default; EntityViews.Dirt = EnemyDirtSettings.Default; }
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        /// <summary>항목별 저장 — 발광만 / 오염만 따로 남길 수 있게.</summary>
        void SaveRow(string what, bool glowSide)
        {
            GUILayout.Space(6f);
            if (GUILayout.Button($"<b>{what}만 저장</b>", Rich(), GUILayout.Height(24f)))
            {
                // 파일은 하나지만, 저장 전에 반대쪽을 파일 값으로 되돌려 덮어쓰지 않게 한다.
                var g = EntityViews.Glow; var d = EntityViews.Dirt;
                var fg = EnemyGlowSettings.Default; var fd = EnemyDirtSettings.Default;
                if (EnemyVisualSave.Load(ref fg, ref fd))
                {
                    if (glowSide) d = fd;   // 발광만 저장 → 오염은 파일 그대로
                    else          g = fg;   // 오염만 저장 → 발광은 파일 그대로
                }
                Debug.Log(EnemyVisualSave.Save(in g, in d)
                    ? $"[F8] {what} 저장 완료" : "[F8] 저장 실패");
            }
        }

        static float F(string label, float v, float min, float max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{label}: {v:0.00}", GUILayout.Width(170f));
            if (GUILayout.Button("−", GUILayout.Width(22f))) v -= (max - min) * 0.02f;
            v = GUILayout.HorizontalSlider(v, min, max);
            if (GUILayout.Button("+", GUILayout.Width(22f))) v += (max - min) * 0.02f;
            GUILayout.EndHorizontal();
            return Mathf.Clamp(v, min, max);
        }

        /// <summary>RGB 슬라이더 — IMGUI에는 색 선택기가 없어 채널별로 조절한다.</summary>
        static Color ColorRow(string label, Color c)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(80f));
            GUILayout.Label("R", GUILayout.Width(12f)); c.r = GUILayout.HorizontalSlider(c.r, 0f, 1f);
            GUILayout.Label("G", GUILayout.Width(12f)); c.g = GUILayout.HorizontalSlider(c.g, 0f, 1f);
            GUILayout.Label("B", GUILayout.Width(12f)); c.b = GUILayout.HorizontalSlider(c.b, 0f, 1f);
            GUILayout.EndHorizontal();
            // 현재 색을 작은 박스로 미리보기
            var r = GUILayoutUtility.GetLastRect();
            GUI.color = c;
            GUI.DrawTexture(new Rect(r.x + 62f, r.y + 2f, 14f, 14f), Texture2D.whiteTexture);
            GUI.color = Color.white;
            return c;
        }

        static GUIStyle Rich() => new GUIStyle(GUI.skin.label) { richText = true };
    }

    public static class EnemyVisualPanelBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Object.FindFirstObjectByType<EnemyVisualPanel>() == null)
                new GameObject("[EnemyVisualPanel]").AddComponent<EnemyVisualPanel>();
        }
    }
}
