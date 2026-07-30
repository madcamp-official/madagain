using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 한 번이라도 해킹당한 경비병은 <b>영구히 고장난다.</b> (기초_설계안 §6.3)
    ///
    /// <para>감지가 사라지고(부채꼴도 같이) 순찰이 멈추며 <b>고장난 대기 동작</b>을 무한 반복한다.
    /// 거미가 내부 기어를 헤집어 <b>기계를 망가뜨렸다</b>는 픽션의 직접적인 결과다 —
    /// 그래서 되돌아오지 않는다.</para>
    ///
    /// <para><b>★ 터렛과 반대다.</b> 터렛은 총구만 돌린 것이라 손을 떼면 다시 위협이 된다(§6.2).
    /// 경비병은 몸속을 파고든 것이라 한 번이면 끝이다. 이 대비가 두 오브젝트의 운용을 갈라놓는다 —
    /// <b>경비병은 처리하면 끝, 터렛은 계속 붙잡고 관리</b>.</para>
    ///
    /// <para><b>순찰을 멈추는 이유</b>: 고장난 동작을 재생하면서 계속 걸어 다니면 "고장"으로 안 읽힌다.
    /// 제자리에서 덜덜거려야 무력화가 전달된다. 그리고 멈춘 자리에 그대로 남으므로
    /// <b>빙의로 데려간 자리에 세워 두는</b> 활용(§6.3 "경비병을 옮겨 길 정리")과도 맞물린다.</para>
    /// </summary>
    // ★ 실행 순서 100 + LateUpdate에서 처리한다.
    //   GuardTurnStep은 [DefaultExecutionOrder(-10)]로 LateUpdate에서 다리 IK를 굴린다. 정리를
    //   Update에서 하면 "그 프레임에 이미 시작된 IK 작업"을 되돌리지 못하고 뒤늦게 덮인다.
    //   그 프레임의 모든 자세 작업이 끝난 뒤에 정리해야 확실히 마지막 작성자가 된다.
    [DefaultExecutionOrder(100)]
    [RequireComponent(typeof(Hackable))]
    [DisallowMultipleComponent]
    public class GuardBreakdown : MonoBehaviour
    {
        [Tooltip("고장 상태에서 무한 반복할 클립. Robot1F 애셋의 '미친 로봇' 대기 동작.\n" +
                 "★ 이 클립은 loopTime이 켜져 있어야 한다 — 꺼져 있으면 한 번 재생하고 얼어붙는다.")]
        public string brokenClip = "Idle_Crazy_Robot";

        [Tooltip("고장 동작으로 넘어가는 블렌드 시간(초). 0이면 즉시 스냅.")]
        public float blendTime = 0.25f;

        /// <summary>이미 고장났는가. <see cref="Hackable.everHacked"/>와 같지만 1회 처리를 위해 따로 둔다.</summary>
        public bool Broken { get; private set; }

        Hackable _hackable;
        GuardDetection _detection;
        Animator _anim;

        void Awake()
        {
            _hackable = GetComponent<Hackable>();
            _detection = GetComponent<GuardDetection>();
            _anim = GetComponent<Animator>();
        }

        void LateUpdate()
        {
            // everHacked에 이벤트가 없어 폴링한다 — bool 하나 읽는 비용이라 무해하고,
            // 이벤트를 추가하면 Hackable(순수 마커)에 상태 통지 책임이 생겨 성격이 흐려진다.
            if (Broken || _hackable == null || !_hackable.everHacked) return;
            Break();
        }

        /// <summary>즉시 고장 상태로 만든다. 되돌리는 경로는 두지 않는다(영구).</summary>
        public void Break()
        {
            if (Broken) return;
            Broken = true;

            // ★ 순서가 중요하다. 각 단계가 앞 단계를 전제한다.

            // ① 순찰을 <b>먼저</b> 끈다 — 새 회전(BeginTurn)이 시작되지 못하게 길을 막는 게 우선이다.
            //    뒤에 끄면 정리한 직후에 회전이 하나 더 시작돼 다리 IK가 되살아난다.
            var patrol = GetComponent<GuardPatrol>();
            if (patrol != null) patrol.enabled = false;

            // ② 진행 중인 회전을 되돌리고 다리 IK를 <b>죽인다</b>.
            //    컴포넌트를 그냥 끄는 것으로는 부족하다 — HandIK는 별도 컴포넌트라 살아남고,
            //    weight=0은 '안 쓴다'일 뿐 이미 써 놓은 뼈 회전을 되돌리지 않는다.
            //    그래서 해킹당한 경비병이 한쪽 발을 든 채 굳었다(실제로 겪은 버그).
            var turn = GetComponent<GuardTurnStep>();
            if (turn != null)
            {
                turn.ShutDown();
                turn.enabled = false;
            }

            // ③ 감지 소멸. GuardDetection.Active 하나가 판정과 부채꼴 표시를 <b>동시에</b> 끈다 —
            //    둘을 따로 끄면 언젠가 한쪽만 꺼져 "안 보이는데 걸리는" 사고가 난다.
            if (_detection != null) _detection.Active = false;

            // ④ 고장난 대기 동작 무한 반복. <b>맨 마지막</b>이어야 한다 — 앞에서 IK를 죽여 놓아야
            //    이 시점부터 뼈를 Animator가 온전히 소유한다.
            if (_anim != null && !string.IsNullOrEmpty(brokenClip))
            {
                if (blendTime > 0f) _anim.CrossFade(brokenClip, blendTime, 0, 0f);
                else _anim.Play(brokenClip, 0, 0f);
            }

            Debug.Log($"[경비병] 고장 — {name}: 감지·순찰 정지, '{brokenClip}' 무한 반복. 영구 무력화.");
        }
    }
}
