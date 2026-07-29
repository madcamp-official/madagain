using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.View
{
    /// <summary>
    /// 최악 시나리오 부하 생성기 — S24+ 실기 최적화 측정용.
    ///
    /// <para>세 가지 부하를 동시에 건다:
    /// <list type="bullet">
    /// <item><b>도시</b> — TallCity DemoScene(수천 오브젝트)을 additive로 로드. 드로우콜 바닥 부하.</item>
    /// <item><b>보스</b> — 리깅된 로봇 N기를 거대 스케일로 세워 계속 애니메이션+이동. 스키닝 부하.</item>
    /// <item><b>경비병 웨이브</b> — 주기적으로 스폰하고 잠시 뒤 파괴(<see cref="GuardDestruction.Destruct"/>).
    ///   스폰 스파이크 + 파편 물리 + 수명 관리가 겹치는 순간 부하.</item>
    /// </list></para>
    ///
    /// <para>측정은 <see cref="VrStatsHud"/>가 한다(화면 + logcat 시계열). 이 컴포넌트는 부하만 만든다.
    /// 강도는 전부 인스펙터 값 — 실기에서 어디까지 버티는지 이 값들을 올려가며 찾는다.</para>
    /// </summary>
    public sealed class StressDirector : MonoBehaviour
    {
        [Header("도시 (additive 씬)")]
        [Tooltip("빌드 세팅에 포함된 씬 이름. 비우면 로드 안 함.")]
        public string citySceneName = "DemoScene";
        public bool loadCity = true;

        [Header("보스 (리깅 로봇)")]
        public GameObject bossPrefab;
        [Tooltip("보스 수.")]
        public int bossCount = 6;
        [Tooltip("배치 반경(m).")]
        public float bossRingRadius = 14f;
        [Tooltip("보스 스케일 배율.")]
        public float bossScale = 5f;
        [Tooltip("제자리 회전 속도(도/초).")]
        public float bossTurnSpeed = 40f;
        [Tooltip("애니메이션 상태를 이 주기(초)로 무작위 전환.")]
        public float bossAnimSwitchInterval = 3f;

        [Header("경비병 웨이브")]
        public GameObject guardPrefab;
        [Tooltip("웨이브 주기(초).")]
        public float waveInterval = 6f;
        [Tooltip("웨이브당 스폰 수.")]
        public int guardsPerWave = 8;
        [Tooltip("스폰 후 파괴까지(초). 파편이 터지는 순간이 스파이크 측정 지점이다.")]
        public float killDelay = 2.5f;
        [Tooltip("스폰 반경(m).")]
        public float guardRingRadius = 8f;

        // Robot1F 애셋의 상태 이름들. HasState로 확인하고 쓴다 — 없는 이름이면 그냥 안 튼다.
        static readonly string[] BossStates = { "Idle_1", "Idle_2", "Idle_3", "Idle_Crazy_Robot", "Walk_IP", "Run_IP", "Attack_Arm_1" };

        readonly List<Animator> _bosses = new List<Animator>();
        readonly List<GameObject> _wave = new List<GameObject>();
        float _nextAnimSwitch;
        float _nextWave;
        float _killAt = -1f;

        void Start()
        {
            if (loadCity && !string.IsNullOrEmpty(citySceneName))
            {
                SceneManager.LoadSceneAsync(citySceneName, LoadSceneMode.Additive);
                Debug.Log($"[Stress] 도시 로드 시작: {citySceneName}");
            }

            SpawnBosses();
            _nextWave = Time.time + 2f;
        }

        void SpawnBosses()
        {
            if (bossPrefab == null) { Debug.LogWarning("[Stress] bossPrefab 미지정"); return; }

            for (int i = 0; i < bossCount; i++)
            {
                float a = i * Mathf.PI * 2f / Mathf.Max(1, bossCount);
                Vector3 pos = transform.position
                            + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * bossRingRadius;
                var go = Instantiate(bossPrefab, pos, Quaternion.Euler(0f, -a * Mathf.Rad2Deg + 180f, 0f));
                go.name = $"StressBoss_{i}";
                go.transform.localScale = Vector3.one * bossScale;

                var anim = go.GetComponentInChildren<Animator>();
                if (anim != null) _bosses.Add(anim);
            }
            Debug.Log($"[Stress] 보스 {bossCount}기 스폰 (스케일 {bossScale})");
        }

        void Update()
        {
            float t = Time.time;

            // 보스 — 계속 돌리고, 주기적으로 애니메이션 무작위 전환
            for (int i = 0; i < _bosses.Count; i++)
            {
                var a = _bosses[i];
                if (a == null) continue;
                a.transform.root.Rotate(0f, bossTurnSpeed * Time.deltaTime * (i % 2 == 0 ? 1f : -1f), 0f);
            }

            if (t >= _nextAnimSwitch)
            {
                _nextAnimSwitch = t + Mathf.Max(0.5f, bossAnimSwitchInterval);
                for (int i = 0; i < _bosses.Count; i++)
                {
                    var a = _bosses[i];
                    if (a == null) continue;
                    string state = BossStates[Random.Range(0, BossStates.Length)];
                    int hash = Animator.StringToHash(state);
                    if (a.HasState(0, hash)) a.CrossFade(hash, 0.25f);
                }
            }

            // 경비병 웨이브 — 스폰 → killDelay 후 일괄 파괴
            if (t >= _nextWave)
            {
                _nextWave = t + Mathf.Max(1f, waveInterval);
                SpawnWave();
                _killAt = t + killDelay;
            }

            if (_killAt > 0f && t >= _killAt)
            {
                _killAt = -1f;
                KillWave();
            }
        }

        void SpawnWave()
        {
            if (guardPrefab == null) { Debug.LogWarning("[Stress] guardPrefab 미지정"); return; }

            for (int i = 0; i < guardsPerWave; i++)
            {
                float a = Random.Range(0f, Mathf.PI * 2f);
                float r = Random.Range(guardRingRadius * 0.5f, guardRingRadius);
                Vector3 pos = transform.position + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
                var go = Instantiate(guardPrefab, pos, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
                go.name = "StressGuard";
                _wave.Add(go);
            }
        }

        void KillWave()
        {
            int killed = 0;
            for (int i = 0; i < _wave.Count; i++)
            {
                var go = _wave[i];
                if (go == null) continue;

                var d = go.GetComponentInChildren<GuardDestruction>();
                if (d != null && !d.Destroyed)
                {
                    Vector3 dir = Random.insideUnitSphere; dir.y = Mathf.Abs(dir.y);
                    d.Destruct(dir.normalized);
                    killed++;
                }
                // 파편(life 8s)이 스스로 정리된 뒤 본체 제거 — 무한 누적 방지.
                Destroy(go, 10f);
            }
            _wave.Clear();
            Debug.Log($"[Stress] 웨이브 파괴 {killed}기");
        }
    }
}
