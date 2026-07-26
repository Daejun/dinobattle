// Flat-shaded scenery: one colour, one light, nothing else.
//
// The environment was on Unity's Standard shader, which is a full physically-based BRDF —
// metallic, smoothness, specular, the lot. None of that is doing anything here. Every prop in
// this arena is a single quantised palette colour from SharedEnvironmentMaterial, with no
// texture and no UVs, and the first thing that code does after creating a Standard material is
// set smoothness to 0.05 to kill the plastic sheen it did not want in the first place. Paying
// for a per-pixel BRDF and then turning it off is the worst of both.
//
// Lambert, for exactly the reason CreatureSkin.shader gives for the creatures: nothing in this
// scene is metallic or glossy, and per-pixel lighting maths is the most expensive thing that can
// be put on a phone's fragment shader. The arena is ~210 props; this is the shader they all run.
//
// No _MainTex. The Mobile/Diffuse shader would have been the obvious stock choice but it has no
// colour property at all — every prop would need a 1x1 texture just to be tinted, which is more
// asset plumbing than writing this.
//
// Shadow casting is turned off on scenery by BattleSceneBuilder, but the fallback still supplies
// a caster pass: the flag is a scene decision and the shader should not be the thing that makes
// it impossible to change back.
Shader "DinoBattle/EnvironmentFlat"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 150

        CGPROGRAM
        // Lambert, not Standard. SurfaceOutput rather than SurfaceOutputStandard to match.
        #pragma surface surf Lambert
        #pragma target 3.0

        fixed4 _Color;

        // A surface shader needs at least one Input member. worldPos is always available and costs
        // nothing here — there is no texture to sample and therefore no uv to declare.
        struct Input
        {
            float3 worldPos;
        };

        void surf(Input IN, inout SurfaceOutput o)
        {
            o.Albedo = _Color.rgb;
        }
        ENDCG
    }

    FallBack "Diffuse"
}
