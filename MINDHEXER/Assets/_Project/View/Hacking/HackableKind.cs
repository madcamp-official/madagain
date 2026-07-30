namespace Game.View
{
    /// <summary>해킹 대상 종류. (기초_설계안 §6.1 로스터 / §11.3 보스 스턴 — 원안 9종 + RotationPlatform 추가)</summary>
    public enum HackableKind
    {
        // ※ 이 목록의 순서는 저장된 int 값이라 <b>바꾸면 기존 프리팹의 종류가 뒤바뀐다.</b>
        //    분류가 바뀌어도 자리를 옮기지 않고 주석으로만 표시한다.

        // 외부 조종 (연두)
        RailCarrier,
        Gantry,
        Piston,
        HydraulicPress,
        // 시점 진입 (청록)
        Guard,
        Turret,          // ← 외부 조종으로 이동(§6.2). 자리는 그대로 둔다(저장된 값 보호).
        CCTV,            // ← 보류(§6.1). 삭제하지 않는다.
        RobotArm,
        // 스턴
        Boss,
        // 외부 조종 (연두) — 기존 프리팹들의 저장된 int(0~8)를 안 건드리려고 끝에 추가.
        RotationPlatform,   // ← 보류(§6.1). 단, 터렛 회전이 이 구현체를 그대로 쓴다.
    }

    /// <summary>대상 종류에서 기본 속성을 유도한다. 프리팹 Reset/편집 편의용.</summary>
    public static class HackableKindEx
    {
        /// <summary>종류 → 결과 타입 기본값. (§6 분류)</summary>
        public static ControlType DefaultControlType(this HackableKind k)
        {
            switch (k)
            {
                case HackableKind.RailCarrier:
                case HackableKind.Gantry:
                case HackableKind.Piston:
                case HackableKind.HydraulicPress:
                case HackableKind.RotationPlatform:
                case HackableKind.Turret:   // 빙의 → 외부 조종으로 이동(§6.2). 회전 조종 + 자동 사격.
                    return ControlType.ExternalControl;
                case HackableKind.Guard:
                case HackableKind.CCTV:     // 보류(§6.1). 되살리면 그대로 시점 진입.
                case HackableKind.RobotArm:
                    return ControlType.ViewEntry;
                case HackableKind.Boss:
                    return ControlType.Stun;
                default:
                    return ControlType.ExternalControl;
            }
        }

        /// <summary>종류 → 점 패턴 선 개수 기본값. (§2.4: 외부=5, 시점/보스=7)</summary>
        public static int DefaultPatternLineCount(this HackableKind k)
        {
            // ★ 종류와 무관하게 5로 통일한다(사용자 확정). 예전에는 외부=5 / 시점·보스=7 이었는데,
            //   같은 UI 안에서 획 수가 달라지면 난이도가 아니라 '일관성 없음'으로 읽힌다.
            //   난이도가 필요하면 획 수가 아닌 다른 축(시간 제한 등)으로 주는 편이 낫다.
            return UnifiedPatternLineCount;
        }

        /// <summary>모든 해킹 대상의 점 패턴 획 수. 개별 조정은 <c>Hackable.patternLineCount</c>로.</summary>
        public const int UnifiedPatternLineCount = 5;
    }
}
