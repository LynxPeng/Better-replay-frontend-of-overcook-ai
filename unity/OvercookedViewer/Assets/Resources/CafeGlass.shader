Shader "OvercookedReplay/CafeGlass"
{
    Properties {
        _BaseMap ("Town palette", 2D) = "white" {}
        _Night ("Lamps on", Range(0,1)) = 0
    }
    SubShader {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Pass {
            Tags { "LightMode"="UniversalForwardOnly" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float _Night;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0; };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; half shade:TEXCOORD1; };
            Varyings vert(Attributes v) {
                Varyings o;
                o.positionCS=TransformObjectToHClip(v.positionOS.xyz);
                o.uv=v.uv;
                o.shade=dot(TransformObjectToWorldNormal(v.normalOS),normalize(float3(-.43,.72,-.57)))*.5+.5;
                return o;
            }
            half4 frag(Varyings i):SV_Target {
                half3 source=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv).rgb;
                // Both native lantern meshes use a strip at the lower right
                // of the atlas for glass. The other swatches are cord and metal.
                if(i.uv.x<.75 || i.uv.y>.20)
                    return half4(source*lerp(.27,.64,i.shade)*lerp(1,.45,_Night),1);
                half3 glass=lerp(half3(.89,.51,.20),half3(1,.89,.64),saturate(source.g+.25));
                half cool=saturate((source.b-source.r)*2)*saturate((source.g-source.r)*2);
                glass=lerp(glass,half3(.62,.81,.78),cool);
                half shade=lerp(.36+.54*i.shade,.55+.48*i.shade,_Night);
                return half4(glass*shade,1);
            }
            ENDHLSL
        }
    }
}
