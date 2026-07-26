using UnityEngine;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// 발이 땅에 닿는 순간을 잡아 스파크·소리를 낸다.
    ///
    /// ★ 애니메이션 이벤트를 쓰지 않는다 —
    ///   클립마다 이벤트를 심으면 몹 종류·클립 수만큼 손이 가고, 재생 배속이 바뀌면 어긋난다.
    ///   대신 <b>발 본의 높이가 내려가다 올라가기 시작하는 순간</b>(최저점)을 접지로 본다.
    ///   어떤 클립이든, 배속이 어떻든 자동으로 맞는다.
    ///
    /// 비용: 몹당 발 본 2개의 Y값 비교뿐. Physics 캐스트도 추가 컴포넌트도 없다.
    /// </summary>
    public struct FootstepDetector
    {
        public Transform left, right;
        public bool bound;

        // 발별 상태 — [0]=왼발 [1]=오른발
        float prevY0, prevY1;      // 직전 높이
        float prevDy0, prevDy1;    // 직전 높이 변화(부호가 뒤집히는 지점이 최저점)
        float cool0, cool1;        // 연속 발동 방지
        bool  primed;              // 첫 프레임은 기준만 잡는다

        public void Bind(Transform root)
        {
            bound = true;
            left = right = null;
            if (root == null) return;

            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                // ToeBase가 있으면 그쪽이 실제 접지점에 더 가깝다
                string n = t.name;
                if (Has(n, "LeftToeBase"))  left  = t;
                else if (Has(n, "RightToeBase")) right = t;
                else if (left  == null && Has(n, "LeftFoot"))  left  = t;
                else if (right == null && Has(n, "RightFoot")) right = t;
            }
            primed = false;
            cool0 = cool1 = 0f;
        }

        static bool Has(string s, string k) => s.IndexOf(k, System.StringComparison.OrdinalIgnoreCase) >= 0;

        public bool HasFeet => left != null || right != null;

        /// <summary>
        /// 접지를 감지해 이벤트를 낸다. LateSync에서 매 프레임 호출.
        /// speed = 몹의 실제 수평 속도(정지 중엔 발이 안 움직이므로 걸러낸다).
        /// </summary>
        public void Tick(in EnemySim e, float speed, float dt, in FootstepSettings s, Vector3 bodyPos)
        {
            if (!HasFeet) return;
            if (cool0 > 0f) cool0 -= dt;
            if (cool1 > 0f) cool1 -= dt;

            // 공중이거나 거의 멈춰 있으면 발을 딛지 않는다
            if (!e.grounded || speed < s.minSpeed) { primed = false; return; }

            Step(left,  ref prevY0, ref prevDy0, ref cool0, in s, bodyPos, primed);
            Step(right, ref prevY1, ref prevDy1, ref cool1, in s, bodyPos, primed);
            primed = true;
        }

        void Step(Transform foot, ref float prevY, ref float prevDy, ref float cool,
                  in FootstepSettings s, Vector3 bodyPos, bool ready)
        {
            if (foot == null) return;
            float y = foot.position.y;
            float dy = y - prevY;
            prevY = y;

            if (!ready) { prevDy = dy; return; }

            // ★ 최저점: 내려가던(음수) 변화가 올라가는(양수) 쪽으로 뒤집히는 순간
            bool bottom = prevDy < -s.minFall && dy >= 0f;
            prevDy = dy;

            if (!bottom || cool > 0f) return;
            // 발이 몸 밑동 근처일 때만 — 다리를 높이 들었다 내리는 중간을 오탐하지 않게
            if (y - bodyPos.y > s.maxFootHeight) return;

            cool = s.cooldown;
            FootstepEvents.Fire(foot.position, s);
        }
    }

    /// <summary>발자국 연출 설정 — 콘솔 step 명령으로 조절.</summary>
    [System.Serializable]
    public struct FootstepSettings
    {
        public bool  enabled;
        public float minSpeed;       // 이 속도 미만이면 발을 안 딛는 것으로 본다
        public float minFall;        // 최저점 판정에 필요한 하강량(잔떨림 무시)
        public float maxFootHeight;  // 발이 몸 밑동보다 이만큼 위면 무시
        public float cooldown;       // 같은 발의 연속 발동 방지(초)

        [Header("스파크")]
        public int   sparkCount;     // 전선 스파크(3~7)보다 작게
        public float sparkSize;
        public float sparkSpeed;
        public float sparkLife;
        public float maxDistance;    // 이 밖에선 생략(멀면 안 보인다)

        public static FootstepSettings Default => new FootstepSettings
        {
            enabled = true,
            minSpeed = 0.6f,
            minFall = 0.0015f,
            maxFootHeight = 0.45f,
            cooldown = 0.18f,

            sparkCount = 3,
            sparkSize = 0.55f,      // 전선보다 작게
            sparkSpeed = 0.5f,
            sparkLife = 0.55f,
            maxDistance = 22f,
        };
    }

    /// <summary>
    /// 발 딛는 순간의 연출 진입점. 스파크는 지금 붙어 있고, <b>소리는 훅만 만들어 뒀다</b> —
    /// 사운드 에셋이 준비되면 <see cref="SoundHook"/>에 재생 함수를 꽂으면 된다.
    /// </summary>
    public static class FootstepEvents
    {
        /// <summary>발 딛는 순간 호출된다(월드 위치). 사운드 시스템이 여기 연결한다.</summary>
        public static System.Action<Vector3> SoundHook;

        /// <summary>훅이 없을 때 임시 합성음이라도 낼지 — 기본 꺼짐(에셋 준비 전 소음 방지).</summary>
        public static bool UseFallbackSound = false;

        public static void Fire(Vector3 pos, in FootstepSettings s)
        {
            var camT = Camera.main != null ? Camera.main.transform : null;
            if (camT != null && Vector3.Distance(camT.position, pos) > s.maxDistance) return;

            WireSparks.EmitScaled(pos, s.sparkCount, s.sparkSize, s.sparkSpeed, s.sparkLife);

            if (SoundHook != null) SoundHook(pos);
            else if (UseFallbackSound) CombatAudio.EnemyStep(pos);   // 위치 기반(가까운 적만 크게)
        }
    }
}
