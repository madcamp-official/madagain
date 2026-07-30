using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.View
{
    /// <summary>
    /// 개발용 커맨드 콘솔(Precog에서 되살림). <b>` 로 토글</b>, ESC로 닫기. IMGUI = 개발툴 표준.
    ///
    /// <para>Precog 원본은 몹 소환·예측 진단 등 지금 없는 시스템에 묶여 있어 그대로는 못 옮긴다.
    /// 껍데기(토글·로그·입력창·명령 분배)만 가져오고 명령은 MINDHEXER 것으로 새로 단다.</para>
    ///
    /// <para>열려 있는 동안 <b>플레이어 이동·해킹 입력을 막는다</b> — 안 그러면 명령을 타이핑하는 동안
    /// WASD로 걸어가고 Space로 해킹이 걸린다. 커서도 풀어 시점 회전이 자동으로 멈춘다.</para>
    /// </summary>
    public class DevConsole : MonoBehaviour
    {
        /// <summary>지금 콘솔이 열려 있는가. 다른 시스템이 입력을 막는 데 쓴다.</summary>
        public static bool Open { get; private set; }

        const string ControlName = "devconsole_input";
        const float Height = 240f;

        string _input = "";
        readonly StringBuilder _log = new StringBuilder();
        Vector2 _scroll;
        FirstPersonPlayer _fpp;
        bool _prevExternalMotion;

        // 도메인 리로드를 끈 환경 대비 — 이전 플레이의 열림 상태가 남지 않게.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void ResetStatics() { Open = false; }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;

            if (kb.backquoteKey.wasPressedThisFrame) { if (Open) Close(); else Show(); }
            else if (Open && kb.escapeKey.wasPressedThisFrame) Close();
        }

        void Show()
        {
            Open = true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            // 타이핑이 이동으로 새지 않게 본체를 멈춘다(시점은 커서 해제로 이미 멈춘다).
            if (_fpp == null) _fpp = Object.FindFirstObjectByType<FirstPersonPlayer>();
            if (_fpp != null) { _prevExternalMotion = _fpp.ExternalMotion; _fpp.ExternalMotion = true; }

            if (_log.Length == 0) Print("명령어: help");
        }

        void Close()
        {
            Open = false;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            if (_fpp != null) _fpp.ExternalMotion = _prevExternalMotion;
        }

        void OnGUI()
        {
            if (!Open) return;

            float w = Screen.width;
            GUI.Box(new Rect(0, 0, w, Height), GUIContent.none);

            _scroll = GUI.BeginScrollView(new Rect(6, 6, w - 12, Height - 38), _scroll,
                                          new Rect(0, 0, w - 40, 4000f));
            GUI.Label(new Rect(0, 0, w - 40, 4000f), _log.ToString());
            GUI.EndScrollView();

            Event e = Event.current;
            bool enter = e.type == EventType.KeyDown &&
                         (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter);

            GUI.SetNextControlName(ControlName);
            _input = GUI.TextField(new Rect(6, Height - 30, w - 12, 24), _input);
            _input = _input.Replace("`", "");   // 토글 키가 입력창에 들어가지 않게
            GUI.FocusControl(ControlName);      // 열자마자 바로 타이핑

            if (enter && !string.IsNullOrEmpty(_input.Trim()))
            {
                Execute(_input.Trim());
                _input = "";
                e.Use();
            }
        }

        // ── 명령 ──────────────────────────────────────────────────────────

        void Execute(string line)
        {
            Print("> " + line);
            string[] a = line.Split(' ');
            string cmd = a[0].ToLowerInvariant();

            switch (cmd)
            {
                case "help":
                    Print("  anim              — 경비병 애니메이션 목록(번호 표시)");
                    Print("  anim <이름|번호>   — 씬의 모든 경비병에게 강제 재생");
                    Print("  spawn guard [n]   — 시선 앞 바닥에 경비병 n기 배치(기본 1)");
                    Print("  kill guard        — 경비병 파괴(조각 흩어짐)");
                    Print("  guards            — 씬의 경비병 목록·위치");
                    Print("  clear guards      — 경비병 전부 제거");
                    Print("  clear             — 로그 지우기");
                    Print("  death             — 사망 연출(암전 1.5s→치지직 0.2s→기상 1.5s)");
                    Print("  intro             — 시작 인트로(치지직→기상). 사망 연출의 뒷부분");
                    Print("  wake              — 기상만(암전 없이). 흔들거림·상승·팔만 볼 때");
                    Print("  veil <검정> [치지직] — 덮개 값 직접 지정(0~1). veil 0 으로 걷음");
                    Print("  far [거리]         — 카메라 far 평면. 인자 없으면 현재값·렌더러 수 표시");
                    Print("  tp <번호>          — 체크포인트로 순간이동. 0=시작 1~3=스테이지1~3 4=보스 직전");
                    Print("  tp                — 체크포인트 번호 목록 표시");
                    break;

                case "spawn":
                    if (a.Length >= 2 && a[1].ToLowerInvariant() == "guard")
                    {
                        int cnt = 1;
                        if (a.Length >= 3) int.TryParse(a[2], out cnt);
                        SpawnGuards(Mathf.Clamp(cnt, 1, 20));
                    }
                    else Print("  사용: spawn guard [개수]");
                    break;

                case "kill":
                    if (a.Length >= 2 && a[1].ToLowerInvariant().StartsWith("guard")) KillGuards();
                    else Print("  사용: kill guard");
                    break;

                case "anim":
                    if (a.Length < 2) ListAnims();
                    else PlayAnim(a[1]);
                    break;

                case "guards":
                    ListGuards();
                    break;

                case "clear":
                    if (a.Length >= 2 && a[1].ToLowerInvariant().StartsWith("guard")) ClearGuards();
                    else _log.Length = 0;
                    break;

                case "death":
                    DeathSequence.Instance.Play();
                    Print("  사망 연출 재생. 콘솔을 닫아야(`) 화면이 보인다");
                    break;

                case "intro":
                    DeathSequence.Instance.PlayIntro();
                    Print("  인트로 재생. 콘솔을 닫아야(`) 화면이 보인다");
                    break;

                case "wake":
                {
                    // 기상만 따로 본다 — 암전·선 없이 눈높이·흔들림·팔만 확인할 때.
                    var w = FindAnyObjectByType<WakeUpSequence>();
                    if (w == null) w = DeathSequence.Instance.wakeUp;
                    if (w == null) { Print("  WakeUpSequence가 없습니다"); break; }
                    w.Play();
                    Print("  기상 연출 재생");
                    break;
                }

                case "veil":
                {
                    var v = DeathSequence.Instance.veil;
                    if (v == null) { Print("  ScreenVeil이 없습니다"); break; }
                    if (a.Length < 2) { Print("  사용: veil <검정 0~1> [선 0~1]"); break; }

                    float b, l = 0f;
                    if (!float.TryParse(a[1], out b)) { Print("  숫자를 넣으십시오"); break; }
                    if (a.Length >= 3) float.TryParse(a[2], out l);

                    v.black = Mathf.Clamp01(b);
                    v.glitch = Mathf.Clamp01(l);
                    Print($"  덮개 검정 {v.black:0.##} / 치지직 {v.glitch:0.##}");
                    break;
                }

                case "far":
                {
                    var cam = Camera.main;
                    if (cam == null) { Print("  Camera.main이 없습니다"); break; }

                    if (a.Length >= 2)
                    {
                        float f;
                        if (!float.TryParse(a[1], out f) || f <= 0f) { Print("  사용: far <거리>"); break; }
                        cam.farClipPlane = f;
                    }

                    // 지금 이 far로 몇 개가 살아남는지 — 컬링 효과를 눈이 아니라 숫자로 본다.
                    // Renderer.isVisible은 에디터가 백그라운드면 갱신이 멈추므로 거리로 직접 센다.
                    int total = 0, within = 0;
                    Vector3 eye = cam.transform.position;
                    float far2 = cam.farClipPlane * cam.farClipPlane;
                    foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                    {
                        if (!r.enabled) continue;
                        total++;
                        if ((r.bounds.ClosestPoint(eye) - eye).sqrMagnitude <= far2) within++;
                    }
                    Print($"  far = {cam.farClipPlane:0.#}m   범위 안 렌더러 {within} / {total}" +
                          $"  ({(total > 0 ? 100f * within / total : 0f):0.#}%)");
                    Print("  ★ 실제 부하는 Game 뷰 Stats의 Batches·Tris로 볼 것 — 이건 후보 개수일 뿐이다");
                    break;
                }

                case "tp":
                    if (a.Length < 2) { ListCheckpoints(); break; }
                    if (!int.TryParse(a[1], out int order)) { Print("  숫자를 넣으십시오 (tp 로 목록 확인)"); break; }
                    TeleportToCheckpoint(order);
                    break;

                default:
                    Print("  알 수 없는 명령: " + cmd + " (help)");
                    break;
            }
        }

        /// <summary>order가 일치하는 체크포인트로 순간이동. 부활 지점(RunCheckpoints.Current)은 안 건드린다 —
        /// 테스트용 이동일 뿐 저장 지점을 바꾸는 게 아니다.</summary>
        void TeleportToCheckpoint(int order)
        {
            Checkpoint best = null;
            foreach (var cp in FindObjectsByType<Checkpoint>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (cp.order == order) { best = cp; break; }

            if (best == null) { Print("  order=" + order + " 체크포인트를 찾지 못했습니다."); ListCheckpoints(); return; }

            RunCheckpoints.MoveTo(best);
            Print("  텔레포트: " + (string.IsNullOrEmpty(best.label) ? best.name : best.label) + " (#" + order + ")");
        }

        void ListCheckpoints()
        {
            var all = FindObjectsByType<Checkpoint>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            System.Array.Sort(all, (x, y) => x.order.CompareTo(y.order));
            if (all.Length == 0) { Print("  체크포인트가 없습니다."); return; }
            Print("  체크포인트 목록 (tp <번호>):");
            foreach (var cp in all)
                Print("   #" + cp.order + "  " + (string.IsNullOrEmpty(cp.label) ? cp.name : cp.label));
        }

        const string GuardPrefabPath = "Assets/_Project/Prefabs/Hackables/ViewEntry/Guard.prefab";

        /// <summary>
        /// 스폰 원본. 에디터에선 프리팹을 직접 읽고, 없으면 씬의 기존 경비병을 복제한다.
        /// (콘솔은 플레이 중에만 도는 개발 도구라 에디터 API 사용이 문제되지 않는다)
        /// </summary>
        GameObject GuardSource()
        {
#if UNITY_EDITOR
            var p = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(GuardPrefabPath);
            if (p != null) return p;
#endif
            foreach (var h in Object.FindObjectsByType<Hackable>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (h.kind == HackableKind.Guard) return h.gameObject;
            return null;
        }

        /// <summary>시선 앞 바닥에 경비병을 세운다. 발끝이 지면에 닿도록 렌더러 바운즈로 보정한다.</summary>
        void SpawnGuards(int count)
        {
            var src = GuardSource();
            if (src == null) { Print("  경비병 프리팹을 찾지 못했습니다: " + GuardPrefabPath); return; }

            Camera c = Camera.main != null ? Camera.main : Object.FindFirstObjectByType<Camera>();
            if (c == null) { Print("  카메라가 없습니다."); return; }

            Vector3 fwd = c.transform.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            Vector3 baseP = c.transform.position + fwd * 3f;

            int made = 0;
            for (int i = 0; i < count; i++)
            {
                // 가로로 벌려 세운다 — 여러 기를 겹치지 않게.
                Vector3 p = baseP + right * ((i - (count - 1) * 0.5f) * 1.2f);

                RaycastHit hit;
                if (Physics.Raycast(p + Vector3.up * 5f, Vector3.down, out hit, 30f, ~0, QueryTriggerInteraction.Ignore))
                    p = hit.point;

                var go = Object.Instantiate(src, p, Quaternion.LookRotation(-fwd, Vector3.up));
                go.name = "Guard_spawn" + (made + 1);
                go.SetActive(true);

                // 모델 원점이 발바닥과 정확히 일치하지 않을 수 있어 바운즈로 지면에 앉힌다.
                var rends = go.GetComponentsInChildren<Renderer>(true);
                if (rends.Length > 0)
                {
                    Bounds b = rends[0].bounds;
                    for (int k = 1; k < rends.Length; k++) b.Encapsulate(rends[k].bounds);
                    go.transform.position += Vector3.up * (p.y - b.min.y);
                }
                made++;
            }
            Print("  경비병 " + made + "기 배치 (앞 3m)");
        }

        /// <summary>씬의 경비병을 파괴 연출로 터뜨린다(§12.0 소규모 = 런타임 리지드바디).</summary>
        void KillGuards()
        {
            Camera c = Camera.main != null ? Camera.main : Object.FindFirstObjectByType<Camera>();
            int n = 0, noComp = 0;
            foreach (var h in Object.FindObjectsByType<Hackable>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (h.kind != HackableKind.Guard) continue;
                var d = h.GetComponent<GuardDestruction>();
                if (d == null) { noComp++; continue; }

                // 맞은 방향 = 카메라에서 대상 쪽. 조각이 시선 반대로 날아간다.
                Vector3 dir = c != null ? (h.transform.position - c.transform.position) : Vector3.zero;
                dir.y = 0f;
                d.Destruct(dir);
                n++;
            }
            Print("  파괴 " + n + "기" + (noComp > 0 ? " / GuardDestruction 없음 " + noComp + "기" : ""));
        }

        void ClearGuards()
        {
            int n = 0;
            foreach (var h in Object.FindObjectsByType<Hackable>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (h.kind != HackableKind.Guard) continue;
                Destroy(h.gameObject);
                n++;
            }
            Print("  경비병 " + n + "기 제거");
        }

        /// <summary>씬의 경비병들. 종류로 찾으므로 이름을 바꿔도 잡힌다.</summary>
        static List<Animator> FindGuardAnimators()
        {
            var list = new List<Animator>();
            foreach (var h in Object.FindObjectsByType<Hackable>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (h.kind != HackableKind.Guard) continue;
                var anim = h.GetComponentInChildren<Animator>(true);
                if (anim != null) list.Add(anim);
            }
            return list;
        }

        /// <summary>
        /// 컨트롤러에 들어 있는 클립 이름 목록. 런타임에는 상태(state) 목록을 직접 못 읽으므로
        /// <c>animationClips</c>로 대신한다 — 이 컨트롤러는 상태 이름 = 클립 이름이라 그대로 통한다.
        /// </summary>
        static List<string> ClipNames(Animator anim)
        {
            var names = new List<string>();
            var rac = anim != null ? anim.runtimeAnimatorController : null;
            if (rac == null) return names;
            foreach (var c in rac.animationClips)
                if (c != null && !names.Contains(c.name)) names.Add(c.name);
            return names;
        }

        void ListAnims()
        {
            var guards = FindGuardAnimators();
            if (guards.Count == 0) { Print("  경비병이 없습니다(또는 Animator 없음)."); return; }

            var names = ClipNames(guards[0]);
            if (names.Count == 0) { Print("  Animator에 컨트롤러가 없습니다."); return; }

            Print("  경비병 " + guards.Count + "명 / 컨트롤러 = " + guards[0].runtimeAnimatorController.name);
            for (int i = 0; i < names.Count; i++)
                Print("   [" + i + "] " + names[i]);
            Print("  사용: anim 3   또는   anim Idle_Crazy_Robot");
        }

        void PlayAnim(string arg)
        {
            var guards = FindGuardAnimators();
            if (guards.Count == 0) { Print("  경비병이 없습니다."); return; }

            var names = ClipNames(guards[0]);
            string state = arg;

            int idx;
            if (int.TryParse(arg, out idx))
            {
                if (idx < 0 || idx >= names.Count) { Print("  번호 범위 밖: " + idx); return; }
                state = names[idx];
            }

            int hash = Animator.StringToHash(state);
            int ok = 0, miss = 0;
            foreach (var anim in guards)
            {
                if (!anim.HasState(0, hash)) { miss++; continue; }
                anim.enabled = true;   // 평상시엔 꺼둬서 자동 재생이 없다 — 명령으로만 움직인다
                anim.Play(hash, 0, 0f);
                anim.speed = 1f;
                anim.Update(0f);   // 즉시 반영 — 정지 상태에서도 포즈가 바로 보이게
                ok++;
            }

            Print("  '" + state + "' 재생: " + ok + "명" + (miss > 0 ? " / 상태 없음 " + miss + "명" : ""));
            if (ok == 0) Print("  ※ 상태 이름이 컨트롤러에 없습니다. 'anim'으로 목록을 확인하십시오.");
        }

        void ListGuards()
        {
            var guards = FindGuardAnimators();
            Print("  경비병 " + guards.Count + "명");
            foreach (var anim in guards)
            {
                var t = anim.transform;
                Print("   " + t.name + "  pos " + t.position.ToString("0.0") +
                      "  rootMotion=" + (anim.applyRootMotion ? "on" : "off"));
            }
        }

        void Print(string s) { _log.Append(s).Append('\n'); }
    }

    /// <summary>Play 시 자동 부착 — 씬 세팅 없이 항상 쓸 수 있게(프로젝트 관례).</summary>
    public static class DevConsoleBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Object.FindFirstObjectByType<DevConsole>() == null)
                new GameObject("[DevConsole]").AddComponent<DevConsole>();
        }
    }
}
