using System.Collections.Generic;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 스폰 실체화 VFX(설계 §4-⑥). 몹 재질을 안 건드리는 <b>디졸브 복제본 오버레이</b> 방식(A').
    ///
    /// 2단계 — 트리거는 EntityViews가 준다:
    ///  ① <see cref="Prepare"/> : 스폰 즉시 호출. 진짜 몹 렌더러를 <b>끄고</b>(안 보임) 오버레이도 꺼둔 채
    ///     대기한다. 팬 아래 재생 박스를 지나기 전까진 계속 숨겨져 있다.
    ///  ② <see cref="Reveal"/>  : 팬 아래 <see cref="SpawnRevealVolume"/>를 몹이 지나는 순간 호출.
    ///     오버레이를 켜고 진짜 몹도 다시 켠 뒤(오버레이가 덮으므로 팝 없음) _Reveal 0→1로 위→아래로
    ///     걷히며 그 자리부터 진짜 몹이 드러난다(X-ray 페이드인).
    ///
    /// 순수 View — sim·결정론엔 영향 0. 진짜 몹 재질은 안 건드린다(A' 안전). 스킨드 메시는 BakeMesh 스냅샷.
    /// ★ 박스를 끝내 안 지나면 계속 숨겨진 채로 남는다(설계상 몹은 항상 팬 박스를 지난다는 전제).
    /// </summary>
    public class SpawnMaterialize : MonoBehaviour
    {
        const string ShaderName = "Precog/SpawnMaterialize";
        static Shader shader;
        static bool shaderMissingLogged;

        public static int TotalBuilt;             // 진단
        public static float DurationOverride;     // 진단: >0이면 지속시간 강제
        public static bool DebugLog = false;
        static bool noVolumeWarned;               // 재생 박스 0개 경고 1회
        static int diagCount;                     // 진단 로그 횟수 제한

        const float RevealDuration = 1.6f;        // 걷히는 데 걸리는 시간(초)

        Transform viewRoot;
        float duration = RevealDuration;
        float revealT;
        bool revealing;    // false = 숨김 대기(armed) · true = 걷히는 중

        readonly List<Material> mats = new List<Material>();
        readonly List<MeshRenderer> overlays = new List<MeshRenderer>();
        readonly List<Mesh> bakedMeshes = new List<Mesh>();
        readonly List<Renderer> hiddenReal = new List<Renderer>();

        static readonly int RevealId = Shader.PropertyToID("_Reveal");
        static readonly int MinYId   = Shader.PropertyToID("_MinY");
        static readonly int MaxYId   = Shader.PropertyToID("_MaxY");

        /// <summary>① 스폰 즉시: 진짜 몹을 숨기고 오버레이는 꺼둔 채 대기.</summary>
        public static void Prepare(Transform viewRoot)
        {
            if (viewRoot == null) return;
            if (shader == null) shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                if (!shaderMissingLogged) { Debug.LogWarning("[SpawnVFX] 셰이더 '" + ShaderName + "' 없음."); shaderMissingLogged = true; }
                return;
            }
            // 진단: 재생 박스가 하나도 없으면 몹이 숨겨진 채 영영 안 드러난다 → 원인 바로 알리기.
            if (SpawnRevealVolume.Count == 0 && !noVolumeWarned)
            {
                noVolumeWarned = true;
                Debug.LogWarning("[SpawnVFX] 재생 박스(SpawnRevealVolume)가 0개입니다. " +
                    "Tools ▸ 'Fan 재생 박스 설정...' ▸ 팬마다 생성·갱신 을 실행하십시오. " +
                    "박스가 없으면 몹이 숨겨진 채로 안 보이고 실체화도 안 뜹니다.");
            }

            var host = new GameObject("~Materialize");
            host.transform.SetParent(viewRoot, false);
            var sm = host.AddComponent<SpawnMaterialize>();
            sm.viewRoot = viewRoot;
            sm.duration = DurationOverride > 0f ? DurationOverride : RevealDuration;
            sm.BuildHidden();
        }

        /// <summary>② 박스 통과: 대기 중인 실체화를 찾아 걷힘을 시작한다.</summary>
        public static void Reveal(Transform viewRoot)
        {
            if (viewRoot == null) return;
            var arr = viewRoot.GetComponentsInChildren<SpawnMaterialize>(true);
            for (int i = 0; i < arr.Length; i++)
                if (arr[i] != null && !arr[i].revealing) { arr[i].StartReveal(); return; }
        }

        void BuildHidden()
        {
            int made = 0, skinned = 0;
            foreach (var r in viewRoot.GetComponentsInChildren<Renderer>(false))
            {
                if (r == null || r.transform.IsChildOf(transform)) continue;

                Mesh mesh = null;
                if (r is SkinnedMeshRenderer smr && smr.sharedMesh != null)
                { mesh = new Mesh(); smr.BakeMesh(mesh); bakedMeshes.Add(mesh); skinned++; }
                else { var mf = r.GetComponent<MeshFilter>(); if (mf != null) mesh = mf.sharedMesh; }
                if (mesh == null) continue;

                var go = new GameObject("~mat_" + r.name);
                var tr = go.transform;
                tr.SetParent(r.transform, false);
                tr.localPosition = Vector3.zero; tr.localRotation = Quaternion.identity; tr.localScale = Vector3.one;

                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.enabled = false;   // 박스 통과 전엔 오버레이도 끔

                var m = new Material(shader);
                Bounds b = mesh.bounds;
                m.SetFloat(MinYId, b.min.y);
                m.SetFloat(MaxYId, b.max.y);
                m.SetFloat(RevealId, 0f);
                mr.sharedMaterial = m;
                mats.Add(m);
                overlays.Add(mr);

                // ★ 스킨드 리그 스케일 보정. 일부 리그(FBX 0.02 임포트 등)는 BakeMesh 복제본이 몸체의
                //   lossyScale을 상속해 실물의 1/100 크기로 찌부러진다(→ 안 보임). BakeMesh의 스케일
                //   의미에 의존하지 말고, 오버레이 월드 크기를 '실물 렌더러 월드 크기'에 직접 맞춘다.
                Bounds realB = r.bounds;
                Bounds ovrB  = mr.bounds;
                if (ovrB.size.y > 1e-4f && realB.size.y > 1e-4f)
                {
                    float f = realB.size.y / ovrB.size.y;
                    if (f > 1.01f || f < 0.99f) tr.localScale = tr.localScale * f;   // 균일 스케일(왜곡 없음)
                    tr.position += r.bounds.center - mr.bounds.center;               // 스케일 후 중심을 실물에 정렬
                }

                made++;
            }

            if (made == 0) { Destroy(gameObject); return; }   // 얹을 게 없으면 정리
            TotalBuilt++;
            if (DebugLog) Debug.Log($"[SpawnVFX] {viewRoot.name}: 오버레이 {made}개 (skinned {skinned}) 숨김 대기");

            // 진짜 몹 숨김 — 박스를 지날 때까지 안 보이게.
            foreach (var r in viewRoot.GetComponentsInChildren<Renderer>(false))
            {
                if (r == null || r.transform.IsChildOf(transform)) continue;
                if (r.enabled) { r.enabled = false; hiddenReal.Add(r); }
            }
        }

        void StartReveal()
        {
            revealing = true;
            revealT = 0f;
            for (int i = 0; i < overlays.Count; i++)
                if (overlays[i] != null) overlays[i].enabled = true;   // 오버레이 켬(진짜 몹 덮음)
            RestoreReal();                                             // 진짜 몹 켬(오버레이가 덮으므로 팝 없음)

            // 진단(임시) — 재생 시작 순간의 오버레이 실제 상태를 콘솔에 남긴다(지연시간과 무관하게 읽힘).
            if (diagCount < 8)
            {
                diagCount++;
                string s = $"[SpawnVFX진단] '{viewRoot.name}' 오버레이 {overlays.Count}개 · 몹pos={viewRoot.position:0.0}";
                if (overlays.Count > 0 && overlays[0] != null)
                {
                    var mr = overlays[0];
                    var mf = mr.GetComponent<MeshFilter>();
                    int vc = mf != null && mf.sharedMesh != null ? mf.sharedMesh.vertexCount : -1;
                    var mat = mr.sharedMaterial;
                    string sh = mat != null && mat.shader != null ? mat.shader.name : "NULL재질";
                    s += $" | enabled={mr.enabled} 활성={mr.gameObject.activeInHierarchy} 정점={vc}"
                       + $" 셰이더={sh} 오버레이bounds중심={mr.bounds.center:0.0} 크기={mr.bounds.size:0.00}";
                }
                Debug.Log(s);
            }
        }

        void Update()
        {
            if (!revealing) return;     // 대기 중: 계속 숨김 유지
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            revealT += dt;

            float reveal = Mathf.Clamp01(revealT / Mathf.Max(0.1f, duration));
            for (int i = 0; i < mats.Count; i++)
                if (mats[i] != null) mats[i].SetFloat(RevealId, reveal);

            if (revealT >= duration) Destroy(gameObject);
        }

        void RestoreReal()
        {
            for (int i = 0; i < hiddenReal.Count; i++)
                if (hiddenReal[i] != null) hiddenReal[i].enabled = true;
            hiddenReal.Clear();
        }

        void OnDestroy()
        {
            RestoreReal();   // 어떤 경로로 끝나도 진짜 몹은 반드시 다시 보이게
            // 오버레이 복제본은 몸체(r.transform) 밑에 붙어 있어 호스트만 지우면 남는다 → 직접 정리.
            for (int i = 0; i < overlays.Count; i++)
                if (overlays[i] != null) Destroy(overlays[i].gameObject);
            foreach (var m in mats) if (m != null) Destroy(m);
            foreach (var mesh in bakedMeshes) if (mesh != null) Destroy(mesh);
        }
    }
}
