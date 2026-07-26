using System.Collections.Generic;
using UnityEngine;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// 보스(빛나는 구 코어) 뷰. 순수 연출 — sim 무영향. EntityViews가 매 프레임 <see cref="Set"/>로
    /// (상태, 발사점, 빔방향)을 넘긴다.
    ///
    ///  · 오브: 발광 구(URP Unlit + HDR 색). 차지/발사 중 더 밝아진다(텔레그래프).
    ///  · 빔: <b>단면 원반 위 하위빔 다발</b>(각각 LineRenderer). 각 하위빔이 <b>독립적으로</b> 지형까지
    ///        레이캐스트(sim과 같은 DefaultRaycastLayers)해 자기 길이로 그려진다 → 단면 일부만 벽에
    ///        가려지면 그쪽 하위빔만 짧아지고 나머지는 뻗어 나간다(부분 차단이 눈에 보임).
    ///        굵기·반지름은 sim의 AIConfig.BossBeamRadius와 공유해 판정과 시각이 일치.
    ///
    /// 발광이 후광처럼 보이려면 URP 볼륨에 Bloom이 필요(없어도 밝은 색으로 보이긴 함).
    /// </summary>
    public class BossView : MonoBehaviour
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        static readonly Color OrbIdle   = new Color(2.2f, 0.15f, 0.08f);
        static readonly Color OrbActive = new Color(6.0f, 0.5f,  0.18f);
        static readonly Color OrbHidden = new Color(0.30f, 0.03f, 0.02f);   // 숨었을 때 — 투명벽 너머 실루엣만
        static readonly Color BeamCol   = new Color(6.0f, 0.35f, 0.15f);
        static readonly Color EmpCol    = new Color(0.4f, 3.5f, 6.0f);      // EMP 충격파(청백)

        Renderer orb;
        Material orbMat;
        Material beamMat;

        LineRenderer[] beams;
        Vector2[]      coords;   // 단면 원반 위 단위 좌표(반지름 0~1). 매 프레임 R·기저로 월드화.

        // ── EMP 충격파 링 (등장/재등장 순간 1회 팽창) ──
        const float EmpPulseSeconds = 0.9f;
        const float EmpPulseRadius  = 26f;
        const int   EmpRingSegments = 48;
        LineRenderer empRing;
        Material     empMat;
        Vector3      empCenter;
        float        empStartTime = -1f;   // <0 = 재생 중 아님

        EnemyState prevState;
        bool       stateSeen;   // 첫 Set 호출(=스폰 등장) 감지용

        // ── 피격 순백 플래시 ── health 감소를 감지해 짧게 흰색으로 번쩍(상태색과 무관하게 "맞았다").
        static readonly Color FlashWhite = new Color(12f, 12f, 12f);   // HDR 순백
        const float FlashDuration = 0.12f;
        int   prevHealth = int.MinValue;
        float flashT;

        // ── 사운드 ── 차징 진입 = 대문소리, 발사 '중' = 레이저 루프(반복), 발사 '끝난 뒤' = 레이저총. 2D(거리 무관).
        AudioSource audioSrc;    // 단발(PlayOneShot): 차징·발사후
        AudioClip   chargeClip, fireClip;
        const float ChargeVolume = 2.0f;   // 보스 소리는 기본 아주 크게 — 증폭(>1). 찢어지면 낮추고, 클립/믹서로 조정.
        const float FireVolume   = 2.0f;   // 발사 끝난 뒤 레이저총도 동일하게 크게.

        AudioSource beamSrc;     // 발사 '중' 반복재생(레이저 지속음). loop 소스는 volume 상한 1.0.
        AudioClip   beamClip;

        /// <summary>ReplaceView가 생성 직후 1회 호출.</summary>
        public void Init()
        {
            orb = GetComponent<Renderer>();
            orbMat = MakeGlow(OrbIdle);
            if (orb != null)
            {
                orb.sharedMaterial = orbMat;
                orb.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                orb.receiveShadows = false;
            }

            BuildCoords();
            beamMat = MakeGlow(BeamCol);
            beams = new LineRenderer[coords.Length];
            for (int i = 0; i < coords.Length; i++)
            {
                var bgo = new GameObject("~Beam" + i);
                bgo.transform.SetParent(transform, false);
                var lr = bgo.AddComponent<LineRenderer>();
                lr.useWorldSpace = true;
                lr.positionCount = 2;
                lr.numCapVertices = 2;
                lr.alignment = LineAlignment.View;
                lr.textureMode = LineTextureMode.Stretch;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;
                lr.sharedMaterial = beamMat;
                lr.enabled = false;
                beams[i] = lr;
            }

            // EMP 충격파 링 — 수평 원이 팽창하며 사그라든다(등장/재등장 텔레그래프).
            empMat = MakeGlow(EmpCol);
            var ego = new GameObject("~EmpRing");
            ego.transform.SetParent(transform, false);
            empRing = ego.AddComponent<LineRenderer>();
            empRing.useWorldSpace = true;
            empRing.loop = true;
            empRing.positionCount = EmpRingSegments;
            empRing.alignment = LineAlignment.View;
            empRing.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            empRing.receiveShadows = false;
            empRing.sharedMaterial = empMat;
            empRing.enabled = false;

            // 사운드 — 2D 소스 + 클립 로드(임포트 전이면 null → 조용히 스킵).
            audioSrc = gameObject.AddComponent<AudioSource>();
            audioSrc.playOnAwake = false;
            audioSrc.spatialBlend = 0f;   // 2D: 거리와 무관하게 크게
            audioSrc.volume = 1f;
            audioSrc.priority = 0;        // 최우선 — 다른 소리에 밀려 안 끊기게(브금 아래 안 깔림)
            chargeClip = Resources.Load<AudioClip>("Sfx/Boss/Boss_Charge");
            fireClip   = Resources.Load<AudioClip>("Sfx/Boss/Boss_Fire");

            // 발사 중 반복재생 루프 — 전용 소스. 최대 음량(1.0, loop 소스는 >1 안 됨 → 더 크게는 클립/믹서).
            beamSrc = gameObject.AddComponent<AudioSource>();
            beamSrc.playOnAwake = false;
            beamSrc.spatialBlend = 0f;
            beamSrc.loop = true;
            beamSrc.volume = 1f;
            beamSrc.priority = 0;
            beamClip = Resources.Load<AudioClip>("Sfx/Boss/Boss_Beam");
        }

        /// <summary>중앙 굵은선 1 + 바깥 링(R, 8) = 9개 하위빔 좌표.</summary>
        void BuildCoords()
        {
            var list = new List<Vector2>(9);
            list.Add(Vector2.zero);   // 중앙(굵게)
            AddRing(list, 1.0f, 8);   // 바깥 링 8
            coords = list.ToArray();
        }

        static void AddRing(List<Vector2> list, float r, int n)
        {
            for (int k = 0; k < n; k++)
            {
                float a = (Mathf.PI * 2f) * k / n;
                list.Add(new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r));
            }
        }

        /// <summary>매 프레임 구동. state==Fire면 하위빔 다발을 켜고 각자 지형까지 그린다.</summary>
        public void Set(EnemyState state, Vector3 emitter, Vector3 beamDir, int health)
        {
            bool firing   = state == EnemyState.Fire;
            bool charging = state == EnemyState.Windup;
            bool hidden   = state == EnemyState.Hide;

            // 피격 감지 → 순백 플래시(감쇠). 첫 프레임(prevHealth 미설정)은 제외.
            if (prevHealth != int.MinValue && health < prevHealth) flashT = FlashDuration;
            prevHealth = health;
            if (flashT > 0f) flashT = Mathf.Max(0f, flashT - Time.deltaTime);

            if (orbMat != null)
            {
                Color baseCol = (firing || charging) ? OrbActive : hidden ? OrbHidden : OrbIdle;
                Color col = flashT > 0f ? Color.Lerp(baseCol, FlashWhite, flashT / FlashDuration) : baseCol;
                orbMat.SetColor(BaseColorId, col);
            }

            // EMP 충격파: EMP가 켜지는 순간(= 레이저 충전 진입, Windup)마다 1회.
            if (state == EnemyState.Windup && prevState != EnemyState.Windup)
                PlayEmpPulse(emitter);

            // 사운드 — 상태 진입 순간 1회. 차징 시작 = 대문소리(크게), 발사 시작 = 레이저총.
            if (audioSrc != null)
            {
                if (state == EnemyState.Windup && prevState != EnemyState.Windup && chargeClip != null)
                    audioSrc.PlayOneShot(chargeClip, ChargeVolume);
                // 레이저총 = 발사를 '끝낸 뒤'(Fire→Recovery) 낸다.
                if (state == EnemyState.Recovery && prevState == EnemyState.Fire && fireClip != null)
                    audioSrc.PlayOneShot(fireClip, FireVolume);
            }

            // 발사 '중' 레이저 루프 — Fire 동안 반복재생, 발사 끝나는 즉시(Fire 아니면) 정지.
            if (beamSrc != null && beamClip != null)
            {
                if (state == EnemyState.Fire)
                {
                    if (!beamSrc.isPlaying) { beamSrc.clip = beamClip; beamSrc.Play(); }
                }
                else if (beamSrc.isPlaying) beamSrc.Stop();
            }

            stateSeen = true;
            prevState = state;

            if (beams == null) return;
            if (!firing)
            {
                for (int i = 0; i < beams.Length; i++)
                    if (beams[i] != null && beams[i].enabled) beams[i].enabled = false;
                return;
            }

            Vector3 dir = beamDir.sqrMagnitude > 1e-8f ? beamDir.normalized : transform.forward;
            Basis(dir, out Vector3 u, out Vector3 v);
            float R = AIConfig.BossBeamRadius;
            float range = AIConfig.BossBeamRange;

            for (int i = 0; i < beams.Length; i++)
            {
                var lr = beams[i];
                if (lr == null) continue;
                Vector3 origin = emitter + (u * coords[i].x + v * coords[i].y) * R;
                float len = range;
                if (Physics.Raycast(origin, dir, out RaycastHit hit, range,
                                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    len = hit.distance;
                lr.widthMultiplier = (i == 0) ? R * 1.6f : R * 0.5f;   // 중앙 굵게, 바깥 링은 얇게
                lr.enabled = true;
                lr.SetPosition(0, origin);
                lr.SetPosition(1, origin + dir * len);
            }
        }

        void PlayEmpPulse(Vector3 center)
        {
            empCenter = center;
            empStartTime = Time.time;
            if (empRing != null) empRing.enabled = true;
        }

        void Update()
        {
            if (empStartTime < 0f || empRing == null) return;
            float u = (Time.time - empStartTime) / EmpPulseSeconds;
            if (u >= 1f) { empRing.enabled = false; empStartTime = -1f; return; }

            float ease = 1f - (1f - u) * (1f - u);   // ease-out — 초반 빠르게 퍼지고 끝에서 느려짐
            float r = Mathf.Lerp(1.5f, EmpPulseRadius, ease);
            empRing.widthMultiplier = Mathf.Lerp(0.9f, 0f, u);   // 얇아지며 소멸(불투명 재질이라 폭으로 페이드)
            for (int k = 0; k < EmpRingSegments; k++)
            {
                float a = (Mathf.PI * 2f) * k / EmpRingSegments;
                empRing.SetPosition(k, empCenter + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r));
            }
        }

        /// <summary>축 dir에 수직인 두 단위 기저(sim BeamBasis와 동일 규칙 → 판정·시각 일치).</summary>
        static void Basis(Vector3 dir, out Vector3 u, out Vector3 v)
        {
            Vector3 refv = Mathf.Abs(dir.y) < 0.99f ? Vector3.up : Vector3.forward;
            u = Vector3.Normalize(Vector3.Cross(dir, refv));
            v = Vector3.Cross(dir, u);
        }

        static Material MakeGlow(Color hdr)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Unlit/Color");
            var m = new Material(sh);
            m.SetColor(BaseColorId, hdr);
            m.SetColor("_Color", hdr);
            return m;
        }

        void OnDestroy()
        {
            if (orbMat != null) Destroy(orbMat);
            if (beamMat != null) Destroy(beamMat);
            if (empMat != null) Destroy(empMat);
        }
    }
}
