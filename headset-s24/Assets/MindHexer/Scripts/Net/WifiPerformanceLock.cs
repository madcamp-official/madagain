using UnityEngine;

namespace MindHexer.Headset.Net
{
    /// <summary>
    /// Android에서 Wi-Fi를 **고성능(절전 없음) 모드**로 유지해 산발적 패킷 지연을 줄인다(= RTT↓). (SPEC 5.4)
    /// 멀티캐스트 락도 잡아 브로드캐스트 디스커버리 수신 안정성을 높인다.
    ///
    /// 필요 권한(Player → Publishing Settings → Custom Main Manifest에 추가):
    ///   &lt;uses-permission android:name="android.permission.WAKE_LOCK" /&gt;
    ///   &lt;uses-permission android:name="android.permission.CHANGE_WIFI_MULTICAST_STATE" /&gt;
    /// 권한이 없으면 조용히 실패(로그만) — 다른 최적화(고빈도 ping/스레드 우선순위)는 그대로 동작.
    /// 비-안드로이드(에디터)에서는 no-op.
    /// </summary>
    public sealed class WifiPerformanceLock : MonoBehaviour
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        private AndroidJavaObject _wifiLock;
        private AndroidJavaObject _multicastLock;

        private void OnEnable()
        {
            try
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                var wifi = activity.Call<AndroidJavaObject>("getApplicationContext")
                                   .Call<AndroidJavaObject>("getSystemService", "wifi");

                _wifiLock = wifi.Call<AndroidJavaObject>("createWifiLock", 3, "MindHexer-WifiHighPerf"); // 3 = WIFI_MODE_FULL_HIGH_PERF
                _wifiLock.Call("setReferenceCounted", false);
                _wifiLock.Call("acquire");

                _multicastLock = wifi.Call<AndroidJavaObject>("createMulticastLock", "MindHexer-Multicast");
                _multicastLock.Call("setReferenceCounted", false);
                _multicastLock.Call("acquire");

                Debug.Log("[Wifi] high-perf + multicast lock acquired (RTT 최적화).");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[Wifi] lock 실패(권한 WAKE_LOCK/CHANGE_WIFI_MULTICAST_STATE 확인): " + e.Message);
            }
        }

        private void OnDisable()
        {
            try { if (_wifiLock != null && _wifiLock.Call<bool>("isHeld")) _wifiLock.Call("release"); } catch { }
            try { if (_multicastLock != null && _multicastLock.Call<bool>("isHeld")) _multicastLock.Call("release"); } catch { }
            _wifiLock = null;
            _multicastLock = null;
        }
#endif
    }
}
