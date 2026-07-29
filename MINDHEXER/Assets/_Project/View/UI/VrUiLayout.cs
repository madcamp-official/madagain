using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// UI 배치값을 <b>JSON으로 저장/불러오기</b>. 실기에서 재빌드 없이 레이아웃을 잡기 위한 것이다.
    ///
    /// <para><b>왜 필요한가</b> — 각도·거리·크기는 <b>헤드셋을 쓰고 봐야</b> 판단이 된다.
    /// 모니터에서 아무리 맞춰도 실기에서 다시 잡게 된다. 그런데 빌드 한 번이 수 분이라,
    /// 값 하나 바꾸려고 재빌드하면 튜닝이 사실상 불가능하다.</para>
    ///
    /// <para><b>쓰는 법</b>
    /// <list type="number">
    /// <item>에디터에서 대략 잡고 <b>저장</b> → JSON이 나온다</item>
    /// <item>값을 고쳐 기기로 밀어넣고 앱 재시작:
    /// <code>adb push ui_layout.json /storage/emulated/0/Android/data/com.mindhexer.headset/files/ui_layout.json</code></item>
    /// <item>헤드셋에서 확인하며 2를 반복</item>
    /// <item>확정되면 그 JSON을 다시 받아 에디터에서 <b>불러오기</b> → 씬을 저장해 굳힌다</item>
    /// </list>
    /// 4단계가 있어야 실기에서 잡은 값이 유실되지 않는다.</para>
    ///
    /// <para>인스펙터 우클릭(컨텍스트 메뉴)에 저장/불러오기/경로 출력이 있다.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public class VrUiLayout : MonoBehaviour
    {
        [Tooltip("파일 이름. 저장 위치는 Application.persistentDataPath.")]
        public string fileName = "ui_layout.json";

        [Tooltip("시작할 때 파일이 있으면 자동으로 불러온다. 실기 튜닝의 핵심 — 끄면 adb push가 무의미해진다.")]
        public bool loadOnStart = true;

        [Tooltip("비우면 이 오브젝트 이하에서 찾는다. 여러 루트를 쓰면 여기에 나열한다.")]
        public List<VrUiSpace> spaces = new List<VrUiSpace>();

        public string FullPath { get { return Path.Combine(Application.persistentDataPath, fileName); } }

        void Start()
        {
            if (loadOnStart && File.Exists(FullPath)) Load();
        }

        // ── 직렬화 형식 ────────────────────────────────────────────────
        // JsonUtility는 최상위 배열을 못 다뤄 래퍼가 필요하다.

        [System.Serializable]
        public class AnchorData
        {
            public string name;
            public float azimuth, elevation, angularSize;
        }

        [System.Serializable]
        public class SpaceData
        {
            public string name;
            public float distance, safeHalfAngleX, safeHalfAngleY;
            public List<AnchorData> anchors = new List<AnchorData>();
        }

        [System.Serializable]
        public class LayoutData
        {
            public List<SpaceData> spaces = new List<SpaceData>();
        }

        // ── 조작 ──────────────────────────────────────────────────────

        List<VrUiSpace> Targets()
        {
            if (spaces != null && spaces.Count > 0) return spaces;
            var found = new List<VrUiSpace>();
            GetComponentsInChildren(true, found);
            return found;
        }

        [ContextMenu("UI 배치 — 저장")]
        public void Save()
        {
            var data = new LayoutData();
            List<VrUiSpace> targets = Targets();

            for (int i = 0; i < targets.Count; i++)
            {
                VrUiSpace sp = targets[i];
                if (sp == null) continue;

                var sd = new SpaceData();
                sd.name = sp.name;
                sd.distance = sp.distance;
                sd.safeHalfAngleX = sp.safeHalfAngleX;
                sd.safeHalfAngleY = sp.safeHalfAngleY;

                var anchors = new List<VrUiAnchor>();
                sp.GetComponentsInChildren(true, anchors);
                for (int j = 0; j < anchors.Count; j++)
                {
                    VrUiAnchor a = anchors[j];
                    if (a == null) continue;
                    var ad = new AnchorData();
                    ad.name = a.name;
                    ad.azimuth = a.azimuth;
                    ad.elevation = a.elevation;
                    ad.angularSize = a.angularSize;
                    sd.anchors.Add(ad);
                }
                data.spaces.Add(sd);
            }

            File.WriteAllText(FullPath, JsonUtility.ToJson(data, true));
            Debug.Log("[VrUiLayout] 저장 — " + FullPath, this);
        }

        [ContextMenu("UI 배치 — 불러오기")]
        public void Load()
        {
            if (!File.Exists(FullPath))
            {
                Debug.LogWarning("[VrUiLayout] 파일이 없습니다 — " + FullPath, this);
                return;
            }

            LayoutData data = JsonUtility.FromJson<LayoutData>(File.ReadAllText(FullPath));
            if (data == null || data.spaces == null) { Debug.LogWarning("[VrUiLayout] 형식이 잘못됐습니다.", this); return; }

            List<VrUiSpace> targets = Targets();
            int hit = 0, miss = 0;

            for (int i = 0; i < data.spaces.Count; i++)
            {
                SpaceData sd = data.spaces[i];
                VrUiSpace sp = FindSpace(targets, sd.name);
                // 이름이 안 맞으면 조용히 넘어가지 않는다 — 오타 하나로 값이 통째로 무시되면
                // "왜 안 바뀌지"로 시간을 버린다.
                if (sp == null) { miss++; Debug.LogWarning("[VrUiLayout] 루트를 못 찾음: " + sd.name, this); continue; }

                sp.distance = sd.distance;
                sp.safeHalfAngleX = sd.safeHalfAngleX;
                sp.safeHalfAngleY = sd.safeHalfAngleY;

                var anchors = new List<VrUiAnchor>();
                sp.GetComponentsInChildren(true, anchors);

                for (int j = 0; j < sd.anchors.Count; j++)
                {
                    AnchorData ad = sd.anchors[j];
                    VrUiAnchor a = FindAnchor(anchors, ad.name);
                    if (a == null) { miss++; Debug.LogWarning("[VrUiLayout] 앵커를 못 찾음: " + ad.name, this); continue; }
                    a.azimuth = ad.azimuth;
                    a.elevation = ad.elevation;
                    a.angularSize = ad.angularSize;
                    hit++;
                }

                sp.Refresh();
            }

            Debug.Log("[VrUiLayout] 불러옴 — 앵커 " + hit + "개 적용, 실패 " + miss + "건. " + FullPath, this);
        }

        [ContextMenu("UI 배치 — 파일 경로 출력")]
        public void PrintPath()
        {
            Debug.Log("[VrUiLayout] " + FullPath, this);
        }

        static VrUiSpace FindSpace(List<VrUiSpace> list, string name)
        {
            for (int i = 0; i < list.Count; i++) if (list[i] != null && list[i].name == name) return list[i];
            return null;
        }

        static VrUiAnchor FindAnchor(List<VrUiAnchor> list, string name)
        {
            for (int i = 0; i < list.Count; i++) if (list[i] != null && list[i].name == name) return list[i];
            return null;
        }
    }
}
