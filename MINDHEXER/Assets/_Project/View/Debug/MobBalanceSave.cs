using System.IO;
using UnityEngine;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// 몹 밸런스 수치(AIConfig)를 파일로 저장·복원한다. F10 패널이 쓴다.
    ///
    /// AIConfig는 static 필드라 Play를 끄거나 스크립트를 다시 컴파일하면 코드 기본값으로 돌아간다.
    /// 튜닝한 값을 잃지 않으려면 파일로 남겨야 한다(CombatTuningSave와 같은 구조).
    ///
    /// 저장된 값은 Play 시작 시 자동으로 적용된다.
    /// ※ Assets 폴더에 쓰므로 에디터 전용이다.
    /// </summary>
    public static class MobBalanceSave
    {
        public static string Path => "Assets/_Project/Poses/mob_balance.json";

        [System.Serializable]
        class Data
        {
            // 근접
            public float mReach, mExtra, mHalfAngle;
            public int   mWindup, mActive, mRecovery, mDamage;
            public float enemyMoveSpeed;
            // 돌진
            public float cMinRange, cSpeed, cMaxDist, cRadiusMul, cWallStop, cChaseMul;
            public int   cWindup, cHitRec, cMissRec, cDamage;
            // 원거리
            public float rMove, rBandMin, rBandMax;
            public int   rAim, rCool, rDamage;
            // 공중
            public float fHover, fSpeed, fBandMin, fBandMax, fClearance;
            public int   fAim, fCool;
            // 신규 동작(예지 영향) — 토글과 계수를 함께 저장해 껐다 켠 상태까지 재현
            public bool  cAccelOn, fInertiaOn;
            public float cAccelK, fAccel, fDrag, fMaxSpeed;
            public float fAccelY, fDragY, fMaxSpeedY, fHoverJitter;
            // 공통
            public float projSpeed, projRadius, lead, missDeg;
            public float sepRadius, sepWeight, sepMaxPush, sepScaleMin;
            public float eyeH, torsoH;
        }

        // 코드 기본값 — 첫 진입 시 한 번 기억해 두고 "기본값" 버튼이 여기로 되돌린다.
        static Data defaults;
        static bool captured;

        static void CaptureDefaults()
        {
            if (captured) return;
            defaults = Snapshot();
            captured = true;
        }

        static Data Snapshot() => new Data
        {
            mReach = AIConfig.MeleeReach, mExtra = AIConfig.MeleeHitExtra,
            mHalfAngle = AIConfig.MeleeHitHalfAngle,
            mWindup = AIConfig.MeleeWindupTicks, mActive = AIConfig.MeleeActiveTicks,
            mRecovery = AIConfig.MeleeRecoveryTicks, mDamage = AIConfig.MeleeDamage,
            enemyMoveSpeed = SimConfig.EnemyMoveSpeed,

            cMinRange = AIConfig.ChargeMinRange, cSpeed = AIConfig.ChargeSpeed,
            cMaxDist = AIConfig.ChargeMaxDist, cRadiusMul = AIConfig.ChargeRadiusMul,
            cWallStop = AIConfig.ChargeWallStopFrac, cChaseMul = AIConfig.ChargeChaseSpeedMul,
            cWindup = AIConfig.ChargeWindupTicks, cHitRec = AIConfig.ChargeHitRecovery,
            cMissRec = AIConfig.ChargeMissRecovery, cDamage = AIConfig.ChargeDamage,

            rMove = AIConfig.RangedMoveSpeed, rBandMin = AIConfig.RangedBandMin,
            rBandMax = AIConfig.RangedBandMax, rAim = AIConfig.RangedAimTicks,
            rCool = AIConfig.RangedCooldown, rDamage = AIConfig.RangedDamage,

            fHover = AIConfig.FlyHoverOffset, fSpeed = AIConfig.FlySpeed,
            fBandMin = AIConfig.FlyBandMin, fBandMax = AIConfig.FlyBandMax,
            fClearance = AIConfig.FlyMinClearance,
            fAim = AIConfig.FlyAimTicks, fCool = AIConfig.FlyCooldown,

            cAccelOn = AIConfig.ChargeAccelOn, cAccelK = AIConfig.ChargeAccelK,
            fInertiaOn = AIConfig.FlyInertiaOn, fAccel = AIConfig.FlyAccel,
            fDrag = AIConfig.FlyDrag, fMaxSpeed = AIConfig.FlyMaxSpeed,
            fAccelY = AIConfig.FlyAccelY, fDragY = AIConfig.FlyDragY,
            fMaxSpeedY = AIConfig.FlyMaxSpeedY, fHoverJitter = AIConfig.FlyHoverJitter,

            projSpeed = AIConfig.ProjectileSpeed, projRadius = AIConfig.ProjectileRadius,
            lead = AIConfig.LeadFactor, missDeg = AIConfig.MissOffsetDeg,
            sepRadius = AIConfig.SeparationRadius, sepWeight = AIConfig.SeparationWeight,
            sepMaxPush = AIConfig.SeparationMaxPush, sepScaleMin = AIConfig.SeparationScaleMin,
            eyeH = AIConfig.EnemyEyeHeight, torsoH = AIConfig.PlayerTorso,
        };

        static void Apply(Data d)
        {
            if (d == null) return;
            AIConfig.MeleeReach = d.mReach; AIConfig.MeleeHitExtra = d.mExtra;
            AIConfig.MeleeHitHalfAngle = d.mHalfAngle;
            AIConfig.MeleeWindupTicks = d.mWindup; AIConfig.MeleeActiveTicks = d.mActive;
            AIConfig.MeleeRecoveryTicks = d.mRecovery; AIConfig.MeleeDamage = d.mDamage;
            SimConfig.EnemyMoveSpeed = d.enemyMoveSpeed;

            AIConfig.ChargeMinRange = d.cMinRange; AIConfig.ChargeSpeed = d.cSpeed;
            AIConfig.ChargeMaxDist = d.cMaxDist; AIConfig.ChargeRadiusMul = d.cRadiusMul;
            AIConfig.ChargeWallStopFrac = d.cWallStop; AIConfig.ChargeChaseSpeedMul = d.cChaseMul;
            AIConfig.ChargeWindupTicks = d.cWindup; AIConfig.ChargeHitRecovery = d.cHitRec;
            AIConfig.ChargeMissRecovery = d.cMissRec; AIConfig.ChargeDamage = d.cDamage;

            AIConfig.RangedMoveSpeed = d.rMove; AIConfig.RangedBandMin = d.rBandMin;
            AIConfig.RangedBandMax = d.rBandMax; AIConfig.RangedAimTicks = d.rAim;
            AIConfig.RangedCooldown = d.rCool; AIConfig.RangedDamage = d.rDamage;

            AIConfig.FlyHoverOffset = d.fHover; AIConfig.FlySpeed = d.fSpeed;
            AIConfig.FlyBandMin = d.fBandMin; AIConfig.FlyBandMax = d.fBandMax;
            AIConfig.FlyMinClearance = d.fClearance;
            AIConfig.FlyAimTicks = d.fAim; AIConfig.FlyCooldown = d.fCool;

            AIConfig.ChargeAccelOn = d.cAccelOn; AIConfig.ChargeAccelK = d.cAccelK;
            AIConfig.FlyInertiaOn = d.fInertiaOn; AIConfig.FlyAccel = d.fAccel;
            AIConfig.FlyDrag = d.fDrag; AIConfig.FlyMaxSpeed = d.fMaxSpeed;
            AIConfig.FlyAccelY = d.fAccelY; AIConfig.FlyDragY = d.fDragY;
            AIConfig.FlyMaxSpeedY = d.fMaxSpeedY; AIConfig.FlyHoverJitter = d.fHoverJitter;

            AIConfig.ProjectileSpeed = d.projSpeed; AIConfig.ProjectileRadius = d.projRadius;
            AIConfig.LeadFactor = d.lead; AIConfig.MissOffsetDeg = d.missDeg;
            AIConfig.SeparationRadius = d.sepRadius; AIConfig.SeparationWeight = d.sepWeight;
            AIConfig.SeparationMaxPush = d.sepMaxPush; AIConfig.SeparationScaleMin = d.sepScaleMin;
            AIConfig.EnemyEyeHeight = d.eyeH; AIConfig.PlayerTorso = d.torsoH;
        }

        public static void Save()
        {
            CaptureDefaults();
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
                File.WriteAllText(Path, JsonUtility.ToJson(Snapshot(), true), System.Text.Encoding.UTF8);
                Debug.Log("[몹 밸런스] 저장: " + Path);
            }
            catch (System.Exception e) { Debug.LogError("[몹 밸런스] 저장 실패: " + e.Message); }
        }

        public static bool Load()
        {
            CaptureDefaults();
            try
            {
                if (!File.Exists(Path)) { Debug.Log("[몹 밸런스] 저장 파일 없음 — 코드 기본값 사용"); return false; }
                Apply(JsonUtility.FromJson<Data>(File.ReadAllText(Path, System.Text.Encoding.UTF8)));
                Debug.Log("[몹 밸런스] 불러옴: " + Path);
                return true;
            }
            catch (System.Exception e) { Debug.LogError("[몹 밸런스] 불러오기 실패: " + e.Message); return false; }
        }

        /// <summary>코드에 적힌 기본값으로 되돌린다(파일은 안 건드림).</summary>
        public static void ResetToDefaults()
        {
            CaptureDefaults();
            Apply(defaults);
            Debug.Log("[몹 밸런스] 코드 기본값으로 복원");
        }

        /// <summary>Play 시작 시 저장된 값을 자동 적용.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void AutoLoad()
        {
            CaptureDefaults();   // 파일을 덮기 전에 코드 기본값을 먼저 기억
            Load();
        }
    }
}
