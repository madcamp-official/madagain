using System.Collections.Generic;
using UnityEngine;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// 처치 연출: 몸을 슬라이스해 "잘린 단면"을 보여준다. ★ combat 소유·독립. 읽기 전용.
    ///
    /// 두 경로:
    ///  1) 일반 처치(alive true→false, 글로리킬 아님): 죽는 프레임에 카메라 대각선 평면으로 2조각(즉시 물리).
    ///  2) 대형몹 글로리킬(gloryStage 1→2→3): 단계식 3조각.
    ///     stage1=위쪽 절단 → 2조각(붙어서 흔들림), stage2=아래쪽 절단 → 3조각(여전히 흔들림),
    ///     stage3=폭발(조각 물리 방출 + 피). 절단면은 위/아래로 오프셋한 평행면이라 교차 없이 3조각.
    /// 크기는 죽은 적 개별 height/radius 반영(대형몹 3배). EntityViews는 gloryStage>0면 캡슐을 끔.
    /// </summary>
    public class Dismemberment : MonoBehaviour
    {
        readonly bool[] prevAlive = new bool[SimConfig.MaxEnemies];
        readonly bool[] seen      = new bool[SimConfig.MaxEnemies];
        readonly byte[] prevGlory = new byte[SimConfig.MaxEnemies];
        readonly GloryVictim[] victims = new GloryVictim[SimConfig.MaxEnemies];
        // ★ sim이 죽은 슬롯을 재사용하므로(SimWorld.AddEnemy) 인덱스가 아니라 id로 점유자를 추적한다.
        //   슬롯 재사용이 View 1프레임 내에 일어나면 alive true→false 전이가 안 보여 시체를 놓치던 버그 수정.
        readonly int[]          prevId       = new int[SimConfig.MaxEnemies];
        readonly Vector3[]      prevPos      = new Vector3[SimConfig.MaxEnemies];   // 떠난 적의 마지막 자리
        readonly float[]        prevYaw      = new float[SimConfig.MaxEnemies];
        readonly float[]        prevHeight   = new float[SimConfig.MaxEnemies];
        readonly float[]        prevScale    = new float[SimConfig.MaxEnemies];
        readonly MobilityType[] prevMobility = new MobilityType[SimConfig.MaxEnemies];
        readonly CombatType[]   prevCombat   = new CombatType[SimConfig.MaxEnemies];
        int slashParity;

        Mesh capsuleSrc;                 // 그런트 기준 캡슐(슬라이스 원본, Traversal 폴백). 대형은 transform 스케일로 확대.
        Mesh flyingSrc, chargeSrc, meleeSrc, rangedSrc;   // 실물 모델 원본(EntityViews 프리팹을 SimConfig.EnemyHeight 절대 크기로 구움)
        Material shellMat, fleshMat;                        // 캡슐 겉면/단면 머티리얼
        Material flyingShellMat, chargeShellMat, meleeShellMat, rangedShellMat;  // 실물 모델 겉면 = 원래 텍스처(잘려도 그 몹처럼 보이도록)
        ParticleSystem blood;

        const float CorpseLife = 5f;
        // EntityViews의 같은 이름 상수들과 반드시 같은 값 — 살아있을 때 보이는 크기와 시체 크기를 맞추기 위함.
        const float ChargeVisualScaleMul = 1.4f;
        const float MeleeVisualScaleMul  = 1.4f;
        const float RangedVisualScaleMul = 1.4f;
        const float FlyingVisualScaleMul = 1.2f;
        // 절단 평면 법선: 위 + 정면(로컬) 살짝 → 사선. 두 절단은 이 법선을 위/아래로만 오프셋(평행 → 3조각).
        static readonly Vector3 CutN = (Vector3.up + Vector3.forward * 0.35f).normalized;

        class GloryVictim
        {
            public readonly List<CorpsePiece> pieces = new();
            public CorpsePiece restPiece;   // stage1의 '나머지'(stage2에서 재분할 후 제거)
            public Mesh restMesh;
            public Vector3 center; public Quaternion rot; public float scale;
            public Material shell;
        }

        void Awake()
        {
            capsuleSrc = BuildScaledCapsule();
            shellMat = Mat(new Color(0.5f, 0.05f, 0.05f), false);
            // ★ 절단면 — 로봇이므로 살점이 아니라 <b>칼에 지져진 금속 단면</b>이다.
            //   어두운 금속 바탕 + HDR 주황 발광 → 갓 잘려 달아오른 자리처럼 보인다.
            //   (HDR 1 초과라야 Bloom threshold 1.05에 걸려 번진다)
            fleshMat = Mat(new Color(0.10f, 0.10f, 0.11f), true);
            if (fleshMat.HasProperty("_Metallic"))   fleshMat.SetFloat("_Metallic", 0.9f);
            if (fleshMat.HasProperty("_Smoothness")) fleshMat.SetFloat("_Smoothness", 0.55f);
            if (fleshMat.HasProperty("_EmissionColor"))
            {
                fleshMat.EnableKeyword("_EMISSION");
                fleshMat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                fleshMat.SetColor("_EmissionColor", new Color(3.6f, 0.75f, 0.12f));   // 백열 주황
            }
            BuildRealMeshSources();
            BuildBlood();
        }

        // EntityViews와 같은 프리팹(FlyingEnemy/ChargeEnemy)에서 정적 메시를 한 번 구워둔다.
        // 실제 텍스처 머티리얼도 같이 기억해뒀다가 시체 겉면에 그대로 써서 "그 몹이 잘린" 것처럼 보이게 한다.
        void BuildRealMeshSources()
        {
            // ★ 비행몹은 예전에 리깅 없는 정적 메시(MeshFilter)였으나, 리깅된 모델로 교체됐다.
            //   MeshFilter만 찾으면 flyingSrc가 null로 남아 <b>비행몹을 죽여도 근접몹 시체가 나온다</b>.
            //   그래서 스킨 메시를 먼저 보고, 없을 때만 옛 경로(정적 메시)로 떨어진다.
            flyingSrc = BakeHumanoidPrefab("Enemies/FlyingEnemy", out flyingShellMat);
            if (flyingSrc == null)
            {
                var flyingPrefab = Resources.Load<GameObject>("Enemies/FlyingEnemy");
                if (flyingPrefab != null)
                {
                    var t = Instantiate(flyingPrefab);
                    var mf = t.GetComponentInChildren<MeshFilter>();
                    var mr = t.GetComponentInChildren<MeshRenderer>();
                    if (mf != null && mr != null)
                    {
                        // 임포트 시 축변환으로 붙은 로컬 회전만 반영(Animator가 없어 언제 재도 안전).
                        flyingSrc = BakeStaticSubmesh(mf.sharedMesh, mf.transform.localRotation, SimConfig.EnemyHeight);
                        flyingShellMat = mr.sharedMaterial;
                    }
                    Destroy(t);
                }
            }

            chargeSrc = BakeHumanoidPrefab("Enemies/ChargeEnemy", out chargeShellMat);
            meleeSrc  = BakeHumanoidPrefab("Enemies/MeleeEnemy",  out meleeShellMat);
            rangedSrc = BakeHumanoidPrefab("Enemies/RangedEnemy", out rangedShellMat);
        }

        /// <summary>
        /// ★ Humanoid Animator가 붙으면 본 계층의 localScale이 아바타 정규화로 즉시 바뀌어
        /// 부모 트랜스폼 체인을 곱하는 방식(TransformPoint)은 신뢰할 수 없다(테스트 중 정점이
        /// 거의 원점(0,0,0)으로 뭉개지는 걸로 확인). BakeMesh 결과 자체가 이미 포즈가 반영된
        /// 자체 좌표라 회전 보정 없이 자기 바운즈 기준으로만 재배율한다.
        /// </summary>
        static Mesh BakeHumanoidPrefab(string resourcePath, out Material shellMat)
        {
            shellMat = null;
            var prefab = Resources.Load<GameObject>(resourcePath);
            if (prefab == null) return null;
            var t = Instantiate(prefab);
            var smr = t.GetComponentInChildren<SkinnedMeshRenderer>();
            Mesh result = null;
            if (smr != null)
            {
                var baked = new Mesh();
                smr.BakeMesh(baked);   // 현재(기본) 포즈 스냅샷 — 스키닝 없는 정적 메시로 변환
                result = BakeStaticSubmesh(baked, Quaternion.identity, SimConfig.EnemyHeight);
                shellMat = smr.sharedMaterial;
            }
            Destroy(t);
            return result;
        }

        /// <summary>
        /// 정점에 보정 회전만 적용한 뒤, 메시 자체의 바운즈로 재중심화·재배율해 targetHeight로 굽는다.
        /// 부모 트랜스폼 체인(스케일 포함)을 곱하지 않으므로 Humanoid 아바타의 런타임 스케일 정규화에
        /// 영향받지 않는다. 캡슐 원본처럼 "로컬 원점 = 메시 중심"이어야 SliceCorpse/GloryStart의
        /// 절단면 계산(Vector3.zero 기준)이 그대로 맞아떨어진다.
        /// </summary>
        static Mesh BakeStaticSubmesh(Mesh src, Quaternion correctiveRotation, float targetHeight)
        {
            Vector3[] v = src.vertices;
            Vector3[] n = src.normals;
            bool hasN = n != null && n.Length == v.Length;
            var rotV = new Vector3[v.Length];
            var rotN = hasN ? new Vector3[v.Length] : null;
            for (int i = 0; i < v.Length; i++)
            {
                rotV[i] = correctiveRotation * v[i];
                if (hasN) rotN[i] = (correctiveRotation * n[i]).normalized;
            }

            Vector3 min = rotV[0], max = rotV[0];
            for (int i = 1; i < rotV.Length; i++) { min = Vector3.Min(min, rotV[i]); max = Vector3.Max(max, rotV[i]); }
            Vector3 center = (min + max) * 0.5f;
            float height = Mathf.Max(max.y - min.y, 1e-4f);
            float k = targetHeight / height;

            var outV = new Vector3[rotV.Length];
            for (int i = 0; i < rotV.Length; i++) outV[i] = (rotV[i] - center) * k;

            var m = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            m.vertices = outV;
            if (hasN) m.normals = rotN; else m.RecalculateNormals();
            // ★ UV를 반드시 옮겨야 한다. 여기서 빠지면 시체가 텍스처의 한 점(0,0)만 샘플링해
            //   <b>단색 회색 덩어리</b>가 되고, 베이스 텍스처에서 빨강을 골라 빛내는 셰이더도
            //   추출할 색이 없어 발광이 통째로 죽는다. 슬라이서를 고쳐도 원본이 이러면 소용없다.
            var uv = src.uv;
            if (uv != null && uv.Length == v.Length) m.uv = uv;
            m.triangles = src.triangles;
            m.RecalculateBounds();
            m.RecalculateTangents();   // 노멀맵이 뒤집히지 않게
            return m;
        }

        /// <summary>피해자 이동/전투 방식에 맞는 슬라이스 원본 메시·겉면 머티리얼·최종 스케일을 고른다(없으면 캡슐 폴백).</summary>
        void GetVictimAssets(MobilityType mobility, CombatType combat, float radiusScale, float height,
                              out Mesh src, out Material shell, out float scale)
        {
            // ★ 배율은 EntityViews의 같은 이름 상수와 반드시 일치해야 한다 — 어긋나면 죽는 순간 시체 크기가 튄다.
            float baseScale = height / SimConfig.EnemyHeight;
            if (mobility == MobilityType.Flying && flyingSrc != null)
            { src = flyingSrc; shell = flyingShellMat; scale = baseScale * FlyingVisualScaleMul; return; }
            if (mobility == MobilityType.Charge && chargeSrc != null)
            // 돌진몹은 몸집 확대(ChargeBodyMul)가 렌더 전용이라 히트박스(height)엔 없다 → 시체도 여기서 곱해 살아있을 때와 크기를 맞춘다.
            { src = chargeSrc; shell = chargeShellMat; scale = baseScale * ChargeVisualScaleMul * AIConfig.ChargeBodyMul; return; }
            // 층이동(Traversal)도 EntityViews와 동일하게 combat 기준으로 몸체를 고른다(총/칼 모델 일치).
            if (combat == CombatType.Ranged && rangedSrc != null)
            { src = rangedSrc; shell = rangedShellMat; scale = baseScale * RangedVisualScaleMul; return; }
            if (combat != CombatType.Ranged && meleeSrc != null)
            { src = meleeSrc; shell = meleeShellMat; scale = baseScale * MeleeVisualScaleMul; return; }
            src = capsuleSrc; shell = shellMat; scale = radiusScale;
        }

        void Update()
        {
            var main = Main.Instance;
            if (main == null) return;
            ref readonly SimWorld w = ref main.World;

            for (int i = 0; i < w.enemyCount; i++)
            {
                ref readonly EnemySim e = ref w.enemies[i];
                byte gs = e.combat.gloryStage;
                bool sameId = seen[i] && e.id == prevId[i];

                if (seen[i])
                {
                    // 이전 점유자가 떠남 = 죽어서 슬롯에 남았거나(dead), 슬롯이 재사용됨(id 변경).
                    // 글로리킬이 아니었으면(prevGlory==0) 마지막 자리에 2조각 슬라이스.
                    bool departed = prevAlive[i] && (!e.alive || !sameId);
                    if (departed && prevGlory[i] == 0)
                    {
                        if (sameId) SliceCorpse(e.pos, e.yaw, e.height, e.radius / SimConfig.EnemyRadius, e.ai.mobility, e.ai.combat);
                        else        SliceCorpse(prevPos[i], prevYaw[i], prevHeight[i], prevScale[i], prevMobility[i], prevCombat[i]);
                    }

                    // 글로리 단계 전이는 동일 적일 때만. 재사용이면 이전 글로리 잔여 정리.
                    if (sameId) { if (gs != prevGlory[i]) GloryTransition(i, gs, in e); }
                    else if (prevGlory[i] != 0) GloryCleanup(i);
                }
                seen[i]      = true;
                prevAlive[i] = e.alive;
                prevGlory[i] = gs;
                prevId[i]    = e.id;
                prevPos[i]   = e.pos;
                prevYaw[i]   = e.yaw;
                prevHeight[i] = e.height;
                prevScale[i] = e.radius / SimConfig.EnemyRadius;
                prevMobility[i] = e.ai.mobility;
                prevCombat[i]   = e.ai.combat;
            }
        }

        // ───────────────────────── 글로리킬 단계식 ─────────────────────────

        void GloryTransition(int i, byte to, in EnemySim e)
        {
            switch (to)
            {
                case 1: GloryStart(i, in e); break;
                case 2: GlorySecondCut(i);   break;
                case 3: GloryExplode(i);     break;
                default: GloryCleanup(i);    break;   // 0 등: 남은 조각 정리
            }
        }

        void GloryStart(int i, in EnemySim e)
        {
            GetVictimAssets(e.ai.mobility, e.ai.combat, e.radius / SimConfig.EnemyRadius, e.height,
                            out Mesh src, out Material shell, out float scale);
            Vector3 center = e.pos + Vector3.up * (e.height * 0.5f);
            Quaternion rot = Quaternion.Euler(0f, e.yaw, 0f);
            float off = SimConfig.EnemyHeight * 0.5f * 0.4f;   // 기준 메시 반높이의 40%

            if (!MeshSlicer.Slice(src, CutN * off, CutN, out Mesh topM, out Mesh restM))
                return;

            var v = new GloryVictim { center = center, rot = rot, scale = scale, shell = shell, restMesh = restM };
            v.pieces.Add(HeldPiece(topM, center, rot, scale, shell));
            v.restPiece = HeldPiece(restM, center, rot, scale, shell);
            v.pieces.Add(v.restPiece);
            victims[i] = v;
        }

        void GlorySecondCut(int i)
        {
            var v = victims[i];
            if (v == null || v.restMesh == null) return;
            float off = SimConfig.EnemyHeight * 0.5f * 0.4f;

            if (!MeshSlicer.Slice(v.restMesh, -CutN * off, CutN, out Mesh midM, out Mesh botM))
                return;

            if (v.restPiece != null) { v.pieces.Remove(v.restPiece); Destroy(v.restPiece.gameObject); v.restPiece = null; }
            v.restMesh = null;
            v.pieces.Add(HeldPiece(midM, v.center, v.rot, v.scale, v.shell));
            v.pieces.Add(HeldPiece(botM, v.center, v.rot, v.scale, v.shell));
        }

        void GloryExplode(int i)
        {
            var v = victims[i];
            if (v == null) return;
            for (int k = 0; k < v.pieces.Count; k++)
            {
                var pc = v.pieces[k];
                if (pc == null) continue;
                Vector3 hor = new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f));
                if (hor.sqrMagnitude < 1e-4f) hor = Vector3.forward;
                Vector3 imp = hor.normalized * 2.2f + Vector3.up * (2.5f + k * 0.6f);
                pc.Release(imp, Random.insideUnitSphere * 6f);
            }
            blood.transform.position = v.center;
            blood.Emit(140);   // 처형 — 가장 큰 순간이라 넉넉히
            victims[i] = null;
        }

        void GloryCleanup(int i)
        {
            var v = victims[i];
            if (v == null) return;
            foreach (var pc in v.pieces) if (pc != null) Destroy(pc.gameObject);
            victims[i] = null;
        }

        // ── 시체 발광 ──
        // 몹 셰이더(Game/EnemyBody)는 발광 강도를 MaterialPropertyBlock으로 <b>런타임에</b> 받는다.
        // 시체 조각은 EnemyGlow가 관리하지 않으니 그 값이 0이라 빨간 라인이 꺼지고 회색이 된다.
        // 죽는 순간이 가장 눈에 띄는 장면인데 오히려 밋밋해지므로, 조각에도 직접 넣어준다.
        static readonly int IdGlowInt   = Shader.PropertyToID("_GlowIntensity");
        static readonly int IdGlowColor = Shader.PropertyToID("_GlowColor");
        static MaterialPropertyBlock corpseMpb;

        /// <summary>죽은 직후 발광 배수 — 살아있을 때보다 밝게 터뜨려 처치를 강조한다.</summary>
        public static float CorpseGlowMul = 1.6f;

        static void ApplyCorpseGlow(Renderer r)
        {
            if (r == null || r.sharedMaterial == null) return;
            if (r.sharedMaterial.shader == null || r.sharedMaterial.shader.name != "Game/EnemyBody") return;

            if (corpseMpb == null) corpseMpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(corpseMpb);
            var g = EntityViews.Glow;
            corpseMpb.SetFloat(IdGlowInt, Mathf.Max(0f, g.baseIntensity * CorpseGlowMul));
            corpseMpb.SetColor(IdGlowColor, g.baseColor);
            r.SetPropertyBlock(corpseMpb);
        }

        CorpsePiece HeldPiece(Mesh mesh, Vector3 center, Quaternion rot, float scale, Material shell)
        {
            var go = new GameObject("GloryPiece");
            go.layer = 2;   // IgnoreRaycast (플레이어 지형 감지 제외, 물리 충돌은 유지)
            go.transform.SetPositionAndRotation(center, rot);
            go.transform.localScale = Vector3.one * scale;

            go.AddComponent<MeshFilter>().mesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = new[] { shell, fleshMat };
            ApplyCorpseGlow(mr);   // 시체도 빨간 라인이 살아 있어야 한다

            // ★ 실물 모델 슬라이스는 정점이 수천 개라 MeshCollider(convex) 훌 계산이 킬마다 수십~백 ms씩
            // 걸려 순간 렉으로 보였다(실측 85ms). 파편은 굴러다니다 사라지는 연출용이라 정확한 모양의
            // 충돌이 필요 없음 — 바운즈만큼의 박스 콜라이더로 대체(생성 비용 거의 0).
            var bc = go.AddComponent<BoxCollider>();
            bc.center = mesh.bounds.center; bc.size = mesh.bounds.size;

            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 3f * scale;

            var cp = go.AddComponent<CorpsePiece>();
            cp.Init(center, rot, scale);

            Destroy(go, CorpseLife + 3f);   // 폭발 안 나도 누수 방지(안전망)
            return cp;
        }

        // ───────────────────────── 일반 처치(2조각 즉시) ─────────────────────────

        void SliceCorpse(Vector3 feet, float yaw, float height, float radiusScale, MobilityType mobility, CombatType combat)
        {
            Vector3 center = feet + Vector3.up * (height * 0.5f);
            Quaternion rot = Quaternion.Euler(0f, yaw, 0f);

            Camera cam = Main.Instance.Cam;
            Vector3 fwd = cam != null ? cam.transform.forward : Vector3.forward;
            Vector3 up  = cam != null ? cam.transform.up      : Vector3.up;
            slashParity ^= 1;
            Vector3 worldN = (Quaternion.AngleAxis(slashParity == 0 ? 45f : -45f, fwd) * up).normalized;
            Vector3 localN = (Quaternion.Inverse(rot) * worldN).normalized;

            GetVictimAssets(mobility, combat, radiusScale, height, out Mesh src, out Material shell, out float scale);
            if (!MeshSlicer.Slice(src, Vector3.zero, localN, out Mesh aboveM, out Mesh belowM))
                return;

            MakePiece(aboveM, center, rot,  worldN, scale, shell);
            MakePiece(belowM, center, rot, -worldN, scale, shell);

            blood.transform.SetPositionAndRotation(center, Quaternion.LookRotation(worldN));
            blood.Emit(90);    // 일반 처치
        }

        void MakePiece(Mesh mesh, Vector3 pos, Quaternion rot, Vector3 pushDir, float scale, Material shell)
        {
            var go = new GameObject("Corpse");
            go.layer = 2;
            go.transform.SetPositionAndRotation(pos, rot);
            go.transform.localScale = Vector3.one * scale;

            go.AddComponent<MeshFilter>().mesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = new[] { shell, fleshMat };
            ApplyCorpseGlow(mr);   // 잘려 날아가는 조각도 빨간 라인 유지

            // ★ HeldPiece와 같은 이유로 MeshCollider(convex) 대신 바운즈 박스 콜라이더 사용.
            var bc = go.AddComponent<BoxCollider>();
            bc.center = mesh.bounds.center; bc.size = mesh.bounds.size;

            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 3f * scale;
            rb.AddForce(pushDir * 2.2f + Vector3.up * 1.5f, ForceMode.VelocityChange);
            rb.AddTorque(Random.insideUnitSphere * 4f, ForceMode.VelocityChange);

            // 일반 처치 조각도 서서히 식게 한다(처형 조각과 같은 연출).
            // Init 직후 곧바로 Release해 물리는 이미 걸린 상태를 유지한다.
            // ★ Init이 rb.isKinematic=true 로 물리를 끄므로, 속도를 기억했다가 되돌려준다.
            //   (안 그러면 방금 준 힘이 사라져 조각이 제자리에 뚝 떨어진다)
            Vector3 keepVel = rb.linearVelocity, keepAng = rb.angularVelocity;
            var cp = go.AddComponent<CorpsePiece>();
            cp.Init(pos, rot, scale);
            cp.ReleaseImmediate();
            rb.linearVelocity = keepVel; rb.angularVelocity = keepAng;

            Destroy(go, CorpseLife);
        }

        // ── 리소스 구성 ──

        static Mesh BuildScaledCapsule()
        {
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            tmp.SetActive(false);
            var srcMesh = tmp.GetComponent<MeshFilter>().sharedMesh;

            Vector3[] v = srcMesh.vertices;
            Vector3[] n = srcMesh.normals;
            Vector3 s = new Vector3(SimConfig.EnemyRadius * 2f, SimConfig.EnemyHeight * 0.5f,
                                    SimConfig.EnemyRadius * 2f);
            for (int i = 0; i < v.Length; i++)
            {
                v[i] = new Vector3(v[i].x * s.x, v[i].y * s.y, v[i].z * s.z);
                if (n != null && n.Length == v.Length)
                    n[i] = new Vector3(n[i].x / s.x, n[i].y / s.y, n[i].z / s.z).normalized;
            }

            var res = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            res.vertices  = v;
            res.normals   = n;
            res.triangles = srcMesh.triangles;
            res.RecalculateBounds();
            Destroy(tmp);
            return res;
        }

        /// <summary>
        /// 절단 순간 분출. ★ 몹이 로봇이라 피가 아니라 <b>불꽃·파편</b>이 튄다.
        /// HDR 색으로 띄워 Bloom에 걸리게 하고, Stretch 렌더로 불똥이 늘어지게 한다.
        /// </summary>
        void BuildBlood()
        {
            var go = new GameObject("SparkBurst");
            go.transform.SetParent(transform, false);
            blood = go.AddComponent<ParticleSystem>();
            blood.Stop();

            var m = blood.main;
            m.startLifetime   = new ParticleSystem.MinMaxCurve(0.25f, 0.7f);
            m.startSpeed      = new ParticleSystem.MinMaxCurve(4f, 11f);
            m.startSize       = new ParticleSystem.MinMaxCurve(0.02f, 0.06f);
            // 흰 → 주황으로 식는 불꽃. 1을 넘겨야 번진다.
            m.startColor      = new ParticleSystem.MinMaxGradient(
                                    new Color(5f, 3.4f, 1.4f), new Color(4.2f, 1.2f, 0.2f));
            m.gravityModifier = 2.2f;
            m.maxParticles    = 500;
            m.simulationSpace = ParticleSystemSimulationSpace.World;
            m.playOnAwake     = false;

            var em = blood.emission; em.enabled = false;
            var sh = blood.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 42f; sh.radius = 0.12f;

            // 수명 끝으로 갈수록 사라지게(툭 끊기면 싸구려로 보인다)
            var col = blood.colorOverLifetime; col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.55f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(grad);

            var r = blood.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.05f;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.material = WireMaterials.Spark;   // 전선 스파크와 같은 가산합성 재질
        }

        static Material Mat(Color c, bool doubleSided)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(sh) { color = c };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (doubleSided && m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);
            return m;
        }
    }

    /// <summary>
    /// 글로리킬 조각. 방출 전엔 kinematic으로 제자리 근처에서 약하게 흔들리다,
    /// Release 시 물리로 폭발한다. ★ 뷰 전용 연출.
    /// </summary>
    public class CorpsePiece : MonoBehaviour
    {
        Vector3 home; Quaternion homeRot; float scale; float seed;
        bool held = true;
        Rigidbody rb;

        // ── 식어가는 연출 ──
        // 잘린 로봇이 계속 벌겋게 빛난 채 굳어 있으면 어색하다. 전원이 끊긴 것처럼 서서히 죽는다.
        static readonly int IdGlow = Shader.PropertyToID("_GlowIntensity");
        static MaterialPropertyBlock coolMpb;
        Renderer rend;
        float glow0, coolT;
        const float CoolTime = 2.2f;

        public void Init(Vector3 pos, Quaternion rot, float scale)
        {
            home = pos; homeRot = rot; this.scale = scale;
            seed = pos.x * 13.1f + pos.z * 7.7f;
            rb = GetComponent<Rigidbody>();
            rb.isKinematic = true;

            rend = GetComponent<Renderer>();
            if (rend != null && rend.sharedMaterial != null &&
                rend.sharedMaterial.shader != null && rend.sharedMaterial.shader.name == "Game/EnemyBody")
            {
                if (coolMpb == null) coolMpb = new MaterialPropertyBlock();
                rend.GetPropertyBlock(coolMpb);
                glow0 = coolMpb.GetFloat(IdGlow);
            }
        }

        void Update()
        {
            Cool();
            if (!held) return;
            float t = Time.time;
            float wob = 0.02f * scale;
            Vector3 off = new Vector3(Mathf.Sin(t * 18f + seed),
                                      Mathf.Sin(t * 15f + seed * 1.7f),
                                      Mathf.Cos(t * 21f + seed)) * wob;
            transform.position = home + off;
            transform.rotation = homeRot * Quaternion.Euler(off * 300f);
        }

        /// <summary>전원이 끊긴 것처럼 발광이 서서히 죽는다. 마지막엔 몇 번 깜빡인다.</summary>
        void Cool()
        {
            if (rend == null || glow0 <= 0f || coolT >= 1f) return;
            coolT += Time.deltaTime / CoolTime;
            float k = Mathf.Clamp01(coolT);
            // 뒤로 갈수록 급격히 꺼지고, 꺼지기 직전 지직거림
            float flicker = k > 0.6f ? Mathf.Lerp(1f, Random.Range(0.15f, 1f), (k - 0.6f) / 0.4f) : 1f;
            float v = glow0 * (1f - k * k) * flicker;

            rend.GetPropertyBlock(coolMpb);
            coolMpb.SetFloat(IdGlow, Mathf.Max(0f, v));
            rend.SetPropertyBlock(coolMpb);
        }

        /// <summary>물리는 이미 걸려 있고 "붙잡힘"만 푼다(일반 처치 조각용).</summary>
        public void ReleaseImmediate()
        {
            held = false;
            if (rb != null) rb.isKinematic = false;
        }

        public void Release(Vector3 impulse, Vector3 torque)
        {
            held = false;
            rb.isKinematic = false;
            rb.AddForce(impulse, ForceMode.VelocityChange);
            rb.AddTorque(torque, ForceMode.VelocityChange);
            Destroy(gameObject, 5f);
        }
    }

    /// <summary>Play 시 자동 부착.</summary>
    public static class DismembermentBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Object.FindFirstObjectByType<Dismemberment>() == null)
                new GameObject("[Dismemberment]").AddComponent<Dismemberment>();
        }
    }
}
