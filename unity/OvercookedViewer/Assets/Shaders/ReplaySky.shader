Shader "OvercookedReplay/SimpleSky" {
    Properties {
        [MainTexture] _BaseMap("Synty sky atlas",2D)="white"{}
        [MainColor] _BaseColor("Tint",Color)=(1,1,1,1)
        [HideInInspector] _ZWrite("Depth write",Float)=0
    }
    SubShader {
        Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Background"}
        Pass {
            Tags {"LightMode"="SRPDefaultUnlit"}
            Cull Off ZWrite Off ZTest LEqual
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_BaseMap);SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;float4 _BaseColor;
            CBUFFER_END
            struct Attributes {float4 positionOS:POSITION;float2 uv:TEXCOORD0;};
            struct Varyings {float4 positionCS:SV_POSITION;float2 uv:TEXCOORD0;};
            Varyings Vert(Attributes input){Varyings output;output.positionCS=TransformObjectToHClip(input.positionOS.xyz);output.uv=TRANSFORM_TEX(input.uv,_BaseMap);return output;}
            half4 Frag(Varyings input):SV_Target{return SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,input.uv)*_BaseColor;}
            ENDHLSL
        }
    }
}
