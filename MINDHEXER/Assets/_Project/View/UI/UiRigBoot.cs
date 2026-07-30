using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// UI 리그 프리팹을 런타임 <c>[Head]</c> 아래에 붙인다.
    ///
    /// <para><b>왜 부트가 따로 필요한가</b> — 리그(<c>[PlayerBody]/[Head]/Main Camera</c>)는
    /// <c>GameBoot</c>이 <c>Awake</c>에서 만든다. 즉 <b>에디터에는 UI를 붙일 부모가 없다.</b>
    /// 그래서 UI는 <c>UiStudio</c> 씬에서 저작해 프리팹으로 굳히고, 실행 시 여기서 붙인다.
    /// (<c>GameBoot</c>은 다른 작업이 진행 중인 파일이라 건드리지 않는다.)</para>
    ///
    /// <para>★ <b><c>Main Camera</c>가 아니라 <c>[Head]</c>에 붙인다.</b> 카메라는 연출
    /// (<c>MotionFeel</c>의 롤·킥·딥) 소유자라, 거기 붙이면 점프·착지마다 UI가 함께 튀고 기울어진다.
    /// 시야에 붙은 패널이 기우는 것은 VR 멀미의 직접 원인이다.</para>
    ///
    /// <para>씬의 <c>[GameBoot]</c> 옆에 하나 두고 프리팹만 물리면 된다.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public class UiRigBoot : MonoBehaviour
    {
        /// <summary><c>Resources</c> 안의 프리팹 이름. 씬에 배치하지 않아도 자동으로 붙는 경로.</summary>
        public const string ResourceName = "UiRig";

        [Tooltip("UiStudio에서 저작해 뽑은 UI 리그 프리팹([UiRig]).")]
        public GameObject uiRigPrefab;

        [Tooltip("비우면 Camera.main에서 [Head]를 거슬러 찾는다.")]
        public Transform head;

        /// <summary>붙은 인스턴스. 없으면 null.</summary>
        public GameObject Instance { get; private set; }

        // GameBoot이 Awake에서 리그를 만드므로 Start까지 기다린다.
        void Start() => Install();

        public void Install()
        {
            if (Instance != null) return;

            if (uiRigPrefab == null)
            {
                Debug.LogWarning("[UiRigBoot] UI 리그 프리팹이 비어 있습니다 — UI가 뜨지 않습니다.", this);
                return;
            }

            Transform h = ResolveHead();
            if (h == null)
            {
                Debug.LogError("[UiRigBoot] [Head]를 찾지 못했습니다 — Camera.main이 없습니다.", this);
                return;
            }

            Instance = Instantiate(uiRigPrefab, h, false);
            Instance.name = uiRigPrefab.name;
            Instance.transform.localPosition = Vector3.zero;
            Instance.transform.localRotation = Quaternion.identity;
            Instance.transform.localScale = Vector3.one;

            // ★ 뷰모델과 같은 레이어에 올린다. [ViewmodelCam]은 오버레이 스택이라 자기 depth 버퍼를
            //   새로 지우고 나중에 그린다 — HackPanel의 ZTest Always는 "같은 카메라 패스 안에서"만
            //   이기므로, 베이스 카메라에만 있으면 나중에 그려지는 뷰모델(팔·도구)이 그 위를 덮어써
            //   UI가 가려진다. 뷰모델 레이어에 같이 올리면 그 늦게-그려지는 패스를 같이 타서 다시
            //   맨 위가 된다.
            int vmLayer = LayerMask.NameToLayer(ViewmodelCamera.DefaultLayer);
            if (vmLayer >= 0) ViewmodelCamera.SetLayerRecursive(Instance.transform, vmLayer);

            WireUp(h);

            Debug.Log($"[UiRigBoot] UI 리그 부착 — 부모={h.name}", this);
        }

        /// <summary>패널을 미니게임에 물리고, 빠진 참조를 채운다.</summary>
        void WireUp(Transform h)
        {
            var panel = Instance.GetComponentInChildren<HackPanel>(true);
            if (panel == null)
            {
                Debug.LogWarning("[UiRigBoot] 프리팹 안에 HackPanel이 없습니다.", this);
                return;
            }

            // 관성 추종 루트 — 프리팹 안에 있어야 하지만, 비어 있으면 형제에서 찾는다.
            if (panel.panelRoot == null)
            {
                var follow = Instance.GetComponentInChildren<VrUiFollow>(true);
                if (follow != null) panel.panelRoot = follow.transform;
            }

            var follows = Instance.GetComponentsInChildren<VrUiFollow>(true);
            for (int i = 0; i < follows.Length; i++)
                if (follows[i].head == null) follows[i].head = h;

            // 미니게임은 HackDriver가 카메라에 붙인다 — 씬 어디에 있든 찾아 물린다.
            var mini = FindFirstObjectByType<PatternMinigame>();
            if (mini != null) mini.panel = panel;
        }

        Transform ResolveHead()
        {
            if (head != null) return head;

            Camera cam = Camera.main;
            if (cam == null) return null;

            // 정상 리그는 [Head] > Main Camera. 부모가 있으면 그쪽이 [Head]다.
            head = cam.transform.parent != null ? cam.transform.parent : cam.transform;
            return head;
        }
    }

    /// <summary>
    /// 씬에 <see cref="UiRigBoot"/>를 두지 않아도 UI가 뜨게 한다.
    ///
    /// <para><b>왜 이렇게 하나</b> — 게임 씬들이 여러 세션에서 동시에 편집되고 있어, 씬마다
    /// 오브젝트를 하나씩 심으면 서로의 변경과 섞인다. <c>Resources</c>에서 읽어 자동으로 붙이면
    /// <b>씬 파일을 한 개도 건드리지 않는다.</b> 이 프로젝트의 <c>ViewmodelCameraBoot</c>·
    /// <c>OneBitControlBoot</c>가 이미 같은 방식이다.</para>
    ///
    /// <para>씬에 <see cref="UiRigBoot"/>가 이미 있으면 그쪽을 따르고 아무것도 하지 않는다 —
    /// 특정 씬만 다른 UI를 쓰고 싶을 때의 탈출구.</para>
    /// </summary>
    public static class UiRigAutoBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Object.FindFirstObjectByType<UiRigBoot>() != null) return;

            var prefab = Resources.Load<GameObject>(UiRigBoot.ResourceName);
            if (prefab == null) return;   // UI가 없어야 하는 씬도 있다 — 조용히 넘어간다

            var go = new GameObject("[UiRigBoot]");
            var boot = go.AddComponent<UiRigBoot>();
            boot.uiRigPrefab = prefab;
        }
    }
}
