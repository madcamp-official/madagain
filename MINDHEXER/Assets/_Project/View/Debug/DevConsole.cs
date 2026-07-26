using System;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// 개발용 커맨드 콘솔. ` 로 토글. 몹을 조합 지정해 소환하고 패턴을 관찰한다.
    /// IMGUI(개발툴 표준) — 릴리스에서 빼려면 이 컴포넌트 생성부를 DEVELOPMENT_BUILD로 감싸면 된다.
    /// 콘솔이 열려 있는 동안 Main이 플레이어 입력을 막는다(sim은 계속 돎).
    /// </summary>
    public class DevConsole : MonoBehaviour
    {
        public bool IsOpen { get; private set; }

        const string ControlName = "devconsole_input";
        string input = "";
        readonly StringBuilder log = new StringBuilder();
        Vector2 scroll;

        // ── 몹 애니메이션 상태 관찰 오버레이(anim 명령) ──
        bool animWatch;

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.backquoteKey.wasPressedThisFrame) { if (IsOpen) Close(); else Open(); }
            else if (IsOpen && kb.escapeKey.wasPressedThisFrame) Close();

            // F9 — 녹슨 관절 즉시 토글. 아직 확정 전이라 콘솔을 열지 않고 켜고 끄며 비교해야 한다.
            // (콘솔이 열려 있을 땐 타이핑을 방해하지 않게 무시)
            if (!IsOpen && kb.f9Key.wasPressedThisFrame)
            {
                EntityViews.RustEnabled = !EntityViews.RustEnabled;
                Debug.Log("[F9] 녹슨 관절 " + (EntityViews.RustEnabled ? "켬" : "끔"));
            }
        }

        void Open()
        {
            IsOpen = true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            if (log.Length == 0) Print("명령어: help  (예: solo pinky)");
        }

        void Close()
        {
            IsOpen = false;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        void OnGUI()
        {
            if (animWatch) DrawAnimWatch();
            if (!IsOpen) return;
            float w = Screen.width;
            const float h = 220f;
            GUI.Box(new Rect(0, 0, w, h), GUIContent.none);

            scroll = GUI.BeginScrollView(new Rect(6, 6, w - 12, h - 38), scroll,
                                         new Rect(0, 0, w - 40, Mathf.Max(2000f, 0f)));
            GUI.Label(new Rect(0, 0, w - 40, 2000f), log.ToString());
            GUI.EndScrollView();

            Event e = Event.current;
            bool enter = e.type == EventType.KeyDown &&
                         (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter);

            GUI.SetNextControlName(ControlName);
            input = GUI.TextField(new Rect(6, h - 30, w - 12, 24), input);
            input = input.Replace("`", "");        // 토글 키가 입력창에 안 들어가게
            GUI.FocusControl(ControlName);         // 열자마자 바로 타이핑

            if (enter && !string.IsNullOrWhiteSpace(input))
            {
                Execute(input.Trim());
                input = "";
                e.Use();
            }
        }

        /// <summary>
        /// 씬에 있는 몹들의 애니메이터가 어떤 선택 파라미터를 갖췄는지 표시(콘솔 <c>animcaps</c>).
        /// 클립을 넣은 뒤 "제대로 연결됐나"를 코드 안 보고 확인하는 용도.
        /// </summary>
        void PrintAnimCaps()
        {
            Print("몹별 애니메이터 파라미터 — 있으면 코드가 자동으로 구동합니다");
            Print("  (이름을 정확히 IsAirborne/IsRunning/IsHurt/IsDead 로 지어야 잡힙니다)");
            bool any = false;
            foreach (var go in GameObject.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
            {
                if (!go.name.StartsWith("Enemy_")) continue;
                var a = go.GetComponentInChildren<Animator>();
                if (a == null) { Print($"  {go.name}: Animator 없음(공중몹/캡슐)"); any = true; continue; }
                var ctrl = a.runtimeAnimatorController;
                var sb = new StringBuilder();
                sb.Append("  ").Append(go.name).Append(" [")
                  .Append(ctrl != null ? ctrl.name : "컨트롤러 없음").Append("] ");
                foreach (var want in new[] { "IsAirborne", "IsRunning", "IsHurt", "IsDead" })
                {
                    bool has = false;
                    if (ctrl != null)
                        foreach (var pp in a.parameters)
                            if (pp.name == want && pp.type == AnimatorControllerParameterType.Bool) { has = true; break; }
                    sb.Append(has ? "O " : "· ").Append(want).Append("  ");
                }
                Print(sb.ToString());
                any = true;
            }
            if (!any) Print("  몹이 없습니다 — solo grunt 등으로 소환 후 다시 실행하십시오");
        }

        /// <summary>anim 명령으로 켠 관찰 오버레이 — Enemy_0의 애니메이터 상태를 매 프레임 표시.</summary>
        void DrawAnimWatch()
        {
            var main = Main.Instance;
            if (main == null) return;

            var sb = new StringBuilder();
            sb.Append("[anim watch] Enemy_0: ");
            var go = GameObject.Find("Enemy_0");
            if (go == null)
            {
                sb.Append("없음(죽었거나 아직 생성 안 됨)");
            }
            else
            {
                var anim = go.GetComponentInChildren<Animator>();
                if (anim == null)
                {
                    sb.Append("Animator 없음(공중/캡슐 유닛)");
                }
                else
                {
                    var st = anim.GetCurrentAnimatorStateInfo(0);
                    sb.Append("t=" + st.normalizedTime.ToString("0.00") + " speed=" + anim.speed.ToString("0.00"));
                    foreach (var pname in new[] { "IsAttacking", "IsAiming", "IsCharging", "IsAirborne" })
                    {
                        bool has = false;
                        foreach (var pp in anim.parameters) if (pp.name == pname) { has = true; break; }
                        if (has) sb.Append("  " + pname + "=" + anim.GetBool(pname));
                    }
                }

                ref readonly SimWorld w = ref main.World;
                if (w.enemyCount > 0)
                    sb.Append("\nsim: ai.state=" + w.enemies[0].ai.state + "  grounded=" + w.enemies[0].grounded);
            }

            const float boxW = 480f, boxH = 54f;
            GUI.Box(new Rect(6, 6, boxW, boxH), GUIContent.none);
            GUI.Label(new Rect(12, 8, boxW - 12, boxH - 4), sb.ToString());
        }

        // ── 텔레포트 지점 (Level_Main 월드좌표, 개발 테스트용 하드코딩) ──
        // 인덱스 = 아레나 번호. 0번은 미사용.
        static readonly Vector3[] ArenaPos =
        {
            Vector3.zero,
            new Vector3(-0.06f, 6.92f,  56.94f),   // 1
            new Vector3(-0.19f, 5.07f, 126.21f),   // 2
            new Vector3(-1.90f, 5.63f, 188.80f),   // 3
            Vector3.zero,                          // 4 (미지정 — hall 4로 접근)
        };
        // 그 아레나 '직전 골목'(복도) — 여기서 걸어 들어가면 wayin 게이트가 자연스럽게 작동.
        static readonly Vector3[] HallPos =
        {
            Vector3.zero, Vector3.zero, Vector3.zero,
            new Vector3(-0.04f,  3.92f, 167.02f),  // 3 직전 골목
            new Vector3(-0.17f, 12.70f, 303.18f),  // 4 직전 골목
        };

        static bool Valid(Vector3 v) => v != Vector3.zero;

        void Execute(string line)
        {
            Print("> " + line);
            string[] p = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            Main main = Main.Instance;
            if (main == null) { Print("Main 없음"); return; }

            switch (p[0].ToLowerInvariant())
            {
                case "help":
                    Print("spawn <종류> · solo <종류> · anim <종류>|off · clear · autospawn on|off|reload · count");
                    Print("종류: grunt pinky soldier caco large  ·  gruntt(근층)/soldiert(원층)은 폐기 예정");
                    Print("[디버그] anim <종류> = 그 몹 하나만 소환하고 화면에 애니메이터 상태 실시간 표시. anim off로 끔");
                    Print("wave list · wave start [n] · wave only <n> · wave stop · wave status  (n은 1부터)");
                    Print("[레벨] arena <1~3>  그 아레나 안으로 순간이동(웨이브 시작) · hall <3~4>  그 아레나 직전 골목으로 이동(걸어 들어가기)");
                    Print("  예) wave only 2 = 웨이브2만 실행 · wave start 2 = 웨이브2부터 순차");
                    Print("[이펙트] vfx <이름> [거리] [pitch] [yaw] [roll] [상하] · vfx list · vfx reload");
                    Print("  pitch=위아래로 눕히기 · yaw=좌우로 돌리기 · roll=화면 안 각도 (전부 0=원래 방향)");
                    Print("[이펙트] slash 1|2|t|down [거리] [pitch] [yaw] [roll] [상하]");
                    Print("[포즈] pose list · pose <이름>   (JSON 포즈 즉시 적용)");
                    Print("[포즈] play <접두어> [초] [loop] [linear|spring] · play stop(기본포즈로) · play release(제어 해제)");
                    Print("[포즈] combo <접두어...> [초] [loop]   예) combo 평타1_ 평타2_ 0.35 loop");
                    Print("[포즈] seq <이름...> [loop]   예) seq 평타1_0 평타1_1 평타1_4  (간격은 F3에서 각각 조절)");
                    Print("[포즈] bind [on|off|<초>]   전투 연동 — 평타 slash1↔slash2, 찌르기 thrust1");
                    Print("[화면] ui [off|on]   게임 UI(HP바 등) 숨기기 — 개발 패널은 유지");
                    Print("[전투] combat · combat save|load|delete   F1·F6 수치 저장 (F6=평타 2연타 콤보)");
                    Print("[몹] <b>F10 = 몹 밸런스 패널</b> — 근접·돌진·원거리·공중 타이밍/거리 (저장·불러오기 포함)");
                    Print("[베기] fx <평타1|평타2|찌르기> · fx off|on   (각도·위치는 F4에서 조절)");
                    Print("[뷰모델] vm · vm info   뷰모델 소환·재생성 (프리팹 저장은 Tools/뷰모델/③)");
                    Print("[뷰모델] vcam · vcam auto · vcam back <m> · vcam near <m>   팔 뚫림(후퇴량·근평면)");
                    Print("[컷신] cut · cut stop   (C 키와 동일. 리그는 Tools/컷신/①로 설치)");
                    Print("[이펙트] swing 1|2|t [loop] · swing stop   (칼 애니메이션 재생)");
                    Print("[절차] proc land [강도] · proc hit · proc breathe [0~1] · proc grip [0~1] · proc pulse · proc reset");
                    Print("[전투] lunge [on|off|<거리m>]   찌르기 자유 시전 — 몹 없어도 발동·스택 무한");
                    Print("[몹] look [on|off] · look <yaw|pitch|speed|weight> <값>   몹이 플레이어를 쳐다봄");
                    Print("[몹] step [on|off|reset] · step <count|size|speed|life|dist|sound> <값>   발 딛을 때 스파크");
                    Print("[몹] glow [on|off|reset] · glow all 10 · glow <base|windup|attack|charge|aim|glory> <값>");
                    Print("     어두운 맵 식별용 발광 + 상태 텔레그래프(공격 예비 시 급격히 밝아짐)");
                    Print("[파손] dmg <확률> · dmg <변형이름> · dmg off · dmg list   부위 떨어진 개체 소환");
                    Print("[파손] dangle <len|weight|stiff|damp|seg|root|tip|body> <값>   대롱거림 튜닝");
                    Print("       예) dmg MeleeEnemy_d02 → solo grunt 하면 왼팔 없는 개체가 나옴");
                    Print("[파손] wire <본이름> · wire off · wire bones · wire <len|seg|root|tip|damp|grav|fric|spark> <값>");
                    Print("[디버그] hitbox [on|off|body|melee|lunge|all|xray]   판정 표시 — 평타 구·찌르기 사거리·캡슐");
                    Print("[몹] pace walk|run <값> · pace reset   보폭 기준 속도(발 미끄러짐) — 실측은 Tools/몹/걷기 속도 측정");
                    Print("[몹] feet <m> · feet reset   몹이 떠 보일 때 렌더만 미세 조절(히트박스 그대로)");
                    Print("[디버그] animcaps   몹 애니메이터에 IsAirborne/IsRunning/IsHurt/IsDead 가 붙었는지 확인");
                    Print("[몹] rust [on|off|all|reset] · rust stiff|damp|stick|jitter <값>   녹슨 관절(고착+스프링)");
                    Print("[몹] tele [on|off|reset] · tele melee|charge|aim <각도> · tele hit <세기>");
                    Print("     ★ 텔레그래프 — 공격 예비·돌진 준비·차징·피격을 자세로 예고(언제 피할지 단서)");
                    Print("[몹] mv [on|off|reset] · mv turn|accel|lean|bob|stagger|recoil|bind|lowhp|bank|heavy <값>");
                    Print("     이동·상황 자세 — 급선회·가감속·경사·휘청·반동·붙잡힘·저체력·뱅킹·대형무게");
                    Print("[이펙트] timescale 0.15  (슬로우모션. 1로 원복)");
                    break;

                case "wave":
                    Wave(p);
                    break;

                case "arena":
                {
                    // 특정 아레나 안으로 순간이동. ArenaRoom이 진입을 감지해 그 방 웨이브를 자동 시작.
                    if (p.Length < 2 || !int.TryParse(p[1], out int an) || an < 1 || an >= ArenaPos.Length)
                    { Print("사용: arena <1~3>   예) arena 3   (그 아레나 안으로 이동)"); break; }
                    if (!Valid(ArenaPos[an]))
                    { Print($"Arena_{an} 위치 미지정 — 'hall {an}'로 골목까지 가서 걸어 들어가세요."); break; }
                    main.PlacePlayerAt(ArenaPos[an]);
                    Print($"Arena_{an} 안으로 이동");
                    break;
                }

                case "hall":
                {
                    // 그 아레나 '직전 골목'으로 순간이동 — 걸어 들어가며 wayin 게이트 정상 작동 확인용.
                    if (p.Length < 2 || !int.TryParse(p[1], out int hn) || hn < 1 || hn >= HallPos.Length)
                    { Print("사용: hall <번호>   예) hall 4   (그 아레나 직전 골목으로 이동)"); break; }
                    if (!Valid(HallPos[hn]))
                    { Print($"Arena_{hn} 직전 골목 위치 미지정."); break; }
                    main.PlacePlayerAt(HallPos[hn]);
                    Print($"Arena_{hn} 직전 골목으로 이동");
                    break;
                }

                case "spawn":
                    if (p.Length < 2) { Print("사용: spawn <종류>"); break; }
                    if (TryType(p[1], out var c, out var m, out var s))
                    { main.DevSpawn(c, m, s); Print("소환: " + p[1]); }
                    else Print("모르는 종류: " + p[1]);
                    break;

                case "solo":
                    if (p.Length < 2) { Print("사용: solo <종류>"); break; }
                    if (TryType(p[1], out var c2, out var m2, out var s2))
                    {
                        main.AutoSpawn = false;
                        main.DevClear();
                        main.DevSpawn(c2, m2, s2);
                        Print("솔로: " + p[1] + " (자동소환 off)");
                    }
                    else Print("모르는 종류: " + p[1]);
                    break;

                case "anim":
                    if (p.Length < 2) { Print("사용: anim <종류> | anim off"); break; }
                    if (p[1].ToLowerInvariant() == "off") { animWatch = false; Print("애니메이션 관찰 꺼짐"); break; }
                    if (TryType(p[1], out var c3, out var m3, out var s3))
                    {
                        main.AutoSpawn = false;
                        main.DevClear();
                        main.DevSpawn(c3, m3, s3);
                        animWatch = true;
                        Print("애니메이션 관찰: " + p[1] + " (Enemy_0, 화면 좌상단 표시. 끄려면 anim off)");
                    }
                    else Print("모르는 종류: " + p[1]);
                    break;

                case "clear":
                    main.DevClear();
                    Print("전부 제거");
                    break;

                case "클리어":
                case "kw":
                {
                    // 지금 진행 중인 아레나(활성 WaveRunner)를 강제 클리어 판정 → 출구 게이트 열림 + 남은 몹 제거.
                    WaveRunner active = null;
                    foreach (var wr in FindObjectsByType<WaveRunner>(FindObjectsSortMode.None))
                        if (wr.IsRunning) { active = wr; break; }
                    if (active == null) { Print("진행 중인 아레나 웨이브 없음"); break; }
                    active.ForceComplete();
                    main.DevClear();
                    Print($"[{active.name}] 웨이브 클리어 판정 + 남은 몹 제거");
                    break;
                }

                case "autospawn":
                    if (p.Length >= 2 && p[1] == "on")  { main.AutoSpawn = true;  Print("자동소환 on"); }
                    else if (p.Length >= 2 && p[1] == "off") { main.AutoSpawn = false; Print("자동소환 off"); }
                    else if (p.Length >= 2 && p[1] == "reload") { main.ReloadSpawnConfig(); Print("씬 스폰 세팅 다시 읽음"); }
                    else Print("사용: autospawn on|off|reload");
                    break;

                case "count":
                    Print("생존: " + main.AliveEnemyCount());
                    break;

                // 애니메이터 선택 파라미터 연결 상태 — 클립 넣고 확인용
                case "animcaps":
                    PrintAnimCaps();
                    break;

                // 보폭 기준 속도 — Tools/몹/걷기 속도 측정 의 결과를 여기에 넣어보며 확인
                case "pace":
                {
                    if (p.Length >= 3 && float.TryParse(p[2], out float pv))
                    {
                        pv = Mathf.Clamp(pv, 0.2f, 20f);
                        switch (p[1].ToLowerInvariant())
                        {
                            case "walk": EntityViews.WalkClipPace = pv; break;
                            case "run":  EntityViews.RunClipPace  = pv; break;
                            case "min":  EntityViews.WalkSpeedClampMin = pv; break;
                            case "max":  EntityViews.WalkSpeedClampMax = pv; break;
                            default: Print("사용: pace walk|run|min|max <값>"); break;
                        }
                    }
                    else if (p.Length >= 2 && p[1] == "reset")
                    {
                        EntityViews.WalkClipPace = 1.8f; EntityViews.RunClipPace = 4f;
                        EntityViews.WalkSpeedClampMin = 0.6f; EntityViews.WalkSpeedClampMax = 1.5f;
                    }
                    else if (p.Length >= 2) { Print("사용: pace walk|run|min|max <값> · pace reset"); break; }

                    Print($"보폭 기준 — 걷기 {EntityViews.WalkClipPace:0.00} · 달리기 {EntityViews.RunClipPace:0.00}" +
                          $" · 배속 범위 {EntityViews.WalkSpeedClampMin:0.00}~{EntityViews.WalkSpeedClampMax:0.00}\n" +
                          "  이 값 = \"이 클립이 가정하는 이동 속도(m/s)\". 실측은 Tools/몹/걷기 속도 측정.\n" +
                          "  맞으면 몹이 걸을 때 배속이 1 근처가 되고 발이 안 미끄러집니다.");
                    break;
                }

                // 몹이 살짝 떠 보일 때 렌더만 미세 조절(히트박스는 그대로)
                case "feet":
                {
                    if (p.Length >= 2)
                    {
                        if (p[1] == "reset") EntityViews.FeetTrim = EntityViews.FeetTrimDefault;
                        else if (float.TryParse(p[1], out float fv))
                            EntityViews.FeetTrim = Mathf.Clamp(fv, -0.3f, 0.3f);
                        else { Print("사용: feet <m>  (음수=내림)  ·  feet reset"); break; }
                    }
                    Print($"발 높이 보정 {EntityViews.FeetTrim:+0.000;-0.000}m  (음수=내림)\n" +
                          "  렌더만 움직입니다 — 히트박스·판정은 그대로. 조금씩(0.01) 바꿔가며 맞추십시오.\n" +
                          "  hitbox 를 켜면 캡슐 바닥과 발이 맞는지 눈으로 볼 수 있습니다.");
                    break;
                }

                // ── 발자국 스파크·소리 ──
                case "step":
                {
                    ref var fc = ref EntityViews.Footstep;
                    if (p.Length >= 3 && float.TryParse(p[2], out float sv))
                    {
                        switch (p[1])
                        {
                            case "count": fc.sparkCount = Mathf.Clamp((int)sv, 0, 20); break;
                            case "size":  fc.sparkSize  = Mathf.Max(0.05f, sv); break;
                            case "speed": fc.sparkSpeed = Mathf.Max(0f, sv); break;
                            case "life":  fc.sparkLife  = Mathf.Max(0.05f, sv); break;
                            case "dist":  fc.maxDistance = Mathf.Max(1f, sv); break;
                            case "minspeed": fc.minSpeed = Mathf.Max(0f, sv); break;
                            case "cool":  fc.cooldown   = Mathf.Max(0f, sv); break;
                            case "fall":  fc.minFall    = Mathf.Max(0.0001f, sv); break;
                            case "height": fc.maxFootHeight = Mathf.Max(0.05f, sv); break;
                            case "sound": FootstepEvents.UseFallbackSound = sv > 0.5f; break;
                            default: Print("항목: count size speed life dist minspeed cool fall height sound"); break;
                        }
                    }
                    else if (p.Length >= 2 && p[1] == "off") { fc.enabled = false; Print("발자국 끔"); break; }
                    else if (p.Length >= 2 && p[1] == "on")  fc.enabled = true;
                    else if (p.Length >= 2 && p[1] == "reset") fc = FootstepSettings.Default;
                    else if (p.Length >= 2) { Print("사용: step [on|off|reset] · step <항목> <값>"); break; }
                    else fc.enabled = !fc.enabled;

                    Print($"발자국 {(fc.enabled ? "켬" : "끔")} — 스파크 {fc.sparkCount}개 · 크기 {fc.sparkSize:0.00} · " +
                          $"속도 {fc.sparkSpeed:0.00} · 수명 {fc.sparkLife:0.00}\n" +
                          $"  최소속도 {fc.minSpeed:0.0}m/s · 재발동간격 {fc.cooldown:0.00}초 · 표시거리 {fc.maxDistance:0}m\n" +
                          $"  임시 발소리 {(FootstepEvents.UseFallbackSound ? "켬" : "끔")} (step sound 1)  ★ 실제 에셋은 SoundHook에 연결");
                    break;
                }

                // ── 파손 변형 (부위가 떨어져 전선으로 대롱거리는 개체) ──
                case "dmg":
                {
                    if (p.Length < 2)
                    {
                        Print($"파손 확률 {EntityViews.DamagedChance:0.00}" +
                              (string.IsNullOrEmpty(EntityViews.ForceVariant) ? " · 무작위 변형"
                                                                              : $" · 강제 [{EntityViews.ForceVariant}]"));
                        Print("사용: dmg <확률 0~1> · dmg <변형이름> · dmg off · dmg list");
                        Print("  예) dmg MeleeEnemy_d02  → 소환하는 근접몹이 전부 그 변형");
                        Print("      dmg 1              → 전부 파손 개체로");
                        break;
                    }
                    if (p[1] == "off") { EntityViews.ForceVariant = "off"; Print("강제 해제 + 파손 안 나옴"); break; }
                    if (p[1] == "list")
                    {
                        foreach (var b in new[] { "MeleeEnemy", "RangedEnemy", "ChargeEnemy", "FlyingEnemy" })
                        {
                            var sb2 = new StringBuilder(b + ": ");
                            for (int i = 1; i <= 12; i++)
                                if (Resources.Load<GameObject>($"Enemies/Damaged/{b}_d{i:00}") != null) sb2.Append($"d{i:00} ");
                            Print("  " + sb2);
                        }
                        Print("  Tools/몹/파손 변형 생성 으로 만듭니다");
                        break;
                    }
                    if (float.TryParse(p[1], out float dc))
                    {
                        EntityViews.DamagedChance = Mathf.Clamp01(dc);
                        EntityViews.ForceVariant = "";
                        Print($"파손 확률 = {EntityViews.DamagedChance:0.00} (무작위 변형)");
                    }
                    else
                    {
                        EntityViews.ForceVariant = p[1];
                        Print($"강제 변형 = {p[1]}");
                    }
                    // ★ 뷰 슬롯은 종류가 바뀔 때만 재생성된다 — 같은 종류를 다시 소환해도
                    //   기존 뷰가 재사용돼 변형이 안 바뀐다. 그래서 여기서 강제로 버린다.
                    main.RebuildEnemyViews();
                    Print("  현재 몹 뷰를 재생성했습니다(즉시 반영)");
                    // 전선 길이 등은 wire 명령 값과 별개다. 여기서 같이 조절할 수 있게 해둔다.
                    if (p.Length >= 3 && float.TryParse(p[2], out float wl))
                    { EntityViews.WireCfg.length = Mathf.Max(0.05f, wl); Print($"  전선 길이 {EntityViews.WireCfg.length:0.00}m"); }
                    break;
                }

                // ── 대롱거림 튜닝 (파손 부위 전선) ──
                case "dangle":
                {
                    ref var wc2 = ref EntityViews.WireCfg;
                    if (p.Length >= 3 && float.TryParse(p[2], out float dv))
                    {
                        switch (p[1])
                        {
                            case "len":    wc2.length    = Mathf.Max(0.05f, dv); break;
                            case "weight": wc2.tipWeight = Mathf.Clamp(dv, 0f, 8f); break;   // 끝 무게 ★
                            case "stiff":  wc2.stiffness = Mathf.Clamp((int)dv, 1, 4); break; // 뻣뻣함 ★
                            case "damp":   wc2.damping   = Mathf.Clamp(dv, 0.80f, 0.999f); break;
                            case "grav":   wc2.gravity   = dv; break;
                            case "seg":    wc2.particles = Mathf.Clamp((int)dv, 3, 24); break;
                            case "root":   wc2.rootRadius= Mathf.Max(0.002f, dv); break;
                            case "tip":    wc2.tipRadius = Mathf.Max(0.002f, dv); break;
                            case "body":   DanglingWire.CollideWithBody = dv > 0.5f; break;  // 몸 충돌
                            default: Print("항목: len weight stiff damp grav seg root tip body"); break;
                        }
                    }
                    else if (p.Length >= 2) { Print("사용: dangle <항목> <값>"); break; }

                    Print($"길이 {wc2.length:0.00}m · 마디 {wc2.particles} · <b>끝무게 {wc2.tipWeight:0.0}</b> · <b>뻣뻣함 {wc2.stiffness}</b>\n" +
                          $"  감쇠 {wc2.damping:0.000} · 중력 {wc2.gravity:0.0} · 굵기 {wc2.rootRadius:0.000}→{wc2.tipRadius:0.000}" +
                          $" · 몸충돌 {(DanglingWire.CollideWithBody ? "켬" : "끔")}\n" +
                          "  ★ 대롱거림은 끝무게↑ 뻣뻣함↓ 감쇠↑ 조합. 바꾼 뒤 solo 로 다시 소환하십시오.");
                    break;
                }

                // ── 이동·상황 자세 (급선회·가감속·휘청·반동·뱅킹·저체력) ──
                case "allproc":
                case "mobproc":
                {
                    // 몹 절차 애니 전부 on/off — 경사 덜컹이 절차 때문인지 가르는 진단용.
                    string psub = p.Length >= 2 ? p[1].ToLowerInvariant() : "";
                    bool on;
                    if (psub == "on") on = true;
                    else if (psub == "off") on = false;
                    else on = !(EntityViews.MoveposeEnabled || EntityViews.PoseEnabled
                             || EntityViews.LookAtEnabled || EntityViews.ChargeAnimEnabled || EntityViews.RustEnabled);
                    EntityViews.MoveposeEnabled   = on;
                    EntityViews.PoseEnabled       = on;
                    EntityViews.LookAtEnabled     = on;
                    EntityViews.ChargeAnimEnabled = on;
                    EntityViews.RustEnabled       = on ? EntityViews.RustEnabled : false;  // 끌 땐 녹슨관절도 확실히 끔
                    Print(on ? "[allproc] 몹 절차 전부 켬" : "[allproc] 몹 절차 전부 끔 → 순수 클립만");
                    Print($"  이동자세={(EntityViews.MoveposeEnabled ? "켬" : "끔")}"
                        + $" 텔레그래프={(EntityViews.PoseEnabled ? "켬" : "끔")}"
                        + $" 시선={(EntityViews.LookAtEnabled ? "켬" : "끔")}"
                        + $" 돌진={(EntityViews.ChargeAnimEnabled ? "켬" : "끔")}"
                        + $" 녹슨관절={(EntityViews.RustEnabled ? "켬" : "끔")}");
                    break;
                }

                case "mv":
                {
                    ref var mset = ref EntityViews.Movepose;
                    string sub = p.Length >= 2 ? p[1].ToLowerInvariant() : "";
                    float v = 0f; bool hasV = p.Length >= 3 && float.TryParse(p[2], out v);
                    switch (sub)
                    {
                        case "":    EntityViews.MoveposeEnabled = !EntityViews.MoveposeEnabled; break;
                        case "on":  EntityViews.MoveposeEnabled = true;  break;
                        case "off": EntityViews.MoveposeEnabled = false; break;
                        case "reset": mset = EnemyMoveSettings.Default; break;
                        case "turn":    if (hasV) mset.turnLean   = v; break;   // 급선회 기울임
                        case "accel":   if (hasV) mset.accelLean  = v; break;   // 가감속 쏠림
                        case "lean":    if (hasV) mset.runLean    = v; break;   // 속도 경사
                        case "bob":     if (hasV) { mset.bobRoll = v; mset.bobPitch = v * 0.45f; } break;
                        case "wall":    if (hasV) mset.wallLean   = v; break;   // 벽 막힘
                        case "stagger": if (hasV) { mset.staggerHit = v; mset.staggerMiss = v * 1.5f; } break;
                        case "recoil":  if (hasV) mset.recoil     = v; break;
                        case "bind":    if (hasV) mset.bindShake  = v; break;
                        case "lowhp":   if (hasV) mset.lowHpDroop = v; break;
                        case "bank":    if (hasV) mset.bankLean   = v; break;   // 공중 뱅킹
                        case "climb":   if (hasV) mset.climbLean  = v; break;   // 고도 변화
                        case "dive":    if (hasV) mset.flyDiveLean = v; break;  // 진행 방향 굽힘
                        case "wing":    if (hasV) { mset.wingBank = v; mset.wingSweep = v * 0.7f; } break;
                        case "launch":  if (hasV) mset.launchShake = v; break;
                        case "heavy":   if (hasV) { mset.heavyLag = v; mset.heavyAmp = v * 0.45f; } break;
                        case "jitter":  if (hasV) mset.jitter     = Mathf.Clamp01(v); break;
                        default:
                            Print("사용: mv [on|off|reset]");
                            Print("  이동: mv turn 0.035 · mv accel 0.5 · mv lean 7 · mv bob 2.2 · mv wall 60");
                            Print("  전투: mv stagger 150(휘청) · mv recoil 130(반동) · mv bind 4.5(붙잡힘)");
                            Print("  상태: mv lowhp 6(저체력) · mv launch 9(스폰펄스)");
                            Print("  공중: mv bank 3.2(뱅킹) · mv climb 2.4(고도) · mv dive 2.8(진행방향 굽힘) · mv wing 5(팔=날개)");
                            Print("  개체: mv heavy 0.8(대형 무게) · mv jitter 0.25(개체 편차)");
                            break;
                    }
                    Print($"이동자세 {(EntityViews.MoveposeEnabled ? "켬" : "끔")}" +
                          $" · 급선회 {mset.turnLean:0.000} · 가감속 {mset.accelLean:0.00} · 경사 {mset.runLean:0}°" +
                          $" · 휘청 {mset.staggerHit:0}/{mset.staggerMiss:0} · 반동 {mset.recoil:0}\n" +
                          $"  뱅킹 {mset.bankLean:0.0} · 저체력 {mset.lowHpDroop:0}° · 대형 {mset.heavyLag:0.0} · 편차 ±{mset.jitter * 100f:0}%");
                    break;
                }

                // ── 상태 텔레그래프 (예비·준비·차징·피격) ──
                case "tele":
                {
                    ref var t = ref EntityViews.Pose;
                    string sub = p.Length >= 2 ? p[1].ToLowerInvariant() : "";
                    float v = 0f; bool hasV = p.Length >= 3 && float.TryParse(p[2], out v);
                    switch (sub)
                    {
                        case "":    EntityViews.PoseEnabled = !EntityViews.PoseEnabled; break;
                        case "on":  EntityViews.PoseEnabled = true;  break;
                        case "off": EntityViews.PoseEnabled = false; break;
                        case "reset": t = EnemyPoseSettings.Default; break;
                        case "melee":  if (hasV) t.meleeLean  = v; break;
                        case "charge": if (hasV) t.chargeLean = v; break;
                        case "aim":    if (hasV) t.aimLean    = v; break;
                        case "hit":    if (hasV) t.hitKick    = v; break;
                        case "shake":  if (hasV) { t.meleeShake = v; t.chargeShake = v * 1.8f; t.aimShake = v * 1.3f; } break;
                        case "crouch": if (hasV) t.chargeCrouch = v; break;
                        default:
                            Print("사용: tele [on|off|reset] · tele melee|charge|aim <각도> · tele hit <세기>");
                            Print("      tele shake <폭> · tele crouch <m>");
                            Print("  근접 선딜 0.4s · 돌진 준비 0.75s · 조준 2.0s 동안 자세로 예고합니다");
                            break;
                    }
                    Print($"텔레그래프 {(EntityViews.PoseEnabled ? "켬" : "끔")}" +
                          $" · 근접젖힘 {t.meleeLean:0}° · 돌진젖힘 {t.chargeLean:0}°(웅크림 {t.chargeCrouch:0.00}m)" +
                          $" · 차징젖힘 {t.aimLean:0}° · 피격킥 {t.hitKick:0}");
                    break;
                }

                // ── 녹슨 관절 (고착 + 스프링) ──
                case "rust":
                {
                    ref var r = ref EntityViews.Rust;
                    string sub = p.Length >= 2 ? p[1].ToLowerInvariant() : "";
                    float v = 0f; bool hasV = p.Length >= 3 && float.TryParse(p[2], out v);
                    switch (sub)
                    {
                        case "":     EntityViews.RustEnabled = !EntityViews.RustEnabled; break;
                        case "on":   EntityViews.RustEnabled = true;  break;
                        case "off":  EntityViews.RustEnabled = false; break;
                        // 종류별 — 확정 전이라 하나씩 껐다 켜며 본다
                        case "melee":  EntityViews.RustMelee  = !EntityViews.RustMelee;  break;
                        case "ranged": EntityViews.RustRanged = !EntityViews.RustRanged; break;
                        case "charge": case "pinky":
                                       EntityViews.RustCharge = !EntityViews.RustCharge; break;
                        case "all":  EntityViews.RustMelee = EntityViews.RustRanged = EntityViews.RustCharge = true; break;
                        case "none": EntityViews.RustMelee = EntityViews.RustRanged = EntityViews.RustCharge = false; break;
                        case "reset": r = RustyJointSettings.Default; break;
                        // 값은 "말단(tip)" 기준으로 받고, 몸통은 기본값과 같은 비율로 자동 배분한다
                        case "stiff":  if (hasV) { r.stiffBody = v * 2.1f; r.stiffTip = v; } break;
                        case "damp":   if (hasV) { r.dampBody  = v * 2f;   r.dampTip  = v; } break;
                        case "stick":  if (hasV) { r.stickBody = v * 2.2f; r.stickTip = v; } break;
                        case "jitter": if (hasV) r.jitter = Mathf.Clamp01(v); break;
                        case "wobble": if (hasV) r.stickWobble = Mathf.Clamp01(v); break;
                        case "back":   if (hasV) r.releaseEnd = Mathf.Max(0.1f, v); break;   // 다시 고착되는 각도
                        // 이동 게이트 — 이 속도(m/s)를 넘어야 효과가 붙는다(서 있을 땐 원본 자세)
                        case "move":   if (hasV) { r.moveMin = Mathf.Max(0f, v); r.moveFull = r.moveMin + 1.3f; } break;
                        case "speed":  Print("몹 실측 속도: " + EntityViews.SpeedReport()); break;
                        default:
                            Print("사용: rust [on|off]  (F9 로도 토글) · rust reset");
                            Print("      종류별: rust melee(근·근층) · rust ranged(원·원층) · rust charge(핑키) · rust all|none");
                            Print("      rust stick 10 · rust stiff 200 · rust damp 8 · rust back 3 · rust jitter 0.45");
                            Print("  ★ stick = 이만큼 벌어져야 툭 풀림(도). 뚝딱 느낌의 핵심 — 키울수록 크게 버팀");
                            Print("  stiff=따라잡는 힘(낮을수록 굼뜸) · damp=출렁임 잦아듦(낮을수록 오래 흔들림)");
                            Print("  back=이 각도 안으로 따라잡으면 다시 고착(키우면 툭·툭 자주 반복)");
                            Print("  (값은 말단 기준. 몸통은 자동으로 2배쯤 뻑뻑하게 배분됩니다)");
                            break;
                    }
                    if (sub == "speed") break;   // 속도만 찍고 끝
                    Print($"녹슨 관절 <b>{(EntityViews.RustEnabled ? "켬" : "끔")}</b>  (F9 로도 토글)\n" +
                          $"  대상: 근·근층 {(EntityViews.RustMelee ? "O" : "X")}" +
                          $" · 원·원층 {(EntityViews.RustRanged ? "O" : "X")}" +
                          $" · 핑키(돌진) {(EntityViews.RustCharge ? "O" : "X")}\n" +
                          $"  강성 {r.stiffTip:0}~{r.stiffBody:0} · 감쇠 {r.dampTip:0}~{r.dampBody:0}" +
                          $" · 고착 {r.stickTip:0.0}~{r.stickBody:0.0}° · 편차 ±{r.jitter * 100f:0}%\n" +
                          $"  이동 게이트: {r.moveMin:0.0}m/s 부터 → {r.moveFull:0.0}m/s 에서 최대 (걷거나 뛸 때만)");
                    break;
                }

                // ── 몹 발광 (어두운 맵 식별 + 상태 텔레그래프) ──
                case "glow":
                {
                    ref var g = ref EntityViews.Glow;
                    if (p.Length >= 3 && float.TryParse(p[2], out float gv))
                    {
                        switch (p[1])
                        {
                            case "base":   g.baseIntensity   = gv; break;
                            case "windup": g.windupIntensity = gv; break;
                            case "attack": g.attackIntensity = gv; break;
                            case "charge": g.chargeIntensity = gv; break;
                            case "aim":    g.aimIntensity    = gv; break;
                            case "glory":  g.gloryIntensity  = gv; break;
                            case "flash":  g.flashIntensity  = gv; break;
                            case "speed":  g.followSpeed     = gv; break;
                            case "jitter": g.colorJitter     = gv; break;
                            case "dirt":   g.grungeScale     = Mathf.Clamp01(gv); break;   // 녹·얼룩(개체마다 편차)
                            case "dist":   g.distanceBoost   = gv; break;
                            case "all":    // 전체를 한 번에 비례 조절
                                g.baseIntensity = gv; g.windupIntensity = gv * 3.5f;
                                g.attackIntensity = gv * 5f; g.chargeIntensity = gv * 2.8f;
                                g.aimIntensity = gv * 2f; g.gloryIntensity = gv * 3.2f;
                                break;
                            default: Print("항목: base windup attack charge aim glory flash speed jitter dist all"); break;
                        }
                    }
                    else if (p.Length >= 2 && p[1] == "off") { EntityViews.GlowEnabled = false; Print("발광 끔"); break; }
                    else if (p.Length >= 2 && p[1] == "on")  EntityViews.GlowEnabled = true;
                    else if (p.Length >= 2 && p[1] == "reset") g = EnemyGlowSettings.Default;
                    else if (p.Length >= 2) { Print("사용: glow [on|off|reset] · glow <항목> <값>"); break; }
                    else EntityViews.GlowEnabled = !EntityViews.GlowEnabled;

                    Print($"발광 {(EntityViews.GlowEnabled ? "켬" : "끔")} — " +
                          $"평상 {g.baseIntensity:0.0} · 예비 {g.windupIntensity:0.0} · 타격 {g.attackIntensity:0.0} · " +
                          $"돌진 {g.chargeIntensity:0.0} · 조준 {g.aimIntensity:0.0} · 처형 {g.gloryIntensity:0.0}\n" +
                          $"  전이속도 {g.followSpeed:0.0} · 피격깜빡 {g.flashIntensity:0.0} · 색편차 {g.colorJitter:0.00} · 원거리보정 ×{1f + g.distanceBoost:0.0}\n" +
                          $"  녹·얼룩 {g.grungeScale:0.00} (glow dirt 0~1, 개체마다 난수 편차)\n" +
                          "  ★ 빨간 부분만 빛납니다(Game/EnemyBody 셰이더). Bloom threshold 1.05 — 1 이하면 안 번짐.");
                    break;
                }

                // ── 대롱거리는 전선 (파손 연출 1단계 테스트) ──
                case "wire":
                {
                    var wt = WireTester.Instance;
                    if (wt == null) { Print("WireTester 없음"); break; }

                    if (p.Length < 2) { Print(wt.IsOn ? $"전선 {wt.Count}개 부착 중 (wire off로 제거)" : "사용: wire <본이름> · wire off · wire bones · wire <항목> <값>"); break; }

                    switch (p[1])
                    {
                        case "off":   wt.Clear(); Print("전선 제거"); break;
                        case "bones":
                        {
                            var bs = WireTester.ListBones();
                            if (bs.Count == 0) Print("몹이 없습니다 — 먼저 소환하십시오(solo grunt)");
                            else { Print($"본 {bs.Count}개:"); foreach (var b in bs) Print("  " + b); }
                            break;
                        }
                        case "len": case "seg": case "root": case "tip":
                        case "damp": case "grav": case "fric": case "spark":
                        {
                            if (p.Length < 3 || !float.TryParse(p[2], out float v)) { Print("값이 필요합니다"); break; }
                            switch (p[1])
                            {
                                case "len":   wt.cfg.length     = Mathf.Max(0.1f, v); break;
                                case "seg":   wt.cfg.particles  = Mathf.Clamp((int)v, 3, 24); break;
                                case "root":  wt.cfg.rootRadius = Mathf.Max(0.001f, v); break;
                                case "tip":   wt.cfg.tipRadius  = Mathf.Max(0.001f, v); break;
                                case "damp":  wt.cfg.damping    = Mathf.Clamp(v, 0.80f, 0.999f); break;
                                case "grav":  wt.cfg.gravity    = v; break;
                                case "fric":  wt.cfg.groundFriction = Mathf.Clamp01(v); break;
                                case "spark": wt.cfg.sparks = v > 0.5f; break;
                            }
                            int n = wt.Rebuild();
                            // 이름 'c'는 case "spawn"의 out var c와 겹친다(switch 전체가 한 스코프).
                            var wc = wt.cfg;
                            Print($"길이 {wc.length:0.00}m · 마디 {wc.particles} · 굵기 {wc.rootRadius:0.000}→{wc.tipRadius:0.000} · " +
                                  $"감쇠 {wc.damping:0.000} · 중력 {wc.gravity:0.0} · 마찰 {wc.groundFriction:0.00} · 스파크 {(wc.sparks ? "켬" : "끔")}" +
                                  (n > 0 ? $"  (전선 {n}개 갱신)" : ""));
                            break;
                        }
                        default:
                        {
                            wt.boneName = p[1];
                            int n = wt.Attach();
                            Print(n > 0 ? $"'{wt.boneName}' 본에 전선 {n}개 부착 — 길이 {wt.cfg.length:0.00}m"
                                        : $"'{p[1]}' 본을 못 찾았습니다. wire bones 로 목록을 확인하십시오.");
                            break;
                        }
                    }
                    break;
                }

                // ── 충돌 캡슐 표시 (모델 크기 대조용) ──
                case "hitbox":
                {
                    var hb = HitboxView.Instance;
                    if (hb == null) { Print("HitboxView 없음"); break; }
                    string sub = p.Length >= 2 ? p[1].ToLowerInvariant() : "";
                    switch (sub)
                    {
                        case "":      hb.showBodies = !hb.showBodies; break;   // 인자 없으면 몸통 토글
                        case "on":    hb.showBodies = true;  break;
                        case "off":   hb.showBodies = false; hb.showCone = false; hb.showLunge = false; break;
                        case "body":  hb.showBodies = !hb.showBodies; break;
                        case "cone":                                          // 옛 이름 유지
                        case "atk":
                        case "melee": hb.showCone   = !hb.showCone;   break;
                        case "lunge":
                        case "thrust": hb.showLunge = !hb.showLunge;  break;
                        case "all":   hb.showBodies = hb.showCone = hb.showLunge = true; break;
                        case "xray":  hb.xray = !hb.xray; break;
                        default:
                            Print("사용: hitbox [on|off|body|melee|lunge|all|xray]");
                            Print("  body=몹·플레이어 캡슐 · melee=평타 판정 · lunge=찌르기 사거리 · xray=모델 뚫고 보기");
                            break;
                    }
                    Print("히트박스 — " + hb.Status);
                    if (hb.showBodies)
                        Print("  초록=몹 · 파랑=플레이어 · 노랑=처형가능   (Sim 실제 판정 크기)");
                    if (hb.showCone)
                        Print(CombatConfig.UseSphereMelee
                            ? $"  평타=빨간 구 · 눈앞 {CombatConfig.MeleeOffset:0.00}m에 반지름 {CombatConfig.MeleeRadius:0.00}m" +
                              $" (실효 {CombatConfig.MeleeReach:0.00}m)\n  판정 틱에만 진해집니다 — 흐리면 아직 판정 전"
                            : $"  평타=빨간 부채꼴 · {CombatConfig.AttackConeRange:0.00}m / 총 {CombatConfig.AttackConeHalfAngle * 2f:0}°");
                    if (hb.showLunge)
                        Print($"  찌르기=바닥 원 · 주황({CombatConfig.LungeMinRange:0.00}m 안쪽은 발동 안 함)" +
                              $" · 하늘색(최대 {CombatConfig.LungeMaxRange:0.00}m) · 선=조준 방향");
                    break;
                }

                // ── 몹 시선 추적 (절차 애니메이션 1단계) ──
                case "look":
                {
                    if (p.Length >= 2 && p[1] == "off")     EntityViews.LookAtEnabled = false;
                    else if (p.Length >= 2 && p[1] == "on") EntityViews.LookAtEnabled = true;
                    else if (p.Length >= 3 && float.TryParse(p[2], out float lv))
                    {
                        ref var bs = ref EntityViews.BipedLook;
                        ref var cs = ref EntityViews.ChargeLook;
                        switch (p[1])
                        {
                            case "yaw":   bs.maxYaw = lv;    cs.maxYaw = lv * 0.65f;  break;
                            case "pitch": bs.maxPitch = lv;  cs.maxPitch = lv * 0.7f; break;
                            case "speed": bs.turnSpeed = lv; cs.turnSpeed = lv * 0.6f; break;
                            case "weight": bs.weight = Mathf.Clamp01(lv); cs.weight = Mathf.Clamp01(lv); break;
                            default: Print("항목: yaw pitch speed weight"); break;
                        }
                    }
                    else if (p.Length >= 2) { Print("사용: look [on|off] · look <yaw|pitch|speed|weight> <값>"); break; }
                    else EntityViews.LookAtEnabled = !EntityViews.LookAtEnabled;

                    var b = EntityViews.BipedLook;
                    Print($"몹 시선 추적 {(EntityViews.LookAtEnabled ? "켬" : "끔")} — " +
                          $"좌우 ±{b.maxYaw:0}° 상하 ±{b.maxPitch:0}° 속도 {b.turnSpeed:0}°/s 가중치 {b.weight:0.00}\n" +
                          "  돌진몹은 더 좁고 느리게 자동 적용됩니다.");
                    break;
                }

                // ── 찌르기 자유 시전 (몹 없이 애니메이션 확인) ──
                case "lunge":
                {
                    if (p.Length >= 2 && float.TryParse(p[1], out float ldist))
                    {
                        CombatConfig.DevLungeBlinkDist = Mathf.Max(0f, ldist);
                        Print($"찌르기 전진 거리 = {CombatConfig.DevLungeBlinkDist:0.0}m (0=제자리)");
                        break;
                    }
                    if (p.Length >= 2 && p[1] == "off")      CombatConfig.DevLungeFree = false;
                    else if (p.Length >= 2 && p[1] == "on")  CombatConfig.DevLungeFree = true;
                    else if (p.Length >= 2) { Print("사용: lunge [on|off|<거리m>]"); break; }
                    else CombatConfig.DevLungeFree = !CombatConfig.DevLungeFree;

                    Print($"찌르기 자유 시전 {(CombatConfig.DevLungeFree ? "켬" : "끔")}" +
                          (CombatConfig.DevLungeFree
                            ? $" — 몹 없어도 발동 · 스택/쿨 무시 · 전진 {CombatConfig.DevLungeBlinkDist:0.0}m\n" +
                              "  ★ Sim 동작이 바뀌므로 예지 결과도 달라집니다. 테스트용으로만 쓰십시오."
                            : ""));
                    break;
                }

                // ── 포즈 JSON 재생 (클립 굽기 전 미리보기) ──
                case "pose":
                {
                    var pp = PosePlayer.Instance;
                    if (pp == null) { Print("PosePlayer 없음"); break; }
                    if (p.Length < 2 || p[1] == "list")
                    {
                        var names = PosePlayer.ListPoses();
                        if (names.Count == 0) Print("포즈 없음 — Tools/뷰모델/포즈 JSON 으로 저장하십시오");
                        else { Print("포즈 " + names.Count + "개:"); foreach (var n in names) Print("  " + n); }
                        break;
                    }
                    Print(pp.ApplySingle(p[1]) ? "포즈 적용: " + p[1] : "그런 포즈 없음: " + p[1]);
                    break;
                }

                case "vcam":
                {
                    // 뷰모델 카메라 — 후퇴량·근평면을 숫자로 직접 지정
                    var vcm = ViewmodelCamera.Instance;
                    if (vcm == null) { Print("ViewmodelCamera 없음"); break; }

                    if (p.Length >= 2)
                    {
                        if (p[1] == "auto")
                        {
                            Print(vcm.AutoFitPullBack()
                                ? $"후퇴량 자동 설정 → {vcm.pullBack:0.000}m"
                                : "측정 실패 — 뷰모델이 없습니다");
                            break;
                        }
                        if (p[1] == "layers") { vcm.RefreshLayers(); Print("레이어 다시 입힘"); break; }
                        if (p[1] == "save")
                        { Print(vcm.Save() ? "저장 완료 — 다음 Play에도 적용됩니다" : "저장 실패"); break; }
                        if (p[1] == "default" || p[1] == "reset")
                        {
                            vcm.ResetToDefaults();
                            Print($"권장 기본값 적용 — 근평면 {vcm.nearClip:0.000} · 후퇴 {vcm.pullBack:0.00}m\n" +
                                  "  씬에 저장하려면 Inspector에서 확인 후 씬을 저장하십시오.");
                            break;
                        }
                        if (p[1] == "back" && p.Length >= 3 && float.TryParse(p[2], out float pb))
                        { vcm.pullBack = Mathf.Clamp(pb, 0f, 3f); Print($"후퇴량 = {vcm.pullBack:0.000}m"); break; }
                        if (p[1] == "near" && p.Length >= 3 && float.TryParse(p[2], out float ncv))
                        { vcm.nearClip = Mathf.Clamp(ncv, 0.001f, 0.5f); Print($"근평면 = {vcm.nearClip:0.000}"); break; }
                    }

                    float z = vcm.NearestZ();
                    Print($"{vcm.Status}\n" +
                          $"  후퇴량 {vcm.pullBack:0.000}m · 근평면 {vcm.nearClip:0.000}\n" +
                          $"  최근접 깊이 {(float.IsNaN(z) ? "측정불가" : z.ToString("0.000") + "m" + (z < 0f ? " (카메라 뒤)" : ""))}\n" +
                          $"  레이어 안 맞는 렌더러 {vcm.CountWrongLayer()}개\n" +
                          "  사용: vcam auto · vcam back 0.5 · vcam near 0.01 · vcam layers");
                    break;
                }

                case "vm":
                {
                    // 뷰모델 소환·재생성 — 어느 씬에서든 Resources 프리팹으로 만든다
                    var sv = SwordView.Instance;
                    if (sv == null) { Print("SwordView 없음"); break; }

                    if (p.Length >= 2 && p[1] == "info")
                    {
                        var cam0 = main.Cam;
                        var cur = cam0 != null ? cam0.transform.Find("KatanaViewmodel") : null;
                        if (cur == null) cur = GameObject.Find("KatanaViewmodel")?.transform;
                        Print(cur != null
                            ? $"뷰모델 있음 — 오브젝트 {cur.GetComponentsInChildren<Transform>(true).Length}개\n" +
                              $"  루트 localPos {cur.localPosition} · 부모 {(cur.parent != null ? cur.parent.name : "없음")}"
                            : "뷰모델 없음 — vm 으로 소환하십시오");
                        var vcam = ViewmodelCamera.Instance;
                        if (vcam != null) Print("  카메라: " + vcam.Status);
                        break;
                    }

                    if (!sv.enabled) { sv.enabled = true; Print("SwordView 다시 켬"); }
                    Print(sv.RebuildViewmodel()
                        ? "뷰모델 재생성 요청 — Resources/KatanaViewmodel 에서 다음 프레임에 생성됩니다.\n" +
                          "  씬 튜닝이 반영되지 않으면 Tools/뷰모델/③ 으로 프리팹에 저장하십시오."
                        : "실패 — Main 또는 카메라가 없습니다");
                    break;
                }

                case "fx":
                {
                    // 베기 이펙트 — fx 평타1 / fx 평타2 / fx 찌르기 / fx off|on
                    var sf = SlashFxDriver.Instance;
                    if (sf == null) { Print("SlashFxDriver 없음"); break; }
                    if (p.Length < 2)
                    {
                        Print("사용: fx <평타1|평타2|찌르기> · fx off|on   (각도 조절은 F4)");
                        break;
                    }
                    if (p[1] == "off")  { sf.active = false; Print("베기 이펙트 끔"); break; }
                    if (p[1] == "on")   { sf.active = true;  Print("베기 이펙트 켬"); break; }
                    if (p[1] == "save") { Print(sf.Save() ? "저장 완료 — 다음 Play에도 적용됩니다" : "저장 실패"); break; }
                    if (p[1] == "burst")
                    {
                        float bd = 2.4f;
                        if (p.Length >= 3) float.TryParse(p[2], out bd);
                        var bc = main.Cam != null ? main.Cam : Camera.main;
                        if (bc == null) { Print("카메라 없음"); break; }
                        sf.BurstAt(bc.transform.position + bc.transform.forward * bd);
                        Print($"피격 버스트 — 카메라 앞 {bd:0.0}m  (갈래 수·퍼짐은 F5 버스트 탭)");
                        break;
                    }
                    var slot = sf.Find(p[1]);
                    if (slot == null) { Print("그런 슬롯 없음: " + p[1] + "  (평타1 / 평타2 / 찌르기)"); break; }

                    // fx <슬롯> follow <0~3>  — 0 월드 · 1 카메라 · 2 지연 · 3 단계
                    if (p.Length >= 4 && p[2] == "follow" && int.TryParse(p[3], out int fmode))
                    {
                        slot.follow = Mathf.Clamp(fmode, 0, 3);
                        string[] fn = { "월드 고정", "카메라 고정", "지연 추종", "단계 전환" };
                        Print($"{slot.name} 추종 = {fn[slot.follow]}  (저장하려면 fx save)");
                        break;
                    }
                    sf.SpawnNow(slot);
                    Print($"이펙트: {slot.name}  roll {slot.roll:0}° pitch {slot.pitch:0}° yaw {slot.yaw:0}° · F4에서 조절");
                    break;
                }

                case "thrust":
                {
                    // 찌르기 연출 — 둠식/예전 전환 + 세부 튜닝
                    var cc = CombatCamera.Instance;
                    if (p.Length >= 2)
                    {
                        string a = p[1].ToLowerInvariant();
                        if (a == "doom" || a == "old" || a == "legacy")
                        {
                            bool doom = a == "doom";
                            CombatConfig.LungeDoomStyle = doom;              // Sim — 예지 영향
                            if (cc != null) cc.doomStyle = doom;             // View — 예지 무해
                            Print($"찌르기 = {(doom ? "둠식(돌진 8틱 ease-out)" : "예전(블링크 3틱 등속)")}\n" +
                                  "  ★ 이동 틱은 Sim 값이라 예지 결과가 함께 바뀝니다.");
                            break;
                        }
                        if (cc != null && p.Length >= 3 && float.TryParse(p[2], out float tv))
                        {
                            switch (a)
                            {
                                case "aim":     cc.aimHeightRatio = Mathf.Clamp(tv, 0.2f, 1.2f); break;
                                case "pitch":   cc.pitchWeight    = Mathf.Clamp01(tv);           break;
                                case "limit":   cc.pitchDownLimit = tv;                          break;
                                case "rate":    cc.enterRate      = Mathf.Max(1f, tv);           break;
                                case "restore": cc.exitRestore    = Mathf.Max(0f, tv);           break;
                                case "ticks":   CombatConfig.LungeTravelTicksDoom = Mathf.Max(1, (int)tv); break;
                                default: Print("모르는 항목: " + a); break;
                            }
                        }
                    }
                    Print($"이동 {CombatConfig.LungeTravel}틱 ({CombatConfig.LungeTravel * SimConfig.TickDelta:0.000}초)" +
                          $" · 히트스톱 {CombatConfig.LungeHitStopTicks}틱 · FOV킥 {CombatConfig.LungeFovKick:0}°\n" +
                          (cc != null ? "  카메라: " + cc.Status : "  CombatCamera 없음") + "\n" +
                          "  사용: thrust doom|old · thrust aim 0.85 · thrust pitch 0.35 · thrust limit 20\n" +
                          "        thrust rate 55 · thrust restore 0.18 · thrust ticks 8");
                    break;
                }

                case "combat":
                {
                    // 전투 수치 저장/불러오기 (F1·F6에서 맞춘 값)
                    if (p.Length >= 2 && p[1] == "save")
                    { Print(CombatTuningSave.Save() ? "전투 수치 저장 — 다음 Play에 자동 적용" : "저장 실패"); break; }
                    if (p.Length >= 2 && p[1] == "load")
                    { Print(CombatTuningSave.Load() ? "저장값 불러옴" : "저장 파일 없음"); break; }
                    if (p.Length >= 2 && p[1] == "delete")
                    { CombatTuningSave.Delete(); Print("저장 파일 삭제 — 다음 Play는 코드 기본값"); break; }

                    Print($"평타1 {CombatConfig.Atk1WindupTicks}/{CombatConfig.Atk1ActiveTicks}/{CombatConfig.Atk1RecoveryTicks} (선딜/판정/후딜)\n" +
                          $"평타2 {CombatConfig.Atk2WindupTicks}/{CombatConfig.Atk2ActiveTicks}/{CombatConfig.Atk2RecoveryTicks}\n" +
                          $"콤보창 {CombatConfig.ComboWindowTicks}틱 · 사거리 {CombatConfig.AttackConeRange:0.00}m · 반각 {CombatConfig.AttackConeHalfAngle:0}°\n" +
                          $"저장 파일 {(CombatTuningSave.Exists ? "있음" : "없음")}  ·  사용: combat save|load|delete");
                    break;
                }

                case "ui":
                {
                    // 게임 UI(HP바 등) 숨기기. 개발 패널(콘솔·F1~F3)은 그대로 둔다.
                    if      (p.Length >= 2 && (p[1] == "off"  || p[1] == "hide")) UiVisibility.Set(true);
                    else if (p.Length >= 2 && (p[1] == "on"   || p[1] == "show")) UiVisibility.Set(false);
                    else UiVisibility.Toggle();
                    Print($"게임 UI {(UiVisibility.Hidden ? "숨김" : "표시")}  (HP·스태미나·예측 표시)\n" +
                          "  개발 패널(콘솔·F1·F2·F3)은 영향 없음");
                    break;
                }

                case "bind":
                {
                    // 실제 전투 연동 on/off (좌클릭=slash1·2 번갈아, 우클릭=thrust)
                    var dv = PoseCombatDriver.Instance;
                    if (dv == null) { Print("PoseCombatDriver 없음"); break; }
                    if (p.Length >= 2)
                    {
                        if      (p[1] == "on")  dv.active = true;
                        else if (p[1] == "off") dv.active = false;
                        else if (float.TryParse(p[1], out float bt)) dv.segTime = Mathf.Max(0.02f, bt);
                    }
                    else dv.active = !dv.active;
                    dv.ResetCombo();
                    Print($"전투 연동 {(dv.active ? "켬" : "끔")} · 포즈당 {dv.segTime:0.00}초 (저장된 타이밍이 있으면 그쪽 우선)\n" +
                          "  평타 = slash1 ↔ slash2 번갈아 · 찌르기 = thrust1 · 실제 공격 입력에 반응");
                    break;
                }

                case "seq":
                {
                    // 정확한 포즈 이름들을 순서대로(중간 건너뛰기). 예) seq 평타1_0 평타1_1 평타1_4 loop
                    var pp = PosePlayer.Instance;
                    if (pp == null) { Print("PosePlayer 없음"); break; }
                    if (p.Length < 3) { Print("사용: seq <포즈이름> <포즈이름> [...] [loop]   예) seq 평타1_0 평타1_1 평타1_4"); break; }
                    var names = new System.Collections.Generic.List<string>();
                    bool slp = false;
                    for (int si = 1; si < p.Length; si++)
                    {
                        if (p[si] == "loop")   { slp = true; continue; }
                        if (p[si] == "linear") { pp.springBetweenPoses = false; continue; }
                        if (p[si] == "spring") { pp.springBetweenPoses = true;  continue; }
                        if (float.TryParse(p[si], out float sdv)) { pp.segTime = Mathf.Max(0.05f, sdv); continue; }
                        names.Add(p[si]);
                    }
                    int sn = pp.PlayNames(names, slp);
                    Print(sn >= 2 ? "재생: " + pp.SequenceSummary + (slp ? " (반복)" : "") + "\n  간격은 F3 패널에서 각각 조절"
                                  : "포즈를 2개 이상 찾지 못했습니다 (pose list 로 이름 확인)");
                    break;
                }

                case "combo":
                {
                    // 여러 접두어를 이어서 재생. 예) combo 평타1_ 평타2_ 0.35 loop
                    var pp = PosePlayer.Instance;
                    if (pp == null) { Print("PosePlayer 없음"); break; }
                    if (p.Length < 3) { Print("사용: combo <접두어1> <접두어2> [...] [초] [loop]   예) combo 평타1_ 평타2_ 0.35 loop"); break; }

                    var prefixes = new System.Collections.Generic.List<string>();
                    float cdur = 0.4f; bool clp = false;
                    for (int ci = 1; ci < p.Length; ci++)
                    {
                        if (p[ci] == "loop")   { clp = true; continue; }
                        if (p[ci] == "linear") { pp.springBetweenPoses = false; continue; }
                        if (p[ci] == "spring") { pp.springBetweenPoses = true;  continue; }
                        if (float.TryParse(p[ci], out float dv)) { cdur = dv; continue; }
                        prefixes.Add(p[ci]);
                    }
                    if (prefixes.Count < 1) { Print("접두어가 없습니다"); break; }
                    int cn = pp.Play(prefixes.ToArray(), cdur, clp);
                    Print(cn >= 2 ? "콤보 재생: " + pp.SequenceSummary + (clp ? " (반복)" : "")
                                  : "포즈를 못 찾았습니다");
                    break;
                }

                case "play":
                {
                    var pp = PosePlayer.Instance;
                    if (pp == null) { Print("PosePlayer 없음"); break; }
                    if (p.Length >= 2 && p[1] == "stop")    { pp.Stop();    Print("재생 중지 — 기본포즈로"); break; }
                    if (p.Length >= 2 && p[1] == "release") { pp.Release(); Print("포즈 제어 해제 — Animator·IK·SwordView 복구"); break; }
                    if (p.Length < 2) { Print("사용: play <접두어> [초] [loop] [linear|spring] · play stop · play release   예) play slash1_ 0.2 linear"); break; }
                    float dur = pp.segTime; bool lp = false;
                    for (int pi = 2; pi < p.Length; pi++)
                    {
                        if (p[pi] == "loop")   { lp = true; continue; }
                        if (p[pi] == "linear") { pp.springBetweenPoses = false; continue; }
                        if (p[pi] == "spring") { pp.springBetweenPoses = true;  continue; }
                        if (float.TryParse(p[pi], out float dv)) dur = dv;
                    }
                    int n = pp.Play(p[1], dur, lp);
                    Print(n >= 2 ? "재생: " + pp.SequenceSummary + (lp ? " (반복)" : "") + (pp.springBetweenPoses ? " [스프링]" : " [선형]")
                                 : $"포즈가 2개 미만입니다 ({p[1]}* 로 시작하는 포즈를 확인하십시오)");
                    break;
                }

                // ── 절차 애니메이션 테스트 (전투 없이 즉시 발동) ──
                case "proc":
                {
                    var sv = SwordView.Instance;
                    var fp = FingerPoser.Instance;
                    string sub = p.Length >= 2 ? p[1].ToLowerInvariant() : "";
                    float arg = 0f;
                    bool hasArg = p.Length >= 3 && float.TryParse(p[2], out arg);

                    switch (sub)
                    {
                        case "land":
                            if (sv == null) { Print("SwordView 없음"); break; }
                            sv.KickLand(hasArg ? arg : 8f);
                            Print("착지 딥 발동 (강도 " + (hasArg ? arg : 8f) + ")");
                            break;

                        case "hit":
                            if (sv == null) { Print("SwordView 없음"); break; }
                            sv.KickHit();
                            Print("피격 움찔 발동");
                            break;

                        case "breathe":
                            if (sv == null) { Print("SwordView 없음"); break; }
                            sv.breatheHpOverride = hasArg ? Mathf.Clamp01(arg) : -1f;
                            Print(hasArg ? $"숨고르기 HP={arg:0.00} 로 강제 (0=빈사, 1=멀쩡)" : "숨고르기 실제 HP로 복귀");
                            break;

                        case "grip":
                            if (fp == null) { Print("FingerPoser 없음 — 손 뼈에 붙이십시오"); break; }
                            fp.grip = hasArg ? Mathf.Clamp01(arg) : 0f;
                            Print("손가락 그립 = " + fp.grip.ToString("0.00"));
                            break;

                        case "pulse":
                            if (fp == null) { Print("FingerPoser 없음 — 손 뼈에 붙이십시오"); break; }
                            fp.PulseGrip(hasArg ? arg : 0.5f);
                            Print("손가락 확 쥠 (세기 " + (hasArg ? arg : 0.5f) + ")");
                            break;

                        case "reset":
                            if (sv != null) sv.ResetProcedural();
                            if (fp != null) { fp.ResetGrip(); fp.grip = 0f; }
                            Print("절차 오프셋 리셋");
                            break;

                        // ── 레이어 on/off ──
                        case "layer":
                        {
                            var vm = ViewmodelMotion.Instance;
                            if (vm == null) { Print("ViewmodelMotion 없음"); break; }
                            if (p.Length < 3)
                            {
                                Print($"숨={vm.enableBreathe} 달리기={vm.enableBob} 기울임={vm.enableStrafe} 공중={vm.enableAir}");
                                Print($"착지={vm.enableLand} 피격={vm.enableHit} 시선={vm.enableSway}");
                                Print("사용: proc layer <이름> [0|1]   이름: breathe bob strafe air land hit sway all");
                                break;
                            }
                            bool on = p.Length < 4 || p[3] != "0";
                            switch (p[2].ToLowerInvariant())
                            {
                                case "breathe": vm.enableBreathe = on; break;
                                case "bob":     vm.enableBob     = on; break;
                                case "strafe":  vm.enableStrafe  = on; break;
                                case "air":     vm.enableAir     = on; break;
                                case "land":    vm.enableLand    = on; break;
                                case "hit":     vm.enableHit     = on; break;
                                case "sway":    vm.enableSway    = on; break;
                                case "all":
                                    vm.enableBreathe = vm.enableBob = vm.enableStrafe =
                                    vm.enableAir = vm.enableLand = vm.enableHit = vm.enableSway = on;
                                    break;
                                default: Print("모르는 레이어: " + p[2]); break;
                            }
                            Print($"레이어 {p[2]} = {(on ? "켜짐" : "꺼짐")}");
                            break;
                        }

                        default:
                            Print("사용: proc land [강도] · proc hit · proc breathe [0~1]");
                            Print("      proc grip [0~1] · proc pulse [세기] · proc reset");
                            Print("      proc layer [이름] [0|1]   (breathe bob strafe air land hit sway all)");
                            Print("  ★ 전체 튜닝은 F4 패널에서 실시간 조절");
                            break;
                    }
                    break;
                }

                // ── 이펙트 튜닝용 ──
                case "swing":
                {
                    var sv = SwordView.Instance;
                    if (sv == null) { Print("SwordView 없음"); break; }
                    if (p.Length >= 2 && p[1] == "stop") { sv.StopPreview(); Print("스윙 프리뷰 중지"); break; }
                    if (p.Length < 2) { Print("사용: swing 1|2|t [loop] · swing stop"); break; }
                    bool loop = p.Length >= 3 && p[2] == "loop";
                    if (sv.PreviewSwing(p[1], loop)) Print("스윙 재생: " + p[1] + (loop ? " (반복)" : ""));
                    else Print("사용: swing 1|2|t [loop]");
                    break;
                }

                case "slash":
                {
                    // 평타1/평타2/찌르기 매핑 + 위→아래(down) 프리셋. 바꾸려면 여기만 수정.
                    // 기본 방향 보정(pitch 90) 위에서 roll로 화면상 베는 각도를 정한다.
                    string vname; float spitch = VfxBasePitch, syaw = 0f, sroll = 0f, sup = 0f;
                    switch (p.Length >= 2 ? p[1] : "")
                    {
                        case "1":    vname = "Slash_Basic";  sroll = -45f; break;   // 우상 → 좌하
                        case "2":    vname = "Slash_Double"; sroll =  45f; break;   // 좌상 → 우하
                        case "t": case "thrust": vname = "Slash_Multi"; sroll = 0f; break;   // 수평
                        case "down": vname = "Slash_Basic";  sroll = 90f; sup = 0.35f; break; // 위 → 아래
                        default:
                            Print("사용: slash 1|2|t|down [거리] [pitch] [yaw] [roll] [상하]");
                            Print("  1=우상→좌하 · 2=좌상→우하 · t=수평 · down=위→아래");
                            Print("  roll이 화면상 각도(0=수평, 90=수직). pitch 90은 이 에셋의 기본 보정");
                            vname = null; break;
                    }
                    if (vname == null) break;

                    float sd = 2.2f;
                    if (p.Length >= 3) float.TryParse(p[2], out sd);
                    if (p.Length >= 4) float.TryParse(p[3], out spitch);
                    if (p.Length >= 5) float.TryParse(p[4], out syaw);
                    if (p.Length >= 6) float.TryParse(p[5], out sroll);
                    if (p.Length >= 7) float.TryParse(p[6], out sup);

                    Print(SpawnVfxInView(main, vname, sd, spitch, syaw, sroll, sup)
                        ? "베기: " + p[1] + " → " + vname + "  pitch=" + spitch + " yaw=" + syaw + " roll=" + sroll
                        : "프리팹 없음: " + vname + " (vfx list 로 확인)");
                    break;
                }

                case "cut":
                {
                    // System.Object / UnityEngine.Object 이름 충돌(using System + using UnityEngine) → 명시
                    var cm = UnityEngine.Object.FindFirstObjectByType<CutsceneManager>();
                    if (cm == null) { Print("CutsceneManager 없음 — Tools/컷신/① 리그 설치 필요"); break; }
                    if (p.Length >= 2 && p[1] == "stop") { cm.StopFromConsole(); Print("컷신 중단"); break; }
                    Print(cm.PlayFromConsole() ? "컷신 재생" : "재생 불가 (이미 재생 중이거나 리그 미설치)");
                    break;
                }

                case "intro":
                {
                    // 등장 컷신은 "새 게임"에서만 뜬다 — 손보는 동안 매번 타이틀을 거치지 않게 재생 훅을 연다.
                    if (p.Length >= 2 && p[1] == "off") { IntroStyle.PlayOnNewGame = false; Print("등장 컷신 off"); break; }
                    if (p.Length >= 2 && p[1] == "on")  { IntroStyle.PlayOnNewGame = true;  Print("등장 컷신 on");  break; }
                    if (IntroCutscene.Instance != null) { Print("이미 재생 중"); break; }
                    IntroCutscene.Play();
                    Print("등장 컷신 재생 (ENTER 건너뛰기)");
                    break;
                }

                case "bossend":
                case "ending":
                {
                    // 보스 처치 엔딩(암전→원래모습 노출→슬로모 폭발)을 즉시 재생. 보스가 없어도 동작(테스트용).
                    // 'slow' 인자면 앞에 막타 슬로모 연출을 붙여 시연한다.
                    var dir = UnityEngine.Object.FindFirstObjectByType<BossDeathDirector>();
                    if (dir == null) { Print("BossDeathDirector 없음 — [BossDeathDirector] 오브젝트를 배치하십시오"); break; }
                    bool intro = p.Length >= 2 && (p[1] == "slow" || p[1] == "slowmo");
                    Print(dir.TriggerFromConsole(intro)
                        ? (intro ? "보스 엔딩 재생 (막타 슬로모 → 암전→노출→슬로모 폭발)"
                                 : "보스 엔딩 재생 (암전→원래모습 노출→슬로모 폭발)")
                        : "재생 불가 (이미 진행 중)");
                    break;
                }

                case "vfx":
                {
                    if (p.Length >= 2 && p[1] == "list")
                    {
                        var names = VfxLibrary.Names();
                        if (names.Count == 0) Print("VFX 없음 — Assets/_Project/Prefabs/Resources/VFX/ 에 프리팹을 넣으십시오");
                        else foreach (var n in names) Print("  " + n);
                        break;
                    }
                    if (p.Length >= 2 && p[1] == "reload") { VfxLibrary.Reload(); Print("VFX 폴더 다시 읽음"); break; }
                    if (p.Length < 2)
                    {
                        Print("사용: vfx <이름> [거리] [pitch] [yaw] [roll] [상하]");
                        Print("  기본 pitch=" + VfxBasePitch + " (이 에셋 팩 방향 보정). roll이 화면상 각도");
                        Print("  예) vfx Slash_Basic 2.2 90 0 90   ← 위→아래 수직");
                        Print("  vfx list · vfx reload");
                        break;
                    }

                    float d = 2.2f, vpitch = VfxBasePitch, vyaw = 0f, vroll = 0f, vup = 0f;
                    if (p.Length >= 3) float.TryParse(p[2], out d);
                    if (p.Length >= 4) float.TryParse(p[3], out vpitch);
                    if (p.Length >= 5) float.TryParse(p[4], out vyaw);
                    if (p.Length >= 6) float.TryParse(p[5], out vroll);
                    if (p.Length >= 7) float.TryParse(p[6], out vup);
                    Print(SpawnVfxInView(main, p[1], d, vpitch, vyaw, vroll, vup)
                        ? "재생: " + p[1] + "  pitch=" + vpitch + " yaw=" + vyaw + " roll=" + vroll + " 상하=" + vup
                        : "없는 VFX: " + p[1] + "  (vfx list 로 확인)");
                    break;
                }

                case "ts":
                case "timescale":
                    if (p.Length < 2) { Print("현재 timescale=" + Time.timeScale + " (사용: timescale 0.15)"); break; }
                    if (float.TryParse(p[1], out float tsv)) { Time.timeScale = Mathf.Clamp(tsv, 0f, 4f); Print("timescale=" + Time.timeScale); }
                    else Print("숫자를 입력하십시오");
                    break;

                default:
                    Print("모르는 명령: " + p[0]);
                    break;
            }
        }

        // ── 웨이브 명령 ──
        // 아레나를 씬에서 런타임에 찾으므로, 지금 테스트 씬이든 나중에 프리팹을 이어붙인
        // 최종 맵(아레나 여러 개)이든 그대로 동작한다. 여러 개면 플레이어에게 가장 가까운 아레나.
        void Wave(string[] p)
        {
            var arenas = UnityEngine.Object.FindObjectsByType<ArenaWaves>(FindObjectsSortMode.None);
            if (arenas.Length == 0) { Print("씬에 ArenaWaves가 없습니다. (Tools/맵 부속/테스트 웨이브 생성)"); return; }

            string sub = p.Length >= 2 ? p[1].ToLowerInvariant() : "status";

            if (sub == "list")
            {
                Print($"아레나 {arenas.Length}개:");
                for (int i = 0; i < arenas.Length; i++)
                {
                    ArenaWaves a = arenas[i];
                    int wc = a.waves != null ? a.waves.Length : 0;
                    var sb = new StringBuilder($"  [{i}] {a.name} — 웨이브 {wc}개");
                    for (int w = 0; w < wc; w++) sb.Append($" / W{w + 1}:{a.SpawnCountOf(w)}마리");
                    Print(sb.ToString());
                }
                return;
            }

            ArenaWaves target = Nearest(arenas);
            WaveRunner runner = target.GetComponent<WaveRunner>();
            if (runner == null) runner = target.gameObject.AddComponent<WaveRunner>();   // 없으면 자동 부착

            switch (sub)
            {
                // 번호는 1부터(표시 이름 W1·W2·W3과 일치). 내부 인덱스는 0부터라 -1 한다.
                case "start":
                {
                    int n = 1;
                    if (p.Length >= 3) int.TryParse(p[2], out n);
                    Print(runner.StartFrom(n - 1, true));
                    break;
                }
                case "only":
                case "go":
                {
                    if (p.Length < 3) { Print("사용: wave only <번호>  (1부터)"); break; }
                    int n; if (!int.TryParse(p[2], out n)) { Print("번호가 숫자가 아닙니다"); break; }
                    Print(runner.StartFrom(n - 1, false));
                    break;
                }
                case "stop":
                    runner.Stop();
                    Print($"[{target.name}] 웨이브 정지");
                    break;
                case "status":
                    Print(runner.Status());
                    break;
                default:
                    Print("사용: wave list | start [n] | only <n> | stop | status");
                    break;
            }
        }

        /// <summary>플레이어에게 가장 가까운 아레나(하나뿐이면 그것).</summary>
        static ArenaWaves Nearest(ArenaWaves[] arenas)
        {
            if (arenas.Length == 1 || Main.Instance == null) return arenas[0];
            Vector3 p = Main.Instance.World.player.pos;
            ArenaWaves best = arenas[0];
            float bestSq = float.MaxValue;
            foreach (ArenaWaves a in arenas)
            {
                float sq = (a.transform.position - p).sqrMagnitude;
                if (sq < bestSq) { bestSq = sq; best = a; }
            }
            return best;
        }

        /// <summary>
        /// 1인칭 시선 기준으로 VFX를 재생한다 — 실제 카메라 위치·회전(피치 포함) 앞에 내고,
        /// 카메라에 붙여 시점을 돌려도 화면에 남게 한다(테스트·튜닝 편의).
        /// </summary>
        /// <summary>
        /// 1인칭 시선 기준 VFX 재생. 회전은 카메라 기준 3축을 전부 노출한다
        /// (프리팹마다 authoring 방향이 달라 맞는 축을 직접 찾아야 함).
        ///   pitch = 카메라 오른쪽 축(X) 기준 — 위아래로 눕히기
        ///   yaw   = 카메라 위쪽 축(Y) 기준   — 좌우로 돌리기
        ///   roll  = 시선 축(Z) 기준          — 화면 안에서 각도만 바꾸기
        /// </summary>
        /// <summary>
        /// 이 슬래시 에셋 팩(Matthew Guz)의 기본 방향 보정.
        /// 프리팹이 눕혀진 채로 authoring돼 있어서 pitch 90을 줘야 화면을 가로지른다.
        /// 이 값을 기준으로 roll이 화면상 베는 각도가 된다(0=수평, 90=수직).
        /// </summary>
        public const float VfxBasePitch = 90f;

        static bool SpawnVfxInView(Main main, string name, float dist,
                                   float pitch = VfxBasePitch, float yaw = 0f, float roll = 0f,
                                   float up = 0f, float right = 0f)
        {
            var cam = main != null ? main.Cam : null;
            if (cam == null) return false;
            Transform ct = cam.transform;
            Vector3 pos = ct.position + ct.forward * dist + ct.up * up + ct.right * right;
            Quaternion rot = ct.rotation * Quaternion.Euler(pitch, yaw, roll);
            var inst = VfxLibrary.Play(name, pos, rot);
            if (inst == null) return false;
            inst.transform.SetParent(ct, true);
            return true;
        }

        static bool TryType(string alias, out CombatType c, out MobilityType m, out SizeClass s)
        {
            c = CombatType.Melee; m = MobilityType.Ground; s = SizeClass.Normal;
            if (!MapSpawnConfig.TryParse(alias, out var k)) return false;
            (c, m, s) = MapSpawnConfig.Axes(k);   // 종류 매핑은 MapSpawnConfig 한 곳에서
            return true;
        }

        void Print(string msg)
        {
            log.Append(msg).Append('\n');
            scroll.y = float.MaxValue;   // 항상 최신으로 스크롤
        }
    }
}
