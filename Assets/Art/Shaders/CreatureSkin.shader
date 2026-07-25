// Creature skin: the material's region colour, modulated by baked per-vertex shading.
//
// The Quaternius models carry no UVs at all, so there is nowhere to put a texture. What they do
// have is a material slot per body region and a few thousand vertices — enough to hold a pattern
// directly in the vertex colour stream. CreatureSkinBuilder bakes counter-shading and dorsal
// banding into that stream; this shader is only what reads it back.
//
// Lambert rather than Standard: there is nothing metallic or glossy on a dinosaur, and the
// per-pixel BRDF is the single most expensive thing we could put on ten skinned meshes on a phone.
Shader "DinoBattle/CreatureSkin"
{
    Properties
    {
        // Per-creature tint comes through here as a MaterialPropertyBlock, which is how two
        // individuals of the same species end up different colours without duplicating materials.
        _Color ("Base Color", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Lambert vertex:vert
        #pragma target 3.0

        fixed4 _Color;

        struct Input
        {
            fixed3 skin;
        };

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            o.skin = v.color.rgb;
        }

        void surf(Input IN, inout SurfaceOutput o)
        {
            // The baked stream only ever darkens (it is 0..1 and the belly bakes to white), so the
            // material colour stays the brightest the region can be and the pattern shades down
            // from it. That keeps the palette the artist chose recognisable.
            o.Albedo = _Color.rgb * IN.skin;
            o.Alpha = 1;
        }
        ENDCG
    }

    FallBack "Diffuse"
}
