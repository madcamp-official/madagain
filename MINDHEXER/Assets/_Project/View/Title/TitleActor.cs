using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 타이틀 화면 오른쪽에 서는 <b>주인공 실물</b>. 2D 일러스트를 따로 그리지 않고
    /// 인게임 아바타(<c>PlayerGhost</c> 프리팹 — 후드 히어로 + 오른손 카타나)를 그대로 세운다.
    ///
    /// <para><b>왜 렌더 텍스처인가</b> — 타이틀 UI는 ScreenSpaceOverlay 캔버스라 무조건 3D보다
    /// 위에 그려진다. 캐릭터를 씬에 그냥 세우면 UI의 어두운 장막이 캐릭터까지 덮어버린다.
    /// 그래서 전용 카메라로 <b>투명 배경</b>에 캐릭터만 따로 찍어, 캔버스 안에서
    /// "장막 위 · 글자 아래"라는 정확한 순서에 끼워 넣는다.</para>
    ///
    /// <para><b>왜 멀리 떨어진 곳인가</b> — 리그를 아레나에서 5천 미터 아래에 둔다. 그러면
    /// 전용 카메라의 시야에 아레나 지형이 끼어들 여지가 아예 없다(레이어 마스크의 이중 안전장치).</para>
    ///
    /// <para><b>포즈</b>는 손으로 뼈를 돌리지 않고 <c>GhostLunge</c> 클립의 한 시점을 굽는다.
    /// 찌르기 준비 자세라 이미 낮게 웅크리고 칼을 앞으로 뻗은 모양이고, 사람이 만든 포즈라
    /// 뼈를 직접 돌려 만든 것보다 훨씬 자연스럽다. 그 시점을 아주 조금씩 앞뒤로 흔들면
    /// 정지 화면이 아니라 <b>숨 쉬는 사람</b>이 된다.</para>
    /// </summary>
    public class TitleActor : MonoBehaviour
    {
        /// <summary>캔버스가 이걸 RawImage로 띄운다.</summary>
        public RenderTexture Target { get; private set; }

        /// <summary>이 값들만 만지면 포즈·구도가 바뀐다(전부 실측 튜닝 대상).</summary>
        // ── 자세 ─────────────────────────────────────────────────────────
        // [자세 재작성, 2026-07-23] 레퍼런스의 "한쪽 무릎 꿇고 앉은" 자세는 어느 클립에도 없다.
        // 클립을 굽는 대신 <b>휴머노이드 근육 값</b>으로 직접 적는다.
        //
        // 왜 근육인가 — 뼈의 localRotation은 Mixamo 바인드 방향이 제각각이라 오일러 값이
        // 직관과 안 맞는다(어느 축이 무릎을 굽히는지 매번 렌더해 봐야 안다). 근육 축은
        // "왼쪽 다리 앞뒤" "왼쪽 다리 펴기"처럼 <b>의미로</b> 정의돼 있어서, 원하는 자세를
        // 말로 적은 그대로 숫자로 옮길 수 있다. 범위도 전부 -1~1로 정규화돼 있다.
        // ── 패널 구획 ────────────────────────────────────────────────────
        /// <summary>튜닝 패널이 이 인덱스 앞에서 소제목을 찍는다(축이 47개라 구획이 없으면 못 찾는다).</summary>
        public static readonly int[] SectionAt = { 0, 7, 14, 23, 29, 38 };
        public static readonly string[] SectionTitle =
        {
            "왼다리 (무릎 꿇는 쪽)", "오른다리 (발 딛는 쪽)", "몸통",
            "목 · 머리", "왼팔 (바닥 짚는 쪽)", "오른팔 (칼)",
        };

        public static readonly string[] PoseMuscles =
        {
            // ★ 이름은 HumanTrait.MuscleName의 철자와 <b>정확히</b> 같아야 한다. 틀리면 인덱스가
            //   -1이 되어 그 축만 조용히 무시된다("Left Leg Stretch"로 잘못 적어 무릎이 통째로
            //   안 접히던 적이 있다 — 무릎의 정식 이름은 "Lower Leg Stretch"다).

            // [0] 왼다리 — 무릎 꿇는 쪽. 무릎·발가락이 바닥, 오른발보다 뒤.
            "Left Upper Leg Front-Back", "Left Upper Leg In-Out", "Left Upper Leg Twist In-Out",
            "Left Lower Leg Stretch", "Left Lower Leg Twist In-Out",
            "Left Foot Up-Down", "Left Foot Twist In-Out",

            // [7] 오른다리 — 앞에서 발바닥을 딛는 쪽. 무릎이 가슴 쪽으로 온다.
            "Right Upper Leg Front-Back", "Right Upper Leg In-Out", "Right Upper Leg Twist In-Out",
            "Right Lower Leg Stretch", "Right Lower Leg Twist In-Out",
            "Right Foot Up-Down", "Right Foot Twist In-Out",

            // [14] 몸통 — 앞뒤(숙임) · 좌우(옆으로 기울임) · 비틀기(어깨선 돌리기)
            "Spine Front-Back", "Spine Left-Right", "Spine Twist Left-Right",
            "Chest Front-Back", "Chest Left-Right", "Chest Twist Left-Right",
            "UpperChest Front-Back", "UpperChest Left-Right", "UpperChest Twist Left-Right",

            // [23] 목·머리
            "Neck Nod Down-Up", "Neck Tilt Left-Right", "Neck Turn Left-Right",
            "Head Nod Down-Up", "Head Tilt Left-Right", "Head Turn Left-Right",

            // [29] 왼팔 — 곧게 펴서 바닥을 짚는다. 굽히면 짚은 게 아니라 늘어뜨린 게 된다.
            "Left Shoulder Down-Up", "Left Shoulder Front-Back",
            "Left Arm Down-Up", "Left Arm Front-Back", "Left Arm Twist In-Out",
            "Left Forearm Stretch", "Left Forearm Twist In-Out",
            "Left Hand Down-Up", "Left Hand In-Out",

            // [38] 오른팔 — 칼을 쥔 쪽. 칼은 손뼈의 자식이라 이 축들을 따라온다. 다만 칼날
            //   각도 자체는 BladeTilt로 따로 잡는다 — 손목만으로는 안 눕는 걸 실측했다.
            "Right Shoulder Down-Up", "Right Shoulder Front-Back",
            "Right Arm Down-Up", "Right Arm Front-Back", "Right Arm Twist In-Out",
            "Right Forearm Stretch", "Right Forearm Twist In-Out",
            "Right Hand Down-Up", "Right Hand In-Out",
        };

        /// <summary>위 근육들의 값. 렌더해 보며 맞추는 값이라 <c>static</c>으로 열어 둔다.</summary>
        public static float[] PoseValues =
        {
            // [자세 갱신, 2026-07-23] 사람이 F7 패널로 직접 맞춘 값을 그대로 옮긴 것이다.
            // 근육 값을 임의로 "정리"하지 말 것 — 0이 아닌 축은 전부 이유가 있다.
            // (컷신 착지 자세는 이제 IntroCutscene.LandValues로 <b>분리</b>돼 있어, 여기를
            //  고쳐도 컷신은 안 따라온다.)

            // [0] 왼다리 — 무릎을 펴(+0.96) 뒤로 뻗는다. 정강이 비틀기(0.40)·발 비틀기(0.60)로
            //   발이 바깥으로 눕는다.
            //   앞뒤    좌우    비틀기   무릎    정강이비틀기  발등    발비틀기
             0.64f,  0.08f,  0.00f,  0.96f,  0.40f, -0.14f,  0.60f,

            // [7] 오른다리 — 무릎을 끝까지 펴(+1.00) 버틴다.
             0.50f,  0.11f,  0.00f,  1.00f,  0.08f,  0.23f, -0.29f,

            // [14] 몸통 — 전부 0. 상체 각도는 몸통 근육이 아니라 ActorYaw(125°)와 카메라가 만든다.
            //   Spine   좌우    비틀기   Chest   좌우    비틀기  UpperChest 좌우 비틀기
             0.00f,  0.00f,  0.00f,  0.00f,  0.00f,  0.00f,  0.00f,  0.00f,  0.00f,

            // [23] 목·머리 — 목을 끝까지 돌려(Turn +1.00) 얼굴이 카메라를 향한다.
             0.00f,  0.15f,  1.00f,  0.00f,  0.00f,  0.00f,

            // [29] 왼팔 — 팔꿈치를 펴(0.64) 앞·아래로 뻗는다.
            -0.12f, -0.14f, -0.20f,  0.40f,  0.00f,  0.64f,  0.00f,  0.09f, -0.08f,

            // [38] 오른팔 — 팔꿈치를 펴(0.58) 칼을 몸 앞으로 낸다.
             0.00f,  0.08f,  0.00f,  0.34f, -0.22f,  0.58f,  0.00f,  0.00f,  0.00f,
        };

        /// <summary>무릎을 꿇으면 골반이 내려앉는다. 근육만으로는 루트 높이가 안 바뀌어서 직접 준다(m).</summary>
        public static float KneelDrop = 0.52f;

        /// <summary>
        /// 칼날 각도 보정(도). 손뼈에 붙은 칼의 로컬 회전에 얹는다.
        ///
        /// <para><b>왜 근육으로 안 되는가</b> — 칼은 오른손 뼈의 자식이고, 그 그립 변환은
        /// <c>GhostSwordTool</c>이 <b>찌르기 자세</b> 기준으로 재서 고정해 둔 값이다. 손목 비틀림
        /// 근육을 끝까지 돌려도(-1~+1 전부 렌더해 확인) 칼끝은 계속 아래로 처진다. 앉은 자세는
        /// 팔 각도가 완전히 달라서, 같은 그립으로는 수평 칼날이 안 나온다.</para>
        ///
        /// <para>그래서 타이틀에서만 칼을 따로 돌린다. 잔상 쪽 그립은 건드리지 않는다 —
        /// 여기서 돌리는 건 우리가 만든 사본의 칼이다.</para>
        /// </summary>
        public static Vector3 BladeTilt = new Vector3(-11.5f, -154.3f, 13.6f);

        /// <summary>칼 위치 보정(m). 그립 지점 기준으로 칼을 밀고 당긴다.
        /// x가 크게(0.476) 들어간 건, 저폴리 박스 기준으로 잰 그립이 진짜 카타나의
        /// 손잡이 위치와 달라 손 안으로 당겨 넣어야 하기 때문이다.</summary>
        public static Vector3 BladeOffset = new Vector3(0.476f, -0.030f, -0.113f);

        /// <summary>칼 크기 배수. 1 = 자동 맞춤(칼날 길이 <see cref="BladeTargetLength"/>).</summary>
        public static float BladeScale = 1.11f;

        /// <summary>
        /// [칼 교체, 2026-07-23] 잔상용 저폴리 칼(36삼각형 박스 3개) 대신 <b>진짜 카타나</b>를 쓴다.
        ///
        /// <para>잔상 쪽이 박스인 데는 이유가 있다 — 예측 화면엔 잔상이 145장 깔려서, 25k버텍스
        /// 카타나를 그대로 쓰면 칼만으로 670만 삼각형이 된다. 하지만 타이틀은 <b>한 장</b>이라
        /// 그 제약이 없다. 여기서만 진짜 메시로 바꾸고, 잔상 프리팹은 건드리지 않는다.</para>
        /// </summary>
        const string BladeResource =
            "Meshy_AI_Sci_fi_Katana_3D_0720015705_image-to-3d-texture_fbx/" +
            "Meshy_AI_Sci_fi_Katana_3D_0720015705_image-to-3d-texture";

        /// <summary>Meshy 생성 메시는 실물 크기가 아니라, 칼날 길이를 이 값(m)에 맞춰 자동 축소한다.</summary>
        const float BladeTargetLength = 1.05f;

        // ── 숨쉬기 ───────────────────────────────────────────────────────
        // 정지 화면을 면하는 최소 장치. 한 축만 흔들면 "떨리는 모형"이 되므로, 실제로
        // 숨 쉴 때 움직이는 축들을 <b>가중치를 달리해</b> 같이 흔든다 — 가슴이 가장 크게,
        // 척추가 따라오고, 어깨가 살짝 들리고, 목이 반대로 미세하게 눌린다.
        static readonly string[] BreathAxes =
        {
            "Chest Front-Back", "UpperChest Front-Back", "Spine Front-Back",
            "Left Shoulder Down-Up", "Right Shoulder Down-Up", "Neck Nod Down-Up",
        };
        static readonly float[] BreathWeight = { 1.00f, 0.70f, 0.35f, 0.45f, 0.45f, -0.30f };
        int[] breathIndex;

        // 호흡과 다른 주기로 아주 느리게 도는 흔들림. 정지 자세가 완전히 굳지 않게 하는 장치다.
        // ★ 예전엔 이걸 <c>transform</c>을 통째로 돌려서 만들었는데, 그러면 <b>발이 제일 크게
        //   움직인다</b> — 회전축에서 멀기 때문이다(실측: 가슴 20mm일 때 발 63mm). 바닥을 딛고
        //   앉은 자세에서 발이 6cm씩 쓸리면 미끄러지는 것으로 보인다. 그래서 몸을 돌리는 대신
        //   <b>골반 위쪽 근육만</b> 돌린다 — 발은 완전히 고정된다.
        static readonly string[] SwayAxes =
        {
            "Spine Twist Left-Right", "Chest Left-Right", "Head Turn Left-Right",
        };
        static readonly float[] SwayWeight = { 1.00f, 0.45f, -0.35f };
        int[] swayIndex;

        /// <summary>흔들림 진폭(근육 단위). 0이면 완전히 굳는다.</summary>
        public static float SwayAmount = 0.022f;
        /// <summary>흔들림 속도. 호흡과 다른 주기여야 둘이 겹쳐 보이지 않는다.</summary>
        public static float SwaySpeed = 0.37f;

        /// <summary>숨쉬기 진폭(근육 단위). 0이면 완전히 멈춘다.
        /// 0.09는 과했다(머리가 46mm 흔들렸다) — 0.035면 "살아 있다"만 읽히고 눈에 걸리지 않는다.</summary>
        public static float BreathAmount = 0.035f;
        /// <summary>숨쉬기 속도(라디안/초). 사람 안정 시 호흡이 분당 12~16회라 0.9 언저리가 자연스럽다.</summary>
        public static float BreathSpeed = 0.9f;
        /// <summary>
        /// 숨쉬기에 맞춰 몸 전체가 오르내리는 양(m). <b>기본 0</b> — 켜면 발이 같이 움직인다.
        ///
        /// <para>이 값은 <c>bodyPosition</c>을 흔드는데, 그건 골반을 올리는 것이라 다리째
        /// 딸려 올라간다. 바닥을 딛고 앉은 자세에서 발이 1cm씩 오르내리면 <b>땅에서 미끄러져
        /// 보인다</b>(선 자세라면 무게 이동으로 읽혀서 오히려 자연스럽다).</para>
        ///
        /// <para>근육 호흡(<see cref="BreathAxes"/>)은 골반 <b>위쪽만</b> 돌리므로 발이 완전히
        /// 고정된다 — 앉은 자세에선 그쪽만으로 충분하다.</para>
        /// </summary>
        public static float BreathBob = 0f;

        /// <summary>카메라가 캐릭터를 잡는 구도. 거리·높이·각도 전부 캐릭터 기준 로컬.</summary>
        public static float CamDist = 5.33f, CamHeight = 0.39f, CamYaw = 45.5f, CamPitch = 10.4f, CamFov = 28.6f;
        /// <summary>캐릭터가 카메라 쪽으로 도는 각도(F7 패널에서 직접 맞춘 값).
        /// 125°는 몸을 카메라에서 크게 돌려세운 각이다 — 몸통 근육을 전부 0으로 두고
        /// 이 회전과 목 돌리기(Neck Turn +1.00)로 "몸은 옆, 얼굴은 정면"을 만든다.</summary>
        public static float ActorYaw = 125f;

        /// <summary>세로로 긴 렌더 텍스처 — 서 있는 사람을 담는 비율.</summary>
        const int RtW = 768, RtH = 1152;

        /// <summary>아레나에서 멀리 떨어뜨릴 위치.</summary>
        static readonly Vector3 Stage = new Vector3(0f, -5000f, 0f);

        Transform rigRoot, bodyRoot;
        Vector3 rigBasePos;
        Quaternion rigBaseRot;
        Camera cam;
        float born;

        HumanPoseHandler poseHandler;
        HumanPose humanPose;
        int[] muscleIndex;
        Vector3 basePosition;
        Quaternion baseRotation;
        Transform hipsBone;     // 발 고정 보정의 기준점
        Transform blade;        // 그립 앵커(원래 GhostKatana 트랜스폼) — 회전의 피벗
        Quaternion bladeBaseRot;
        Transform bladeMesh;    // 그 자식으로 붙인 진짜 카타나
        float bladeFitScale = 1f;

        /// <summary>만들어 세운다. 아바타 프리팹이 없으면 null을 돌려주고 조용히 빠진다
        /// (타이틀은 캐릭터 없이도 성립해야 한다).</summary>
        public static TitleActor Spawn()
        {
            var prefab = Resources.Load<GameObject>("PlayerGhost");
            if (prefab == null)
            {
                Debug.LogWarning("[타이틀] Resources/PlayerGhost 프리팹이 없어 캐릭터 없이 표시합니다.");
                return null;
            }
            var go = new GameObject("[TitleActor]");
            var actor = go.AddComponent<TitleActor>();
            return actor.Build(prefab) ? actor : null;
        }

        bool Build(GameObject prefab)
        {
            born = Time.unscaledTime;
            transform.position = Stage;

            int layer = LayerMask.NameToLayer("TitleActor");
            if (layer < 0) layer = 0;   // 레이어를 안 만들었어도 동작은 한다(멀리 떨어뜨린 게 1차 방어)

            var body = Instantiate(prefab, transform);
            body.name = "Actor";
            bodyRoot = body.transform;
            bodyRoot.localPosition = Vector3.zero;
            bodyRoot.localRotation = Quaternion.Euler(0f, ActorYaw, 0f);
            SetLayerRecursive(bodyRoot, layer);

            var animator = body.GetComponentInChildren<Animator>(true);
            if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
            {
                Debug.LogWarning("[타이틀] PlayerGhost에 휴머노이드 Animator가 없어 바인드 포즈로 섭니다.");
            }
            else
            {
                // 우리가 매 프레임 직접 한 시점을 구울 것이므로 Animator는 재워 둔다
                // (켜 두면 Animator가 쓴 포즈를 우리가 덮고, 다음 프레임에 또 덮여 떨린다).
                animator.enabled = false;
                rigRoot = animator.transform;
                rigBasePos = rigRoot.localPosition;
                rigBaseRot = rigRoot.localRotation;

                poseHandler = new HumanPoseHandler(animator.avatar, rigRoot);
                poseHandler.GetHumanPose(ref humanPose);   // 바인드 자세 = 근육 전부 0인 기준점
                basePosition = humanPose.bodyPosition;
                baseRotation = humanPose.bodyRotation;

                hipsBone = animator.GetBoneTransform(HumanBodyBones.Hips);

                // 손에 붙은 칼 — 타이틀 자세에 맞게 따로 돌려야 하므로 찾아 둔다.
                var hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
                if (hand != null) blade = hand.Find("GhostKatana");
                if (blade != null)
                {
                    bladeBaseRot = blade.localRotation;
                    SwapInRealKatana(layer);
                }

                // 근육 이름 → 인덱스. 이름이 틀리면 -1이 되고 그 항목만 조용히 무시된다.
                muscleIndex = new int[PoseMuscles.Length];
                for (int i = 0; i < PoseMuscles.Length; i++)
                {
                    muscleIndex[i] = System.Array.IndexOf(HumanTrait.MuscleName, PoseMuscles[i]);
                    if (muscleIndex[i] < 0)
                        Debug.LogError($"[타이틀] 근육 이름을 못 찾음: '{PoseMuscles[i]}' — 이 항목은 " +
                            "무시되므로 자세가 조용히 어긋난다. HumanTrait.MuscleName의 철자와 맞출 것 " +
                            "(무릎은 'Left/Right Lower Leg Stretch').");
                }

                breathIndex = new int[BreathAxes.Length];
                for (int i = 0; i < BreathAxes.Length; i++)
                {
                    breathIndex[i] = System.Array.IndexOf(HumanTrait.MuscleName, BreathAxes[i]);
                    if (breathIndex[i] < 0) Debug.LogError($"[타이틀] 숨쉬기 축 이름 오류: '{BreathAxes[i]}'");
                }

                swayIndex = new int[SwayAxes.Length];
                for (int i = 0; i < SwayAxes.Length; i++)
                {
                    swayIndex[i] = System.Array.IndexOf(HumanTrait.MuscleName, SwayAxes[i]);
                    if (swayIndex[i] < 0) Debug.LogError($"[타이틀] 흔들림 축 이름 오류: '{SwayAxes[i]}'");
                }
            }

            // ── 조명 ──
            // 씬에 광원이 사실상 없다(배경은 자체발광 패널). 캐릭터는 우리 빛으로만 보인다.
            // 정면 키 + 뒤쪽 주황 림 두 장 — 림이 후드 윤곽을 어둠에서 떼어내는 역할이라
            // 이게 없으면 검은 실루엣이 배경에 묻는다.
            // 세기는 렌더해 보고 맞춘 값이다 — 처음 넣었던 1.45/2.6/0.55는 흰 장갑이 다 날아가
            // 캐릭터가 통째로 흰 실루엣이 됐다. 낮추니 검은 수트와 흰 장갑이 갈라진다.
            MakeLight("Key", new Vector3(28f, -34f, 0f), new Color(0.86f, 0.92f, 1f), 0.55f, layer);
            MakeLight("Rim", new Vector3(8f, 152f, 0f), new Color(1f, 0.55f, 0.22f), 1.15f, layer);
            MakeLight("Fill", new Vector3(12f, 46f, 0f), new Color(0.35f, 0.72f, 0.9f), 0.22f, layer);
            // 아래에서 올려 비추는 보조광 — 앉은 자세는 다리가 몸통 그늘에 들어가는데,
            // 위쪽 광원들만으로는 하반신이 통째로 검게 묻힌다(실측으로 확인).
            MakeLight("Low", new Vector3(-18f, -30f, 0f), new Color(0.75f, 0.85f, 1f), 1.0f, layer);

            // ── 카메라 ──
            Target = new RenderTexture(RtW, RtH, 24, RenderTextureFormat.ARGB32)
            {
                name = "TitleActorRT",
                antiAliasing = 4,
            };
            var camGo = new GameObject("Cam");
            camGo.transform.SetParent(transform, false);
            cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);   // 투명 — 캔버스에서 합성한다
            cam.cullingMask = 1 << layer;
            cam.fieldOfView = CamFov;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 40f;
            cam.targetTexture = Target;
            cam.allowHDR = false;
            cam.useOcclusionCulling = false;

            ApplyFraming();
            return true;
        }

        /// <summary>
        /// 구도를 다시 계산한다. 카메라 값들을 <c>static</c> 필드로 둔 것과 짝인데 —
        /// 구도는 <b>눈으로 보고 맞춰야</b> 하는 값이라, 컴파일 없이 값만 바꾸고 이걸 부르면
        /// 곧바로 다시 잡힌다(에디터에서 스크린샷 찍어가며 조정하는 용도).
        /// </summary>
        public void ApplyFraming()
        {
            if (cam == null) return;
            Vector3 focus = Vector3.up * CamHeight;
            Quaternion orbit = Quaternion.Euler(CamPitch, CamYaw, 0f);
            Transform ct = cam.transform;
            ct.localPosition = focus + orbit * new Vector3(0f, 0f, -CamDist);
            ct.localRotation = Quaternion.LookRotation((focus - ct.localPosition).normalized, Vector3.up);
            cam.fieldOfView = CamFov;
            if (bodyRoot != null) bodyRoot.localRotation = Quaternion.Euler(0f, ActorYaw, 0f);
        }

        /// <summary>
        /// 저폴리 박스 칼을 숨기고 그 자리에 진짜 카타나를 끼운다.
        ///
        /// <para>박스 칼의 트랜스폼은 <b>지우지 않고 앵커로 남긴다</b> — 그 로컬 위치·회전이
        /// <c>GhostSwordTool</c>이 손뼈 기준으로 실측해 둔 그립이라, 여기에 자식으로 달면
        /// 쥔 위치가 저절로 맞는다. 렌더러만 꺼서 박스가 안 보이게 한다.</para>
        /// </summary>
        void SwapInRealKatana(int layer)
        {
            var prefab = Resources.Load<GameObject>(BladeResource);
            if (prefab == null)
            {
                Debug.LogWarning($"[타이틀] Resources/{BladeResource} 를 못 찾아 기본 칼로 표시합니다.");
                return;
            }

            foreach (var r in blade.GetComponentsInChildren<Renderer>(true)) r.enabled = false;

            var go = Instantiate(prefab, blade);
            go.name = "TitleKatana";
            bladeMesh = go.transform;
            bladeMesh.localPosition = Vector3.zero;
            bladeMesh.localRotation = Quaternion.identity;
            SetLayerRecursive(bladeMesh, layer);
            foreach (var c in bladeMesh.GetComponentsInChildren<Collider>(true)) Destroy(c);

            // 크기 자동 맞춤 — Meshy 생성 메시는 실물 스케일이 아니라, 렌더러 바운즈의
            // 가장 긴 축을 칼날 길이로 보고 목표값에 맞춘다. 손으로 배수를 찾을 필요가 없다.
            var rends = bladeMesh.GetComponentsInChildren<Renderer>(true);
            if (rends.Length > 0)
            {
                Bounds b = rends[0].bounds;
                for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
                float longest = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
                if (longest > 0.0001f)
                {
                    // bounds는 월드 크기라, 부모(리그 ~90배)의 스케일을 되나눠야 로컬 배수가 된다.
                    float parentScale = blade.lossyScale.x;
                    if (parentScale < 0.0001f) parentScale = 1f;
                    bladeFitScale = (BladeTargetLength / longest) * bladeMesh.localScale.x;
                    bladeFitScale /= parentScale > 0f ? 1f : 1f;   // lossyScale은 이미 bounds에 반영돼 있다
                }
            }
            Debug.Log($"[타이틀] 진짜 카타나 장착 — 자동 배수 {bladeFitScale:0.0000}");
        }

        /// <summary>근육 하나를 이름으로 바꾼다(구도와 같은 이유로 런타임 조정용).</summary>
        public static void SetMuscle(string name, float value)
        {
            int i = System.Array.IndexOf(PoseMuscles, name);
            if (i >= 0) PoseValues[i] = value;
        }

        /// <summary>근육을 채우고 <c>SetHumanPose</c>까지 적용한다. 숨쉬기·흔들림 파형을 인자로 받는다.</summary>
        void ApplyPose(float breathWave, float swayWave)
        {
            // 매 프레임 전 근육을 0으로 지우고 우리 값만 다시 쓴다 — 남은 값이 누적되지 않게.
            if (humanPose.muscles == null || humanPose.muscles.Length != HumanTrait.MuscleCount)
                humanPose.muscles = new float[HumanTrait.MuscleCount];
            else
                System.Array.Clear(humanPose.muscles, 0, humanPose.muscles.Length);

            for (int i = 0; i < muscleIndex.Length; i++)
            {
                int m = muscleIndex[i];
                if (m < 0) continue;
                humanPose.muscles[m] = Mathf.Clamp(PoseValues[i], -1f, 1f);
            }

            // 숨쉬기·흔들림은 자세를 <b>다 쓴 뒤에</b> 더한다. 둘 다 다리엔 얹지 않는다.
            float breath = breathWave * BreathAmount;
            if (breathIndex != null)
                for (int i = 0; i < breathIndex.Length; i++)
                {
                    int m = breathIndex[i];
                    if (m < 0) continue;
                    humanPose.muscles[m] = Mathf.Clamp(
                        humanPose.muscles[m] + breath * BreathWeight[i], -1f, 1f);
                }

            float sway = swayWave * SwayAmount;
            if (swayIndex != null)
                for (int i = 0; i < swayIndex.Length; i++)
                {
                    int m = swayIndex[i];
                    if (m < 0) continue;
                    humanPose.muscles[m] = Mathf.Clamp(
                        humanPose.muscles[m] + sway * SwayWeight[i], -1f, 1f);
                }

            humanPose.bodyPosition = basePosition - Vector3.up * (KneelDrop - breathWave * BreathBob);
            humanPose.bodyRotation = baseRotation;
            poseHandler.SetHumanPose(ref humanPose);
        }

        /// <summary>골반 뼈의 위치를 rigRoot의 부모 공간에서 읽는다(발 고정 보정의 기준).</summary>
        Vector3 HipsInParent()
        {
            if (hipsBone == null || rigRoot.parent == null) return Vector3.zero;
            return rigRoot.parent.InverseTransformPoint(hipsBone.position);
        }

        /// <summary>골반 뼈의 회전을 rigRoot의 부모 공간에서 읽는다.</summary>
        Quaternion HipsRotInParent()
        {
            if (hipsBone == null || rigRoot.parent == null) return Quaternion.identity;
            return Quaternion.Inverse(rigRoot.parent.rotation) * hipsBone.rotation;
        }

        void LateUpdate()
        {
            if (poseHandler == null || rigRoot == null) return;

            float age = Time.unscaledTime - born;
            float breathWave = Mathf.Sin(age * BreathSpeed);   // -1~1
            float swayWave = Mathf.Sin(age * SwaySpeed);

            // ★ 발을 고정하려면 자세를 <b>두 번</b> 푼다.
            //
            //   HumanPose의 bodyPosition은 골반이 아니라 <b>몸의 무게중심</b> 쪽 기준점이다.
            //   그래서 상체만 굽혀도 유니티가 무게중심을 맞추느라 <b>골반을 반대로 밀어</b>
            //   버리고, 골반에 매달린 다리가 통째로 따라간다 — 근육은 다리를 하나도 안
            //   건드렸는데 발이 7cm씩 쓸리던 원인이 이것이다(실측).
            //
            //   그래서 ①숨·흔들림 없는 자세를 풀어 골반 기준점을 재고, ②실제 자세를 푼 뒤,
            //   그 사이 골반이 어긋난 만큼 루트를 반대로 돌리고 옮긴다. 몸 전체를 강체로
            //   되돌리기 때문에 골반과 발이 제자리로 오고, 상체 움직임만 남는다.
            //   (KneelDrop처럼 <b>의도한</b> 이동은 ①에도 똑같이 들어가므로 상쇄되지 않는다.)
            //
            //   ★ <b>회전 보정이 위치 보정보다 중요하다</b>. 척추를 비틀면 유니티는 골반을
            //     반대로 <b>돌려서</b> 균형을 맞춘다 — 골반 위치는 0mm인데 발은 89mm 쓸렸다(실측).
            //     회전축에서 먼 발이 가장 크게 움직이기 때문이다. 그래서 회전을 먼저 되돌리고,
            //     그 결과 바뀐 위치를 다시 재서 옮긴다(순서를 바꾸면 위치가 다시 어긋난다).
            rigRoot.localPosition = rigBasePos;
            rigRoot.localRotation = rigBaseRot;

            ApplyPose(0f, 0f);
            Vector3 hipsRefPos = HipsInParent();
            Quaternion hipsRefRot = HipsRotInParent();

            ApplyPose(breathWave, swayWave);

            // SetHumanPose는 루트 트랜스폼까지 건드린다 — 배치는 우리가 정한 값으로 되돌린다.
            rigRoot.localPosition = rigBasePos;
            rigRoot.localRotation = rigBaseRot;
            if (hipsBone != null)
            {
                rigRoot.localRotation = (hipsRefRot * Quaternion.Inverse(HipsRotInParent())) * rigBaseRot;
                rigRoot.localPosition = rigBasePos + (hipsRefPos - HipsInParent());
            }

            // 칼날 각도는 자세를 다 잡은 <b>뒤에</b> 얹는다(손뼈가 먼저 제자리를 잡아야 한다).
            if (blade != null) blade.localRotation = bladeBaseRot * Quaternion.Euler(BladeTilt);
            if (bladeMesh != null)
            {
                bladeMesh.localPosition = BladeOffset;
                bladeMesh.localScale = Vector3.one * (bladeFitScale * Mathf.Max(0.01f, BladeScale));
            }

            // ★ 몸 전체를 돌리지 않는다 — 흔들림은 위에서 근육으로 처리했다(SwayAxes).
            //   여기서 transform을 돌리면 회전축에서 먼 발이 가장 크게 쓸린다.
        }

        void OnDestroy()
        {
            if (cam != null) cam.targetTexture = null;
            if (Target != null)
            {
                Target.Release();
                Destroy(Target);
                Target = null;
            }
        }

        Light MakeLight(string name, Vector3 euler, Color color, float intensity, int layer)
        {
            var go = new GameObject("Light_" + name);
            go.transform.SetParent(transform, false);
            go.transform.localRotation = Quaternion.Euler(euler);
            go.layer = layer;
            var l = go.AddComponent<Light>();
            l.type = LightType.Directional;
            l.color = color;
            l.intensity = intensity;
            l.shadows = LightShadows.None;   // 그림자 받을 바닥이 없다 — 켜면 비용만 든다
            l.cullingMask = 1 << layer;      // 빌트인 RP에서 아레나까지 밝히지 않게(URP는 무시)
            return l;
        }

        static void SetLayerRecursive(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++) SetLayerRecursive(t.GetChild(i), layer);
        }
    }
}
