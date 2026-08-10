// 스프라이트를 잠깐 단색(기본 흰색)으로 물들이기 위한 셰이더.
//
// URP 2D 기본 스프라이트 셰이더("Universal Render Pipeline/2D/Sprite-Unlit-Default")를
// 그대로 복제하고 _FlashAmount(0~1)만 추가했다. 0이면 기본 스프라이트와 완전히 동일하게
// 보이므로, 유닛에 이 머티리얼을 항상 물려두고 피격 순간에만 값을 1로 올렸다가 되돌리면 된다
// (HitFlash.cs 참고).
//
// 색을 "곱하는" 방식(SpriteRenderer.color)으로는 흰색을 만들 수 없다 - 곱셈은 밝게만 할 수
// 있고 원래 색상이 그대로 비친다. 그래서 알파는 그대로 두고 RGB만 목표 색으로 lerp한다.
//
// **이 셰이더는 반드시 URP(HLSL) 셰이더여야 한다. 빌트인 렌더 파이프라인용 스프라이트 셰이더
// (CGPROGRAM + UnitySprites.cginc)로 작성하면 안 된다.**
// 이 프로젝트는 URP 2D Renderer를 쓰는데, 빌트인 RP용 스프라이트 셰이더는 URP에서
// SpriteRenderer의 렌더러별 데이터(_MainTex 등)를 전달받지 못한다. 그 결과 각 파츠가
// 엉뚱한 텍스처(다른 파츠·좀비·배경)로 그려진다. Unity 기본 "Sprites/Default"를 붙여도
// 똑같이 깨지는 것을 실측으로 확인했다(2026-08-10). MaterialPropertyBlock을 한 번이라도
// 설정하면 렌더러별 데이터가 강제로 업로드되어 정상으로 돌아오는데, 이것이 "첫 피격
// 전까지만 파츠 이미지가 깨져 보이던" 증상의 정체였다. 상세는 작업.md 참고.
Shader "Comstock/SpriteFlash"
{
    Properties
    {
        _MainTex ("Sprite Texture", 2D) = "white" {}
        [MaterialToggle] _ZWrite("ZWrite", Float) = 0

        _FlashColor ("Flash Color", Color) = (1,1,1,1)
        _FlashAmount ("Flash Amount", Range(0,1)) = 0

        // 레거시 스프라이트 셰이더로 폴백될 때를 위한 값들(URP 기본 스프라이트 셰이더와 동일)
        [HideInInspector] _Color ("Tint", Color) = (1,1,1,1)
        [HideInInspector] PixelSnap ("Pixel snap", Float) = 0
        [HideInInspector] _RendererColor ("RendererColor", Color) = (1,1,1,1)
        [HideInInspector] _AlphaTex ("External Alpha", 2D) = "white" {}
        [HideInInspector] _EnableExternalAlpha ("Enable External Alpha", Float) = 0
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
        Cull Off
        ZWrite [_ZWrite]

        Pass
        {
            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

            #pragma vertex UnlitVertex
            #pragma fragment FlashFragment

            struct Attributes
            {
                COMMON_2D_INPUTS
                half4 color : COLOR;
                UNITY_SKINNED_VERTEX_INPUTS
            };

            struct Varyings
            {
                COMMON_2D_OUTPUTS
                half4 color : COLOR;
            };

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/2DCommon.hlsl"

            // GPU Instancing
            #pragma multi_compile_instancing
            #pragma multi_compile _ DEBUG_DISPLAY SKINNED_SPRITE

            // NOTE: SRP Batcher는 레이아웃이 다르면 처리하지 못하므로 여기서 #ifdef를 쓰지 않는다.
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _FlashColor;
                float _FlashAmount;
            CBUFFER_END

            Varyings UnlitVertex(Attributes input)
            {
                UNITY_SKINNED_VERTEX_COMPUTE(input);
                SetUpSpriteInstanceProperties();
                input.positionOS = UnityFlipSprite(input.positionOS, unity_SpriteProps.xy);

                Varyings o = CommonUnlitVertex(input);
                o.color = input.color * _Color * unity_SpriteColor;
                return o;
            }

            half4 FlashFragment(Varyings input) : SV_Target
            {
                half4 c = CommonUnlitFragment(input, input.color);

                // 알파는 건드리지 않는다 - 실루엣 모양이 그대로 유지되어야 한다.
                // URP 2D 스프라이트는 straight alpha 블렌딩이므로 premultiply 하지 않는다.
                c.rgb = lerp(c.rgb, _FlashColor.rgb, saturate(_FlashAmount));
                return c;
            }
            ENDHLSL
        }
    }
}
