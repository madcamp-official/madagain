using UnityEngine;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// 몹 종류 별칭 → 3축 조합. 콘솔·씬 세팅이 공유하는 단일 정의.
    /// ※ 값은 씬에 숫자로 직렬화된다 — <b>새 종류는 반드시 맨 뒤에 추가</b>할 것.
    ///   중간에 끼우면 이미 저장된 웨이브 설정의 몹 종류가 전부 어긋난다.
    /// </summary>
    public enum MobKind : byte
    {
        Grunt = 0, Pinky = 1, Soldier = 2, Caco = 3, Large = 4,
        // ★ 아래 둘(층이동)은 폐기 예정 — 층이동 특성을 안 쓰기로 했습니다(2026-07-22, 확정은 아님).
        //   상세는 MobilityType 주석 참고. 값은 남겨둡니다(위 주석대로 번호를 바꾸면 기존 웨이브 설정이 어긋남).
        //   새 웨이브를 짤 때는 Grunt/Soldier를 쓰십시오.
        GruntT = 5,     // 근층 — 근접 + 층이동   [폐기 예정]
        SoldierT = 6,   // 원층 — 원거리 + 층이동 [폐기 예정]
        Boss = 7,       // 보스 — 빛나는 구 코어, 추적 레이저(mobility=Orb)
    }

    /// <summary>스폰 지점 하나 = 위치(씬의 빈 오브젝트) + 그 지점에서 나올 종류.</summary>
    [System.Serializable]
    public struct SpawnEntry
    {
        public Transform point;
        public MobKind    kind;
    }

    /// <summary>
    /// 맵별 오토스폰 세팅. 각 테스트 씬에 하나 배치한다. 스폰 지점(빈 오브젝트)마다 종류를 지정.
    /// Main이 Start에서 이걸 찾아 스폰지점·종류·주기·상한·기본 on/off를 가져온다(맵 열면 세팅이 딸려옴).
    /// </summary>
    public class MapSpawnConfig : MonoBehaviour
    {
        [Tooltip("맵 로드 시 자동스폰을 켠 채로 시작할지")]
        public bool autoSpawnOnStart = true;
        [Tooltip("스폰 주기(틱, 60틱=1초)")]
        public int intervalTicks = 45;
        [Tooltip("동시 최대 적 수")]
        public int cap = 12;
        [Tooltip("스폰 지점 + 그 지점에서 나올 종류")]
        public SpawnEntry[] entries;

        /// <summary>종류 별칭 → (전투, 기동, 크기).</summary>
        public static (CombatType, MobilityType, SizeClass) Axes(MobKind k)
        {
            switch (k)
            {
                case MobKind.Pinky:    return (CombatType.Melee,  MobilityType.Charge,    SizeClass.Normal);
                case MobKind.Soldier:  return (CombatType.Ranged, MobilityType.Ground,    SizeClass.Normal);
                case MobKind.Caco:     return (CombatType.Ranged, MobilityType.Flying,    SizeClass.Normal);
                case MobKind.Large:    return (CombatType.Melee,  MobilityType.Ground,    SizeClass.Large);
                case MobKind.GruntT:   return (CombatType.Melee,  MobilityType.Traversal, SizeClass.Normal);  // 근층
                case MobKind.SoldierT: return (CombatType.Ranged, MobilityType.Traversal, SizeClass.Normal);  // 원층
                case MobKind.Boss:     return (CombatType.Melee,  MobilityType.Orb,       SizeClass.Normal);  // 보스
                default:              return (CombatType.Melee,  MobilityType.Ground, SizeClass.Normal);  // Grunt
            }
        }

        /// <summary>이름 문자열 → MobKind(콘솔 입력용). 실패 시 false.</summary>
        public static bool TryParse(string s, out MobKind kind)
        {
            switch (s.ToLowerInvariant())
            {
                case "grunt":   kind = MobKind.Grunt;   return true;
                case "pinky":   kind = MobKind.Pinky;   return true;
                case "soldier": kind = MobKind.Soldier; return true;
                case "caco":    kind = MobKind.Caco;    return true;
                case "large":   kind = MobKind.Large;   return true;
                // 층이동 변종(근층·원층)
                case "gruntt":
                case "근층":    kind = MobKind.GruntT;   return true;
                case "soldiert":
                case "원층":    kind = MobKind.SoldierT; return true;
                case "boss":
                case "보스":    kind = MobKind.Boss;     return true;
                default:        kind = MobKind.Grunt;   return false;
            }
        }

        // ── 씬 뷰 시각화: 지점 위치·종류를 색 구체로 표시 ──
        void OnDrawGizmos()
        {
            if (entries == null) return;
            foreach (var e in entries)
            {
                if (e.point == null) continue;
                Gizmos.color = KindColor(e.kind);
                Gizmos.DrawWireSphere(e.point.position, 0.6f);
                Gizmos.DrawLine(e.point.position, e.point.position + Vector3.up * 2f);
            }
        }

        static Color KindColor(MobKind k)
        {
            switch (k)
            {
                case MobKind.Pinky:    return new Color(1f, 0.4f, 0.2f);
                case MobKind.Soldier:  return new Color(0.3f, 0.7f, 1f);
                case MobKind.Caco:     return new Color(0.8f, 0.3f, 1f);
                case MobKind.Large:    return new Color(1f, 0.85f, 0.2f);
                case MobKind.GruntT:   return new Color(0.5f, 1f, 0.5f);    // 근층 = 근접색 계열 + 층이동
                case MobKind.SoldierT: return new Color(0.2f, 1f, 0.85f);   // 원층 = 원거리색 계열 + 층이동
                case MobKind.Boss:     return new Color(1f, 0.2f, 0.1f);    // 보스 = 강한 적색
                default:              return Color.white;   // Grunt
            }
        }
    }
}
