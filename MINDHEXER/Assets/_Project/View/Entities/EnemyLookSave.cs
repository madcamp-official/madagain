using System;
using System.IO;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 몹 발광·오염 설정 저장/불러오기.
    /// F8 패널에서 맞춘 값을 파일로 남겨 Play를 꺼도 유지되게 한다.
    /// (머티리얼 프로퍼티가 아니라 런타임 설정이라 인스펙터로는 안 남는다)
    /// </summary>
    [Serializable]
    public class EnemyVisualSave
    {
        // ── 발광 ──
        public float baseIntensity, windupIntensity, attackIntensity;
        public float chargeIntensity, aimIntensity, gloryIntensity;
        public float[] baseColor, windupColor, gloryColor;
        public float followSpeed, chargePulseSpeed, stunFlickerSpeed;
        public float flashIntensity, flashDecay, colorJitter;
        public float distanceBoost, boostStart, boostRange;

        // ── 오염 ──
        public float grungeScale;                  // 전체 배수(개체별 난수에 곱해짐)
        public float[] rustColor, rustDark;        // 머티리얼 색
        public float rustBoost, darken, grungeTexScale, streak;
        public float cleanSmoothness, rustSmoothness, rustMetallic;

        // ── 빨강 추출 ──
        public float redThreshold, redSoftness;

        public static string Path =>
            System.IO.Path.Combine(Application.dataPath, "_Project/Poses/enemy_visual.json");

        static float[] C(Color c) => new[] { c.r, c.g, c.b };
        static Color   C(float[] a, Color fallback) =>
            a != null && a.Length >= 3 ? new Color(a[0], a[1], a[2]) : fallback;

        public static EnemyVisualSave From(in EnemyGlowSettings g, in EnemyDirtSettings d)
        {
            return new EnemyVisualSave
            {
                baseIntensity = g.baseIntensity, windupIntensity = g.windupIntensity,
                attackIntensity = g.attackIntensity, chargeIntensity = g.chargeIntensity,
                aimIntensity = g.aimIntensity, gloryIntensity = g.gloryIntensity,
                baseColor = C(g.baseColor), windupColor = C(g.windupColor), gloryColor = C(g.gloryColor),
                followSpeed = g.followSpeed, chargePulseSpeed = g.chargePulseSpeed,
                stunFlickerSpeed = g.stunFlickerSpeed,
                flashIntensity = g.flashIntensity, flashDecay = g.flashDecay,
                colorJitter = g.colorJitter,
                distanceBoost = g.distanceBoost, boostStart = g.boostStart, boostRange = g.boostRange,
                grungeScale = g.grungeScale,

                rustColor = C(d.rustColor), rustDark = C(d.rustDark),
                rustBoost = d.rustBoost, darken = d.darken,
                grungeTexScale = d.grungeTexScale, streak = d.streak,
                cleanSmoothness = d.cleanSmoothness, rustSmoothness = d.rustSmoothness,
                rustMetallic = d.rustMetallic,
                redThreshold = d.redThreshold, redSoftness = d.redSoftness,
            };
        }

        public void Into(ref EnemyGlowSettings g, ref EnemyDirtSettings d)
        {
            var gd = EnemyGlowSettings.Default;
            g.baseIntensity = baseIntensity; g.windupIntensity = windupIntensity;
            g.attackIntensity = attackIntensity; g.chargeIntensity = chargeIntensity;
            g.aimIntensity = aimIntensity; g.gloryIntensity = gloryIntensity;
            g.baseColor = C(baseColor, gd.baseColor);
            g.windupColor = C(windupColor, gd.windupColor);
            g.gloryColor = C(gloryColor, gd.gloryColor);
            g.followSpeed = followSpeed; g.chargePulseSpeed = chargePulseSpeed;
            g.stunFlickerSpeed = stunFlickerSpeed;
            g.flashIntensity = flashIntensity; g.flashDecay = flashDecay;
            g.colorJitter = colorJitter;
            g.distanceBoost = distanceBoost; g.boostStart = boostStart; g.boostRange = boostRange;
            g.grungeScale = grungeScale;

            var dd = EnemyDirtSettings.Default;
            d.rustColor = C(rustColor, dd.rustColor);
            d.rustDark = C(rustDark, dd.rustDark);
            d.rustBoost = rustBoost; d.darken = darken;
            d.grungeTexScale = grungeTexScale; d.streak = streak;
            d.cleanSmoothness = cleanSmoothness; d.rustSmoothness = rustSmoothness;
            d.rustMetallic = rustMetallic;
            d.redThreshold = redThreshold; d.redSoftness = redSoftness;
        }

        public static bool Save(in EnemyGlowSettings g, in EnemyDirtSettings d)
        {
            try
            {
                var dir = System.IO.Path.GetDirectoryName(Path);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(Path, JsonUtility.ToJson(From(in g, in d), true), System.Text.Encoding.UTF8);
                return true;
            }
            catch (Exception e) { Debug.LogError("[몹 비주얼] 저장 실패: " + e.Message); return false; }
        }

        public static bool Load(ref EnemyGlowSettings g, ref EnemyDirtSettings d)
        {
            try
            {
                if (!File.Exists(Path)) return false;
                var s = JsonUtility.FromJson<EnemyVisualSave>(File.ReadAllText(Path, System.Text.Encoding.UTF8));
                if (s == null) return false;
                s.Into(ref g, ref d);
                return true;
            }
            catch (Exception e) { Debug.LogError("[몹 비주얼] 불러오기 실패: " + e.Message); return false; }
        }

        public static bool Exists => File.Exists(Path);
    }

    /// <summary>
    /// 오염·반사 설정 — 머티리얼 프로퍼티로 매 프레임 밀어넣어 몹 4종에 동시 적용한다.
    /// (머티리얼을 하나씩 인스펙터로 만지지 않아도 되게)
    /// </summary>
    [Serializable]
    public struct EnemyDirtSettings
    {
        public Color rustColor;        // 넓게 퍼지는 밝은 황토
        public Color rustDark;         // 뭉치는 탁한 회백
        public float rustBoost;        // 녹이 얼마나 밝게 올라오는가
        public float darken;           // 짙은 부분 어둡게(어두운 맵에선 낮게)
        public float grungeTexScale;   // 얼룩 크기(낮을수록 큼)
        public float streak;           // 흘러내린 자국

        // 어둠 속에서 색보다 잘 읽히는 신호 — 반사 대비
        public float cleanSmoothness;
        public float rustSmoothness;
        public float rustMetallic;

        // 빨강 추출(발광 마스크)
        public float redThreshold;
        public float redSoftness;

        public static EnemyDirtSettings Default => new EnemyDirtSettings
        {
            rustColor = new Color(0.72f, 0.55f, 0.34f),
            rustDark  = new Color(0.55f, 0.50f, 0.45f),
            rustBoost = 1.35f,
            darken = 0.10f,
            grungeTexScale = 9f,
            streak = 0.6f,
            cleanSmoothness = 0.85f,
            rustSmoothness = 0.04f,
            rustMetallic = 0.05f,
            redThreshold = 0.25f,
            redSoftness = 0.15f,
        };
    }
}
