namespace Game.View
{
    /// <summary>해킹 대상 종류. (기초_설계안 §6.1 로스터 / §11.3 보스 스턴 — 원안 9종 + RotationPlatform 추가)</summary>
    public enum HackableKind
    {
        // 외부 조종 (연두)
        RailCarrier,
        Gantry,
        Piston,
        HydraulicPress,
        // 시점 진입 (청록)
        Guard,
        Turret,
        CCTV,
        RobotArm,
        // 스턴
        Boss,
        // 외부 조종 (연두) — 기존 프리팹들의 저장된 int(0~8)를 안 건드리려고 끝에 추가.
        RotationPlatform,
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
                    return ControlType.ExternalControl;
                case HackableKind.Guard:
                case HackableKind.Turret:
                case HackableKind.CCTV:
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
            return k.DefaultControlType() == ControlType.ExternalControl ? 5 : 7;
        }
    }
}
