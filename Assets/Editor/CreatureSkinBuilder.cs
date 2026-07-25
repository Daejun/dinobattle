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
        private const float BandShade = 0.72f;

        /// <summary>Bands along the body, counted across the whole animal rather than per unit.</summary>
        private const float BandCount = 6f;

        /// <summary>Hue the irregular patches pull toward, and how strongly.</summary>
        private static readonly Color BlotchTint = new(1.15f, 0.86f, 0.62f);
        private const float BlotchStrength = 0.85f;

        /// <summary>Warm throat and lower jaw, as most reptiles and birds have.</summary>
        private static readonly Color ThroatTint = new(1.2f, 0.78f, 0.62f);

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
            float tintStrength = 0.6f)
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
                skinned.sharedMesh = EnsureBakedMesh(skinned.sharedMesh, speciesKey, shape, skinned.bones);
                skinned.sharedMaterials = EnsureSkinMaterials(skinned.sharedMaterials, speciesKey, shader, tint, tintStrength);
            }

            // Not every creature in the roster is necessarily skinned — a static prop rigged as a
            // creature would come through as a plain MeshRenderer and deserves the same treatment.
            foreach (var filter in visual.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!filter.TryGetComponent<MeshRenderer>(out var renderer)) continue;

                // No shape pass: reshaping is driven by bone weights, and an unskinned mesh has none.
                filter.sharedMesh = EnsureBakedMesh(filter.sharedMesh, speciesKey, null, null);
                renderer.sharedMaterials = EnsureSkinMaterials(renderer.sharedMaterials, speciesKey, shader, tint, tintStrength);
            }
        }

        /// <summary>
        /// A copy of <paramref name="source"/> with the pattern written into its colour stream.
        ///
        /// A copy because the original lives inside the .fbx and cannot be written to. The copies are
        /// cached as assets, so this is a one-off cost per species rather than per prefab rebuild.
        /// </summary>
        private static Mesh EnsureBakedMesh(Mesh source, string speciesKey, BodyShape shape, Transform[] bones)
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

            baked.colors = BakeVertexColors(baked, speciesKey);

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
        private static Color[] BakeVertexColors(Mesh mesh, string speciesKey)
        {
            var vertices = mesh.vertices;
            var colors = new Color[vertices.Length];
            var bounds = mesh.bounds;

            // Per-species offset so two species do not end up wearing identical stripes.
            float phase = (Mathf.Abs(speciesKey.GetHashCode()) % 1000) * 0.01f;

            // Everything below works in NORMALISED mesh space — 0 to 1 across the model's own bounds
            // — not in absolute units.
            //
            // This was the bug that made every creature look plain. The frequencies were per world
            // unit, but these meshes are authored tiny and scaled up on the prefab: a body is about
            // 0.15 units long, so "2.3 bands per unit" worked out at a third of one band across the
            // whole animal. The pattern code ran on every creature and produced nothing to see.
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

                float dorsal = Mathf.SmoothStep(0f, 1f, n.y);

                // Counter-shading: pale warm belly, dark cool back. The commonest colour scheme in
                // land animals and the one that most makes a shape read as alive.
                Color belly = new(1f, 0.95f, 0.84f);
                Color back = new(DorsalShade, DorsalShade * 1.06f, DorsalShade * 0.84f);
                Color tone = Color.Lerp(belly, back, dorsal);

                // Cross-body bands, strongest along the spine and gone by the belly. Now counted
                // across the body rather than per unit, so a creature actually gets BandCount of them.
                float wave = Mathf.Sin(n.z * BandCount * Mathf.PI * 2f + phase) * 0.5f + 0.5f;
                float band = Mathf.SmoothStep(0.4f, 0.92f, wave) * dorsal;
                tone *= Mathf.Lerp(1f, BandShade, band);

                // Irregular blotching, on top of the regular bands. Bands alone read as a painted
                // pattern; real hide has large soft patches that ignore the banding, and the two
                // together are what stops it looking printed on.
                float blotch = Blotch(n, phase);
                tone = MultiplyRgb(tone, Color.Lerp(Color.white, BlotchTint, blotch * BlotchStrength));

                // A warm throat and jaw, which most reptiles and birds have and which gives the head
                // somewhere brighter than the body to read against.
                float throat = Mathf.SmoothStep(0.75f, 1f, n.z) * (1f - dorsal);
                tone = MultiplyRgb(tone, Color.Lerp(Color.white, ThroatTint, throat));

                // Fine noise so large flat panels do not look moulded.
                tone *= 1f - Mottle * Hash01(v);

                colors[i] = new Color(Mathf.Clamp01(tone.r), Mathf.Clamp01(tone.g), Mathf.Clamp01(tone.b), 1f);
            }

            return colors;
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

        /// <summary>
        /// Per-channel multiply. Tints here deliberately carry channels above 1 so a patch can
        /// brighten as well as darken; Color's own operator would be fine, but naming it makes the
        /// intent obvious next to the Lerps.
        /// </summary>
        private static Color MultiplyRgb(Color a, Color b) =>
            new(a.r * b.r, a.g * b.g, a.b * b.b, 1f);

        /// <summary>Deterministic 0..1 noise from a position. Same vertex, same value, every rebuild.</summary>
        private static float Hash01(Vector3 v)
        {
            float h = Mathf.Sin(v.x * 127.1f + v.y * 311.7f + v.z * 74.7f) * 43758.5453f;
            return h - Mathf.Floor(h);
        }

        private static Material[] EnsureSkinMaterials(
            Material[] sources, string speciesKey, Shader shader, Color? tint, float tintStrength)
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
                if (tint.HasValue) regionColor = Color.Lerp(regionColor, tint.Value, tintStrength);
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
