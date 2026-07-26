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
        private const float DorsalShade = 0.66f;

        /// <summary>Extra darkening inside a band. Bands fade out toward the belly.</summary>
        private const float BandShade = 0.48f;

        /// <summary>Bands along the body, counted across the whole animal rather than per unit.</summary>
        private const float BandCount = 6f;

        /// <summary>Strongest the markings ever get. Below 1 so the base hide always shows.</summary>
        private const float MarkingCoverage = 0.8f;

        /// <summary>Throat and lower jaw. An absolute colour now, not a multiplier.</summary>
        private static readonly Color ThroatColor = new(0.98f, 0.86f, 0.45f);

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
        /// <param name="shape">
        /// Proportion changes to apply before baking, or null to leave the model as imported. This is
        /// how one base model becomes a different animal: uniform scaling only reads as "the same
        /// dinosaur, nearer the camera", whereas changing which parts are big changes the species.
        /// </param>
        internal static void Apply(GameObject visual, string speciesKey, Color? tint = null, BodyShape shape = null,
            float tintStrength = 0.6f, Color? accentColor = null)
        {
            if (visual == null || string.IsNullOrEmpty(speciesKey)) return;

            // Markings default to a warm contrast when a species does not name one.
            Color accent = accentColor ?? new Color(0.95f, 0.62f, 0.15f);

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
                var skinRegions = RegionColors(skinned.sharedMaterials, tint, tintStrength);
                skinned.sharedMesh = EnsureBakedMesh(skinned.sharedMesh, speciesKey, shape, skinned.bones, skinRegions, accent);
                skinned.sharedMaterials = EnsureSkinMaterials(skinned.sharedMaterials, speciesKey, shader);
            }

            // Not every creature in the roster is necessarily skinned — a static prop rigged as a
            // creature would come through as a plain MeshRenderer and deserves the same treatment.
            foreach (var filter in visual.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!filter.TryGetComponent<MeshRenderer>(out var renderer)) continue;

                // No shape pass: reshaping is driven by bone weights, and an unskinned mesh has none.
                var meshRegions = RegionColors(renderer.sharedMaterials, tint, tintStrength);
                filter.sharedMesh = EnsureBakedMesh(filter.sharedMesh, speciesKey, null, null, meshRegions, accent);
                renderer.sharedMaterials = EnsureSkinMaterials(renderer.sharedMaterials, speciesKey, shader);
            }
        }

        /// <summary>
        /// A copy of <paramref name="source"/> with the pattern written into its colour stream.
        ///
        /// A copy because the original lives inside the .fbx and cannot be written to. The copies are
        /// cached as assets, so this is a one-off cost per species rather than per prefab rebuild.
        /// </summary>
        private static Mesh EnsureBakedMesh(Mesh source, string speciesKey, BodyShape shape, Transform[] bones,
            Color[] regionColors, Color accent)
        {
            if (source == null) return null;

            string path = $"{MeshFolder}/{speciesKey}_{Sanitize(source.name)}_skin.asset";

            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null) return existing;

            var baked = Object.Instantiate(source);
            baked.name = $"{speciesKey}_{source.name}_skin";

            // Reshape before colouring: the vertex colours are baked from vertex positions, so the
            // shading has to be computed against the silhouette the creature will actually have.
            if (shape != null && bones != null)
            {
                foreach (var part in shape.Parts) ScaleBoneGroup(baked, bones, part.Bones, part.Scale);
            }

            baked.colors = BakeVertexColors(baked, speciesKey, regionColors, accent);

            AssetDatabase.CreateAsset(baked, path);
            return baked;
        }

        /// <summary>
        /// Reshape one part of the body, blending out wherever the rig blends.
        ///
        /// Each vertex is pushed away from its own driving bone's bind-pose position, in proportion
        /// to how much that bone controls it. Using the skin weight as the falloff is what makes the
        /// result look deliberate rather than like a bulge: a vertex fully weighted to the skull
        /// scales fully, one shared between skull and neck scales partly, and the body does not move
        /// at all. The seam takes care of itself, because smooth falloff is exactly what the weights
        /// already encode.
        ///
        /// Each vertex resolves its own pivot from its dominant bone in the group, which is what
        /// makes a left and a right arm both work from one call — a single shared pivot would swing
        /// one of them across the body.
        ///
        /// Only positions change, so bone weights, bind poses and the triangle list stay valid and
        /// the mesh animates exactly as it did before.
        /// </summary>
        private static void ScaleBoneGroup(Mesh mesh, Transform[] bones, string[] nameContains, Vector3 scale)
        {
            var group = new System.Collections.Generic.Dictionary<int, Vector3>();

            for (int i = 0; i < bones.Length && i < mesh.bindposes.Length; i++)
            {
                if (bones[i] == null) continue;

                foreach (string fragment in nameContains)
                {
                    if (bones[i].name.IndexOf(fragment, System.StringComparison.OrdinalIgnoreCase) < 0) continue;

                    // The bind pose is the mesh-to-bone matrix, so its inverse puts the bone's origin
                    // back into mesh space — the point this part should grow around.
                    group[i] = mesh.bindposes[i].inverse.MultiplyPoint3x4(Vector3.zero);
                    break;
                }
            }

            if (group.Count == 0) return;

            var weights = mesh.boneWeights;
            var vertices = mesh.vertices;
            if (weights.Length != vertices.Length) return;

            for (int i = 0; i < vertices.Length; i++)
            {
                if (!TryResolveGroup(weights[i], group, out float influence, out Vector3 pivot)) continue;

                Vector3 offset = vertices[i] - pivot;

                vertices[i] = pivot + new Vector3(
                    offset.x * (1f + (scale.x - 1f) * influence),
                    offset.y * (1f + (scale.y - 1f) * influence),
                    offset.z * (1f + (scale.z - 1f) * influence));
            }

            mesh.vertices = vertices;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        /// <summary>
        /// Total influence this vertex takes from the group, and the pivot of whichever member drives
        /// it hardest.
        /// </summary>
        private static bool TryResolveGroup(
            BoneWeight weight,
            System.Collections.Generic.Dictionary<int, Vector3> group,
            out float influence,
            out Vector3 pivot)
        {
            // Locals rather than writing straight to the out parameters: C# will not let a local
            // function capture them, and four near-identical inline blocks would be worse.
            float total = 0f;
            float strongest = 0f;
            Vector3 best = Vector3.zero;

            var indices = new[] { weight.boneIndex0, weight.boneIndex1, weight.boneIndex2, weight.boneIndex3 };
            var amounts = new[] { weight.weight0, weight.weight1, weight.weight2, weight.weight3 };

            for (int i = 0; i < indices.Length; i++)
            {
                if (!group.TryGetValue(indices[i], out Vector3 bonePivot)) continue;

                total += amounts[i];

                if (amounts[i] <= strongest) continue;

                strongest = amounts[i];
                best = bonePivot;
            }

            influence = Mathf.Clamp01(total);
            pivot = best;

            return influence > 0.001f;
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
        private static Color[] BakeVertexColors(Mesh mesh, string speciesKey, Color[] regionColors, Color accent)
        {
            var vertices = mesh.vertices;
            var colors = new Color[vertices.Length];
            var bounds = mesh.bounds;
            var regionOf = MapVerticesToSubmeshes(mesh);

            // Per-species offset so two species do not end up wearing identical markings.
            float phase = (Mathf.Abs(speciesKey.GetHashCode()) % 1000) * 0.01f;

            // Normalised mesh space. These meshes are authored tiny and scaled up on the prefab, so
            // anything measured in world units produced less than one band across a whole animal.
            Vector3 span = new(
                Mathf.Max(0.0001f, bounds.size.x),
                Mathf.Max(0.0001f, bounds.size.y),
                Mathf.Max(0.0001f, bounds.size.z));

            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 v = vertices[i];

                Vector3 n = new(
                    (v.x - bounds.min.x) / span.x,
                    (v.y - bounds.min.y) / span.y,
                    (v.z - bounds.min.z) / span.z);

                Color region = regionColors != null && regionOf[i] < regionColors.Length
                    ? regionColors[regionOf[i]]
                    : Color.grey;

                float dorsal = Mathf.SmoothStep(0f, 1f, n.y);

                // Markings are a LERP toward a different colour, not a multiply. That is the whole
                // difference between a patterned animal and a plain one with shadows on it: a stripe
                // has to be able to be brighter and a different hue than what it crosses, which a
                // multiply can never produce.
                float wave = Mathf.Sin(n.z * BandCount * Mathf.PI * 2f + phase) * 0.5f + 0.5f;
                // Narrow bands. The window has to be tight or the "stripe" covers most of the body:
                // at 0.42-0.9 combined with blotching the accent summed past 1 over large areas and
                // saturated, turning every creature a solid accent colour — the exact single-colour
                // look this is meant to break.
                float band = SmoothThreshold(0.62f, 0.86f, wave) * Mathf.Lerp(0.3f, 1f, dorsal);

                float blotch = SmoothThreshold(0.62f, 0.88f, Blotch(n, phase));

                // Bounded, and a maximum rather than a sum.
                //
                // Adding the two let them saturate: on some species the blotch field sat high across
                // the whole body, the total passed 1 everywhere, and the creature came out solid
                // accent — the single-colour look this exists to prevent, just in a different colour.
                // A capped maximum means the markings can never fully cover the hide, whatever the
                // noise happens to do on a given mesh.
                float marking = Mathf.Max(band, blotch * 0.7f) * MarkingCoverage;

                Color tone = Color.Lerp(region, accent, marking);

                // Counter-shading on top: pale belly, darker spine. Applied after the markings so it
                // shades the pattern rather than replacing it, which is how real hide reads.
                tone *= Mathf.Lerp(1.25f, DorsalShade, dorsal);

                // A brighter throat and jaw, as most reptiles and birds have.
                float throat = SmoothThreshold(0.78f, 1f, n.z) * (1f - dorsal);
                tone = Color.Lerp(tone, ThroatColor, throat * 0.7f);

                // Fine noise so large flat panels do not look moulded.
                tone *= 1f - Mottle * Hash01(v);

                colors[i] = new Color(Mathf.Clamp01(tone.r), Mathf.Clamp01(tone.g), Mathf.Clamp01(tone.b), 1f);
            }

            return colors;
        }

        /// <summary>
        /// A real threshold curve: 0 below <paramref name="edge0"/>, 1 above <paramref name="edge1"/>,
        /// smooth between.
        ///
        /// Not Mathf.SmoothStep, which despite the name is NOT the shader function of the same name.
        /// Unity's takes (from, to, t) and interpolates BETWEEN from and to — so SmoothStep(0.68,
        /// 0.95, x) returns something in 0.68..0.95 and never approaches zero. Used as a threshold it
        /// silently made every marking cover the entire body: measured mean 0.80 for a field whose
        /// raw value averaged 0.47, which is why creatures kept coming out one solid accent colour
        /// however the weights were tuned.
        /// </summary>
        private static float SmoothThreshold(float edge0, float edge1, float x)
        {
            float t = Mathf.Clamp01(Mathf.InverseLerp(edge0, edge1, x));
            return t * t * (3f - 2f * t);
        }

        /// <summary>
        /// Smooth low-frequency field over the body, 0 to 1, used for irregular patches.
        ///
        /// Summed sines rather than real value noise: it needs to be smooth, deterministic and cheap,
        /// and at this scale — a handful of patches over one animal — nobody can tell the difference.
        /// The axes use different frequencies so the result does not fall into visible stripes.
        /// </summary>
        private static float Blotch(Vector3 n, float phase)
        {
            float a = Mathf.Sin(n.z * 7.3f + phase) * Mathf.Sin(n.y * 5.1f - phase * 0.6f);
            float b = Mathf.Sin(n.x * 9.7f - phase * 1.3f) * Mathf.Sin(n.z * 3.9f + phase * 0.4f);

            return Mathf.Clamp01(Mathf.InverseLerp(-1.3f, 1.3f, a + b * 0.7f));
        }

        /// <summary>Deterministic 0..1 noise from a position. Same vertex, same value, every rebuild.</summary>
        private static float Hash01(Vector3 v)
        {
            float h = Mathf.Sin(v.x * 127.1f + v.y * 311.7f + v.z * 74.7f) * 43758.5453f;
            return h - Mathf.Floor(h);
        }

        /// <summary>
        /// One white material per region, so the vertex stream decides the colour outright.
        ///
        /// This is the change that made real patterning possible. The shader multiplies material
        /// colour by vertex colour, and vertex colours are clamped to 0..1 — so while the material
        /// carried the hue, the vertex stream could only ever darken it. Bands and blotches came out
        /// as shadows on one flat colour, which is exactly the "single colour" problem. With the
        /// material at white the multiply is the identity and the baked colours pass through
        /// untouched, so a green flank and an orange stripe can sit on the same mesh.
        ///
        /// The materials still exist per region because the renderer needs one per submesh, and
        /// keeping them distinct leaves room to give a region its own shader settings later.
        /// </summary>
        private static Material[] EnsureSkinMaterials(Material[] sources, string speciesKey, Shader shader)
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
                material.color = Color.white;
                EditorUtility.SetDirty(material);

                result[i] = material;
            }

            return result;
        }

        /// <summary>
        /// The colour each submesh's vertices start from, before shading and markings.
        ///
        /// Read off the imported materials so the pack's own region split — dark claws, a paler jaw,
        /// a coloured crest — survives, then lifted for legibility and pulled toward the species tint.
        /// </summary>
        private static Color[] RegionColors(Material[] sources, Color? tint, float tintStrength)
        {
            var colors = new Color[sources.Length];

            for (int i = 0; i < sources.Length; i++)
            {
                Color region = sources[i] != null ? Readable(sources[i].color) : Color.grey;
                if (tint.HasValue) region = Color.Lerp(region, tint.Value, tintStrength);

                colors[i] = region;
            }

            return colors;
        }

        /// <summary>
        /// Which submesh drives each vertex, so a baked colour can respect the model's own regions.
        /// Vertices shared between submeshes take whichever is resolved last; on these models that is
        /// a handful of seam vertices and invisible in the result.
        /// </summary>
        private static int[] MapVerticesToSubmeshes(Mesh mesh)
        {
            var map = new int[mesh.vertexCount];

            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
            {
                var indices = mesh.GetTriangles(submesh);
                foreach (int index in indices)
                {
                    if (index >= 0 && index < map.Length) map[index] = submesh;
                }
            }

            return map;
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
            v = Mathf.Max(v, 0.26f);

            // Hard saturation push. The pack's colours are not just dark, they are nearly grey, and
            // a lifted grey is still grey — against a dim green jungle the creatures read as mud
            // unless the hue is forced to actually assert itself.
            s = Mathf.Clamp01(s * 1.9f + 0.12f);

            return Color.HSVToRGB(h, s, v);
        }

        private static string Sanitize(string value) => value.Replace(" ", "_").Replace("/", "_");
    }
}
