using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// "뷰모델 루트가 무엇인가"의 <b>유일한 판정처</b>.
    ///
    /// <para><b>왜 만들었나</b> — 예전엔 <see cref="ViewmodelMotion"/>·<see cref="ViewmodelCamera"/>·
    /// <see cref="PosePlayer"/>가 각자 찾았고, 셋 다 못 찾으면 <c>GetChild(0)</c>로 <b>아무거나</b>
    /// 집었다. 그 결과 <c>[PlayerBody]</c>의 첫 자식인 <b>Main Camera가 뷰모델로 잡혀</b>
    /// <see cref="ViewmodelMotion"/>이 매 LateUpdate마다 카메라의 위치·회전을 절대값으로 덮어썼다.
    /// PC에선 시점 회전이 통째로 죽었고, VR에선 <c>TrackedPoseDriver</c>가 BeforeRender에 회전을
    /// 다시 써서 회전만 살아남고 <b>눈높이·위치가 죽었다</b>(실제로 겪은 버그).</para>
    ///
    /// <para><b>규칙</b>
    /// <list type="number">
    /// <item>이름(<see cref="Name"/>)으로만 찾는다. 순서 기반 폴백은 두지 않는다 —
    ///       "못 찾으면 아무거나"가 위 사고의 뿌리다.</item>
    /// <item><b>카메라는 절대 뷰모델이 될 수 없다.</b> 찾은 대상이 <see cref="Camera"/>를 갖고 있거나
    ///       카메라의 조상이면 거부한다. 앞의 탐색이 전부 어긋나도 시점만은 안 죽는 마지막 방어선.</item>
    /// <item>못 찾으면 <c>null</c>. 호출자는 <b>아무것도 하지 않아야</b> 한다.
    ///       팔이 안 움직이는 것보다 시점이 죽는 게 훨씬 나쁘다.</item>
    /// </list></para>
    /// </summary>
    public static class ViewmodelRoot
    {
        /// <summary>뷰모델 루트 오브젝트 이름. 정상 계층은 <c>Main Camera > Viewmodel</c>.</summary>
        public const string Name = "Viewmodel";

        /// <summary>
        /// 뷰모델 루트를 찾는다. 없으면 <c>null</c>.
        /// </summary>
        /// <param name="preferred">먼저 뒤져볼 부모(선택). 보통 호출자 자신.</param>
        public static Transform Find(Transform preferred = null)
        {
            Transform t = null;

            if (preferred != null) t = Accept(preferred.Find(Name));

            if (t == null)
            {
                Camera cam = Camera.main;
                if (cam != null) t = Accept(cam.transform.Find(Name));
            }

            // 씬 전역 탐색은 마지막. GameObject.Find는 비활성 오브젝트를 못 찾는다 —
            // 꺼둔 뷰모델을 쓰려면 참조를 직접 물려야 한다(이 한계는 의도적으로 남긴다).
            if (t == null)
            {
                GameObject go = GameObject.Find(Name);
                if (go != null) t = Accept(go.transform);
            }

            return t;
        }

        /// <summary>카메라를 뷰모델로 잡는 것을 막는다. 통과하면 그대로, 아니면 <c>null</c>.</summary>
        static Transform Accept(Transform t)
        {
            if (t == null) return null;

            if (t.GetComponent<Camera>() != null)
            {
                Warn(t, "이 오브젝트에 Camera가 붙어 있습니다");
                return null;
            }

            // 카메라의 조상도 안 된다 — 부모를 흔들면 자식 카메라가 통째로 끌려간다.
            if (t.GetComponentInChildren<Camera>(true) != null)
            {
                Warn(t, "이 오브젝트 아래에 Camera가 있습니다");
                return null;
            }

            return t;
        }

        static void Warn(Transform t, string why)
        {
            Debug.LogWarning(
                $"[뷰모델] '{t.name}'을(를) 뷰모델 루트로 쓰지 않습니다 — {why}. " +
                $"시점(카메라)을 흔들어 회전·눈높이를 죽이기 때문입니다. " +
                $"정상 계층은 Main Camera 아래에 '{Name}' 오브젝트를 두는 것입니다.", t);
        }
    }
}
