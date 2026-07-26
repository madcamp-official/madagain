using System;
using UnityEngine;

namespace Game.View
{
    /// <summary>웨이브를 다음으로 넘길 조건. 전멸을 강제하지 않는다(잔당을 남긴 채 다음 웨이브 시작 가능).</summary>
    public enum WaveAdvanceMode : byte
    {
        KillAll = 0,            // 이 웨이브 몹 전멸
        RemainingCount = 1,     // 이 웨이브 몹이 N마리 이하로 남으면
        RemainingPercent = 2,   // 이 웨이브 몹이 P% 이하로 남으면
        Timer = 3,              // advanceValue초 경과(웨이브 시작 기준)면 킬 무관하게 다음 — 주기 스폰용
    }

    /// <summary>낑기거나 튕겨나간 몹 처리 방식.</summary>
    public enum CleanupAction : byte
    {
        Kill = 0,       // 자동 처치
        Relocate = 1,   // 아레나 안 유효 지점으로 재배치(전투 유지)
    }

    /// <summary>배관 하나가 뱉는 몹 한 마리. 목록 순서가 곧 뱉는 순서다.</summary>
    [Serializable]
    public struct MobEmit
    {
        [Tooltip("이 순번에 나올 몹 종류.")]
        public MobKind kind;

        [Tooltip("직전 마리 이후 이 마리까지의 대기(초). 0 = 직전과 동시. 음수 = 이 배관의 기본 간격 사용.")]
        public float intervalOverride;

        [Tooltip("공백(대기) 엔트리 — 이 순번엔 몹을 안 뱉고 간격만 소비한다(번갈아 소환 리듬용). " +
                 "Fan은 이 엔트리에서 '또잉'하지 않는다. 공백만 있는 배관도 Fan은 준비 동작에 참여한다.")]
        public bool isGap;
    }

    /// <summary>
    /// 큐브(배관) 하나의 방출 설정. **큐브마다 개별 커스터마이즈**한다 —
    /// 어떤 몹을 몇 마리, 어떤 순서로, 어떤 간격으로 뱉을지 이 배관 혼자 정한다.
    /// 한 웨이브 안의 여러 배관은 **각자 동시에** 자기 목록을 진행한다.
    /// </summary>
    [Serializable]
    public class PipeEmission
    {
        [Tooltip("스폰 마커 큐브(배관). 위치와 방향(forward = 출구, 노란 면)을 제공한다.")]
        public Transform marker;

        [Tooltip("이 배관만의 시작 지연(초). 웨이브 시작 후 이만큼 뒤에 뱉기 시작한다.")]
        public float startDelay = 0f;

        [Tooltip("이 배관의 기본 몹 간격(초). 각 몹이 오버라이드하지 않으면 이 값을 쓴다.")]
        public float interval = 0.5f;

        [Tooltip("이 배관이 뱉을 몹들. 배열 순서 = 뱉는 순서 (예: 근·원·근·원 교대).")]
        public MobEmit[] mobs = new MobEmit[0];

        public int MobCount => mobs != null ? mobs.Length : 0;
    }

    /// <summary>웨이브 하나. 여러 배관이 동시에 각자의 목록을 뱉는다.</summary>
    [Serializable]
    public class Wave
    {
        [Tooltip("식별용 이름(선택).")]
        public string name = "Wave";

        [Tooltip("이 웨이브가 시작되기 전 대기(초). 각 배관의 시작 지연은 이후에 더해진다.")]
        public float startDelay = 0f;

        [Tooltip("이 웨이브에서 동작하는 배관들. 각 배관이 자기 목록을 동시에 진행한다.")]
        public PipeEmission[] pipes = new PipeEmission[0];

        [Tooltip("다음 웨이브로 넘어갈 조건.")]
        public WaveAdvanceMode advance = WaveAdvanceMode.KillAll;

        [Tooltip("RemainingCount면 남은 마리 수 N, RemainingPercent면 남은 비율 P(0~100), " +
                 "Timer면 웨이브 시작 후 경과 초. KillAll이면 무시.")]
        public float advanceValue = 0f;

        /// <summary>이 웨이브가 스폰할 총 마리 수(마커 없는 배관 제외).</summary>
        public int TotalMobs()
        {
            if (pipes == null) return 0;
            int n = 0;
            foreach (PipeEmission p in pipes)
                if (p != null && p.marker != null) n += p.MobCount;
            return n;
        }
    }

    /// <summary>
    /// 낑김·튕김으로 클리어 불가(소프트락)가 되는 것을 막는 안전장치.
    /// ※ 이건 안전장치이지 버그 해결이 아니다 — 발동 시 로그를 남겨 진짜 원인을 추적한다.
    /// </summary>
    [Serializable]
    public class CleanupSettings
    {
        [Tooltip("마무리 정리 사용 여부.")]
        public bool enabled = true;

        [Tooltip("남은 몹이 이 수 이하일 때만 동작(마무리 국면에서만).")]
        public int triggerRemaining = 3;

        [Tooltip("플레이어와 이 거리 이상 떨어져 있어야 대상이 된다.")]
        public float minDistance = 20f;

        [Tooltip("'안 보임 + 충분히 멀다' 상태가 이 시간(초) 지속되면 처리한다.")]
        public float holdSeconds = 3f;

        [Tooltip("처리 방식. Kill = 자동 처치, Relocate = 아레나 안으로 재배치.")]
        public CleanupAction action = CleanupAction.Kill;
    }

    /// <summary>
    /// 아레나 하나의 웨이브 구성 데이터. 아레나 루트(또는 _Waves 자식)에 붙인다.
    /// 위치는 전부 수동 지정(랜덤 없음) — 배관마다 특정 마커 큐브를 참조한다.
    /// 런타임 진행은 WaveRunner가 담당하고, 이 컴포넌트는 "무엇을·어디서·어떤 순서로"만 들고 있다.
    /// 설계 문서: docs/shared/웨이브_시스템_설계.md
    /// </summary>
    public class ArenaWaves : MonoBehaviour
    {
        [Tooltip("웨이브 목록. 위에서부터 순서대로 진행한다.")]
        public Wave[] waves = new Wave[0];

        [Tooltip("마지막 웨이브 다음에 첫 웨이브(0)로 되돌아가 무한 반복. " +
                 "advance=Timer 웨이브 몇 개 + loop로 '진입하면 30초마다 순환 스폰'이 된다.")]
        public bool loop = false;

        [Tooltip("낑김·튕김 대비 마무리 정리 설정.")]
        public CleanupSettings cleanup = new CleanupSettings();

        [Header("임시 조치")]
        [Tooltip("【임시방편】 몹이 벽·천장에 끼는 것을 피하려고 스폰 위치를 이만큼 아래로 내린다.\n" +
                 "근본 해결(마커를 벽면에서 띄우기 / 스폰 시 충돌 밀어내기 / 펄스 도입)이 되면 0으로 되돌릴 것.")]
        public float spawnDropOffset = 1.5f;

        public bool HasWave(int index) => waves != null && index >= 0 && index < waves.Length;

        /// <summary>해당 웨이브가 스폰할 총 마리 수.</summary>
        public int SpawnCountOf(int index) => HasWave(index) ? waves[index].TotalMobs() : 0;

        /// <summary>해당 웨이브의 배관 수.</summary>
        public int PipeCountOf(int index)
            => HasWave(index) && waves[index].pipes != null ? waves[index].pipes.Length : 0;

        // ── 씬 뷰 시각화: 선택 시 웨이브별 색으로 배관 위치와 방출 수를 표시 ──
        void OnDrawGizmosSelected()
        {
            if (waves == null) return;
            for (int w = 0; w < waves.Length; w++)
            {
                Wave wave = waves[w];
                if (wave == null || wave.pipes == null) continue;
                Gizmos.color = WaveColor(w);

                foreach (PipeEmission p in wave.pipes)
                {
                    if (p == null || p.marker == null) continue;
                    // 방출 수만큼 구체를 위로 쌓아 "이 배관이 몇 마리 뱉는지" 표시
                    for (int i = 0; i < p.MobCount; i++)
                        Gizmos.DrawWireSphere(p.marker.position + Vector3.up * (0.6f + i * 0.45f), 0.2f);
                    Gizmos.DrawLine(p.marker.position,
                                    p.marker.position + p.marker.forward * 2f);   // 출구 방향
                }
            }
        }

        /// <summary>웨이브 인덱스별 구분색(순환).</summary>
        public static Color WaveColor(int index)
        {
            switch (index % 5)
            {
                case 0:  return new Color(0.2f, 1f, 0.4f);    // 초록
                case 1:  return new Color(1f, 0.75f, 0.1f);   // 주황
                case 2:  return new Color(0.3f, 0.7f, 1f);    // 파랑
                case 3:  return new Color(1f, 0.35f, 0.9f);   // 분홍
                default: return new Color(1f, 1f, 1f);        // 흰
            }
        }
    }
}
