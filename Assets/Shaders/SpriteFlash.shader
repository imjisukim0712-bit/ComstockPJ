// 스프라이트를 잠깐 단색(기본 흰색)으로 물들이기 위한 셰이더.
//
// Unity 기본 "Sprites/Default"와 렌더링 결과가 같도록 만들고, _FlashAmount(0~1)만 추가했다.
// 0이면 기본 스프라이트와 완전히 동일하게 보이므로, 유닛에 이 머티리얼을 항상 물려두고
// 피격 순간에만 값을 1로 올렸다가 되돌리면 된다(HitFlash.cs 참고).
//
// 색을 "곱하는" 방식(SpriteRenderer.color)으로는 흰색을 만들 수 없다 - 곱셈은 밝게만 할 수
// 있고 원래 색상이 그대로 비친다. 그래서 알파는 그대로 두고 RGB만 목표 색으로 lerp한다.
Shader "Comstock/SpriteFlash"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _FlashColor ("Flash Color", Color) = (1,1,1,1)
        _FlashAmount ("Flash Amount", Range(0,1)) = 0
        [MaterialToggle] PixelSnap ("Pixel snap", Float) = 0
        [HideInInspector] _RendererColor ("RendererColor", Color) = (1,1,1,1)
        [HideInInspector] _Flip ("Flip", Vector) = (1,1,1,1)
        [HideInInspector] _AlphaTex ("External Alpha", 2D) = "white" {}
        [HideInInspector] _EnableExternalAlpha ("Enable External Alpha", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend One OneMinusSrcAlpha

        Pass
        {
        CGPROGRAM
            #pragma vertex SpriteVert
            #pragma fragment FlashFrag
            #pragma target 2.0
            #pragma multi_compile_instancing
            #pragma multi_compile_local _ PIXELSNAP_ON
            #pragma multi_compile _ ETC1_EXTERNAL_ALPHA
            #include "UnitySprites.cginc"

            fixed4 _FlashColor;
            float _FlashAmount;

            fixed4 FlashFrag(v2f IN) : SV_Target
            {
                fixed4 c = SampleSpriteTexture(IN.texcoord) * IN.color;

                // 알파는 건드리지 않는다 - 실루엣 모양이 그대로 유지되어야 한다.
                c.rgb = lerp(c.rgb, _FlashColor.rgb, saturate(_FlashAmount));

                c.rgb *= c.a; // Sprites/Default와 동일한 premultiplied alpha 처리
                return c;
            }
        ENDCG
        }
    }
}
