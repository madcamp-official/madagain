using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.View
{
    /// <summary>
    /// F7 — 타이틀 화면 주인공 자세·구도 튜닝 패널. 타이틀이 떠 있는 동안만 열린다.
    ///
    /// 존재 이유: 자세는 <b>눈으로 보고 맞추는 값</b>인데, 코드 숫자를 고치면 컴파일을 기다려야
    /// 한다. 슬라이더로 즉시 반영해 보고, 마음에 들면 "값 콘솔에 출력"을 눌러 나온 블록을
    /// TitleActor.cs의 PoseValues에 그대로 붙여넣으면 된다(저장은 코드가 진실).
    ///
    /// 다른 튜닝 패널(F1~F6)과 같은 IMGUI 관례를 따르되, 타이틀 전용이라 DevPanels에는
    /// 등록하지 않는다 — 타이틀 중엔 Main이 꺼져 있어 게임 입력 충돌이 없다.
    /// </summary>
    public class TitleTunePanel : MonoBehaviour
    {
        /// <summary>열려 있는 동안 TitleScreen이 커서 숨김·메뉴 키 입력을 멈춘다.</summary>
        public static bool Open { get; private set; }

        Vector2 scroll;

        /// <summary>
        /// 패널 확대 배율. IMGUI는 화면 픽셀에 고정된 크기로 그려지는데, Game 뷰가 QHD이고
        /// 에디터에서 0.4배쯤으로 축소해 보면 기본 글씨가 화면상 7px도 안 돼 읽을 수가 없다.
        /// GUI.matrix로 패널 전체를 키운다(슬라이더 조작 좌표도 같이 변환되므로 드래그는 그대로).
        /// </summary>
        public static float Scale = 2f;

        void OnDisable() { Open = false; }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.f7Key.wasPressedThisFrame && TitleScreen.Instance != null)
            {
                Open = !Open;
                Cursor.visible = Open;
                Cursor.lockState = CursorLockMode.None;
            }
            // 타이틀이 사라지면(게임 시작) 패널도 조용히 닫힌다.
            if (Open && TitleScreen.Instance == null) { Open = false; Cursor.visible = false; }
        }

        void OnGUI()
        {
            if (!Open || TitleScreen.Instance == null) return;
            var actor = TitleScreen.Instance.Actor;

            // 배율 적용 — 이 아래의 좌표는 전부 "확대 전" 기준이다.
            Matrix4x4 prevMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one * Scale);

            const float W = 470f;
            float H = Mathf.Min(Screen.height / Scale - 24f, 660f);
            GUILayout.BeginArea(new Rect(12f, 12f, W, H), GUI.skin.box);

            GUILayout.BeginHorizontal();
            GUILayout.Label("<b>타이틀 주인공 튜닝 (F7)</b>", Head());
            GUILayout.FlexibleSpace();
            GUILayout.Label("글씨", Head(), GUILayout.Width(38f));
            if (GUILayout.Button("−", Btn(), GUILayout.Width(32f))) Scale = Mathf.Max(1f, Scale - 0.25f);
            if (GUILayout.Button("+", Btn(), GUILayout.Width(32f))) Scale = Mathf.Min(4f, Scale + 0.25f);
            GUILayout.EndHorizontal();
            GUILayout.Label("<size=12>슬라이더 = 즉시 반영 · 값은 Play 정지 시 사라짐</size>", Rich());

            if (actor == null)
            {
                GUILayout.Label("TitleActor 없음 (PlayerGhost 프리팹 확인)", Head());
                GUILayout.EndArea();
                GUI.matrix = prevMatrix;
                return;
            }

            scroll = GUILayout.BeginScrollView(scroll);

            GUILayout.Label("<b>구도</b>", Rich());
            bool camDirty = false;
            camDirty |= Slider("거리", ref TitleActor.CamDist, 2f, 8f);
            camDirty |= Slider("초점 높이", ref TitleActor.CamHeight, -0.5f, 2f);
            camDirty |= Slider("카메라 각", ref TitleActor.CamYaw, -90f, 90f);
            camDirty |= Slider("내려보기", ref TitleActor.CamPitch, -30f, 45f);
            camDirty |= Slider("FOV", ref TitleActor.CamFov, 15f, 70f);
            camDirty |= Slider("몸 방향", ref TitleActor.ActorYaw, 0f, 360f);
            if (camDirty) actor.ApplyFraming();

            Slider("골반 낙차(앉는 깊이)", ref TitleActor.KneelDrop, 0f, 1f);

            GUILayout.Label("<b>▸ 움직임</b>  <size=12>숨쉬기 · 흔들림 (0이면 정지)</size>", Head());
            Slider("숨쉬기 진폭", ref TitleActor.BreathAmount, 0f, 0.15f, true);
            Slider("숨쉬기 속도", ref TitleActor.BreathSpeed, 0.2f, 2f);
            Slider("몸 오르내림(m)", ref TitleActor.BreathBob, 0f, 0.03f, true);
            Slider("흔들림 진폭", ref TitleActor.SwayAmount, 0f, 0.12f, true);
            Slider("흔들림 속도", ref TitleActor.SwaySpeed, 0.1f, 1.5f);

            GUILayout.Label("<b>▸ 칼</b>  <size=12>각도 · 위치 · 크기</size>", Head());
            Slider("각도 X(끝 올리기/내리기)", ref TitleActor.BladeTilt.x, -180f, 180f, true);
            Slider("각도 Y(좌우 스윙)", ref TitleActor.BladeTilt.y, -180f, 180f, true);
            Slider("각도 Z(눕히기)", ref TitleActor.BladeTilt.z, -180f, 180f, true);
            Slider("위치 X(좌우)", ref TitleActor.BladeOffset.x, -0.5f, 0.5f, true);
            Slider("위치 Y(위아래)", ref TitleActor.BladeOffset.y, -0.5f, 0.5f, true);
            Slider("위치 Z(앞뒤·손잡이 밀기)", ref TitleActor.BladeOffset.z, -0.5f, 0.5f, true);
            Slider("크기 배수", ref TitleActor.BladeScale, 0.2f, 3f);

            GUILayout.Space(8f);
            GUILayout.Label("<b>자세 근육</b>  <size=12>−1 ~ +1 · 앞뒤 축은 <b>음수가 앞</b></size>", Head());
            for (int i = 0; i < TitleActor.PoseMuscles.Length; i++)
            {
                // 구획 제목 — 축이 47개라 이게 없으면 원하는 슬라이더를 못 찾는다.
                for (int s = 0; s < TitleActor.SectionAt.Length; s++)
                    if (TitleActor.SectionAt[s] == i)
                    {
                        GUILayout.Space(6f);
                        GUILayout.Label("<b>▸ " + TitleActor.SectionTitle[s] + "</b>", Head());
                    }
                Slider(Ko(TitleActor.PoseMuscles[i]), ref TitleActor.PoseValues[i], -1f, 1f, true);
            }

            GUILayout.EndScrollView();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("값 콘솔에 출력", Btn())) Dump();
            if (GUILayout.Button("닫기 (F7)", Btn())) { Open = false; Cursor.visible = false; }
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
            GUI.matrix = prevMatrix;
        }

        static bool Slider(string label, ref float v, float min, float max, bool zeroButton = false)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, Head(), GUILayout.Width(186f));
            // 슬라이더 손잡이를 잡기 쉽게 세로로 키운다 — 기본 높이는 확대해도 얇다.
            float nv = GUILayout.HorizontalSlider(v, min, max,
                GUILayout.ExpandWidth(true), GUILayout.Height(18f));
            GUILayout.Label(nv.ToString("+0.00;-0.00"), Head(), GUILayout.Width(46f));
            // 축 하나만 되돌리기 — 여러 축을 만지다 보면 어느 게 원인인지 지워보며 찾게 된다.
            if (zeroButton && GUILayout.Button("0", Btn(), GUILayout.Width(24f))) nv = 0f;
            GUILayout.EndHorizontal();
            bool changed = !Mathf.Approximately(nv, v);
            v = nv;
            return changed;
        }

        /// <summary>유니티 근육 이름을 한국어 축 이름으로. 못 찾으면 원문 그대로 둔다.</summary>
        static string Ko(string muscle)
        {
            string s = muscle;
            s = s.Replace("Left ", "왼 ").Replace("Right ", "오른 ");
            s = s.Replace("Upper Leg", "허벅지").Replace("Lower Leg", "정강이");
            s = s.Replace("Forearm", "팔뚝").Replace("Shoulder", "어깨");
            s = s.Replace("Arm", "팔").Replace("Foot", "발").Replace("Hand", "손");
            s = s.Replace("UpperChest", "윗가슴").Replace("Chest", "가슴");
            s = s.Replace("Spine", "허리").Replace("Neck", "목").Replace("Head", "머리");
            s = s.Replace("Front-Back", "앞뒤").Replace("Left-Right", "좌우");
            s = s.Replace("Down-Up", "위아래").Replace("Up-Down", "위아래");
            s = s.Replace("In-Out", "안팎").Replace("Twist", "비틀기");
            s = s.Replace("Stretch", "펴기").Replace("Nod", "끄덕").Replace("Tilt", "기울기").Replace("Turn", "돌리기");
            return s;
        }

        static GUIStyle head, btn;
        /// <summary>기본 라벨보다 큰 글씨 — 축소된 Game 뷰에서도 읽히게.</summary>
        static GUIStyle Head()
        {
            if (head == null)
                head = new GUIStyle(GUI.skin.label) { richText = true, fontSize = 14, wordWrap = false };
            return head;
        }

        /// <summary>버튼은 라벨 스타일을 쓰면 배경이 사라진다 — 버튼 스킨에서 글씨만 키운다.</summary>
        static GUIStyle Btn()
        {
            if (btn == null)
                btn = new GUIStyle(GUI.skin.button) { richText = true, fontSize = 14 };
            return btn;
        }

        /// <summary>현재 값 전부를 코드에 붙여넣을 수 있는 형태로 콘솔에 찍는다.</summary>
        static void Dump()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[타이틀 튜닝] 현재 값 — TitleActor.cs에 붙여넣기:");
            sb.AppendLine($"CamDist = {TitleActor.CamDist:0.00}f, CamHeight = {TitleActor.CamHeight:0.00}f, " +
                          $"CamYaw = {TitleActor.CamYaw:0.0}f, CamPitch = {TitleActor.CamPitch:0.0}f, CamFov = {TitleActor.CamFov:0.0}f");
            sb.AppendLine($"ActorYaw = {TitleActor.ActorYaw:0.0}f;  KneelDrop = {TitleActor.KneelDrop:0.00}f;");
            sb.AppendLine($"BladeTilt = new Vector3({TitleActor.BladeTilt.x:0.0}f, {TitleActor.BladeTilt.y:0.0}f, {TitleActor.BladeTilt.z:0.0}f);");
            sb.AppendLine($"BladeOffset = new Vector3({TitleActor.BladeOffset.x:0.000}f, {TitleActor.BladeOffset.y:0.000}f, {TitleActor.BladeOffset.z:0.000}f);  BladeScale = {TitleActor.BladeScale:0.00}f;");
            sb.AppendLine($"BreathAmount = {TitleActor.BreathAmount:0.000}f;  BreathSpeed = {TitleActor.BreathSpeed:0.00}f;  BreathBob = {TitleActor.BreathBob:0.000}f;");
            sb.AppendLine($"SwayAmount = {TitleActor.SwayAmount:0.000}f;  SwaySpeed = {TitleActor.SwaySpeed:0.00}f;");
            sb.AppendLine("PoseValues =");
            sb.AppendLine("{");
            for (int i = 0; i < TitleActor.PoseValues.Length; i++)
                sb.AppendLine($"    {TitleActor.PoseValues[i]:+0.00;-0.00}f,   // {TitleActor.PoseMuscles[i]}");
            sb.AppendLine("};");
            Debug.Log(sb.ToString());
        }

        static GUIStyle rich;
        static GUIStyle Rich()
        {
            if (rich == null) rich = new GUIStyle(GUI.skin.label) { richText = true };
            return rich;
        }
    }

    /// <summary>Play 시 자동 부착 — 다른 패널들과 같은 방식.</summary>
    public static class TitleTunePanelBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Object.FindFirstObjectByType<TitleTunePanel>() == null)
                new GameObject("[TitleTunePanel]").AddComponent<TitleTunePanel>();
        }
    }
}
