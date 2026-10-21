Shader "GourmetAbyss/Effects/Wind Sway"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        [MainColor] _Color ("颜色叠加", Color) = (1,1,1,1)
        _WindStrength ("摆动幅度（世界单位）", Range(0,0.5)) = 0.04
        _WindSpeed ("摆动速度", Range(0,5)) = 1.5
        _WindDirection ("世界风向 XYZ", Vector) = (1,0,0,0)
        _WindSpatialScale ("物件节奏差异", Range(0,5)) = 0.7
        _GustStrength ("阵风变化", Range(0,1)) = 0.45
        _RootFixedRange ("底部固定比例", Range(0,0.9)) = 0.1
        _Flexibility ("弯曲曲线指数", Range(0.5,4)) = 1.5
        _SpriteUVRect ("图片 UV 范围 XYWH", Vector) = (0,0,1,1)
        [HideInInspector] _RendererColor ("Renderer Color", Color) = (1,1,1,1)
        [HideInInspector] _Flip ("Flip", Vector) = (1,1,1,1)
        [PerRendererData] [HideInInspector] _AlphaTex ("External Alpha", 2D) = "white" {}
        [PerRendererData] [HideInInspector] _EnableExternalAlpha ("External Alpha Enabled", Float) = 0
        [HideInInspector] _WindTimeOverride ("Test time; negative uses Unity time", Float) = -1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent"
            "CanUseSpriteAtlas"="True" "IgnoreProjector"="True" "DisableBatching"="True" }
        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"
        #include "../../Common/WindWave.hlsl"
        TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
        TEXTURE2D(_AlphaTex); SAMPLER(sampler_AlphaTex);
        CBUFFER_START(UnityPerMaterial)
            half4 _Color;
            float _WindStrength;
            float _WindSpeed;
            float4 _WindDirection;
            float _WindSpatialScale;
            float _GustStrength;
            float _RootFixedRange;
            float _Flexibility;
            float4 _SpriteUVRect;
            float _WindTimeOverride;
        CBUFFER_END
        half4 _RendererColor;
        float _EnableExternalAlpha;

        struct Attributes
        {
            float3 positionOS : POSITION;
            float2 uv : TEXCOORD0;
            half4 color : COLOR;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };
        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float2 uv : TEXCOORD0;
            half4 color : COLOR;
            UNITY_VERTEX_OUTPUT_STEREO
        };
        Varyings WindVertex(Attributes input)
        {
            Varyings output = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            float3 positionOS = input.positionOS;
            #ifdef UNITY_INSTANCING_ENABLED
                positionOS = UnityFlipSprite(positionOS, unity_SpriteFlip);
            #endif
            // Source UVs stay attached to the artwork when SpriteRenderer flips it.
            float imageY = (input.uv.y - _SpriteUVRect.y) / max(_SpriteUVRect.w, 0.0001);
            float weight = GA_RootWeight(imageY, saturate(_RootFixedRange), _Flexibility);
            float3 anchorWS = TransformObjectToWorld(float3(0,0,0));
            float time = _WindTimeOverride >= 0 ? _WindTimeOverride : _Time.y;
            float3 direction = _WindDirection.xyz;
            direction = dot(direction, direction) > 0.000001 ? normalize(direction) : float3(1,0,0);
            // Keep movement inside the sprite plane as it faces the camera.
            float3 rightWS = normalize(TransformObjectToWorldDir(float3(1,0,0)));
            float projection = dot(direction, rightWS);
            float3 positionWS = TransformObjectToWorld(positionOS);
            positionWS += rightWS * projection * max(_WindStrength, 0) * weight
                * GA_WindWave(anchorWS, time, max(_WindSpeed, 0), _WindSpatialScale, _GustStrength);
            output.positionCS = TransformWorldToHClip(positionWS);
            output.uv = input.uv;
            output.color = input.color * _Color * _RendererColor;
            #ifdef UNITY_INSTANCING_ENABLED
                output.color *= unity_SpriteColor;
            #endif
            return output;
        }
        half4 WindFragment(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            half4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
            half externalAlpha = SAMPLE_TEXTURE2D(_AlphaTex, sampler_AlphaTex, input.uv).r;
            color.a = lerp(color.a, externalAlpha, _EnableExternalAlpha);
            return color * input.color;
        }
        ENDHLSL
        Pass
        {
            Name "SpriteWindForward"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex WindVertex
            #pragma fragment WindFragment
            #pragma multi_compile_instancing
            ENDHLSL
        }
        Pass
        {
            Name "SpriteWind2D"
            Tags { "LightMode"="Universal2D" }
            HLSLPROGRAM
            #pragma vertex WindVertex
            #pragma fragment WindFragment
            #pragma multi_compile_instancing
            ENDHLSL
        }
    }
    Fallback "Sprites/Default"
}
