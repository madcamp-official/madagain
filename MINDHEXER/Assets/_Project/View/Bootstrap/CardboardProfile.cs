using System;
using System.Collections.Generic;
using System.Text;

namespace Game.View
{
    /// <summary>
    /// Cardboard <b>렌즈 프로파일</b>(디바이스 파라미터)을 <see cref="VrTuningData"/>로부터 생성·주입한다.
    ///
    /// <para>왜곡 k1/k2·FOV·IPD는 플러그인이 <b>디바이스 프로파일로만</b> 받는다(실시간 슬라이더 없음).
    /// 그래서 값 → protobuf → base64url URI → <c>Api.SaveDeviceParams</c> + <c>ReloadDeviceParams</c> 경로로
    /// "조정 → 적용 → 확인" 한다. WWGC로 만든 URI(<see cref="VrTuningData.cardboardProfileUri"/>)가 있으면 그걸 우선.</para>
    ///
    /// <para>Cardboard 어셈블리에 하드 의존하지 않도록 <b>리플렉션</b>으로 API를 호출한다(플러그인 없으면 no-op).
    /// <c>SaveDeviceParams</c>는 XR 초기화 후에만 동작하므로 <see cref="VrMode"/>.Enabled(온디바이스)에서만 적용한다.</para>
    ///
    /// <para>⚠️ 미검증: (1) protobuf 필드 배치 정확성, (2) SaveDeviceParams 리다이렉트 이슈(#323) —
    /// 실기에서 확인 필요. 안 되면 WWGC URI 폴백.</para>
    /// </summary>
    public static class CardboardProfile
    {
        // cardboard_device.proto 필드 번호:
        //  1 vendor(str) 2 model(str) 3 screen_to_lens(f32) 4 inter_lens(f32)
        //  5 left_eye_fov[4](packed f32: 좌·우·하·상 deg) 6 tray_to_lens(f32)
        //  7 distortion[2](packed f32: k1,k2) 11 vertical_alignment(enum varint, BOTTOM=0)
        const string Vendor = "MINDHEXER";
        const string Model = "VR-01";

        /// <summary>렌즈값 → Cardboard 디바이스 파라미터 protobuf 바이트.</summary>
        public static byte[] BuildDeviceParams(VrTuningData d)
        {
            var b = new List<byte>(96);
            WriteString(b, 1, Vendor);
            WriteString(b, 2, Model);
            WriteFloat(b, 3, d.screenToLensDistance);
            WriteFloat(b, 4, d.interLensDistance);
            WritePackedFloats(b, 5, new[] { d.fovLeft, d.fovRight, d.fovBottom, d.fovTop });
            WriteFloat(b, 6, d.trayToLensDistance);
            WritePackedFloats(b, 7, new[] { d.distortionK1, d.distortionK2 });
            WriteVarintField(b, 11, 0);   // vertical_alignment = BOTTOM
            return b.ToArray();
        }

        /// <summary>표준 Cardboard 프로파일 URI(<c>google.com/cardboard/cfg?p=&lt;base64url&gt;</c>).</summary>
        public static string BuildUri(VrTuningData d)
        {
            return "https://google.com/cardboard/cfg?p=" + ToBase64Url(BuildDeviceParams(d));
        }

        /// <summary>
        /// 렌즈 프로파일 주입. VR(온디바이스)에서만 동작. WWGC URI가 있으면 우선, 없으면 값에서 생성.
        /// 성공(호출됨) 시 true.
        /// </summary>
        public static bool Apply(VrTuningData d)
        {
            if (d == null || !VrMode.Enabled) return false;   // 에디터/PC(XR 미초기화)에선 no-op

            string uri = !string.IsNullOrEmpty(d.cardboardProfileUri) ? d.cardboardProfileUri : BuildUri(d);

            var apiType = Type.GetType("Google.XR.Cardboard.Api, Google.XR.Cardboard");
            if (apiType == null) return false;
            var save = apiType.GetMethod("SaveDeviceParams", new[] { typeof(string) });
            var reload = apiType.GetMethod("ReloadDeviceParams", Type.EmptyTypes);
            if (save == null || reload == null) return false;

            save.Invoke(null, new object[] { uri });
            reload.Invoke(null, null);
            return true;
        }

        // ── protobuf 인코딩 헬퍼 ───────────────────────────────────────────
        static void WriteKey(List<byte> b, int field, int wire) { WriteVarint(b, (uint)((field << 3) | wire)); }

        static void WriteVarint(List<byte> b, uint v)
        {
            while (v >= 0x80) { b.Add((byte)(v | 0x80)); v >>= 7; }
            b.Add((byte)v);
        }

        static void WriteVarintField(List<byte> b, int field, uint v) { WriteKey(b, field, 0); WriteVarint(b, v); }

        static void WriteFloat(List<byte> b, int field, float f)
        {
            WriteKey(b, field, 5);           // 32-bit
            AddFloatLE(b, f);
        }

        static void WriteString(List<byte> b, int field, string s)
        {
            WriteKey(b, field, 2);           // length-delimited
            byte[] bytes = Encoding.UTF8.GetBytes(s);
            WriteVarint(b, (uint)bytes.Length);
            b.AddRange(bytes);
        }

        static void WritePackedFloats(List<byte> b, int field, float[] fs)
        {
            WriteKey(b, field, 2);           // packed → length-delimited
            WriteVarint(b, (uint)(fs.Length * 4));
            for (int i = 0; i < fs.Length; i++) AddFloatLE(b, fs[i]);
        }

        static void AddFloatLE(List<byte> b, float f)
        {
            byte[] by = BitConverter.GetBytes(f);
            if (!BitConverter.IsLittleEndian) Array.Reverse(by);
            b.AddRange(by);
        }

        static string ToBase64Url(byte[] data)
        {
            return Convert.ToBase64String(data).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        }
    }
}
