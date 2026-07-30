using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 마일스톤(스테퍼) 양자화 — 연속 입력을 <b>정해진 크기의 딸깍 단위</b>로만 내보낸다.
    /// (기초_설계안 §6.2 마일스톤)
    ///
    /// <para><b>왜 필요한가</b> — VR 조종 입력은 휴대폰의 6DoF 추적에서 온다. 그 신호에는 늘
    /// <b>떨림</b>(가만히 들고 있어도 미세하게 진동)과 <b>아웃라이어</b>(추적이 한 프레임 튐)가 섞인다.
    /// 신호를 잘라내는(데드존·저역통과) 방식은 진짜 조작까지 뭉개지고, 임계값 밖 스파이크는 그대로
    /// 통과한다. 그래서 <b>자르지 않고 양자화</b>한다.</para>
    ///
    /// <list type="number">
    /// <item><b>떨림</b>: 스텝보다 작은 변화는 누적만 되고 출력이 0이다. 좌우로 흔들리면 누적이 서로
    ///       상쇄돼 아예 안 움직인다.</item>
    /// <item><b>아웃라이어</b>: 간격당 최대 1스텝이라 신호가 튀어도 한 칸만 간다. 남은 양은 누적기에
    ///       남아 다음 틱에 나오므로 <b>버려지지 않는다</b>.</item>
    /// </list>
    ///
    /// <para>부수 효과로 <b>딸깍딸깍 톱니바퀴 손맛</b>이 나온다 — §6.2의 "기계식 인덱싱"과 같은
    /// 언어라 오히려 어울린다. 필터가 연출이 되는 드문 경우다.</para>
    ///
    /// <para><b>★ 스텝과 간격은 따로 정할 수 없다</b>:
    /// <code>스텝 ÷ 간격 = 최대 속도</code>
    /// 5도 스텝에 간격 0.5초면 최대 10°/s로 답답해진다. 그래서 <b>간격을 딸깍 빈도로 고정하고
    /// (기본 <see cref="DefaultInterval"/> = 초당 12틱), 스텝은 그 부품의 기존 속도에서 유도</b>한다.
    /// 튜닝 손잡이가 "속도" 하나로 유지되고, 딸깍 빈도가 부품마다 같아 손맛이 통일된다.</para>
    ///
    /// <para><b>플릭에는 쓰지 않는다.</b> 플릭은 격자 지점으로 가는 각본된 이동이고 자체 가감속 곡선이
    /// 있다 — 거기에 양자화를 또 걸면 딸깍이 이중으로 겹쳐 덜덜거린다. 노이즈가 들어오는 경로는
    /// 홀드(아날로그)뿐이므로 거기만 걸면 충분하다.</para>
    /// </summary>
    [System.Serializable]
    public struct MilestoneStepper
    {
        /// <summary>딸깍 빈도 기본값(초). 0.0833 ≈ 초당 12틱 — 딸깍이 읽히면서 끊기진 않는 지점.</summary>
        public const float DefaultInterval = 0.0833f;

        [Tooltip("한 번에 움직이는 양(딸깍 한 칸). 0 이하면 양자화를 끄고 연속으로 움직인다.\n" +
                 "★ 스텝 ÷ 간격 = 최대 속도다 — 이 값만 키우면 빨라지고, 줄이면 느려진다.")]
        public float stepSize;

        [Tooltip("딸깍 사이 최소 간격(초). 0.0833 = 초당 12번. 키우면 딸깍이 뚜렷해지지만 최대 속도가 준다.")]
        public float minInterval;

        [Tooltip("누적기에 쌓아 둘 수 있는 최대 스텝 수. 입력이 최대 속도를 넘게 요구하면 초과분이 여기까지만 " +
                 "쌓인다 — 안 두면 손을 뗀 뒤에도 쌓인 만큼 계속 움직인다(밀린 딸깍이 줄줄이 나온다).")]
        public float maxPendingSteps;

        float _accum;      // 아직 스텝이 안 된 잔량
        float _cooldown;   // 다음 스텝까지 남은 시간

        /// <summary>기본값 세트. 스텝은 부품 속도에서 유도한 값을 넣는다.</summary>
        public MilestoneStepper(float step)
        {
            stepSize = step;
            minInterval = DefaultInterval;
            maxPendingSteps = 2f;
            _accum = 0f;
            _cooldown = 0f;
        }

        /// <summary>
        /// 이번 프레임 요구량을 넣고, 실제로 움직일 양을 받는다. <b>0 아니면 ±stepSize</b>다.
        /// </summary>
        /// <param name="demand">이번 프레임 움직이고 싶은 양(부품 단위: 도, 미터, 비율 등).</param>
        public float Advance(float demand, float dt)
        {
            if (stepSize <= 0f) return demand;   // 양자화 끔 — 예전 동작 그대로

            _accum += demand;

            // 최대 속도를 넘는 요구는 여기서 잘린다. 이게 없으면 손을 뗀 뒤에도 잔량이 계속 나온다.
            float cap = stepSize * Mathf.Max(1f, maxPendingSteps);
            _accum = Mathf.Clamp(_accum, -cap, cap);

            // ⚠️ 먼저 깎고 나서 판정한다. 예전엔 "쿨다운>0이면 깎고 return"이었는데, 그러면 쿨다운이
            //    0이 되는 프레임까지 버려서 주기가 프레임 하나만큼 길어졌다 — 60°/s를 요구했는데
            //    실측 50°/s가 나왔다(초당 12회가 아니라 10회).
            _cooldown -= dt;
            if (_cooldown > 0f) return 0f;
            if (Mathf.Abs(_accum) < stepSize) return 0f;

            float sign = Mathf.Sign(_accum);
            _accum -= sign * stepSize;

            // '=' 대신 '+='. 남은 음수 시간을 다음 주기로 넘겨 <b>평균 주기가 정확히</b> minInterval이 된다.
            // 프레임이 간격보다 길면 밀린 몫을 포기한다(안 그러면 한 프레임에 여러 스텝이 터진다).
            _cooldown += minInterval > 0f ? minInterval : 0f;
            if (_cooldown < 0f) _cooldown = 0f;

            return sign * stepSize;
        }

        /// <summary>판 재시작·조종 해제 시. 잔량이 남아 있으면 다음 조종 첫 프레임에 튄다.</summary>
        public void Reset()
        {
            _accum = 0f;
            _cooldown = 0f;
        }
    }
}
