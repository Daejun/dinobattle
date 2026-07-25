using UnityEditor;
using UnityEngine;

namespace DinoBattle.EditorTools
{
    /// <summary>
    /// Turns the imported models into creatures you can actually tell apart.
    ///
    /// Two separate problems, both visible the moment you look at a screenshot of a fight:
    ///
    /// 1. The pack's materials are extremely dark. The T-Rex's main body colour is (0.06, 0.07,
    ///    0.06) — near black. Under the arena's lighting every species rendered as the same dark
    ///    grey silhouette, so a Triceratops and a raptor were distinguishable only by outline.
    ///
    /// 2. Each material is one flat untextured colour, and the meshes carry NO UVs, so there is
    ///    nowhere to put a texture to break that up.
    ///
    /// The fix for (1) is a value lift that brightens the darks hard while preserving the ordering
    /// the artist chose. The fix for (2) is the vertex colour stream: a few thousand vertices is
    /// plenty to hold counter-shading and dorsal banding on a low-poly model, it needs no UVs, and
    /// it costs nothing at runtime beyond a multiply in the shader.
    /// </summary>
    public static class CreatureSkinBuilder
    {
        private const string MeshFolder = "Assets/Art/Models/Generated";
        private const string MaterialFolder = "Assets/Art/Materials/Skin";

        /// <summary>Darkest the top of the back gets, as a multiplier on the region colour.</summary>
        private const float DorsalShade = 0.52f;

        /// <summary>Extra darkening inside a band. Bands fade out toward the belly.</summary>
        private const float BandShade = 0.72f;

        /// <summary>Bands per world unit along the body's long axis.</summary>
        private const float BandFrequency = 2.3f;

        /// <summary>Amplitude of the fine per-vertex mottling that stops flat areas reading as plastic.</summary>
        private const float Mottle = 0.10f;

        /// <summary>
        /// Point <paramref name="visual"/>'s renderers at baked meshes and skin materials, creating
        /// them on first use. Called from the prefab generator so re-running the menu cannot lose it.
        /// </summary>
        /// <param name="tint">
        /// Set for a deliberate reskin (Bio T-Rex shares the T-Rex model). The region colours are
        /// blended toward it rather than replaced by it — painting every slot the same colour is
        /// exactly the flattening this class exists to undo.
        /// </param>
        public static void Apply(GameObject visual, string speciesKey, Color? tint = null)
        {
            if (visual == null || string.IsNullOrEmpty(speciesKey)) return;

            var shader = Shader.Find("DinoBattle/CreatureSkin");
            if (shader == null)
            {
                Debug.LogWarning("[CreatureSkinBuilder] DinoBattle/CreatureSkin shader not found; " +
                                 "leaving the imported materials alone.");
                return;
            }

            SampleContentBuilder.EnsureFolder("Assets/Art/Models");
            SampleContentBuilder.EnsureFolder(MeshFolder);
            SampleContentBuilder.EnsureFolder("Assets/Art/Materials");
            SampleContentBuilder.EnsureFolder(MaterialFolder);

            foreach (var skinned in visual.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                skinned.sharedMesh = EnsureBakedMesh(skinned.sharedMesh, speciesKey);
                skinned.sharedMaterials = EnsureSkinMaterials(skinned.sharedMaterials, speciesKey, shader, tint);
            }

            // Not every creature in the roster is necessarily skinned — a static prop rigged as a
            // creature would come through as a plain MeshRenderer and deserves the same treatment.
            foreach (var filter in visual.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!filter.TryGetComponent<MeshRenderer>(out var renderer)) continue;

                filter.sharedMesh = EnsureBakedMesh(filter.sharedMesh, speciesKey);
                renderer.sharedMaterials = EnsureSkinMaterials(renderer.sharedMaterials, speciesKey, shader, tint);
            }
        }

        /// <summary>
        /// A copy of <paramref name="source"/> with the pattern written into its colour stream.
        ///
        /// A copy because the original lives inside the .fbx and cannot be written to. The copies are
        /// cached as assets, so this is a one-off cost per species rather than per prefab rebuild.
        /// </summary>
        private static Mesh EnsureBakedMesh(Mesh source, string speciesKey)
        {
            if (source == null) return null;

            string path = $"{MeshFolder}/{speciesKey}_{Sanitize(source.name)}_skin.asset";

            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null) return existing;

            var baked = Object.Instantiate(source);
            baked.name = $"{speciesKey}_{source.name}_skin";
            baked.colors = BakeVertexColors(baked, speciesKey);

            AssetDatabase.CreateAsset(baked, path);
            return baked;
        }

        /// <summary>
        /// Counter-shading plus dorsal banding, evaluated on bind-pose vertex positions.
        ///
        /// Bind pose specifically, NOT the animated position: baking against animated positions
        /// would be impossible here anyway, but it is worth being explicit that this is why the
        /// pattern is stored per vertex rather than computed in the shader from object space. A
        /// shader reading object-space Y on a skinned mesh would have the stripes crawl over the
        /// body as it walked.
        ///
        /// Counter-shading — dark above, pale below — is the single most common colour pattern in
        /// land animals, and it is what makes a shape read as a real creature rather than a toy.
        /// </summary>
        private static Color[] BakeVertexColors(Mesh mesh, string speciesKey)
        {
            var vertices = mesh.vertices;
            var colors = new Color[vertices.Length];
            var bounds = mesh.bounds;

            // Per-species offset so two species do not end up wearing identical stripes.
            float phase = (Mathf.Abs(speciesKey.GetHashCode()) % 1000) * 0.01f;

            float spanY = Mathf.Max(0.0001f, bounds.size.y);

            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 v = vertices[i];

                // 0 at the belly, 1 at the spine.
                float height = Mathf.Clamp01((v.y - bounds.min.y) / spanY);
                float dorsal = Mathf.SmoothStep(0f, 1f, height);

                // Warm and pale underneath, cool and dark on top. Tinting the channels differently
                // is what makes this read as two colours rather than one colour plus shadow.
                Color belly = new(1f, 0.97f, 0.88f);
                Color back = new(DorsalShade, DorsalShade * 1.06f, DorsalShade * 0.88f);
                Color tone = Color.Lerp(belly, back, dorsal);

                // Bands across the body, strongest along the spine and gone by the belly.
                float wave = Mathf.Sin(v.z * BandFrequency + phase) * 0.5f + 0.5f;
                float band = Mathf.SmoothStep(0.45f, 0.9f, wave) * dorsal;
                tone *= Mathf.Lerp(1f, BandShade, band);

                // Fine noise so large flat panels do not look moulded.
                tone *= 1f - Mottle * Hash01(v);

                colors[i] = new Color(Mathf.Clamp01(tone.r), Mathf.Clamp01(tone.g), Mathf.Clamp01(tone.b), 1f);
            }

            return colors;
        }

        /// <summary>Deterministic 0..1 noise from a position. Same vertex, same value, every rebuild.</summary>
        private static float Hash01(Vector3 v)
        {
            float h = Mathf.Sin(v.x * 127.1f + v.y * 311.7f + v.z * 74.7f) * 43758.5453f;
            return h - Mathf.Floor(h);
        }

        private static Material[] EnsureSkinMaterials(
            Material[] sources, string speciesKey, Shader shader, Color? tint)
        {
            var result = new Material[sources.Length];

            for (int i = 0; i < sources.Length; i++)
            {
                var source = sources[i];
                if (source == null) continue;

                string path = $"{MaterialFolder}/{speciesKey}_{Sanitize(source.name)}.mat";

                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    material = new Material(shader);
                    AssetDatabase.CreateAsset(material, path);
                }

                material.shader = shader;

                Color regionColor = Readable(source.color);
                if (tint.HasValue) regionColor = Color.Lerp(regionColor, tint.Value, 0.6f);
                material.color = regionColor;

                EditorUtility.SetDirty(material);

                result[i] = material;
            }

            return result;
        }

        /// <summary>
        /// Lift a near-black import colour into something that survives being lit.
        ///
        /// A square root on value: it brightens the darkest colours hardest while leaving the
        /// already-bright ones nearly alone, and it is monotonic, so the artist's relative ordering
        /// of the regions is preserved — the dark parts stay the dark parts. A flat multiply would
        /// either leave the blacks black or blow out the highlights.
        ///
        /// Saturation is pushed up as well. Once value is raised, the original low-saturation
        /// colours look like grey paint rather than hide.
        /// </summary>
        private static Color Readable(Color source)
        {
            Color.RGBToHSV(source, out float h, out float s, out float v);

            v = Mathf.Clamp01(Mathf.Lerp(v, Mathf.Sqrt(v), 0.85f));

            // Floor, so pure black regions (claws, eyes at 0.004) become visible dark detail rather
            // than holes in the silhouette.
            v = Mathf.Max(v, 0.16f);

            s = Mathf.Clamp01(s * 1.35f);

            return Color.HSVToRGB(h, s, v);
        }

        private static string Sanitize(string value) => value.Replace(" ", "_").Replace("/", "_");
    }
}
