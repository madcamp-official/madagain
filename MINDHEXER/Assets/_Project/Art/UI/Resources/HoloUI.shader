// [홀로그램 HUD, 2026-07-22] 코드로 그리는 UI 전용 가산 합성 셰이더.
// 텍스처가 없다 — 정점 색만 쓴다. 겹칠수록 밝아지는 가산(Blend One One)이라 얇은 선을
// 여러 겹 깔면 그것만으로 글로우가 생긴다(홀로그램 느낌의 핵심).
//
// Resources에 두는 이유: 런타임에 Shader.Find로 찾으면 빌드에서 스트립될 수 있어서,
// Resources.Load<Shader>("HoloUI")로 참조를 명시적으로 남긴다.
Shader "Precog/HoloUI"
{
    SubShader
    {
        Tags
        {
            "Queue" = "Overlay"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest Always
        Blend One One          // 정점 색을 미리 곱해 내보내므로(premultiplied) One One이 맞다

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color  : COLOR;
            };

            struct v2f
            {
                float4 pos   : SV_POSITION;
                fixed4 color : COLOR;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // 알파를 "밝기"로 쓴다 — 색 * 알파를 그대로 더한다.
                return fixed4(i.color.rgb * i.color.a, i.color.a);
            }
            ENDCG
        }
    }

    Fallback Off
}
