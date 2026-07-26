using UnityEngine;
using UnityEngine.XR;

namespace Game.View
{
    /// <summary>
    /// PC 경로와 VR(Cardboard) 경로를 가르는 런타임 게이트.
    ///
    /// <para>헤드셋(S24+)에서 Cardboard XR 로더가 뜨면 <see cref="XRSettings.isDeviceActive"/>가
    /// true가 된다 → VR 모드. 에디터/PC 빌드에는 XR 디바이스가 없어 false → 기존 Cinemachine
    /// 1인칭 경로 그대로.</para>
    ///
    /// <para><b>원칙</b>: PC 경로 코드는 이 게이트가 false일 때 <b>바이트 단위로 이전과 동일</b>하게
    /// 동작해야 한다. VR 분기는 항상 <c>if (VrMode.Enabled)</c>로만 얹는다.</para>
    /// </summary>
    public static class VrMode
    {
        static int cached = -1;

        /// <summary>
        /// 에디터에서 실기 없이 VR 분기를 켜/끄고 싶을 때 강제 지정. null이면 자동 판별.
        /// (예: 디버그 콘솔에서 <c>VrMode.ForceOverride = true</c>.)
        /// </summary>
        public static bool? ForceOverride = null;

        /// <summary>현재 프레임 기준 VR 모드 여부.</summary>
        public static bool Enabled
        {
            get
            {
                if (ForceOverride.HasValue) return ForceOverride.Value;
                if (cached < 0) cached = Detect() ? 1 : 0;
                return cached == 1;
            }
        }

        static bool Detect()
        {
            // XR 디스플레이가 실제 구동 중이면 VR. Cardboard 로더가 뜨면 true.
            return XRSettings.isDeviceActive;
        }
    }
}
