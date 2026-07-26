using System.IO;
using UnityEngine;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// 전투 수치(CombatConfig)를 파일로 저장·복원한다.
    ///
    /// CombatConfig는 static 필드라 Play를 끄거나 스크립트를 다시 컴파일하면
    /// 코드 기본값으로 돌아간다. 튜닝한 값을 잃지 않으려면 파일로 남겨야 한다.
    ///
    /// 저장된 값은 Play 시작 시 자동으로 적용된다(RuntimeInitializeOnLoadMethod).
    /// ※ Assets 폴더에 쓰므로 에디터 전용이다.
    /// </summary>
    public static class CombatTuningSave
    {
        public static string Path => "Assets/_Project/Poses/combat.json";

        [System.Serializable]
        class Data
        {
            // 평타 — 2연타 콤보 (F6)
            public int atk1W, atk1A, atk1R;
            public int atk2W, atk2A, atk2R;
            public int comboWindow;
            public int stunExtra, atk1HitStop, atk2HitStop;
            // 평타 판정 (F6 판정 탭)
            public float coneRange, coneHalfAngle, heightTol;
            public bool  sphereMelee;
            public float meleeOffset, meleeRadius, meleeEye;
            // 런지 (F1)
            public int lgWindup, lgTravel, lgRecovery, lgHitStop;
            public float lgMaxRange, lgMinRange;
            // 찌르기 연출 방식 — 예전(블링크) 값을 보존한 채 둠식과 전환
            public bool lgDoom;
            public int  lgTravelDoom;
            public float lgFovKick;
            // 락온 카메라 (View 전용 — 예지 무해)
            public float camAim, camPitchW, camLimit, camRate, camRestore;
            public bool  camLockAim;
            // 대시·찌르기 FOV 연출 (View 전용)
            public float fbDashFwd, fbDashBack, fbAttack, fbDecay, fbRoll, fbRollDecay;
            public float fbLungeIn, fbLungeOut, fbLungeRise, fbLungeFall;
            public float fbLungeInTime, fbLungeOutTime;
            // 플레이어
            public int maxHp, hitStun;
        }

        public static bool Save()
        {
            var cc = CombatCamera.Instance;
            var fb = CombatFeedback.Instance;
            var d = new Data
            {
                lgFovKick  = CombatConfig.LungeFovKick,
                camAim     = cc != null ? cc.aimHeightRatio : 0f,
                camPitchW  = cc != null ? cc.pitchWeight    : -1f,
                camLimit   = cc != null ? cc.pitchDownLimit : 0f,
                camRate    = cc != null ? cc.enterRate      : 0f,
                camRestore = cc != null ? cc.exitRestore    : -1f,
                camLockAim = cc == null || cc.lockLungeAim,
                fbDashFwd   = fb != null ? fb.dashFovFwd       : 0f,
                fbDashBack  = fb != null ? fb.dashFovBackScale : -1f,
                fbAttack    = fb != null ? fb.fovKickAttack    : 0f,
                fbDecay     = fb != null ? fb.fovKickDecay     : 0f,
                fbRoll      = fb != null ? fb.dashRoll         : -1f,
                fbRollDecay = fb != null ? fb.dashRollDecay    : 0f,
                fbLungeIn   = fb != null ? fb.lungeZoomIn      : -1f,
                fbLungeOut  = fb != null ? fb.lungeZoomOut     : -1f,
                fbLungeRise = fb != null ? fb.lungeReleaseRise : -1f,
                fbLungeFall = fb != null ? fb.lungeReleaseFall : 0f,
                fbLungeInTime  = fb != null ? fb.lungeZoomInTime  : 0f,
                fbLungeOutTime = fb != null ? fb.lungeZoomOutTime : 0f,
                atk1W = CombatConfig.Atk1WindupTicks,
                atk1A = CombatConfig.Atk1ActiveTicks,
                atk1R = CombatConfig.Atk1RecoveryTicks,
                atk2W = CombatConfig.Atk2WindupTicks,
                atk2A = CombatConfig.Atk2ActiveTicks,
                atk2R = CombatConfig.Atk2RecoveryTicks,
                comboWindow   = CombatConfig.ComboWindowTicks,
                stunExtra     = CombatConfig.StunExtraTicks,
                atk1HitStop   = CombatConfig.Atk1HitStopTicks,
                atk2HitStop   = CombatConfig.Atk2HitStopTicks,
                coneRange     = CombatConfig.AttackConeRange,
                coneHalfAngle = CombatConfig.AttackConeHalfAngle,
                heightTol     = CombatConfig.AttackHeightTolerance,
                sphereMelee   = CombatConfig.UseSphereMelee,
                meleeOffset   = CombatConfig.MeleeOffset,
                meleeRadius   = CombatConfig.MeleeRadius,
                meleeEye      = CombatConfig.MeleeEyeHeight,
                lgWindup      = CombatConfig.LungeWindupTicks,
                lgTravel      = CombatConfig.LungeTravelTicks,
                lgRecovery    = CombatConfig.LungeRecoveryTicks,
                lgHitStop     = CombatConfig.LungeHitStopTicks,
                lgMaxRange    = CombatConfig.LungeMaxRange,
                lgMinRange    = CombatConfig.LungeMinRange,
                lgDoom        = CombatConfig.LungeDoomStyle,
                lgTravelDoom  = CombatConfig.LungeTravelTicksDoom,
                maxHp         = CombatConfig.PlayerMaxHp,
                hitStun       = CombatConfig.PlayerHitStunTicks,
            };
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
                File.WriteAllText(Path, JsonUtility.ToJson(d, true), System.Text.Encoding.UTF8);
                Debug.Log($"[전투 튜닝] 저장 — 평타1 {d.atk1W}/{d.atk1A}/{d.atk1R} · " +
                          $"평타2 {d.atk2W}/{d.atk2A}/{d.atk2R} · 콤보창 {d.comboWindow}  →  {Path}");
                return true;
            }
            catch (System.Exception e) { Debug.LogWarning("[전투 튜닝] 저장 실패: " + e.Message); return false; }
        }

        /// <summary>파일이 있으면 읽어 적용. 성공 시 true.</summary>
        public static bool Load()
        {
            try
            {
                if (!File.Exists(Path)) return false;
                var d = JsonUtility.FromJson<Data>(File.ReadAllText(Path, System.Text.Encoding.UTF8));
                if (d == null || d.atk1W <= 0) return false;   // 빈 파일 방어

                CombatConfig.Atk1WindupTicks   = d.atk1W;
                CombatConfig.Atk1ActiveTicks   = d.atk1A;
                CombatConfig.Atk1RecoveryTicks = d.atk1R;
                CombatConfig.Atk2WindupTicks   = d.atk2W;
                CombatConfig.Atk2ActiveTicks   = d.atk2A;
                CombatConfig.Atk2RecoveryTicks = d.atk2R;
                CombatConfig.ComboWindowTicks  = d.comboWindow;
                // 구버전 파일엔 이 항목이 없어 0으로 읽힌다 — 0이면 코드 기본값을 유지한다.
                // (히트스톱은 0이 정상값이라 그대로 받는다)
                if (d.stunExtra > 0) CombatConfig.StunExtraTicks = d.stunExtra;
                CombatConfig.Atk1HitStopTicks  = d.atk1HitStop;
                CombatConfig.Atk2HitStopTicks  = d.atk2HitStop;

                if (d.coneRange     > 0f) CombatConfig.AttackConeRange       = d.coneRange;
                if (d.coneHalfAngle > 0f) CombatConfig.AttackConeHalfAngle   = d.coneHalfAngle;
                if (d.heightTol     > 0f) CombatConfig.AttackHeightTolerance = d.heightTol;

                // 구 판정 — 구버전 파일엔 없으므로 값이 있을 때만 덮는다
                if (d.meleeRadius > 0f)
                {
                    CombatConfig.UseSphereMelee = d.sphereMelee;
                    CombatConfig.MeleeOffset    = d.meleeOffset;
                    CombatConfig.MeleeRadius    = d.meleeRadius;
                    if (d.meleeEye > 0f) CombatConfig.MeleeEyeHeight = d.meleeEye;
                }

                CombatConfig.LungeWindupTicks   = d.lgWindup;
                if (d.lgTravel   > 0) CombatConfig.LungeTravelTicks   = d.lgTravel;
                if (d.lgRecovery > 0) CombatConfig.LungeRecoveryTicks = d.lgRecovery;
                if (d.lgHitStop  > 0) CombatConfig.LungeHitStopTicks  = d.lgHitStop;
                if (d.lgTravelDoom > 0)
                {
                    CombatConfig.LungeDoomStyle       = d.lgDoom;
                    CombatConfig.LungeTravelTicksDoom = d.lgTravelDoom;
                }
                if (d.lgFovKick > 0f) CombatConfig.LungeFovKick = d.lgFovKick;

                // 락온 카메라 — 구버전 파일엔 없으므로 값이 있을 때만 덮는다
                var cc = CombatCamera.Instance;
                if (cc != null && d.camAim > 0f)
                {
                    cc.doomStyle      = d.lgDoom;
                    cc.aimHeightRatio = d.camAim;
                    if (d.camPitchW  >= 0f) cc.pitchWeight    = d.camPitchW;
                    if (d.camLimit    > 0f) cc.pitchDownLimit = d.camLimit;
                    if (d.camRate     > 0f) cc.enterRate      = d.camRate;
                    if (d.camRestore >= 0f) cc.exitRestore    = d.camRestore;
                    cc.lockLungeAim = d.camLockAim;
                }
                // 대시·찌르기 FOV 연출 — 구버전 파일엔 없으므로 값이 있을 때만 덮는다
                var fb = CombatFeedback.Instance;
                if (fb != null && d.fbDashFwd > 0f)
                {
                    fb.dashFovFwd = d.fbDashFwd;
                    if (d.fbDashBack  >= 0f) fb.dashFovBackScale  = d.fbDashBack;
                    if (d.fbAttack     > 0f) fb.fovKickAttack     = d.fbAttack;
                    if (d.fbDecay      > 0f) fb.fovKickDecay      = d.fbDecay;
                    if (d.fbRoll      >= 0f) fb.dashRoll          = d.fbRoll;
                    if (d.fbRollDecay  > 0f) fb.dashRollDecay     = d.fbRollDecay;
                    if (d.fbLungeIn   >= 0f) fb.lungeZoomIn       = d.fbLungeIn;
                    if (d.fbLungeOut  >= 0f) fb.lungeZoomOut      = d.fbLungeOut;
                    if (d.fbLungeRise >= 0f) fb.lungeReleaseRise = d.fbLungeRise;
                    if (d.fbLungeFall  > 0f) fb.lungeReleaseFall = d.fbLungeFall;
                    if (d.fbLungeInTime  > 0f) fb.lungeZoomInTime  = d.fbLungeInTime;
                    if (d.fbLungeOutTime > 0f) fb.lungeZoomOutTime = d.fbLungeOutTime;
                }

                if (d.lgMaxRange > 0f) CombatConfig.LungeMaxRange = d.lgMaxRange;
                if (d.lgMinRange >= 0f) CombatConfig.LungeMinRange = d.lgMinRange;

                if (d.maxHp > 0) CombatConfig.PlayerMaxHp = d.maxHp;
                CombatConfig.PlayerHitStunTicks = d.hitStun;
                return true;
            }
            catch (System.Exception e) { Debug.LogWarning("[전투 튜닝] 불러오기 실패: " + e.Message); return false; }
        }

        public static bool Exists => File.Exists(Path);

        public static void Delete()
        {
            try { if (File.Exists(Path)) { File.Delete(Path); Debug.Log("[전투 튜닝] 저장 파일 삭제 — 다음 Play는 코드 기본값"); } }
            catch (System.Exception e) { Debug.LogWarning("[전투 튜닝] 삭제 실패: " + e.Message); }
        }

        /// <summary>Play 시작 시 자동 적용 — Main.Start보다 먼저 돈다.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void AutoLoad()
        {
            if (Load()) Debug.Log("[전투 튜닝] 저장값 적용 — " + Path);
        }
    }
}
