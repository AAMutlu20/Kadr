// The Kadr look: one full-screen pass for the whole game.
//  - Fog from depth (exponential), thicker near the floor, with slow drifting noise.
//  - Lit fog: the fog is marched along each view ray and lit by up to 8 nearby lights, so lamps glow in the air and
//    the flashlight shows as a beam. There is no shadowing: a lamp behind a wall still glows in the air near it.
//  - Lights punch through the fog (bright pixels are fogged less) and get a soft halo that is stronger in thick fog.
//  - A vignette.
// All values come from the KadrLook script as global shader values (_Kadr...), so no material asset is ever changed
// at run time. With no KadrLook in the scene the pass does nothing (the main menu is untouched).
//
// Setup: URP Renderer > Add Renderer Feature > Kadr Look Feature, Material = a material that uses this shader.
Shader "Hidden/Kadr/Look"
{
    Properties { }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off Blend Off

        Pass
        {
            Name "KadrLook"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float4 _KadrFogColor;
            float4 _KadrWind;            // xyz = drift direction and speed
            float  _KadrDensity;
            float  _KadrNoiseAmount;
            float  _KadrNoiseScale;
            float  _KadrHeightInfluence; // 0 = even fog, 1 = all fog hugs the floor
            float  _KadrFogBase;         // the floor height
            float  _KadrHeightFalloff;
            float  _KadrPunchStart;
            float  _KadrPunchStrength;
            float  _KadrGlowThreshold;
            float  _KadrGlowStrength;
            float  _KadrGlowRadius;      // in screen heights
            float  _KadrVigAmount;
            float  _KadrVigInner;
            float  _KadrVigOuter;
            float  _KadrDebug;           // 0 off, 1 distance, 2 fog amount, 3 raw depth

            #define KADR_LIGHTS 8
            #define KADR_STEPS 16
            float  _KadrLightCount;
            float4 _KadrLightPos[KADR_LIGHTS];   // xyz = position, w = range
            float4 _KadrLightCol[KADR_LIGHTS];   // rgb = colour * intensity, a = 1 / (cosInner - cosOuter) for spots
            float4 _KadrLightDir[KADR_LIGHTS];   // xyz = spot direction, w = cosOuter (-1 = point light)
            float  _KadrScatter;                 // strength of the lit fog
            float  _KadrScatterMax;              // longest ray that is marched, in metres
            float  _KadrAniso;                   // forward scattering: beams are brighter when you look into the light

            float Hash13(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.zyx + 31.32);
                return frac((p.x + p.y) * p.z);
            }

            float Noise3(float3 x)
            {
                float3 i = floor(x);
                float3 f = frac(x);
                f = f * f * (3.0 - 2.0 * f);

                return lerp(
                    lerp(lerp(Hash13(i),                  Hash13(i + float3(1, 0, 0)), f.x),
                         lerp(Hash13(i + float3(0, 1, 0)), Hash13(i + float3(1, 1, 0)), f.x), f.y),
                    lerp(lerp(Hash13(i + float3(0, 0, 1)), Hash13(i + float3(1, 0, 1)), f.x),
                         lerp(Hash13(i + float3(0, 1, 1)), Hash13(i + float3(1, 1, 1)), f.x), f.y), f.z);
            }

            // Light scattered towards the camera by the fog along one view ray.
            float3 Scatter(float3 origin, float3 dir, float len, float jitter)
            {
                float3 sum = 0;
                float transmittance = 1.0;
                float stepLen = len / KADR_STEPS;
                float g = _KadrAniso;
                int count = (int)_KadrLightCount;

                for (int s = 0; s < KADR_STEPS; s++)
                {
                    float3 p = origin + dir * ((s + jitter) * stepLen);

                    float d = _KadrDensity * lerp(1.0, exp(-max(0.0, p.y - _KadrFogBase) * _KadrHeightFalloff), _KadrHeightInfluence);

                    float3 lit = 0;
                    for (int i = 0; i < count; i++)
                    {
                        float3 toL = _KadrLightPos[i].xyz - p;
                        float dl = length(toL);
                        float att = saturate(1.0 - dl / max(_KadrLightPos[i].w, 0.01));
                        att *= att;

                        float3 L = toL / max(dl, 0.0001);
                        float cone = 1.0;
                        if (_KadrLightDir[i].w > -0.5)
                        {
                            cone = saturate((dot(-L, _KadrLightDir[i].xyz) - _KadrLightDir[i].w) * _KadrLightCol[i].a);
                            cone *= cone;
                        }

                        float cosv = dot(dir, L);
                        float phase = (1.0 - g * g) / pow(max(1.0 + g * g - 2.0 * g * cosv, 0.0001), 1.5);

                        lit += _KadrLightCol[i].rgb * (att * cone * phase);
                    }

                    sum += transmittance * d * stepLen * lit;
                    transmittance *= exp(-d * stepLen);
                }

                return sum * _KadrScatter;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;
                float3 scene = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;

                // Nothing set by the script (menu, other scenes): leave the picture alone.
                if (_KadrDensity <= 0.0 && _KadrVigAmount <= 0.0 && _KadrGlowStrength <= 0.0)
                    return half4(scene, 1);

                // Per-pixel noise that breaks up banding and the halo's sampling pattern.
                float ign = frac(52.9829189 * frac(dot(input.positionCS.xy, float2(0.06711056, 0.00583715))));

                float3 colour = scene;

                // ---------- fog ----------
                float raw = SampleSceneDepth(uv);
                #if UNITY_REVERSED_Z
                    bool isFar = raw <= 0.000001;
                #else
                    bool isFar = raw >= 0.999999;
                #endif

                float3 nearWorld = ComputeWorldSpacePosition(uv, UNITY_NEAR_CLIP_VALUE, UNITY_MATRIX_I_VP);
                float3 rayDir = normalize(nearWorld - _WorldSpaceCameraPos);

                float fog = 1.0;
                float dist = 0.0;
                if (!isFar)
                {
                    float3 world = ComputeWorldSpacePosition(uv, raw, UNITY_MATRIX_I_VP);
                    dist = length(world - _WorldSpaceCameraPos);

                    float t = _Time.y;
                    float n = Noise3(world * _KadrNoiseScale + _KadrWind.xyz * t) * 0.65
                            + Noise3(world * _KadrNoiseScale * 2.3 - _KadrWind.xyz * t * 1.4) * 0.35;

                    float density = _KadrDensity * (1.0 + (n * 2.0 - 1.0) * _KadrNoiseAmount);
                    float low = exp(-max(0.0, world.y - _KadrFogBase) * _KadrHeightFalloff);
                    density *= lerp(1.0, low, _KadrHeightInfluence);

                    float optical = density * dist;
                    fog = 1.0 - exp(-optical); // plain exponential: haze builds up from the first metre, like the reference
                }

                // Debug views (set in the KadrLook script): what the shader sees.
                //  1 = distance (a repeating grey ramp every 10 m; black = far plane / no depth)
                //  2 = fog amount (white = fully fogged)
                //  3 = the raw depth value
                if (_KadrDebug > 0.5)
                {
                    if (_KadrDebug < 1.5) return half4(frac(dist / 10.0).xxx * (isFar ? 0.0 : 1.0), 1);
                    if (_KadrDebug < 2.5) return half4(fog.xxx, 1);
                    return half4(pow(saturate(raw), 0.25).xxx, 1);
                }

                // Bright pixels (lamps, lit surfaces) shine through the fog instead of dissolving into it.
                float luma = dot(scene, float3(0.2126, 0.7152, 0.0722));
                float punch = saturate((luma - _KadrPunchStart) * _KadrPunchStrength);
                fog *= 1.0 - punch;

                // ---------- halo: light that the fog scatters around bright spots ----------
                float3 glow = 0;
                float angle = ign * 6.2831853;
                float2 aspect = float2(_ScreenParams.y / _ScreenParams.x, 1.0);

                [unroll]
                for (int k = 0; k < 10; k++)
                {
                    float r = sqrt((k + 0.5) / 10.0) * _KadrGlowRadius;
                    float a = angle + k * 2.3999632;
                    float2 offset = float2(cos(a), sin(a)) * r * aspect;
                    float3 tap = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + offset).rgb;
                    glow += max(0.0, tap - _KadrGlowThreshold);
                }
                glow /= 10.0;

                colour = lerp(colour, _KadrFogColor.rgb, fog);
                colour += glow * _KadrGlowStrength * (0.15 + fog);

                // ---------- lit fog: lamps glow in the air, the flashlight shows as a beam ----------
                if (_KadrLightCount > 0.5 && _KadrScatter > 0.0 && _KadrDensity > 0.0)
                {
                    float marchLen = isFar ? _KadrScatterMax : min(dist, _KadrScatterMax);
                    colour += Scatter(_WorldSpaceCameraPos, rayDir, marchLen, ign);
                }

                // ---------- vignette ----------
                float2 p = (uv - 0.5) * float2(_ScreenParams.x / _ScreenParams.y, 1.0);
                float v = smoothstep(_KadrVigInner, _KadrVigOuter, length(p));
                colour *= 1.0 - v * _KadrVigAmount;

                return half4(colour, 1);
            }
            ENDHLSL
        }
    }
}