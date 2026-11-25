// Kadr / Stochastic Glass
// The glass version of Kadr / Stochastic Surface: URP (Forward / Forward+), for flat or boxy glass built in ProBuilder.
//  - No UVs: the texture is projected from world position along the face's dominant axis, so panes line up and
//    "Texture size in metres" is the only scale you set.
//  - Stochastic sampling: each pixel blends two randomly offset copies of the texture, so the repeat can't be seen.
//  - One texture set: base, normal, roughness, opacity, metallic, height (parallax) and ambient occlusion.
//  - Transparent, with premultiplied blending: dirt and tint fade with the opacity map, but highlights and reflections
//    stay, which is what makes glass read as glass.
//  - Casts a shadow only where the opacity is above "Shadow opacity threshold", so clear glass lets light through.
Shader "Kadr/Stochastic Glass"
{
    Properties
    {
        _BaseMap ("Base Map", 2D) = "white" {}
        [Normal] _NormalMap ("Normal Map", 2D) = "bump" {}
        _RoughMap ("Roughness Map", 2D) = "white" {}
        _OpacityMap ("Opacity Map (white = solid)", 2D) = "white" {}
        _MetallicMap ("Metallic Map", 2D) = "black" {}
        _HeightMap ("Height (Displacement) Map", 2D) = "gray" {}
        _OcclusionMap ("Occlusion Map", 2D) = "white" {}

        [Header(Look)]
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        _TileMeters ("Texture size in metres", Float) = 1
        _Opacity ("Opacity (multiplies the map)", Range(0, 1)) = 1
        _Metallic ("Metallic (multiplies the map)", Range(0, 1)) = 1
        _Smoothness ("Smoothness (multiplies 1 - roughness)", Range(0, 1)) = 1
        _NormalStrength ("Normal strength", Range(0, 2)) = 1
        _OcclusionStrength ("Occlusion strength", Range(0, 1)) = 1
        _HeightScale ("Height depth (m)", Range(0, 0.1)) = 0.01

        [Header(Quality)]
        [Toggle(_PARALLAX)] _Parallax ("Use height map (parallax). Turn off for the low quality mode.", Float) = 1

        [Header(Hiding the repeat)]
        _BreakupScale ("Offset patch size (patches per tile)", Range(0.05, 3)) = 0.5

        [Header(Shadows and sides)]
        _ShadowCutoff ("Shadow opacity threshold (casts a shadow above this)", Range(0, 1)) = 0.6
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Render faces (Off = both sides, for a single plane)", Float) = 2
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "UniversalMaterialType" = "Lit" "Queue" = "Transparent" "IgnoreProjector" = "True" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_BaseMap);       SAMPLER(sampler_BaseMap);
        TEXTURE2D(_NormalMap);     SAMPLER(sampler_NormalMap);
        TEXTURE2D(_RoughMap);      SAMPLER(sampler_RoughMap);
        TEXTURE2D(_OpacityMap);    SAMPLER(sampler_OpacityMap);
        TEXTURE2D(_MetallicMap);   SAMPLER(sampler_MetallicMap);
        TEXTURE2D(_HeightMap);     SAMPLER(sampler_HeightMap);
        TEXTURE2D(_OcclusionMap);  SAMPLER(sampler_OcclusionMap);

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST; // unused, kept so Unity's texture tooling stays quiet
            float4 _Tint;
            float _TileMeters; float _Opacity; float _Metallic; float _Smoothness;
            float _NormalStrength; float _OcclusionStrength; float _HeightScale;
            float _BreakupScale; float _ShadowCutoff; float _Cull;
        CBUFFER_END

        float Hash21(float2 p)
        {
            p = frac(p * float2(123.34, 456.21));
            p += dot(p, p + 45.32);
            return frac(p.x * p.y);
        }

        float ValueNoise(float2 p)
        {
            float2 i = floor(p);
            float2 f = frac(p);
            float a = Hash21(i);
            float b = Hash21(i + float2(1, 0));
            float c = Hash21(i + float2(0, 1));
            float d = Hash21(i + float2(1, 1));
            float2 u = f * f * (3.0 - 2.0 * f);
            return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
        }

        // World position -> 2D texture coordinates along the dominant axis of the face. Flipping U on back faces keeps
        // the texture (and the normal map's X) from mirroring. No branches around the derivatives, so they stay valid.
        struct Projection
        {
            float2 uv;      // in tiles
            float2 dx;
            float2 dy;
            float xFlip;
            float3 uAxis;
            float3 vAxis;
            bool useX;
            bool useY;
        };

        Projection Project(float3 posWS, float3 N)
        {
            float3 an = abs(N);
            float3 s = float3(N.x >= 0 ? 1 : -1, N.y >= 0 ? 1 : -1, N.z >= 0 ? 1 : -1);

            float2 uvX = float2(posWS.z * s.x, posWS.y);
            float2 uvY = float2(posWS.x * s.y, posWS.z);
            float2 uvZ = float2(posWS.x * -s.z, posWS.y);

            Projection p;
            p.useY = an.y >= an.x && an.y >= an.z;
            p.useX = !p.useY && an.x >= an.z;

            float2 uvM = p.useY ? uvY : (p.useX ? uvX : uvZ);
            p.xFlip = p.useY ? s.y : (p.useX ? s.x : -s.z);
            p.uAxis = p.useY ? float3(s.y, 0, 0) : (p.useX ? float3(0, 0, s.x) : float3(-s.z, 0, 0));
            p.vAxis = p.useY ? float3(0, 0, 1) : float3(0, 1, 0);

            float inv = rcp(max(_TileMeters, 0.01));
            p.uv = uvM * inv;
            p.dx = ddx(uvM) * inv;
            p.dy = ddy(uvM) * inv;
            return p;
        }

        // Stochastic sampling after Inigo Quilez ("texture repetition"): two taps with different random offsets,
        // blended by a weight that changes smoothly over the surface. The same offsets serve every map.
        void StochasticOffsets(float2 uv, out float2 offA, out float2 offB, out float w)
        {
            float k = ValueNoise(uv * _BreakupScale);
            float index = k * 8.0;
            float f = frac(index);
            float ia = floor(index);
            offA = sin(float2(3.0, 7.0) * ia);
            offB = sin(float2(3.0, 7.0) * (ia + 1.0));
            w = smoothstep(0.2, 0.8, f);
        }

        struct GlassData
        {
            float3 albedo;
            float3 tn;       // tangent space normal
            float ao;
            float smoothness;
            float metallic;
            float opacity;
        };

        // parallaxDir = tangent space view direction xy / z (in the projection's own axes).
        GlassData SampleGlass(Projection p, float2 parallaxDir)
        {
            float2 uv = p.uv;
            float2 offA, offB;
            float w;
            StochasticOffsets(uv, offA, offB, w);

        #if defined(_PARALLAX)
            // One step of parallax: read the height here, then slide where every other map is read. Centred on 0.5 so
            // the surface as a whole does not shift.
            float hA = SAMPLE_TEXTURE2D_GRAD(_HeightMap, sampler_HeightMap, uv + offA, p.dx, p.dy).r;
            float hB = SAMPLE_TEXTURE2D_GRAD(_HeightMap, sampler_HeightMap, uv + offB, p.dx, p.dy).r;
            float height = lerp(hA, hB, w);
            uv += parallaxDir * ((height - 0.5) * _HeightScale * rcp(max(_TileMeters, 0.01)));
        #endif

            float4 baseA = SAMPLE_TEXTURE2D_GRAD(_BaseMap, sampler_BaseMap, uv + offA, p.dx, p.dy);
            float4 baseB = SAMPLE_TEXTURE2D_GRAD(_BaseMap, sampler_BaseMap, uv + offB, p.dx, p.dy);
            float3 nA = UnpackNormalScale(SAMPLE_TEXTURE2D_GRAD(_NormalMap, sampler_NormalMap, uv + offA, p.dx, p.dy), _NormalStrength);
            float3 nB = UnpackNormalScale(SAMPLE_TEXTURE2D_GRAD(_NormalMap, sampler_NormalMap, uv + offB, p.dx, p.dy), _NormalStrength);
            float aoA = SAMPLE_TEXTURE2D_GRAD(_OcclusionMap, sampler_OcclusionMap, uv + offA, p.dx, p.dy).g;
            float aoB = SAMPLE_TEXTURE2D_GRAD(_OcclusionMap, sampler_OcclusionMap, uv + offB, p.dx, p.dy).g;
            float roughA = SAMPLE_TEXTURE2D_GRAD(_RoughMap, sampler_RoughMap, uv + offA, p.dx, p.dy).g;
            float roughB = SAMPLE_TEXTURE2D_GRAD(_RoughMap, sampler_RoughMap, uv + offB, p.dx, p.dy).g;
            float opaA = SAMPLE_TEXTURE2D_GRAD(_OpacityMap, sampler_OpacityMap, uv + offA, p.dx, p.dy).g;
            float opaB = SAMPLE_TEXTURE2D_GRAD(_OpacityMap, sampler_OpacityMap, uv + offB, p.dx, p.dy).g;
            float metA = SAMPLE_TEXTURE2D_GRAD(_MetallicMap, sampler_MetallicMap, uv + offA, p.dx, p.dy).g;
            float metB = SAMPLE_TEXTURE2D_GRAD(_MetallicMap, sampler_MetallicMap, uv + offB, p.dx, p.dy).g;

            GlassData data;
            data.albedo = lerp(baseA.rgb, baseB.rgb, w) * _Tint.rgb;
            data.tn = normalize(lerp(nA, nB, w));
            data.ao = lerp(1.0, lerp(aoA, aoB, w), _OcclusionStrength);
            data.smoothness = (1.0 - lerp(roughA, roughB, w)) * _Smoothness;
            data.metallic = lerp(metA, metB, w) * _Metallic;
            data.opacity = saturate(lerp(opaA, opaB, w) * _Opacity * _Tint.a);
            return data;
        }

        // Opacity only, for the shadow pass (no parallax: shadows do not need it).
        float SampleOpacityOnly(Projection p)
        {
            float2 offA, offB;
            float w;
            StochasticOffsets(p.uv, offA, offB, w);
            float opaA = SAMPLE_TEXTURE2D_GRAD(_OpacityMap, sampler_OpacityMap, p.uv + offA, p.dx, p.dy).g;
            float opaB = SAMPLE_TEXTURE2D_GRAD(_OpacityMap, sampler_OpacityMap, p.uv + offB, p.dx, p.dy).g;
            return saturate(lerp(opaA, opaB, w) * _Opacity * _Tint.a);
        }
        ENDHLSL

        // ------------------------------------------------------------------ forward lit
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            // Premultiplied: the diffuse colour is multiplied by the opacity in the shader, highlights are not.
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma shader_feature_local _PARALLAX

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _FORWARD_PLUS _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DYNAMICLIGHTMAP_ON
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 staticLightmapUV : TEXCOORD1;
                float2 dynamicLightmapUV : TEXCOORD2;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                half fogFactor : TEXCOORD2;
                DECLARE_LIGHTMAP_OR_SH(staticLightmapUV, vertexSH, 3);
            #ifdef DYNAMICLIGHTMAP_ON
                float2 dynamicLightmapUV : TEXCOORD4;
            #endif
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;

                VertexPositionInputs vp = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = vp.positionCS;
                output.positionWS = vp.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.fogFactor = ComputeFogFactor(vp.positionCS.z);

                OUTPUT_LIGHTMAP_UV(input.staticLightmapUV, unity_LightmapST, output.staticLightmapUV);
            #ifdef DYNAMICLIGHTMAP_ON
                output.dynamicLightmapUV = input.dynamicLightmapUV.xy * unity_DynamicLightmapST.xy + unity_DynamicLightmapST.zw;
            #endif
                OUTPUT_SH(output.normalWS.xyz, output.vertexSH);

                return output;
            }

            half4 Frag(Varyings input, FRONT_FACE_TYPE isFront : FRONT_FACE_SEMANTIC) : SV_Target
            {
                float3 posWS = input.positionWS;

                // With "Render faces: Off" the back of a pane is drawn too: turn its normal to face the viewer.
                float3 N = normalize(input.normalWS) * IS_FRONT_VFACE(isFront, 1.0, -1.0);

                Projection p = Project(posWS, N);

                // View direction in the projection's own axes (U axis, V axis, face normal), for the height map.
                float3 V = GetWorldSpaceNormalizeViewDir(posWS);
                float3 viewTS = float3(dot(V, p.uAxis), dot(V, p.vAxis), dot(V, N));
                float2 parallaxDir = viewTS.xy / max(abs(viewTS.z), 0.3);

                GlassData glass = SampleGlass(p, parallaxDir);

                // Tangent space normal to world space, with the face's own normal as the base (UDN blend).
                float3 tn = glass.tn;
                tn.x *= p.xFlip;
                float3 worldN;
                if (p.useX) worldN = float3(N.x, tn.y + N.y, tn.x + N.z);
                else if (p.useY) worldN = float3(tn.x + N.x, N.y, tn.y + N.z);
                else worldN = float3(tn.x + N.x, tn.y + N.y, N.z);
                worldN = normalize(worldN);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = glass.albedo * glass.opacity; // premultiplied: dirt and tint fade with the opacity
                surface.metallic = glass.metallic;
                surface.specular = half3(0, 0, 0);
                surface.smoothness = glass.smoothness;
                surface.occlusion = glass.ao;
                surface.alpha = glass.opacity;
                surface.normalTS = half3(0, 0, 1);

                InputData inputData = (InputData)0;
                inputData.positionWS = posWS;
                inputData.positionCS = input.positionCS;
                inputData.normalWS = worldN;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(posWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(posWS);
                inputData.fogCoord = InitializeInputDataFog(float4(posWS, 1.0), input.fogFactor);
                inputData.vertexLighting = half3(0, 0, 0);
            #if defined(DYNAMICLIGHTMAP_ON)
                inputData.bakedGI = SAMPLE_GI(input.staticLightmapUV, input.dynamicLightmapUV, input.vertexSH, inputData.normalWS);
            #else
                inputData.bakedGI = SAMPLE_GI(input.staticLightmapUV, input.vertexSH, inputData.normalWS);
            #endif
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = SAMPLE_SHADOWMASK(input.staticLightmapUV);

                half4 color = UniversalFragmentPBR(inputData, surface);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                color.a = glass.opacity;
                return color;
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------ shadows
        // Casts a shadow only where the opacity is above the threshold, so clear glass lets light through and
        // dirty or frosted glass does not.
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct ShadowAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
            };

            ShadowVaryings ShadowVert(ShadowAttributes input)
            {
                ShadowVaryings output;

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);

            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirectionWS = _LightDirection;
            #endif

                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
            #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #endif

                output.positionCS = positionCS;
                output.positionWS = positionWS;
                output.normalWS = normalWS;
                return output;
            }

            half4 ShadowFrag(ShadowVaryings input) : SV_Target
            {
                Projection p = Project(input.positionWS, normalize(input.normalWS));
                clip(SampleOpacityOnly(p) - _ShadowCutoff);
                return 0;
            }
            ENDHLSL
        }
    }
}
