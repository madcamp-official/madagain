using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 보스 양팔의 <b>유일한 소유자</b> — 리그 주석 + 2본 IK 솔버 + 손가락 쥐기. 동작을 모른다.
    /// (보스전_설계 §팔 IK)
    ///
    /// <para><b>관절 규약(확정)</b>: 쓰는 관절은 <b>어깨(Upperarm) · 팔꿈치(Forearm) · 손가락</b>뿐이다.
    /// <b>손목(Hand)과 트위스트 본은 전혀 쓰지 않는다</b> — 매 프레임 홈 회전으로 동결한다.
    /// 손목을 놀리면 손이 잘못된 피벗으로 돌고, 트위스트를 놀리면 팔꿈치에서 메시가 꼬인다.</para>
    ///
    /// <para><b>왜 동작에서 분리하나</b>: 손바닥 오프셋을 걷기 말고 다른 동작(내려찍기·잡기·프레스
    /// 반응)에서도 쓴다. 팔 IK가 동작 컴포넌트 안에 있으면 동작이 늘 때마다 같은 코드가 복사되고,
    /// 두 동작이 동시에 팔을 건드리면 서로 덮어쓴다. 여기로 모으면 동작 쪽은
    /// <see cref="Aim"/>/<see cref="Relax"/>/<see cref="Curl"/>만 부르면 되고, 마지막 지시가 이긴다.</para>
    ///
    /// <para><b>IK 말단은 손 본이 아니라 손바닥 마커</b>다. 손으로 배치한 지점이라 리그가 어떻게
    /// 생겼든 "여기가 닿는다"를 저작이 직접 정한다.</para>
    ///
    /// <para><b>손바닥 각도 = 남은 자유도 하나</b>: 2본 IK는 목표에 닿고도 "어깨→목표 축을 중심으로
    /// 팔이 통째로 도는 각도"가 남는다. 손목을 안 돌리므로 이 하나가 손바닥 방향을 전부 정한다.
    /// 축이 목표점을 지나므로 <b>얼마를 돌려도 손은 목표에 그대로 붙어 있다</b> — 도달을 해치지 않고
    /// 손바닥만 맞출 수 있다. 최적각은 스캔이 아니라 한 번에 구한다(§Solve 주석).</para>
    /// </summary>
    [ExecuteAlways]
    [DefaultExecutionOrder(50)]   // Animator 평가(Update) 뒤에 팔을 덮어쓴다
    [DisallowMultipleComponent]
    public class BossArmRig : MonoBehaviour
    {
        public enum Side { Left = 0, Right = 1 }
        public enum Axis { X, Y, Z }

        [System.Serializable]
        public class Arm
        {
            [Header("관절 (쓰는 것)")]
            [Tooltip("어깨 관절. 이 트랜스폼의 위치가 곧 어깨다.")]
            public Transform upperarm;

            [Tooltip("팔꿈치 관절.")]
            public Transform forearm;

            [Tooltip("★ 손바닥 마커 — Hand의 자식. 위치=손바닥 중심, +Z(파랑)=손바닥 법선. " +
                     "이 지점이 IK 목표에 닿는다. 손으로 배치할 것.")]
            public Transform palm;

            [Tooltip("엄지 외 손가락 본들(마디 포함). 각도 하나로 함께 굽는다.")]
            public Transform[] fingers;

            [Tooltip("엄지 본들(마디 포함). 굽힘량에 thumbScale이 곱해진다.")]
            public Transform[] thumb;

            [Header("동결 (안 쓰는 것)")]
            [Tooltip("손목. 규약상 쓰지 않는다 — 홈 회전으로 동결.")]
            public Transform hand;

            [Tooltip("트위스트 본들. 규약상 쓰지 않는다 — 홈 회전으로 동결.")]
            public Transform[] twists;

            [Header("보정")]
            [Tooltip("손바닥 자동 정렬 결과에 더하는 수동 보정각(도). 어색하면 여기만 돌린다.")]
            [Range(-180f, 180f)] public float rollOffset;
        }

        [Header("팔")]
        public Arm left = new Arm();
        public Arm right = new Arm();

        [Header("팔꿈치 방향")]
        [Tooltip("팔꿈치가 향할 방향(보스 로컬, 좌우 자동 미러). x=바깥 / y=위 / z=앞.\n" +
                 "★ 이 값이 클수록 팔꿈치가 벽 쪽으로 시원하게 벌어진다. 기본은 바깥·살짝 위·살짝 뒤.")]
        public Vector3 elbowHint = new Vector3(1f, 0.15f, -0.25f);

        [Header("손바닥 정렬")]
        [Tooltip("1=손바닥 법선을 목표 법선에 맞춘다. ★ 하지만 그러면 팔 전체가 돌아가 팔꿈치 방향이 " +
                 "무너진다 — 자유도가 하나뿐이라 팔꿈치와 손바닥 중 하나만 잡을 수 있다.\n" +
                 "기본 0 = 팔꿈치 우선. 손바닥이 어색하면 아래 rollOffset으로 따로 맞출 것.")]
        [Range(0f, 1f)] public float alignStrength = 0f;

        [Tooltip("정렬이 요구하는 롤이 이 각도를 넘으면 포기한다 — 무리하게 맞추다 팔꿈치에서 " +
                 "메시가 꼬이는 것을 막는다(도).")]
        [Range(0f, 180f)] public float maxRollDeg = 120f;

        [Header("손가락")]
        [Tooltip("손가락이 굽는 축(본 로컬). 이 리그는 본이 +Y로 뻗으므로 X 또는 Z가 맞다 — 눈으로 확인할 것.")]
        public Axis fingerBendAxis = Axis.Z;

        [Tooltip("굽힘 1.0일 때 마디당 각도(도).")]
        public float fingerMaxDeg = 55f;

        [Tooltip("엄지에 곱하는 배율. 엄지는 보통 덜 굽고 방향도 달라 따로 둔다.")]
        [Range(-1f, 1f)] public float thumbScale = 0.6f;

        [Header("미리보기 (편집 모드) — 손바닥 마커 배치용")]
        [Tooltip("켜면 Play 없이 아래 타겟을 따라 팔이 움직인다. 끄면 홈 포즈로 복원된다.")]
        public bool preview;

        public Transform previewTargetL;
        public Transform previewTargetR;

        [Tooltip("미리보기에서 손바닥이 향할 방향(월드). 0이면 타겟에서 어깨 쪽을 향한다.")]
        public Vector3 previewPalmNormal = Vector3.zero;

        [Tooltip("미리보기 손가락 굽힘(0~1).")]
        [Range(0f, 1f)] public float previewCurl;

        [Header("진단 (읽기 전용)")]
        public float alignErrorL, alignErrorR;
        public bool reachableL, reachableR;
        public float reachL, reachR;

        // ── 요청 (동작 쪽이 밀어넣는 것) ──────────────────────────────────

        struct Request
        {
            public bool active;
            public Vector3 target;
            public Vector3 palmNormal;
            public float curl;
        }

        readonly Request[] _req = new Request[2];

        /// <summary>이 손을 이 지점으로. <paramref name="palmNormal"/>은 손바닥이 <b>향할</b> 방향
        /// (벽을 짚으면 벽 안쪽). 한 번 부르면 <see cref="Relax"/>까지 유지된다 — 벽에 고정된 손은
        /// 목표가 안 바뀌므로 매 프레임 다시 부를 필요가 없다.</summary>
        public void Aim(Side side, Vector3 worldTarget, Vector3 palmNormal)
        {
            int i = (int)side;
            _req[i].active = true;
            _req[i].target = worldTarget;
            _req[i].palmNormal = palmNormal.sqrMagnitude > 1e-6f ? palmNormal.normalized : Vector3.forward;
        }

        /// <summary>손가락 쥐기(0=펴짐, 1=최대). 팔 IK와 독립이라 Relax 중에도 유지된다.</summary>
        public void Curl(Side side, float t) { _req[(int)side].curl = Mathf.Clamp01(t); }

        /// <summary>이 손을 놓는다 — 팔이 애니메이션 포즈로 돌아간다(동결·손가락은 유지).</summary>
        public void Relax(Side side) { _req[(int)side].active = false; }

        /// <summary>유효 팔 길이(어깨→팔꿈치→손바닥마커). 목표가 닿는지 동작 쪽이 판정할 때 쓴다.</summary>
        public float Reach(Side side)
        {
            Arm a = Get(side);
            if (a.upperarm == null || a.forearm == null || a.palm == null) return 0f;
            return Vector3.Distance(a.upperarm.position, a.forearm.position)
                 + Vector3.Distance(a.forearm.position, a.palm.position);
        }

        public Vector3 PalmPos(Side side) { var a = Get(side); return a.palm != null ? a.palm.position : transform.position; }
        public Vector3 PalmNormal(Side side) { var a = Get(side); return a.palm != null ? a.palm.forward : transform.forward; }
        public Vector3 ShoulderPos(Side side) { var a = Get(side); return a.upperarm != null ? a.upperarm.position : transform.position; }

        public Arm Get(Side side) => side == Side.Left ? left : right;

        // ── 홈 포즈 (동결·복원의 기준) ────────────────────────────────────
        //
        // 편집 모드에서도 도는 컴포넌트라, 본을 돌린 채 씬이 저장되면 원본 포즈가 영구히 오염된다.
        // (이 프로젝트가 ViewmodelCamera로 이미 겪은 사고다.) 그래서 홈 회전을 직렬화해 두고
        // 미리보기 해제·비활성·씬 저장 직전 세 시점 모두에서 되돌린다.

        [SerializeField, HideInInspector] Transform[] _homeBones;
        [SerializeField, HideInInspector] Quaternion[] _homeRots;

        [ContextMenu("본 자동 탐색")]
        public void AutoFind()
        {
            FindArm(left, "L_");
            FindArm(right, "R_");
            CaptureHome();
        }

        void FindArm(Arm a, string p)
        {
            var twists = new System.Collections.Generic.List<Transform>();
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                if (!t.name.StartsWith(p)) continue;
                if (t.name == p + "Upperarm") a.upperarm = t;
                else if (t.name == p + "Forearm") a.forearm = t;
                else if (t.name == p + "Hand") a.hand = t;
                else if (t.name.Contains("Twist") && !t.name.EndsWith("_end")
                         && (t.name.Contains("Upperarm") || t.name.Contains("Forearm")))
                    twists.Add(t);
            }
            a.twists = twists.ToArray();

            // 손가락 — Hand 아래를 훑는다. 이름 규칙에 기대지 않고 계층으로 잡되, 엄지만 구분한다.
            // (_end는 말단 마커라 메시에 영향이 없으므로 제외)
            var fingers = new System.Collections.Generic.List<Transform>();
            var thumb = new System.Collections.Generic.List<Transform>();
            if (a.hand != null)
            {
                foreach (var t in a.hand.GetComponentsInChildren<Transform>(true))
                {
                    if (t == a.hand) continue;
                    if (t.name.EndsWith("_end")) continue;
                    if (t.name.Contains("Palm")) continue;              // 손바닥 마커는 손가락이 아니다
                    if (t.name.Contains("Thumb")) thumb.Add(t);
                    else fingers.Add(t);
                }
            }
            a.fingers = fingers.ToArray();
            a.thumb = thumb.ToArray();

            // 손바닥 마커가 없으면 Hand 원점에 만들어 둔다 — 끌어서 맞추기만 하면 되게.
            // Hand는 규약상 동결이라 팔뚝에 대해 강체다. 어디에 매달든 결과는 같지만,
            // 의미상 손의 자식으로 두는 편이 나중에 손목을 쓰게 돼도 안 깨진다.
            if (a.palm == null && a.hand != null)
            {
                Transform found = a.hand.Find(p + "Palm");
                if (found == null)
                {
                    var go = new GameObject(p + "Palm");
                    go.transform.SetParent(a.hand, false);
                    found = go.transform;
                }
                a.palm = found;
            }
        }

        [ContextMenu("홈 포즈 재캡처")]
        public void CaptureHome()
        {
            var bones = new System.Collections.Generic.List<Transform>();
            AddBones(left, bones);
            AddBones(right, bones);
            _homeBones = bones.ToArray();
            _homeRots = new Quaternion[_homeBones.Length];
            for (int i = 0; i < _homeBones.Length; i++)
                _homeRots[i] = _homeBones[i] != null ? _homeBones[i].localRotation : Quaternion.identity;
        }

        static void AddBones(Arm a, System.Collections.Generic.List<Transform> list)
        {
            if (a.upperarm != null) list.Add(a.upperarm);
            if (a.forearm != null) list.Add(a.forearm);
            if (a.hand != null) list.Add(a.hand);
            Add(a.twists, list);
            Add(a.fingers, list);
            Add(a.thumb, list);
        }

        static void Add(Transform[] arr, System.Collections.Generic.List<Transform> list)
        {
            if (arr == null) return;
            foreach (var t in arr) if (t != null) list.Add(t);
        }

        void RestoreHome()
        {
            if (_homeBones == null || _homeRots == null) return;
            int n = Mathf.Min(_homeBones.Length, _homeRots.Length);
            for (int i = 0; i < n; i++)
                if (_homeBones[i] != null) _homeBones[i].localRotation = _homeRots[i];
        }

        bool TryHome(Transform t, out Quaternion q)
        {
            q = Quaternion.identity;
            if (_homeBones == null) return false;
            for (int i = 0; i < _homeBones.Length; i++)
                if (_homeBones[i] == t) { q = _homeRots[i]; return true; }
            return false;
        }

        void SetHome(Transform t)
        {
            if (t == null) return;
            if (TryHome(t, out Quaternion q)) t.localRotation = q;
        }

        /// <summary>손목·트위스트를 홈으로 고정. 규약상 이 관절들은 쓰지 않는다.</summary>
        void FreezeUnused(Arm a)
        {
            SetHome(a.hand);
            if (a.twists != null) foreach (var t in a.twists) SetHome(t);
        }

        Vector3 BendAxis()
        {
            if (fingerBendAxis == Axis.X) return Vector3.right;
            if (fingerBendAxis == Axis.Y) return Vector3.up;
            return Vector3.forward;
        }

        void ApplyCurl(Arm a, float t)
        {
            Vector3 ax = BendAxis();
            if (a.fingers != null)
                foreach (var f in a.fingers)
                {
                    if (f == null || !TryHome(f, out Quaternion h)) continue;
                    f.localRotation = h * Quaternion.AngleAxis(t * fingerMaxDeg, ax);
                }
            if (a.thumb != null)
                foreach (var f in a.thumb)
                {
                    if (f == null || !TryHome(f, out Quaternion h)) continue;
                    f.localRotation = h * Quaternion.AngleAxis(t * fingerMaxDeg * thumbScale, ax);
                }
        }

        // ── 수명 ─────────────────────────────────────────────────────────

        void OnEnable()
        {
            if (_homeBones == null || _homeBones.Length == 0) CaptureHome();
#if UNITY_EDITOR
            UnityEditor.SceneManagement.EditorSceneManager.sceneSaving += OnSceneSaving;
#endif
        }

        void OnDisable()
        {
#if UNITY_EDITOR
            UnityEditor.SceneManagement.EditorSceneManager.sceneSaving -= OnSceneSaving;
#endif
            RestoreHome();
        }

#if UNITY_EDITOR
        void OnSceneSaving(UnityEngine.SceneManagement.Scene scene, string path)
        {
            RestoreHome();   // 저장되는 것은 항상 홈 포즈 — 미리보기 자세가 씬에 굳지 않는다
        }
#endif

        // 편집 모드에서 미리보기가 꺼져 있는 동안은 <b>아무것도 건드리지 않는다</b>.
        // 매 프레임 홈으로 되돌리면 인스펙터에서 본을 손으로 돌려도 즉시 원복돼 조사가 불가능하다
        // (실제로 겪음). 미리보기가 꺼지는 <b>그 프레임에만</b> 한 번 복원하고, 그 뒤로는 손을 뗀다.
        // 씬 오염은 OnDisable·sceneSaving 복원이 계속 막아 준다.
        bool _prevPreview;

        void LateUpdate()
        {
            if (!Application.isPlaying)
            {
                if (!preview)
                {
                    if (_prevPreview) { RestoreHome(); _prevPreview = false; }
                    return;
                }
                _prevPreview = true;
                PushPreview(Side.Left, previewTargetL);
                PushPreview(Side.Right, previewTargetR);
                Curl(Side.Left, previewCurl);
                Curl(Side.Right, previewCurl);
            }

            FreezeUnused(left);
            FreezeUnused(right);
            ApplyCurl(left, _req[0].curl);
            ApplyCurl(right, _req[1].curl);

            Solve(Side.Left);
            Solve(Side.Right);
        }

        void PushPreview(Side side, Transform t)
        {
            if (t == null) { Relax(side); return; }
            Vector3 n = previewPalmNormal.sqrMagnitude > 1e-6f
                ? previewPalmNormal
                : (ShoulderPos(side) - t.position);   // 기본: 손바닥이 몸 쪽을 본다(벽을 짚는 자세)
            Aim(side, t.position, n);
        }

        // ── 2본 IK ───────────────────────────────────────────────────────

        void Solve(Side side)
        {
            Arm a = Get(side);
            bool ok = a.upperarm != null && a.forearm != null && a.palm != null;

            float reach = ok ? Reach(side) : 0f;
            if (side == Side.Left) reachL = reach; else reachR = reach;

            if (!ok || !_req[(int)side].active) return;

            Vector3 S = a.upperarm.position;
            Vector3 T = _req[(int)side].target;
            Vector3 want = _req[(int)side].palmNormal;

            float lenU = Vector3.Distance(S, a.forearm.position);
            float lenL = Vector3.Distance(a.forearm.position, a.palm.position);
            float full = lenU + lenL;

            Vector3 toT = T - S;
            float dist = toT.magnitude;
            bool reachable = dist <= full && dist > 1e-3f;
            if (side == Side.Left) reachableL = reachable; else reachableR = reachable;
            if (dist < 1e-3f) return;

            float c = Mathf.Clamp(dist, Mathf.Abs(lenU - lenL) + 1e-3f, full - 1e-3f);
            Vector3 n = toT / dist;

            // ① 팔꿈치 방향(폴 벡터) — <b>여기가 자유도 하나를 쓴다.</b>
            //    2본 IK는 목표에 닿고 나면 "어깨→목표 축 둘레로 팔이 어디로 꺾이나" 하나가 남는데,
            //    그걸 손바닥 정렬(②)에 주면 팔꿈치가 제멋대로 안쪽으로 오므라든다. 벽을 짚고 가는
            //    연출에서는 팔꿈치가 바깥(벽 쪽)으로 벌어져야 하므로 이쪽이 우선권을 갖는다.
            //    손바닥은 ②의 alignStrength를 올리거나 rollOffset으로 따로 맞춘다.
            Vector3 hintLocal = new Vector3(elbowHint.x * (side == Side.Left ? -1f : 1f),
                                            elbowHint.y, elbowHint.z);
            Vector3 pole = transform.TransformDirection(hintLocal);
            pole -= Vector3.Dot(pole, n) * n;                     // 축에 수직인 성분만 의미가 있다
            if (pole.sqrMagnitude < 1e-6f)
            {
                pole = a.forearm.position - S;                    // 힌트가 축과 평행하면 현재 자세로 대체
                pole -= Vector3.Dot(pole, n) * n;
            }
            if (pole.sqrMagnitude < 1e-6f)
            {
                pole = Vector3.Cross(n, transform.up);
                if (pole.sqrMagnitude < 1e-6f) pole = Vector3.Cross(n, transform.right);
            }
            pole.Normalize();

            float cosU = Mathf.Clamp((lenU * lenU + c * c - lenL * lenL) / (2f * lenU * c), -1f, 1f);
            float sinU = Mathf.Sqrt(Mathf.Max(0f, 1f - cosU * cosU));
            Vector3 upperDir = n * cosU + pole * sinU;

            a.upperarm.rotation = Quaternion.FromToRotation(a.forearm.position - S, upperDir) * a.upperarm.rotation;
            a.forearm.rotation = Quaternion.FromToRotation(a.palm.position - a.forearm.position,
                                                           T - a.forearm.position) * a.forearm.rotation;

            // ② 손바닥 정렬 — 팔 전체를 어깨→목표 축(n) 둘레로 θ 만큼 돌린다.
            //    목표점이 축 위에 있으므로 <b>얼마를 돌려도 손은 목표에 붙은 채</b> 손바닥만 돈다.
            //    그래서 최적각을 스캔할 필요가 없다: 손바닥 법선과 목표 법선을 각각 축에 수직인
            //    평면으로 투영하고, 그 둘 사이의 부호 있는 각도가 곧 답이다(축 성분은 어차피 불변).
            Vector3 fPerp = Vector3.ProjectOnPlane(a.palm.forward, n);
            Vector3 dPerp = Vector3.ProjectOnPlane(want, n);

            float theta = 0f;
            if (fPerp.sqrMagnitude > 1e-6f && dPerp.sqrMagnitude > 1e-6f)
                theta = Vector3.SignedAngle(fPerp, dPerp, n) * alignStrength;
            theta = Mathf.Clamp(theta, -maxRollDeg, maxRollDeg);   // 무리한 롤로 꼬는 것보다 안 맞추는 게 낫다
            theta += a.rollOffset;

            // 어깨가 회전 중심이므로 <b>회전만</b> 곱한다. Transform.RotateAround는 수학적으로 같지만
            // position까지 쓰는데, S를 읽은 시점과 실제 위치가 부동소수점 오차만큼 어긋나 있어
            // 그 차이가 localPosition에 매 프레임 누적된다(스케일 50배라 증폭). 위팔이 어깨에서
            // 조금씩 떨어져 나가면 몸통~팔에 걸친 메시가 늘어나 부채꼴로 뭉개진다 — 실제로 겪었다.
            // 이 형태는 position을 아예 안 건드리므로 손으로 돌리는 것과 완전히 같다.
            if (Mathf.Abs(theta) > 1e-4f)
                a.upperarm.rotation = Quaternion.AngleAxis(theta, n) * a.upperarm.rotation;

            float err = Vector3.Angle(a.palm.forward, want);
            if (side == Side.Left) alignErrorL = err; else alignErrorR = err;
        }

#if UNITY_EDITOR
        void OnDrawGizmos()
        {
            DrawArm(Side.Left, new Color(1f, 0.55f, 0.2f));
            DrawArm(Side.Right, new Color(0.25f, 0.7f, 1f));
        }

        void DrawArm(Side side, Color col)
        {
            Arm a = Get(side);
            if (a.upperarm == null || a.forearm == null || a.palm == null) return;

            Vector3 S = a.upperarm.position, E = a.forearm.position, P = a.palm.position;
            float scale = Mathf.Max(0.2f, Vector3.Distance(S, E) * 0.12f);

            Gizmos.color = col;
            Gizmos.DrawLine(S, E);
            Gizmos.DrawLine(E, P);
            Gizmos.DrawWireSphere(S, scale);
            Gizmos.DrawWireSphere(E, scale * 0.8f);
            Gizmos.DrawWireSphere(P, scale);

            // 손바닥 법선 — 노랑=현재. 목표(초록)와 벌어져 보이면 그게 곧 오류다.
            float arrow = scale * 6f;
            Gizmos.color = Color.yellow;
            Gizmos.DrawRay(P, a.palm.forward * arrow);

            if (_req[(int)side].active)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawRay(P, _req[(int)side].palmNormal * arrow);
                Gizmos.DrawWireCube(_req[(int)side].target, Vector3.one * scale);
            }

            Gizmos.color = new Color(col.r, col.g, col.b, 0.15f);
            Gizmos.DrawWireSphere(S, Reach(side));
        }
#endif
    }
}
