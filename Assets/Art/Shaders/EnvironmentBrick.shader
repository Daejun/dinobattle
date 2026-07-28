// Textured scenery, tiled in WORLD space rather than by UV.
//
// The gauntlet board is built from stretched cubes — a platform is 26 x 1 x 22, a ramp is longer
// still — and a primitive cube's UVs run 0..1 across each face regardless of how far it has been
// stretched. Sampling by UV would therefore paint one enormous brick on the long slabs and normal
// ones on the short, which reads as a texturing bug rather than as masonry.
//
// So the UV is derived from world position instead, picked per face from the world normal. Bricks
// then come out the same physical size on every slab, courses line up where two slabs meet, and the
// builder can stretch geometry freely without thinking about texture scale.
//
// Lambert like the rest of the environment, for the reason CreatureSkin.shader gives: nothing here
// is metallic or glossy, and a per-pixel BRDF is the most expensive thing that can go on a phone's
// fragment shader.
Shader "DinoBattle/EnvironmentBrick"
{
    Properties
    {
        _Color ("Tint", Color) = (1,1,1,1)
        _MainTex ("Brick", 2D) = "white" {}

        // World units per texture repeat. Smaller = bigger bricks.
        _Tiling ("World Tiling", Float) = 0.25
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Lambert
        #pragma target 3.0

        sampler2D _MainTex;
        fixed4 _Color;
        half _Tiling;

        struct Input
        {
            float3 worldPos;
            float3 worldNormal;
        };

        void surf(Input IN, inout SurfaceOutput o)
        {
            // Pick the projection plane from the dominant axis of the face. Cheaper than blending
            // three samples, and these are axis-aligned boxes — there are no curved surfaces here
            // for a hard switch to show a seam on.
            float3 n = abs(IN.worldNormal);
            float2 uv;

            if (n.y > n.x && n.y > n.z)      uv = IN.worldPos.xz;   // floor and ceiling
            else if (n.x > n.z)              uv = IN.worldPos.zy;   // side walls
            else                             uv = IN.worldPos.xy;   // front and back

            o.Albedo = tex2D(_MainTex, uv * _Tiling).rgb * _Color.rgb;
        }
        ENDCG
    }

    FallBack "Diffuse"
}
