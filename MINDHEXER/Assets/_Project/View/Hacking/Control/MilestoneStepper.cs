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
    /// <para><b>★ 목적은 노이즈 제거 하나다. 연출이 아니다.</b> 예전엔 "딸깍딸깍 기계 손맛"을 노리고
    /// 스텝을 크게(초당 12틱) 잡았는데, 그건 목적을 잘못 잡은 것이었다. 그 값으로는 <b>레일에 올라탄
    /// 플레이어가 17cm씩 순간이동</b>해 조작이 망가졌다(실기 확인). <b>부드럽고 빠른 것이 최우선</b>이고,
    /// 양자화는 떨림이 통과하지 못할 만큼만 걸면 된다.</para>
    ///
    /// <para><b>★ 스텝은 손으로 정하지 않는다 — 속도에서 유도한다</b>(<see cref="SyncTo"/>):
    /// <code>스텝 = 최고속도 × 간격(1프레임)</code>
    /// 이렇게 잡으면 성질이 이렇게 갈린다.
    /// <list type="bullet">
    /// <item><b>최고 속도로 밀 때</b> — 매 틱 정확히 1스텝이라 <b>완전히 부드럽다.</b> 양자화가 안 보인다.</item>
    /// <item><b>입력이 작거나 떨릴 때</b> — 누적이 스텝에 못 미쳐 출력 0. <b>떨림만 걸러진다.</b></item>
    /// <item><b>스파이크</b> — 틱당 1스텝 상한이 그대로 살아 있다.</item>
    /// </list>
    /// 즉 <b>거를 것만 거르고 정상 조작은 손대지 않는다.</b> 속도를 바꾸면 스텝이 따라오므로 튜닝
    /// 손잡이도 "속도" 하나로 유지된다.</para>
    ///
    /// <para><b>간격 = VR 1프레임</b>(<see cref="DefaultInterval"/> = 1/72초). 그보다 짧게 잡아도 프레임당
    /// 한 번밖에 못 나가 의미가 없다. <b>PC·VR 동일하게 적용한다</b> — PC는 VR의 개발용 대역이라
    /// 거동이 다르면 PC에서 본 것이 VR에서 그대로가 아니게 된다.</para>
    ///
    /// <para><b>플릭에는 쓰지 않는다.</b> 플릭은 격자 지점으로 가는 각본된 이동이고 자체 가감속 곡선이
    /// 있다 — 거기에 양자화를 또 걸면 이중으로 겹쳐 덜덜거린다. 노이즈가 들어오는 경로는
    /// 홀드(아날로그)뿐이므로 거기만 걸면 충분하다.</para>
    /// </summary>
    [System.Serializable]
    public struct MilestoneStepper
    {
        /// <summary>간격 기본값(초) = VR 1프레임(72Hz). 그보다 짧게 잡아도 프레임당 한 번뿐이라 무의미.</summary>
        public const float DefaultInterval = 1f / 72f;

        [Tooltip("한 번에 움직이는 양. 0 이하면 양자화를 끄고 연속으로 움직인다.\n" +
                 "★ 손으로 넣지 말 것 — SyncTo(속도)가 '속도 × 간격'으로 자동 계산한다.")]
        public float stepSize;

        [Tooltip("스텝 사이 최소 간격(초). 기본 1/72 = VR 1프레임.")]
        public float minInterval;

        [Tooltip("노이즈 여유 배율. 1이면 스텝이 '최고 속도 1프레임치'라 최고 속도에서 완전히 부드럽고, " +
                 "그보다 작은 입력만 걸러진다.\n" +
                 "실기에서 떨림이 여전히 새어 나오면 1.5~2로 올린다 — 대신 저속 조작이 거칠어진다.")]
        public float noiseScale;

        [Tooltip("누적기에 쌓아 둘 수 있는 최대 스텝 수. 입력이 최대 속도를 넘게 요구하면 초과분이 여기까지만 " +
                 "쌓인다 — 안 두면 손을 뗀 뒤에도 쌓인 만큼 계속 움직인다(밀린 스텝이 줄줄이 나온다).")]
        public float maxPendingSteps;

        float _accum;      // 아직 스텝이 안 된 잔량
        float _cooldown;   // 다음 스텝까지 남은 시간

        /// <summary>기본값 세트. 스텝은 <see cref="SyncTo"/>가 채운다.</summary>
        public MilestoneStepper(float unused = 0f)
        {
            stepSize = 0f;
            minInterval = DefaultInterval;
            noiseScale = 1f;
            maxPendingSteps = 2f;
            _accum = 0f;
            _cooldown = 0f;
        }

        /// <summary>
        /// 그 부품의 <b>최고 속도</b>에서 스텝을 유도한다. 부품이 <c>Awake</c>·<c>OnValidate</c>에서 부른다.
        ///
        /// <para>속도를 인스펙터에서 바꾸면 스텝이 따라오므로, 튜닝 손잡이가 "속도" 하나로 유지된다.
        /// 손으로 스텝을 넣으면 반드시 둘이 어긋나고, 어긋난 결과가 "왜 이렇게 끊기지"로 나타난다.</para>
        /// </summary>
        /// <param name="maxSpeed">부품 단위의 최고 속도(m/s, °/s, 비율/s 등).</param>
        public void SyncTo(float maxSpeed)
        {
            if (minInterval <= 0f) minInterval = DefaultInterval;
            if (noiseScale <= 0f) noiseScale = 1f;
            if (maxPendingSteps < 1f) maxPendingSteps = 2f;
            stepSize = Mathf.Max(0f, maxSpeed) * minInterval * noiseScale;
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
