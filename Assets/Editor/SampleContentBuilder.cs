using System.Collections.Generic;
using System.IO;
using DinoBattle.Data;
using DinoBattle.UI;
using DinoBattle.Units;
using UnityEditor;
using UnityEngine;

namespace DinoBattle.EditorTools
{
    /// <summary>
    /// Builds a creature prefab, <see cref="CreatureDefinition"/> and roster for every entry in
    /// <see cref="CreatureBlueprints"/>. Uses the imported model when one is present and falls back
    /// to blocked-out primitives when it is not, so the game is playable before any art lands.
    ///
    /// This file is the factory only — the balance numbers live in CreatureBlueprints.cs, which is
    /// what a designer actually edits.
    ///
    /// Menu: Dino Battle > 1. Generate Sample Content
    /// </summary>
    public static class SampleContentBuilder
    {
        private const string CreatureDataPath = "Assets/GameData/Creatures";
        /// <summary>Public because the gauntlet draws its heroes from it — see BattleSceneBuilder.</summary>
        public const string RosterPath = "Assets/GameData/Rosters/Roster_Default.asset";

        /// <summary>
        /// Bosses live in their own roster. Anything that fills a team walks the default roster, and
        /// a boss appearing as an ordinary pick would spend the whole budget on one model.
        /// </summary>
        public const string BossRosterPath = "Assets/GameData/Rosters/Roster_Bosses.asset";

        public const string GauntletLadderPath = "Assets/GameData/Rosters/GauntletLadder.asset";

        /// <summary>
        /// The gauntlet's monsters get their own roster for the same reason the bosses do: anything
        /// in the default roster can be auto-filled onto the player's side, and the premise of the
        /// climb is that the board supplies the opposition.
        /// </summary>
        public const string GauntletRosterPath = "Assets/GameData/Rosters/Roster_Gauntlet.asset";
        private const string PrefabPath = "Assets/Prefabs/Creatures";

        [MenuItem("Dino Battle/1. Generate Sample Content", priority = 100)]
        public static void Generate()
        {
            EnsureFolder("Assets/GameData");
            EnsureFolder(CreatureDataPath);
            EnsureFolder("Assets/GameData/Rosters");
            EnsureFolder("Assets/Prefabs");
            EnsureFolder(PrefabPath);

            var definitions = new List<CreatureDefinition>();

            foreach (var blueprint in CreatureBlueprints.All)
            {
                string safeName = blueprint.Name.Replace(" ", "").Replace("-", "");
                GameObject prefab = CreatePlaceholderPrefab(blueprint, safeName);
                CreatureDefinition definition = CreateDefinition(blueprint, safeName, prefab);
                definitions.Add(definition);
            }

            var bosses = new List<CreatureDefinition>();

            foreach (var blueprint in BossBlueprints.All)
            {
                string safeName = blueprint.Name.Replace(" ", "").Replace("-", "");
                GameObject prefab = CreatePlaceholderPrefab(blueprint, safeName);
                bosses.Add(CreateDefinition(blueprint, safeName, prefab));
            }

            var monsters = new List<CreatureDefinition>();

            foreach (var blueprint in GauntletBlueprints.All)
            {
                string safeName = blueprint.Name.Replace(" ", "").Replace("-", "");
                GameObject prefab = CreatePlaceholderPrefab(blueprint, safeName);
                monsters.Add(CreateDefinition(blueprint, safeName, prefab));
            }

            var roster = WriteRoster(RosterPath, definitions);
            WriteRoster(BossRosterPath, bosses);
            WriteRoster(GauntletRosterPath, monsters);
            WriteGauntletLadder(monsters, bosses);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            ForceReimportPrefabs();

            Debug.Log($"[SampleContentBuilder] Generated {definitions.Count} creatures, " +
                      $"{bosses.Count} boss(es) and {monsters.Count} gauntlet monster(s).");
            Selection.activeObject = roster;
        }

        private static CreatureDefinition CreateDefinition(CreatureBlueprint blueprint, string safeName, GameObject prefab)
        {
            string path = $"{CreatureDataPath}/Creature_{safeName}.asset";

            var definition = AssetDatabase.LoadAssetAtPath<CreatureDefinition>(path);
            bool isNew = definition == null;
            if (isNew) definition = ScriptableObject.CreateInstance<CreatureDefinition>();

            definition.displayName = blueprint.Name;
            definition.description = $"Placeholder stat block for {blueprint.Name}.";
            definition.prefab = prefab;
            definition.cost = blueprint.Cost;
            definition.footprintRadius = Mathf.Max(blueprint.BodySize.x, blueprint.BodySize.z) * 0.6f;
            definition.maxHealth = blueprint.Health;
            definition.armor = blueprint.Armor;
            definition.moveSpeed = blueprint.Speed;
            // Heavy still turns slower than light, but the floor is much higher than it was. At the
            // old 240..90 mapping a T-Rex turned at 102 deg/s — 1.8 seconds to come about — and
            // against a raptor circling it at melee range that is slower than the bearing to its own
            // target changes. It could never finish a turn, so it never attacked and read as
            // wandering off. 320..165 keeps the weight difference legible without the deadlock.
            definition.turnSpeedDegrees = Mathf.Lerp(320f, 165f, Mathf.InverseLerp(900f, 8600f, blueprint.Mass));
            definition.mass = blueprint.Mass;
            definition.attackDamage = blueprint.Damage;
            definition.attackInterval = blueprint.Interval;
            definition.attackRange = blueprint.Range;
            definition.attackWindup = blueprint.Interval * 0.25f;
            definition.aggroRange = 90f;

            if (isNew) AssetDatabase.CreateAsset(definition, path);
            else EditorUtility.SetDirty(definition);

            return definition;
        }

        /// <summary>
        /// Blocked-out stand-in creature: a capsule body, a snout marker, and an aim point. Good enough
        /// to watch a fight resolve and to validate stats before art exists.
        /// </summary>
        private static GameObject CreatePlaceholderPrefab(CreatureBlueprint blueprint, string safeName)
        {
            string path = $"{PrefabPath}/Creature_{safeName}.prefab";

            var root = new GameObject($"Creature_{safeName}");

            var collider = root.AddComponent<CapsuleCollider>();
            collider.direction = 2; // Z, so the capsule lies along the body length.

            // Deliberately narrower and shorter than the visible body. The collider is a spacing
            // constraint, not a hitbox: sized to the full silhouette it held attackers a body-width
            // apart and bites appeared to land from thin air. Undersizing lets them close and overlap
            // slightly, which is what a real scrap looks like.
            // Much smaller than the visible body on purpose. This capsule is not a hitbox — hits are
            // resolved by distance in MeleeAttack — it exists only to keep the creature standing on
            // the ground and to stop two of them occupying the exact same point. Every unit of size
            // here is a unit of air the AI cannot close, so at half the body length two creatures
            // could never get their heads near each other.
            collider.radius = blueprint.BodySize.x * 0.22f;
            collider.height = Mathf.Max(blueprint.BodySize.z * 0.28f, collider.radius * 2f);

            // center.y MUST equal the radius. This is a horizontal capsule, so its lowest point sits
            // (center.y - radius) above the root; anything higher and the creature settles that far
            // BELOW the ground once physics drops it. Centring on the body mass buried them by 0.6
            // units, which sank the team ring out of sight and — worse — put the root under the
            // ground probe's ray origin, so IsGrounded never returned true and nothing could move.
            collider.center = new Vector3(0f, collider.radius, 0f);

            var body = root.AddComponent<Rigidbody>();
            body.mass = blueprint.Mass;
            body.freezeRotation = true;

            // Order matters. RequireComponent auto-adds a missing dependency, so adding CreatureBrain
            // first pulled in a CreatureUnit, and the explicit AddComponent<CreatureUnit>() below then
            // created a SECOND one. Only one of the pair ever got Initialize() called, leaving the
            // other on its default team -- two hostile ghosts sharing one transform, killing each
            // other on spawn. Add dependencies before dependents, and use EnsureComponent so a
            // reordering mistake reuses the existing component instead of duplicating it.
            EnsureComponent<Health>(root);
            var unit = EnsureComponent<CreatureUnit>(root);
            EnsureComponent<CreatureLocomotion>(root);
            EnsureComponent<CreatureBrain>(root);
            var attack = EnsureComponent<MeleeAttack>(root);

            // Added after the brain, since it reads the brain's current target. Harmless on the
            // placeholder primitives: with no skeleton it finds no head bone and does nothing.
            EnsureComponent<HeadLook>(root);

            // Body-to-body shoves. Needs the Rigidbody and collider added above it.
            EnsureComponent<CreatureImpact>(root);

            // Celebrates if this creature is on the winning side. Inert until then.
            EnsureComponent<VictoryDance>(root);

            // Real art if the pack has been imported, blocked-out primitives otherwise. Keeping both
            // paths here means one generator owns the prefab and re-running the menu never wipes art.
            Animator animator = AttachModelVisual(root, blueprint, safeName);
            if (animator == null) AttachPlaceholderVisual(root, blueprint, safeName);

            AddTeamRing(root, blueprint);

            var aimPoint = new GameObject(CreatureRig.AimPoint).transform;
            aimPoint.SetParent(root.transform, false);
            aimPoint.localPosition = new Vector3(0f, blueprint.BodySize.y * 0.8f, blueprint.BodySize.z * 0.4f);

            // Wire the serialized references that Initialize does not set at runtime.
            var unitSerialized = new SerializedObject(unit);
            var voice = AttachVoice(root, blueprint);

            unitSerialized.FindProperty("aimPoint").objectReferenceValue = aimPoint;
            unitSerialized.FindProperty("corpseLifetime").floatValue = 12f;
            unitSerialized.FindProperty("voice").objectReferenceValue = voice;
            unitSerialized.FindProperty("roarClip").objectReferenceValue = LoadClip(blueprint, "roar");
            unitSerialized.FindProperty("deathClip").objectReferenceValue = LoadClip(blueprint, "death");
            unitSerialized.ApplyModifiedPropertiesWithoutUndo();

            var attackSerialized = new SerializedObject(attack);
            attackSerialized.FindProperty("damage").floatValue = blueprint.Damage;
            attackSerialized.FindProperty("interval").floatValue = blueprint.Interval;
            attackSerialized.FindProperty("range").floatValue = blueprint.Range;
            attackSerialized.FindProperty("windup").floatValue = blueprint.Interval * 0.25f;
            attackSerialized.FindProperty("animator").objectReferenceValue = animator;
            attackSerialized.FindProperty("attackAudio").objectReferenceValue = voice;
            attackSerialized.FindProperty("attackClip").objectReferenceValue = LoadClip(blueprint, "bite");
            attackSerialized.ApplyModifiedPropertiesWithoutUndo();

            // Wire the Animator explicitly rather than leaving it to the runtime GetComponent fallbacks.
            // check-project.sh verifies these property names, so a rename cannot silently unhook it.
            var brainSerialized = new SerializedObject(root.GetComponent<CreatureBrain>());
            brainSerialized.FindProperty("animator").objectReferenceValue = animator;
            brainSerialized.ApplyModifiedPropertiesWithoutUndo();

            AddHealthBar(root, blueprint, safeName);
            StripMarkerShadows(root);

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }

        /// <summary>
        /// The health bar and the team ring are readouts, not objects in the world. Take them out of
        /// the shadow pass.
        ///
        /// Four renderers per creature, three of them markers: the bar's trough and fill, and the
        /// disc under its feet. At a full field that is three quarters of the creature renderers
        /// being submitted to shadow culling for no possible benefit — a floating rectangle and a
        /// ground decal have no shadow anyone would want.
        ///
        /// Mostly a culling saving rather than a drawing one, and worth being precise about: both
        /// use Sprites/Default, which has no ShadowCaster pass, so they were never actually drawn
        /// into the shadow map. What they were doing was being gathered, bounds-tested and sorted as
        /// candidates every frame. Turning the flag off is free and removes them from that entirely.
        ///
        /// The body keeps casting. A dinosaur's shadow is what plants it on the ground.
        /// </summary>
        private static void StripMarkerShadows(GameObject root)
        {
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                bool marker = renderer.GetComponentInParent<HealthBarBillboard>() != null
                              || renderer.gameObject.name == CreatureRig.TeamRing;
                if (!marker) continue;

                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

                // Nor should they be lit by the shadows of others: a bar that darkens when its owner
                // walks under a tree is a bar the player cannot read.
                renderer.receiveShadows = false;
            }
        }

        /// <summary>
        /// Parent the imported model under the creature root, scaled so its length matches the design's
        /// BodySize.z. The pack authors models roughly six times larger than this game's scale, and the
        /// stat block — attackRange above all — is written in game units, so the model must come to the
        /// stats rather than the other way round.
        ///
        /// Returns the Animator, or null when the model or its controller is not present.
        /// </summary>
        private static Animator AttachModelVisual(GameObject root, CreatureBlueprint blueprint, string safeName)
        {
            if (string.IsNullOrEmpty(blueprint.Model)) return null;

            var modelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Art/Models/{blueprint.Model}.fbx");
            if (modelPrefab == null) return null;

            var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
                $"Assets/Art/Animations/AC_{blueprint.Model}.controller");

            var visual = (GameObject)PrefabUtility.InstantiatePrefab(modelPrefab, root.transform);
            visual.name = CreatureRig.ModelVisual;
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;

            Vector3 measured = CreatureArtImporter.MeasureModel(modelPrefab);
            float scale = measured.z > 0.001f ? blueprint.BodySize.z / measured.z : 1f;
            visual.transform.localScale = Vector3.one * scale;

            AttachStrayArmatures(visual);

            // Re-skin every creature, reskin or not. The imported materials are not merely flat, they
            // are nearly black — the T-Rex body colour ships at (0.06, 0.07, 0.06) — so every species
            // rendered as the same dark silhouette and neither species nor team was readable.
            // CreatureSkinBuilder lifts the palette and bakes counter-shading into the vertex stream;
            // a deliberate reskin passes its tint through to be blended in rather than pasted over.
            CreatureSkinBuilder.Apply(visual, safeName, blueprint.Recolor ? blueprint.Tint : (Color?)null,
                blueprint.Shape, blueprint.TintStrength, blueprint.Accent);

            var animator = visual.GetComponent<Animator>();
            if (animator == null) animator = visual.AddComponent<Animator>();

            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;   // CreatureLocomotion drives the Rigidbody, not the clip.

            // Do not pose a rig nobody can see. The default is AlwaysAnimate, which evaluates every
            // bone of every creature whether or not it is on screen — and the camera deliberately
            // frames only 80% of the battle (framingCoverage), so there is reliably a share of the
            // field being animated for no one.
            //
            // CullUpdateTransforms and not CullCompletely: the state machine has to keep running.
            // Death is a transition, and a creature that dies off screen must be lying down when the
            // camera swings back to it rather than frozen mid-stride. This mode keeps the graph
            // ticking and skips only the transform writes, which is exactly the expensive half.
            //
            // Safe because nothing reads bone positions: locomotion drives the Rigidbody, attacks
            // resolve on distance, and the animator is purely visual. If a future feature attaches
            // anything to a bone — a mounted rider, a bite socket — this has to be revisited.
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

            if (controller == null)
            {
                Debug.LogWarning($"[SampleContentBuilder] {blueprint.Name}: model found but no Animator " +
                                 $"Controller (AC_{blueprint.Model}). Run 'Dino Battle > 4c. Prepare Creature Animation'.");
            }

            return animator;
        }

        /// <summary>
        /// One 3D AudioSource per creature, so a fight is spatialised rather than a flat wall of
        /// noise. Rolloff is tuned to the arena — audible across a scrum, gone at the far edge.
        /// </summary>
        private static AudioSource AttachVoice(GameObject root, CreatureBlueprint blueprint)
        {
            var voice = EnsureComponent<AudioSource>(root);

            voice.playOnAwake = false;
            voice.spatialBlend = 1f;
            voice.rolloffMode = AudioRolloffMode.Linear;
            voice.minDistance = blueprint.BodySize.z * 2f;
            voice.maxDistance = 90f;
            voice.dopplerLevel = 0f;   // Creatures are slow; doppler only adds wobble.

            return voice;
        }

        /// <summary>
        /// Pick the small or large variant of a generated clip by body mass. Returns null when the
        /// audio has not been generated, which simply leaves the creature silent.
        /// </summary>
        private static AudioClip LoadClip(CreatureBlueprint blueprint, string kind)
        {
            string size = blueprint.Mass >= 4000f ? "large" : "small";

            // .ogg first. The real CC0 recordings land as .ogg and the procedural fallback writes
            // .wav, so preferring ogg means downloaded audio wins automatically wherever it exists
            // and a fresh clone without it still gets sound from the generator.
            foreach (string extension in new[] { "ogg", "wav" })
            {
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>($"Assets/Audio/SFX/sfx_{kind}_{size}.{extension}");
                if (clip != null) return clip;
            }

            return null;
        }

        /// <summary>
        /// Flat disc on the ground, recoloured per team at spawn by CreatureUnit. This carries the
        /// team read so the dinosaurs themselves can keep realistic colouring.
        /// </summary>
        private static void AddTeamRing(GameObject root, CreatureBlueprint blueprint)
        {
            // Cylinder, not Quad: a quad is square, and a square patch under each creature reads as a
            // paint blob rather than a unit marker. A flattened cylinder gives a disc.
            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = CreatureRig.TeamRing;
            Object.DestroyImmediate(ring.GetComponent<Collider>());

            ring.transform.SetParent(root.transform, false);

            // Above the ground plane AND above the scattered ground patches, whose staggered heights
            // top out around 0.14. Sitting below them let the scenery cover a creature's team marker.
            ring.transform.localPosition = new Vector3(0f, 0.2f, 0f);

            // Wide enough to read from the spectator camera but still inside the creature's footprint.
            // Keyed to body length: width alone made a raptor's marker a barely-visible speck.
            float diameter = Mathf.Max(1.2f, blueprint.BodySize.z * 0.6f);
            ring.transform.localScale = new Vector3(diameter, 0.01f, diameter);

            // Unlit so the marker stays readable regardless of how the sun hits the creature.
            // Saved as one shared asset: an unsaved Material would be serialised into each prefab
            // separately, giving six near-identical copies with no way to retune them together.
            // CreatureUnit calls renderer.material at spawn, which instances it per creature anyway.
            EnsureFolder("Assets/Art");
            EnsureFolder("Assets/Art/Materials");
            const string ringMaterialPath = "Assets/Art/Materials/TeamRing.mat";

            // Sprites/Default, not Unlit/Color. Unlit/Color has no blending at all — it writes the
            // RGB and throws the alpha away, so asking for a 20%-opacity ring through it produced a
            // ring at 100%. Sprites/Default is the simplest built-in shader that is unlit AND
            // alpha-blended, with ZWrite off, which is exactly what a ground decal wants.
            var ringShader = Shader.Find("Sprites/Default")
                             ?? Shader.Find("Legacy Shaders/Transparent/Diffuse")
                             ?? Shader.Find("Standard");

            var ringMaterial = AssetDatabase.LoadAssetAtPath<Material>(ringMaterialPath);
            if (ringMaterial == null)
            {
                ringMaterial = new Material(ringShader);
                AssetDatabase.CreateAsset(ringMaterial, ringMaterialPath);
            }
            else if (ringMaterial.shader != ringShader)
            {
                // Reassign on rebuild too. The asset already existed from before the transparency
                // fix, and only creating it when missing would leave it on the opaque shader forever.
                ringMaterial.shader = ringShader;
                EditorUtility.SetDirty(ringMaterial);
            }

            ring.GetComponent<Renderer>().sharedMaterial = ringMaterial;
        }

        /// <summary>Blocked-out capsule body plus a snout cube, used until real art is imported.</summary>
        private static void AttachPlaceholderVisual(GameObject root, CreatureBlueprint blueprint, string safeName)
        {
            var visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            visual.name = CreatureRig.PlaceholderBody;
            Object.DestroyImmediate(visual.GetComponent<Collider>());
            visual.transform.SetParent(root.transform, false);
            visual.transform.localPosition = new Vector3(0f, blueprint.BodySize.y * 0.5f, 0f);
            visual.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            visual.transform.localScale = new Vector3(blueprint.BodySize.x, blueprint.BodySize.z * 0.5f, blueprint.BodySize.x);
            TintRenderer(visual, blueprint.Tint, safeName);

            // Snout marker so it is obvious which way the creature faces.
            var snout = GameObject.CreatePrimitive(PrimitiveType.Cube);
            snout.name = CreatureRig.PlaceholderHead;
            Object.DestroyImmediate(snout.GetComponent<Collider>());
            snout.transform.SetParent(root.transform, false);
            snout.transform.localPosition = new Vector3(0f, blueprint.BodySize.y * 0.85f, blueprint.BodySize.z * 0.45f);
            snout.transform.localScale = Vector3.one * (blueprint.BodySize.x * 0.55f);
            TintRenderer(snout, blueprint.Tint * 0.7f, safeName);
        }

        /// <summary>
        /// Height of the tallest point of the creature's rendered mesh, in root-local units. Falls
        /// back to the blueprint height when there is no visual to measure, which is the placeholder
        /// case. Called at build time, with the prefab root at the origin, so world Y is local Y.
        /// </summary>
        private static float MeasuredTop(GameObject root)
        {
            Bounds? combined = null;

            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                // The ring lies flat on the ground and the bar does not exist yet; neither says
                // anything about how tall the animal is.
                if (renderer.name == CreatureRig.TeamRing) continue;

                if (combined == null)
                {
                    combined = renderer.bounds;
                    continue;
                }

                Bounds grown = combined.Value;
                grown.Encapsulate(renderer.bounds);
                combined = grown;
            }

            if (combined == null) return 1f;

            return combined.Value.max.y - root.transform.position.y;
        }

        private static void AddHealthBar(GameObject root, CreatureBlueprint blueprint, string safeName)
        {
            // Styled after the reference game: a short bar at mid-body height, not a wide banner over
            // the head. Sized to a fraction of body length so a raptor's marker is not as wide as a
            // T-Rex's, and kept small enough that a scrum of creatures does not become a wall of bars.
            const float barLength = 0.3f;
            const float barThickness = 0.18f;

            var bar = new GameObject(CreatureRig.HealthBar);
            bar.transform.SetParent(root.transform, false);

            // Anchor just above the ACTUAL mesh rather than at a fraction of the blueprint's nominal
            // height. The two diverge once the model is scaled to fit its blueprint, and on the large
            // creatures the bar ended up floating more than a tenth of the screen clear of the animal
            // it belonged to — far enough that it read as an unrelated object.
            bar.transform.localPosition = new Vector3(0f, MeasuredTop(root) + 0.25f, 0f);

            float width = blueprint.BodySize.z * barLength;

            var background = GameObject.CreatePrimitive(PrimitiveType.Quad);
            background.name = "Background";
            Object.DestroyImmediate(background.GetComponent<Collider>());
            background.transform.SetParent(bar.transform, false);
            background.transform.localScale = new Vector3(width, barThickness, 1f);

            // Warm dark red behind the fill, so the drained portion reads as damage taken rather than
            // as empty space — the same green-over-orange treatment the reference uses. Darker than
            // the fill by a wide margin: contrast against the fill is what makes the level readable
            // at a glance, and the two used to sit close enough in value to blur together.
            TintRenderer(background, new Color(0.22f, 0.05f, 0.03f), safeName, unlit: true);

            var fillPivot = new GameObject("FillPivot");
            fillPivot.transform.SetParent(bar.transform, false);
            fillPivot.transform.localPosition = new Vector3(-width * 0.5f, 0f, -0.02f);

            var fill = GameObject.CreatePrimitive(PrimitiveType.Quad);
            fill.name = "Fill";
            Object.DestroyImmediate(fill.GetComponent<Collider>());
            fill.transform.SetParent(fillPivot.transform, false);
            // Pivot the quad at its left edge so scaling X drains the bar from the right.
            fill.transform.localPosition = new Vector3(0.5f, 0f, 0f);
            fill.transform.localScale = Vector3.one;
            // Unlit, like the team rings. A health bar is a readout, not part of the scene, and a lit
            // one is at the mercy of the arena: under green fog and an overcast sky the fill was
            // being darkened and desaturated toward the exact colour of the ground behind it.
            // HealthBarBillboard recolours this at runtime; the value here is only the full-health case.
            TintRenderer(fill, new Color(0.10f, 1f, 0.20f), safeName, unlit: true);

            fillPivot.transform.localScale = new Vector3(width, barThickness * 0.8f, 1f);

            var billboard = bar.AddComponent<HealthBarBillboard>();
            var serialized = new SerializedObject(billboard);
            serialized.FindProperty("health").objectReferenceValue = root.GetComponent<Health>();
            serialized.FindProperty("fill").objectReferenceValue = fillPivot.transform;
            serialized.FindProperty("fillRenderer").objectReferenceValue = fill.GetComponent<Renderer>();

            // Always on, like the reference. Hiding full bars makes a fresh army look unselectable and
            // gives no read on who has not been touched yet.
            serialized.FindProperty("hideWhenFull").boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Parent any secondary armature onto the head of the main one.
        ///
        /// The dragon ships its eyes as a separate skinned mesh bound to a single bone on its own
        /// armature — Dragon/EyeArmature/Bone — while the head lives at
        /// Dragon/DragonArmature/Root/BodyRoot/Body/Neck/Head. Every animation clip in the file has
        /// ten curves driving Head and none at all driving Bone, so the head turns and the eyes stay
        /// exactly where the bind pose left them. On screen the dragon's eyes hang in the air beside
        /// it while it fights.
        ///
        /// That is a flaw in the source model, not in anything here, and it is invisible in a static
        /// preview — which is presumably how it shipped. The fix is to hang the orphan armature off
        /// the head so it inherits the animation that already exists.
        ///
        /// Reparented preserving world position, because the bind pose is correct: the eyes start in
        /// the right place and only need to be told what to follow. Written generally rather than as
        /// a check for "Dragon", so the next model with the same rigging habit is handled too, and it
        /// does nothing at all for a creature whose meshes share one armature.
        /// </summary>
        private static void AttachStrayArmatures(GameObject visual)
        {
            var renderers = visual.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (renderers.Length < 2) return;

            // The rig with the most bones is the real one.
            SkinnedMeshRenderer primary = null;
            foreach (var renderer in renderers)
                if (primary == null || renderer.bones.Length > primary.bones.Length) primary = renderer;

            if (primary == null || primary.rootBone == null) return;

            // Unpack first, or the reparent below is silently thrown away.
            //
            // Visual_Model is a nested prefab instance of the FBX, and moving a child ACROSS that
            // boundary is not something Unity can record as an override — it applies in the open
            // scene, survives right up to the save, and is gone from the asset on disk. The first
            // version of this looked like it worked: the console logged the reparent, and even a
            // runtime check passed, because the eyes ride the body's transform whether or not they
            // are attached to the head. They only come adrift when the head moves on its own, which
            // is exactly when anyone is looking at the dragon.
            var visualInstance = visual;
            if (PrefabUtility.IsPartOfPrefabInstance(visualInstance))
            {
                PrefabUtility.UnpackPrefabInstance(
                    PrefabUtility.GetOutermostPrefabInstanceRoot(visualInstance),
                    PrefabUnpackMode.Completely,
                    InteractionMode.AutomatedAction);
            }

            Transform head = null;
            foreach (var bone in primary.bones)
                if (bone != null && bone.name == "Head") head = bone;

            // No head to attach to — leave it alone rather than guessing at some other bone.
            if (head == null) return;

            foreach (var renderer in renderers)
            {
                if (renderer == primary) continue;

                Transform root = renderer.rootBone;
                if (root == null || root.IsChildOf(primary.rootBone)) continue;

                // The armature object itself, not the bone inside it, so the whole stray hierarchy
                // moves as one.
                Transform stray = root.parent != null && root.parent != visual.transform ? root.parent : root;

                stray.SetParent(head, worldPositionStays: true);
                Debug.Log($"[SampleContentBuilder] {visual.name}: reparented '{stray.name}' onto Head — " +
                          "its bones are not animated by any clip in the model.");
            }
        }

        /// <summary>
        /// Give <paramref name="target"/> its own tinted material, named per creature so the asset is
        /// identifiable and so re-running this menu command reuses it.
        ///
        /// This used to call GenerateUniqueAssetPath keyed on the GameObject name, which produced
        /// "Placeholder_Visual_Body 1..5" -- unidentifiable, and another full set on every re-run.
        /// </summary>
        private static void TintRenderer(GameObject target, Color color, string creatureName, bool unlit = false)
        {
            if (!target.TryGetComponent<Renderer>(out var renderer) ) return;

            EnsureFolder("Assets/Art");
            EnsureFolder("Assets/Art/Materials");
            string path = $"Assets/Art/Materials/Placeholder_{creatureName}_{target.name}.mat";

            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(renderer.sharedMaterial);
                AssetDatabase.CreateAsset(material, path);
            }

            // Re-point an existing material too: these assets are reused across runs, so a material
            // created before this option existed would otherwise keep its lit shader forever.
            if (unlit)
            {
                var flat = Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default");
                if (flat != null && material.shader != flat) material.shader = flat;
            }

            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            EditorUtility.SetDirty(material);

            renderer.sharedMaterial = material;
        }

        /// <summary>
        /// Return the existing component of type T, or add one. Guards against the RequireComponent
        /// duplication trap: a dependency may already have been auto-added by a dependent.
        /// </summary>
        private static T EnsureComponent<T>(GameObject target) where T : Component
        {
            var existing = target.GetComponent<T>();
            return existing != null ? existing : target.AddComponent<T>();
        }

        /// <summary>
        /// Reimport every generated prefab, so what the editor reports matches what was just written.
        ///
        /// Without this, clearing the baked-mesh cache and regenerating left the editor holding the
        /// prefabs as they were before: they read back with a null mesh and null materials while the
        /// files on disk were perfectly correct. That is indistinguishable from generation having
        /// failed, and it cost several rounds of chasing a bug that was not there — and once it
        /// reached play mode, where every creature spawned invisible.
        ///
        /// SaveAssets and Refresh are not enough on their own: they flush writes and rescan the
        /// folder, but leave already-loaded prefab instances alone.
        /// </summary>
        private static void ForceReimportPrefabs()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { PrefabPath }))
            {
                AssetDatabase.ImportAsset(
                    AssetDatabase.GUIDToAssetPath(guid), ImportAssetOptions.ForceUpdate);
            }
        }

        /// <summary>Create or overwrite a roster asset listing exactly these definitions.</summary>
        /// <summary>
        /// The gauntlet's difficulty ladder: ten tiers, growing in both count and toughness.
        ///
        /// Both levers, deliberately. Count alone turns the late tiers into crowd management; stat
        /// scaling alone draws the same picture with bigger numbers. Together the run changes
        /// character as you climb, from a skirmish into a swarm.
        ///
        /// Health climbs far faster than damage — 2.4x against 1.4x by tier nine. Damage scaling
        /// one-shots the player's creatures, which reads as unfair rather than hard; health scaling
        /// only makes the fight longer. The boss balancing already taught this once: time-to-kill
        /// predicted the win rate and raw health did not.
        ///
        /// There is no armour scale. Damage is applied as max(1, raw - armor), so armour is not a
        /// proportional knob — past the point where it exceeds the attackers' damage every hit in
        /// the game does exactly 1, and the curve becomes a wall.
        ///
        /// Peak concurrency is tier nine's ten monsters against roughly ten of the player's, so
        /// twenty on the field — inside the twenty-four that Docs/performance.md measured as the
        /// current ceiling. Lengthen this ladder and check that document first.
        /// </summary>
        private static void WriteGauntletLadder(List<CreatureDefinition> roster, List<CreatureDefinition> bosses)
        {
            var ladder = AssetDatabase.LoadAssetAtPath<GauntletLadder>(GauntletLadderPath);
            if (ladder == null)
            {
                ladder = ScriptableObject.CreateInstance<GauntletLadder>();
                AssetDatabase.CreateAsset(ladder, GauntletLadderPath);
            }

            // Sorted by cost so "weak" and "strong" mean something, and the ladder can reach for the
            // cheap end early and the expensive end late without naming species in code.
            var byCost = new List<CreatureDefinition>(roster);
            byCost.Sort((a, b) => a.cost.CompareTo(b.cost));

            // A window that slides up the cost-ordered list as the tiers climb, rather than two
            // fixed buckets. With buckets the pool jumped — the cheapest four for tiers three to
            // five, then the strongest four from six on — so the middle of the ladder was the same
            // four monsters three times and tier six changed the cast wholesale. A sliding window
            // introduces one new species at a time and retires one, so each tier is recognisably the
            // last one plus something worse.
            List<CreatureDefinition> Window(float t, int width)
            {
                int span = Mathf.Max(1, byCost.Count - width);
                int start = Mathf.Clamp(Mathf.RoundToInt(t * span), 0, span);
                return byCost.GetRange(start, Mathf.Min(width, byCost.Count - start));
            }

            List<CreatureDefinition> Strongest(int n) =>
                byCost.GetRange(Mathf.Max(0, byCost.Count - n), Mathf.Min(n, byCost.Count));

            int[] counts = { 3, 4, 5, 5, 6, 7, 8, 9, 10 };
            float[] health = { 1.0f, 1.10f, 1.20f, 1.35f, 1.50f, 1.70f, 1.90f, 2.10f, 2.40f };
            float[] damage = { 1.0f, 1.00f, 1.05f, 1.10f, 1.15f, 1.20f, 1.25f, 1.30f, 1.40f };

            var tiers = new List<GauntletTierSpec>();
            for (int i = 0; i < counts.Length; i++)
            {
                tiers.Add(new GauntletTierSpec
                {
                    label = $"{i + 1}층",
                    // Slide from the cheap end to the expensive one across the nine combat tiers.
                    species = Window(i / (float)(counts.Length - 1), 3),
                    count = counts[i],
                    healthScale = health[i],
                    damageScale = damage[i],
                });
            }

            tiers.Add(new GauntletTierSpec
            {
                label = "보스",
                // The arthropod, by name rather than by index.
                //
                // A scorpion was asked for and there is still no getting one: re-checked against
                // poly.pizza (both the site and its API), the full 45-model Quaternius monster
                // catalogue, and open search — every hit is paid, account-gated or CC-BY-SA. That is
                // the same wall ATTRIBUTIONS.md records from the first time this came up, and
                // nothing has moved.
                //
                // Megarachne is the nearest thing the project has: a giant arthropod, eight legs, a
                // silhouette nothing else in the roster shares, and a real extinct genus that was
                // itself reclassified as a SEA SCORPION. Picked by name so that reordering the boss
                // list cannot silently swap the climb's finale back to a repainted T-Rex, which is
                // what bosses[0] was giving it.
                species = new List<CreatureDefinition> { PickBoss(bosses, "Megarachne") },
                count = 1,
                healthScale = 1f,
                damageScale = 1f,
                isBoss = true,
            });

            ladder.SetTiers(tiers);
            EditorUtility.SetDirty(ladder);
        }

        /// <summary>Boss by display name, falling back to the first so a rename cannot break the ladder.</summary>
        private static CreatureDefinition PickBoss(List<CreatureDefinition> bosses, string displayName)
        {
            foreach (var boss in bosses)
                if (boss != null && boss.displayName == displayName) return boss;

            if (bosses.Count > 0)
            {
                Debug.LogWarning($"[SampleContentBuilder] No boss named '{displayName}'; " +
                                 $"the gauntlet will end on {bosses[0].displayName} instead.");
                return bosses[0];
            }

            return null;
        }

        private static CreatureRoster WriteRoster(string path, List<CreatureDefinition> definitions)
        {
            var roster = AssetDatabase.LoadAssetAtPath<CreatureRoster>(path);
            if (roster == null)
            {
                roster = ScriptableObject.CreateInstance<CreatureRoster>();
                AssetDatabase.CreateAsset(roster, path);
            }

            // creatures is private; write through SerializedObject so the change is recorded properly.
            var serialized = new SerializedObject(roster);
            var list = serialized.FindProperty("creatures");
            list.arraySize = definitions.Count;

            for (int i = 0; i < definitions.Count; i++)
            {
                list.GetArrayElementAtIndex(i).objectReferenceValue = definitions[i];
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            return roster;
        }

        internal static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;

            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(leaf)) return;

            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
