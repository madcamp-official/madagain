using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 시점 진입(빙의) 대상의 공통 설정. CCTV·터렛·경비병이 <b>같은 컴포넌트를 값만 다르게</b> 쓴다. (§6.1·§2.5)
    ///
    /// <para>대상별 차이는 전부 여기 값으로 표현된다 — 새 종류를 추가할 때 코드가 아니라 값만 정하면 된다:
    /// <list type="bullet">
    ///  <item>CCTV — 좌우 ±45, 상하 ±45, 이동 없음, 시야 밖 차폐 있음</item>
    ///  <item>터렛 — 구형 광각 렌즈. 좌우 360(파노라마, 마스크 안 걸림) + 상하 ±<see cref="tiltRange"/>
    ///        (그 밖은 CCTV와 같은 방식으로 차폐), 이동 없음</item>
    ///  <item>경비병 — 시야 제한 없음, 이동 가능(<see cref="allowsMove"/>), 차폐 없음</item>
    /// </list>
    /// 발사·이동 같은 <b>행동</b>은 이 컴포넌트가 아니라 별도 컴포넌트로 얹는다(관심사 분리).</para>
    /// </summary>
    [DisallowMultipleComponent]
    public class ViewEntryTarget : MonoBehaviour
    {
        [Header("시점")]
        [Tooltip("빙의 시 카메라가 들어갈 자리이자 시야 기준. 비우면 이름이 'Camera'인 자식을 찾는다.")]
        public Transform eye;

        [Tooltip("좌우 시야 범위(± 도). 이 밖은 차폐된다.")]
        public float panRange = 45f;

        [Tooltip("상하 시야 범위(± 도). 이 밖은 차폐된다. 터렛은 렌즈 시야각(예: ±30)만큼만 — 완전 차단 아님.")]
        public float tiltRange = 45f;

        [Tooltip("좌우를 범위에서 실제로 못 넘어가게 막을지. false면 자유 회전(밖은 차폐로 안 보임).")]
        public bool hardClampPan;

        [Tooltip("상하를 범위에서 실제로 못 넘어가게 막을지. false면 마우스는 계속 돌아가되 밖은 차폐만 된다.")]
        public bool hardClampTilt;

        [Tooltip("완전히 실명 상태인 특수 케이스 전용(렌즈가 아예 막혀 아무 방향도 안 보임). 켜면 카메라가 " +
                 "항상 검정만 낸다 — 부분 시야 제한(대부분의 경우)은 tiltRange만으로 충분하니 이건 거의 안 쓴다.")]
        public bool eyeBlocked;

        [Header("행동")]
        [Tooltip("빙의 중 WASD 이동 가능 여부. 경비병만 true.")]
        public bool allowsMove;

        [Tooltip("빙의 시 [Head]를 이만큼(m) 더 들어올린다. 몸 원점(=충돌박스)은 그대로 두고 " +
                 "시점만 살짝 올려 \"평소보다 눈높이가 높다\"는 인상만 준다 — 대상의 실제 눈높이에 " +
                 "맞추지 않는다(예: 경비병은 스케일 2라 진짜 눈높이가 3.6m지만, 몸 크기가 그대로 " +
                 "플레이어 것이므로 그 높이까지 올리면 충돌·이동 판정과 시야가 어긋난다).")]
        public float possessEyeLift;

        [Header("차폐")]
        [Tooltip("시야 범위 밖을 가릴지. CCTV·터렛 true, 경비병 false.")]
        public bool useBlocker = true;

        [Tooltip("차폐 색.")]
        public Color blockerColor = new Color(0.15f, 0.85f, 0.25f, 1f);

        [Tooltip("빙의 중 이 대상의 메시를 숨길지. 눈이 모델 안에 있으면 자기 껍질이 보이므로 보통 true.")]
        public bool hideOwnMeshWhilePossessed = true;

        /// <summary>시야 기준 트랜스폼. eye 미지정 시 자식에서 탐색하고, 그래도 없으면 자기 자신.</summary>
        public Transform Eye
        {
            get
            {
                if (eye == null) eye = FindEye();
                return eye != null ? eye : transform;
            }
        }

        Transform FindEye()
        {
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (t != transform && t.name == "Camera") return t;
            return null;
        }

        void Reset()
        {
            var h = GetComponent<Hackable>();
            if (h == null) return;
            switch (h.kind)
            {
                case HackableKind.Turret:
                    // 구형 광각 렌즈: 수평은 파노라마처럼 360도 자유(마스크 안 걸림 — panRange=360).
                    // 수직은 렌즈 시야각만큼만 보이고 그 밖(위·아래로 조금이라도 벗어나면)은
                    // ViewConeMask가 가린다 — CCTV와 같은 메커니즘, 범위만 다르다.
                    panRange = 360f; hardClampPan = false;
                    tiltRange = 30f; hardClampTilt = false;
                    allowsMove = false; useBlocker = true; eyeBlocked = false;
                    break;
                case HackableKind.Guard:
                    panRange = 180f; tiltRange = 85f;
                    allowsMove = true; useBlocker = false;
                    possessEyeLift = 0.35f;   // "높아졌다"는 인상만 — 실제 눈높이(스케일 2 기준 3.6m)엔 안 맞춘다
                    break;
                default:   // CCTV·로봇팔
                    panRange = 45f; tiltRange = 45f;
                    allowsMove = false; useBlocker = true;
                    break;
            }
        }

        Renderer[] _hidden;

        /// <summary>빙의 중 자기 메시를 숨긴다(눈이 모델 안에 있어 껍질이 보이는 문제). 복귀 시 원복.</summary>
        public void SetOwnMeshVisible(bool visible)
        {
            if (!hideOwnMeshWhilePossessed) return;

            if (!visible)
            {
                _hidden = GetComponentsInChildren<Renderer>(true);
                foreach (var r in _hidden) if (r != null) r.enabled = false;
            }
            else if (_hidden != null)
            {
                foreach (var r in _hidden) if (r != null) r.enabled = true;
                _hidden = null;
            }
        }
    }
}
