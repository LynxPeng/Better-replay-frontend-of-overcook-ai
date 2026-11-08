Shader "Hidden/OvercookedReplay/RecipeIllustration"
{
    Properties { _Atlas ("Restaurant palette", 2D) = "white" {} }
    SubShader
    {
        Pass
        {
            Cull Off
            ZWrite On
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _Atlas;
            float4x4 _IconVP;
            struct Input { float4 vertex : POSITION; float3 normal : NORMAL; float2 uv : TEXCOORD0; };
            struct Varying { float4 clip : SV_POSITION; float2 uv : TEXCOORD0; float light : TEXCOORD1; };

            Varying vert(Input input) {
                Varying o;
                o.clip = mul(_IconVP, mul(unity_ObjectToWorld, input.vertex));
                o.uv = input.uv;
                float facing = dot(UnityObjectToWorldNormal(input.normal), normalize(float3(-.4, .78, -.48)));
                o.light = .69 + .41 * saturate(facing);
                return o;
            }
            half4 frag(Varying i) : SV_Target { return half4(tex2D(_Atlas, i.uv).rgb * i.light, 1); }
            ENDHLSL
        }
    }
}
