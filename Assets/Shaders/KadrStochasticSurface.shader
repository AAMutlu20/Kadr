// Kadr / Stochastic Surface
// A URP (Forward / Forward+) lit shader for level geometry built from boxes (ProBuilder).
//  - No UVs: the texture is projected from world position, along the face's dominant axis, so every cube lines up with
//    its neighbours and "Texture size in metres" is the only scale you set.
//  - Stochastic sampling: each pixel blends two randomly offset copies of the texture (offset changes smoothly over the
//    surface), so the repeat can't be seen. Offsets only, no rotation, so normal maps need no correction.
//  - Per layer: base, normal, occlusion, roughness and height (parallax, toggle "Use height maps").
//  - Two layers blended by world height with a noisy edge (lower = red dado, upper = plaster), plus large scale
//    brightness variation and grime near the floor.
Shader "Kadr/Stochastic Surface"
{
    Properties
    {
        [Header(Layer A  the lower part and floors)]
        _BaseMapA ("Base Map", 2D) = "white" {}
        [Normal] _NormalMapA ("Normal Map", 2D) = "bump" {}
        _OcclusionMapA ("Occlusion Map", 2D) = "white" {}
        _RoughMapA ("Roughness Map", 2D) = "white" {}
        _HeightMapA ("Height (Displacement) Map", 2D) = "gray" {}
        _TintA ("Tint", Color) = (1, 1, 1, 1)
        _TileMetersA ("Texture size in metres", Float) = 2
        _NormalStrengthA ("Normal strength", Range(0, 2)) = 1
        _OcclusionStrengthA ("Occlusion strength", Range(0, 1)) = 1
        _SmoothnessA ("Smoothness (multiplies 1 - roughness)", Range(0, 1)) = 1
        _HeightScaleA ("Height depth (m)", Range(0, 0.1)) = 0.03

        [Header(Layer B  the upper part)]
        [Toggle(_TWO_LAYERS)] _TwoLayers ("Use upper layer (off = floor material)", Float) = 1
        _BaseMapB ("Base Map", 2D) = "white" {}
        [Normal] _NormalMapB ("Normal Map", 2D) = "bump" {}
        _OcclusionMapB ("Occlusion Map", 2D) = "white" {}
        _RoughMapB ("Roughness Map", 2D) = "white" {}
        _HeightMapB ("Height (Displacement) Map", 2D) = "gray" {}
        _TintB ("Tint", Color) = (1, 1, 1, 1)
        _TileMetersB ("Texture size in metres", Float) = 1
        _NormalStrengthB ("Normal strength", Range(0, 2)) = 1
        _OcclusionStrengthB ("Occlusion strength", Range(0, 1)) = 1
        _SmoothnessB ("Smoothness (multiplies 1 - roughness)", Range(0, 1)) = 1
        _HeightScaleB ("Height depth (m)", Range(0, 0.1)) = 0.03

        [Header(Quality)]
        [Toggle(_PARALLAX)] _Parallax ("Use height maps (parallax). Turn off for the low quality mode.", Float) = 1

        [Header(Where the upper layer starts)]
        _BlendHeight ("Height in world metres", Float) = 1.2
        _BlendSoftness ("Softness", Range(0.001, 0.5)) = 0.05
        _EdgeNoiseAmount ("Ragged edge amount (m)", Range(0, 0.6)) = 0.12
        _EdgeNoiseScale ("Ragged edge size (noise per metre)", Range(0.2, 8)) = 2.5

        [Header(Hiding the repeat)]
        _BreakupScale ("Offset patch size (patches per tile)", Range(0.05, 3)) = 0.5

        [Header(Large scale variation)]
        _MacroScale ("Size (noise per metre)", Range(0.02, 1)) = 0.12
        _MacroStrength ("Strength", Range(0, 0.8)) = 0.25

        [Header(Grime near the floor)]
        _GrimeHeight ("Height (m)", Range(0.05, 3)) = 0.8
        _GrimeStrength ("Strength", Range(0, 1)) = 0.35
        _GrimeColor ("Darkening colour", Color) = (0.25, 0.22, 0.2, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "UniversalMaterialType" = "Lit" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_BaseMapA);       SAMPLER(sampler_BaseMapA);
        TEXTURE2D(_NormalMapA);     SAMPLER(sampler_NormalMapA);
        TEXTURE2D(_OcclusionMapA);  SAMPLER(sampler_OcclusionMapA);
        TEXTURE2D(_RoughMapA);      SAMPLER(sampler_RoughMapA);
        TEXTURE2D(_HeightMapA);     SAMPLER(sampler_HeightMapA);
        TEXTURE2D(_BaseMapB);       SAMPLER(sampler_BaseMapB);
        TEXTURE2D(_NormalMapB);     SAMPLER(sampler_NormalMapB);
        TEXTURE2D(_OcclusionMapB);  SAMPLER(sampler_OcclusionMapB);
        TEXTURE2D(_RoughMapB);      SAMPLER(sampler_RoughMapB);
        TEXTURE2D(_HeightMapB);     SAMPLER(sampler_HeightMapB);

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMapA_ST; float4 _BaseMapB_ST; // unused, kept so Unity's texture tooling stays quiet
            float4 _TintA; float _TileMetersA; float _NormalStrengthA; float _OcclusionStrengthA; float _SmoothnessA; float _HeightScaleA;
            float4 _TintB; float _TileMetersB; float _NormalStrengthB; float _OcclusionStrengthB; float _SmoothnessB; float _HeightScaleB;
            float _BlendHeight; float _BlendSoftness; float _EdgeNoiseAmount; float _EdgeNoiseScale;
            float _BreakupScale;
            float _MacroScale; float _MacroStrength;
            float _GrimeHeight; float _GrimeStrength; float4 _GrimeColor;
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

        struct LayerData
        {
            float3 albedo;
            float3 tn;   // tangent space normal, xy only matters
            float ao;
            float smoothness;
        };

        // Stochastic sampling after Inigo Quilez ("texture repetition"): two taps with different random offsets,
        // blended by a weight that changes smoothly over the surface. Same offsets/weight for every map of the layer.
        // parallaxDir = tangent space view direction xy / z (in the projection's own axes).
        LayerData SampleLayer(
            TEXTURE2D_PARAM(baseT, baseS), TEXTURE2D_PARAM(nrmT, nrmS), TEXTURE2D_PARAM(aoT, aoS),
            TEXTURE2D_PARAM(roughT, roughS), TEXTURE2D_PARAM(heightT, heightS),
            float2 uvM, float2 dxM, float2 dyM, float2 parallaxDir,
            float tileMeters, float3 tint, float normalStrength, float aoStrength,
            float smoothnessScale, float heightScale)
        {
            float inv = rcp(max(tileMeters, 0.01));
            float2 uv = uvM * inv;
            float2 dx = dxM * inv;
            float2 dy = dyM * inv;

            float k = ValueNoise(uv * _BreakupScale);
            float index = k * 8.0;
            float f = frac(index);
            float ia = floor(index);
            float ib = ia + 1.0;
            float2 offA = sin(float2(3.0, 7.0) * ia);
            float2 offB = sin(float2(3.0, 7.0) * ib);
            float w = smoothstep(0.2, 0.8, f);

        #if defined(_PARALLAX)
            // One step of parallax: read the height here, then slide where every other map is read. Centred on 0.5 so
            // the surface as a whole does not shift.
            float hA = SAMPLE_TEXTURE2D_GRAD(heightT, heightS, uv + offA, dx, dy).r;
            float hB = SAMPLE_TEXTURE2D_GRAD(heightT, heightS, uv + offB, dx, dy).r;
            float height = lerp(hA, hB, w);
            uv += parallaxDir * ((height - 0.5) * heightScale * inv);
        #endif

            float4 baseA = SAMPLE_TEXTURE2D_GRAD(baseT, baseS, uv + offA, dx, dy);
            float4 baseB = SAMPLE_TEXTURE2D_GRAD(baseT, baseS, uv + offB, dx, dy);
            float3 nA = UnpackNormalScale(SAMPLE_TEXTURE2D_GRAD(nrmT, nrmS, uv + offA, dx, dy), normalStrength);
            float3 nB = UnpackNormalScale(SAMPLE_TEXTURE2D_GRAD(nrmT, nrmS, uv + offB, dx, dy), normalStrength);
            float aoA = SAMPLE_TEXTURE2D_GRAD(aoT, aoS, uv + offA, dx, dy).g;
            float aoB = SAMPLE_TEXTURE2D_GRAD(aoT, aoS, uv + offB, dx, dy).g;
            float roughA = SAMPLE_TEXTURE2D_GRAD(roughT, roughS, uv + offA, dx, dy).g;
            float roughB = SAMPLE_TEXTURE2D_GRAD(roughT, roughS, uv + offB, dx, dy).g;

            LayerData data;
            data.albedo = lerp(baseA.rgb, baseB.rgb, w) * tint;
            data.tn = normalize(lerp(nA, nB, w));
            data.ao = lerp(1.0, lerp(aoA, aoB, w), aoStrength);
            data.smoothness = (1.0 - lerp(roughA, roughB, w)) * smoothnessScale;
            return data;
        }
        ENDHLSL

        // forward lit
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma shader_feature_local _TWO_LAYERS
            #pragma shader_feature_local _PARALLAX

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _FORWARD_PLUS _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _DBUFFER_MRT1 _DBUFFER_MRT2 _DBUFFER_MRT3
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DYNAMICLIGHTMAP_ON
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DBuffer.hlsl"

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

            half4 Frag(Varyings input) : SV_Target
            {
                float3 posWS = input.positionWS;
                float3 N = normalize(input.normalWS);
                float3 an = abs(N);
                float3 s = float3(N.x >= 0 ? 1 : -1, N.y >= 0 ? 1 : -1, N.z >= 0 ? 1 : -1);

                // Project along the dominant axis of the face. Flipping U on back faces keeps the texture (and the
                // normal map's X) from mirroring. Computed without branches so the screen derivatives stay valid.
                float2 uvX = float2(posWS.z * s.x, posWS.y);
                float2 uvY = float2(posWS.x * s.y, posWS.z);
                float2 uvZ = float2(posWS.x * -s.z, posWS.y);
                bool useY = an.y >= an.x && an.y >= an.z;
                bool useX = !useY && an.x >= an.z;
                float2 uvM = useY ? uvY : (useX ? uvX : uvZ);
                float xFlip = useY ? s.y : (useX ? s.x : -s.z);
                float2 dxM = ddx(uvM);
                float2 dyM = ddy(uvM);

                // View direction in the projection's own axes (U axis, V axis, face normal), for the height maps.
                float3 V = GetWorldSpaceNormalizeViewDir(posWS);
                float3 uAxis = useY ? float3(s.y, 0, 0) : (useX ? float3(0, 0, s.x) : float3(-s.z, 0, 0));
                float3 vAxis = useY ? float3(0, 0, 1) : float3(0, 1, 0);
                float3 viewTS = float3(dot(V, uAxis), dot(V, vAxis), dot(V, N));
                float2 parallaxDir = viewTS.xy / max(abs(viewTS.z), 0.3);

                // Which layer? Lower layer below the (ragged) height, upper layer above it.
                float mask = 0.0;
            #if defined(_TWO_LAYERS)
                float edge = _BlendHeight + (ValueNoise(uvM * _EdgeNoiseScale) - 0.5) * 2.0 * _EdgeNoiseAmount;
                mask = smoothstep(edge - _BlendSoftness, edge + _BlendSoftness, posWS.y);
            #endif

                LayerData layer = SampleLayer(
                    TEXTURE2D_ARGS(_BaseMapA, sampler_BaseMapA), TEXTURE2D_ARGS(_NormalMapA, sampler_NormalMapA),
                    TEXTURE2D_ARGS(_OcclusionMapA, sampler_OcclusionMapA),
                    TEXTURE2D_ARGS(_RoughMapA, sampler_RoughMapA), TEXTURE2D_ARGS(_HeightMapA, sampler_HeightMapA),
                    uvM, dxM, dyM, parallaxDir, _TileMetersA, _TintA.rgb, _NormalStrengthA, _OcclusionStrengthA,
                    _SmoothnessA, _HeightScaleA);
                float smoothness = layer.smoothness;

            #if defined(_TWO_LAYERS)
                if (mask > 0.001)
                {
                    LayerData upper = SampleLayer(
                        TEXTURE2D_ARGS(_BaseMapB, sampler_BaseMapB), TEXTURE2D_ARGS(_NormalMapB, sampler_NormalMapB),
                        TEXTURE2D_ARGS(_OcclusionMapB, sampler_OcclusionMapB),
                        TEXTURE2D_ARGS(_RoughMapB, sampler_RoughMapB), TEXTURE2D_ARGS(_HeightMapB, sampler_HeightMapB),
                        uvM, dxM, dyM, parallaxDir, _TileMetersB, _TintB.rgb, _NormalStrengthB, _OcclusionStrengthB,
                        _SmoothnessB, _HeightScaleB);

                    layer.albedo = lerp(layer.albedo, upper.albedo, mask);
                    layer.tn = lerp(layer.tn, upper.tn, mask);
                    layer.ao = lerp(layer.ao, upper.ao, mask);
                    smoothness = lerp(smoothness, upper.smoothness, mask);
                }
            #endif

                // Tangent space normal to world space, with the face's own normal as the base (UDN blend).
                float3 tn = layer.tn;
                tn.x *= xFlip;
                float3 worldN;
                if (useX) worldN = float3(N.x, tn.y + N.y, tn.x + N.z);
                else if (useY) worldN = float3(tn.x + N.x, N.y, tn.y + N.z);
                else worldN = float3(tn.x + N.x, tn.y + N.y, N.z);
                worldN = normalize(worldN);

                // Large scale brightness variation and grime near the floor.
                float macro = ValueNoise(uvM * _MacroScale) * 0.65 + ValueNoise(uvM * _MacroScale * 2.7) * 0.35;
                layer.albedo *= lerp(1.0 - _MacroStrength, 1.0 + _MacroStrength * 0.5, macro);

                float grime = 1.0 - saturate(posWS.y / _GrimeHeight);
                grime *= lerp(0.4, 1.0, ValueNoise(uvM * 1.7));
                layer.albedo *= lerp(float3(1, 1, 1), _GrimeColor.rgb, grime * _GrimeStrength);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = layer.albedo;
                surface.metallic = 0.0;
                surface.specular = half3(0, 0, 0);
                surface.smoothness = smoothness;
                surface.occlusion = layer.ao;
                surface.alpha = 1.0;
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

            #if defined(_DBUFFER)
                // DBuffer decals are drawn into a buffer during the opaque pass; the receiving shader has to apply it.
                ApplyDecalToSurfaceData(input.positionCS, surface, inputData);
            #endif

                half4 color = UniversalFragmentPBR(inputData, surface);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                color.a = 1.0;
                return color;
            }
            ENDHLSL
        }

        // shadows
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

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

            float4 ShadowVert(ShadowAttributes input) : SV_POSITION
            {
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
                return positionCS;
            }

            half4 ShadowFrag() : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        // depth
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthVert
            #pragma fragment DepthFrag

            struct DepthAttributes { float4 positionOS : POSITION; };

            float4 DepthVert(DepthAttributes input) : SV_POSITION
            {
                return TransformObjectToHClip(input.positionOS.xyz);
            }

            half DepthFrag() : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DNVert
            #pragma fragment DNFrag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            struct DNAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct DNVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
            };

            DNVaryings DNVert(DNAttributes input)
            {
                DNVaryings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 DNFrag(DNVaryings input) : SV_Target
            {
                // The face normal only (the normal map's effect is too fine for SSAO / decals to matter).
                float3 normalWS = NormalizeNormalPerPixel(input.normalWS);
            #if defined(_GBUFFER_NORMALS_OCT)
                float2 octNormalWS = PackNormalOctQuadEncode(normalWS);
                float2 remapped = saturate(octNormalWS * 0.5 + 0.5);
                return half4(PackFloat2To888(remapped), 0.0);
            #else
                return half4(normalWS, 0.0);
            #endif
            }
            ENDHLSL
        }
    }
}
