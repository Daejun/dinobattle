using UnityEngine;

namespace DinoBattle.EditorTools
{
    /// <summary>
    /// Authoring data for one generated creature: the numbers a designer tunes, with no knowledge of
    /// how the prefab gets built.
    ///
    /// Plain settable fields with object-initializer syntax, deliberately not a positional constructor.
    /// The constructor form reached thirteen parameters, and every new field meant editing all six
    /// rows and counting commas to see which unlabelled float was which.
    /// </summary>
    internal sealed class CreatureBlueprint
    {
        public string Name = "Creature";

        /// <summary>FBX file name (no extension) under Assets/Art/Models. Empty = capsule placeholder.</summary>
        public string Model = "";

        /// <summary>
        /// Repaint the imported model with <see cref="Tint"/>. Only for variants that share another
        /// entry's model — otherwise the pack's own colouring is kept.
        /// </summary>
        public bool Recolor;

        public int Cost = 100;
        public float Health = 1000f;
        public float Armor;
        public float Damage = 100f;
        public float Interval = 1.5f;

        /// <summary>
        /// Attack reach as root-to-root distance, so roughly half this creature's body length plus
        /// how far past its head it can bite. Not an aim-point offset — see MeleeAttack.IsInRange.
        /// </summary>
        public float Range = 3f;
        public float Speed = 6f;
        public float Mass = 1000f;

        /// <summary>Design-space size in game units. Imported models are scaled to match its Z.</summary>
        public Vector3 BodySize = new(1f, 1f, 2f);

        /// <summary>Placeholder colour, and the reskin colour when <see cref="Recolor"/> is set.</summary>
        public Color Tint = Color.gray;

        /// <summary>
        /// Mesh proportion changes, or null to use the model as imported. Lets one base rig carry
        /// more than one species — see <see cref="BodyShape"/>.
        /// </summary>
        public BodyShape Shape;

        /// <summary>
        /// How far <see cref="Tint"/> overrides the pack's own region colours, when
        /// <see cref="Recolor"/> is set. The default keeps the artist's light and dark regions
        /// clearly readable; push it near 1 for a creature that is meant to be one colour all over,
        /// where leaving the regions showing reads as a two-tone paint job rather than as skin.
        /// </summary>
        [UnityEngine.Range(0f, 1f)]
        public float TintStrength = 0.6f;

        /// <summary>
        /// The colour of this creature's markings — the stripes and blotches, not the base hide.
        /// Baked into the vertex stream as an absolute colour, so it is free to be brighter and a
        /// completely different hue from the body. That is what makes a creature look patterned
        /// rather than shaded.
        /// </summary>
        public Color Accent = new(0.95f, 0.62f, 0.15f);
    }

    /// <summary>
    /// Bosses: one enormous creature for a whole team to bring down.
    ///
    /// Kept in their own list rather than in the roster. Everything that fills a team — AUTO FILL,
    /// mirror matches — walks the roster, and a boss turning up as an ordinary pick would not be a
    /// fight, it would be a rout with the budget spent on one model.
    ///
    /// LICENSING, read before shipping this: "Indominus Rex" is a Universal / Jurassic World
    /// trademark, and Docs/legal.md and CLAUDE.md both rule it out for exactly that reason — a Play
    /// Store listing using it is removed and the account is at risk. It is here because the owner of
    /// this project asked for it for a build that only ever goes on their own phone, where trademark
    /// does not bite: the concern is use in commerce, and there is none. Rename this entry before any
    /// public release. The model is not Universal's either — no free Indominus model exists — it is
    /// the CC0 T-Rex scaled up and painted bone white, which is what the animal looks like anyway.
    /// </summary>
    internal static class BossBlueprints
    {
        public static readonly CreatureBlueprint[] All =
        {
            new()
            {
                Name = "Indominus Rex", Model = "Trex", Recolor = true,
                Cost = 3000, Health = 42000f, Armor = 70f,
                Damage = 900f, Interval = 1.9f, Range = 7.5f,
                Speed = 5.4f, Mass = 60000f,
                BodySize = new Vector3(4.6f, 6.4f, 11.5f),
                Accent = new Color(0.42f, 0.46f, 0.52f),
                Tint = new Color(0.90f, 0.89f, 0.85f),
                // Near-total override: the hybrid is described as a uniform whitish-grey, and at
                // the default strength the pack's dark body region stayed dark enough that it
                // came out cream-headed with an olive body.
                TintStrength = 0.93f,
                Shape = BodyShape.Hybrid,
            },
            new()
            {
                // The other pack's dragon, for variety. Genuinely a different silhouette rather than
                // a second big theropod, and it comes with its own wing-beat animation.
                Name = "Wyrm Titan", Model = "Dragon", Recolor = true,
                Cost = 3000, Health = 38000f, Armor = 60f,
                Damage = 820f, Interval = 1.7f, Range = 7f,
                Speed = 6.2f, Mass = 52000f,
                BodySize = new Vector3(5.0f, 5.6f, 10.5f),
                TintStrength = 0.85f,
                Accent = new Color(0.98f, 0.72f, 0.18f),
                Tint = new Color(0.62f, 0.10f, 0.42f),
            },
        };
    }

    /// <summary>
    /// The generated roster.
    ///
    /// Pinned to the species the CC0 pack actually ships, so every entry has real art. Spinosaurus and
    /// Ankylosaurus were dropped for Parasaurolophus and Stegosaurus rather than putting the wrong
    /// animal behind a name.
    ///
    /// "Bio T-Rex" deliberately reuses the T-Rex model with a different tint — an original variant,
    /// which is fine, unlike the trademarked movie names ruled out in Docs/legal.md.
    ///
    /// Balance intent is written up in Docs/game-design.md; keep the two in step.
    /// </summary>
    internal static class CreatureBlueprints
    {
        public static readonly CreatureBlueprint[] All =
        {
            new()
            {
                Name = "T-Rex", Model = "Trex",
                Cost = 420, Health = 4200f, Armor = 12f,
                Damage = 480f, Interval = 1.6f, Range = 4.0f,
                Speed = 6.5f, Mass = 8000f,
                BodySize = new Vector3(2.0f, 2.4f, 5.0f),
                Recolor = true, TintStrength = 0.80f,
                Accent = new Color(0.96f, 0.78f, 0.20f),
                Tint = new Color(0.13f, 0.62f, 0.30f),
            },
            new()
            {
                // Bio-engineered variant: the T-Rex model in a different skin, costed above it.
                Name = "Bio T-Rex", Model = "Trex", Recolor = true,
                Cost = 520, Health = 4800f, Armor = 20f,
                Damage = 560f, Interval = 1.5f, Range = 4.2f,
                Speed = 7.0f, Mass = 8600f,
                BodySize = new Vector3(2.1f, 2.5f, 5.2f),
                TintStrength = 0.80f,
                Accent = new Color(0.95f, 0.35f, 0.55f),
                Tint = new Color(0.10f, 0.62f, 0.68f),
            },
            new()
            {
                Name = "Triceratops", Model = "Triceratops",
                Cost = 300, Health = 4400f, Armor = 22f,
                Damage = 300f, Interval = 1.9f, Range = 3.4f,
                Speed = 5.8f, Mass = 7400f,
                BodySize = new Vector3(2.2f, 1.9f, 4.4f),
                Recolor = true, TintStrength = 0.80f,
                Accent = new Color(0.25f, 0.30f, 0.62f),
                Tint = new Color(0.85f, 0.42f, 0.10f),
            },
            new()
            {
                // Cheap and fast. Four of these cost less than one T-Rex and should nearly beat it.
                Name = "Velociraptor", Model = "Velociraptor",
                Cost = 90, Health = 600f, Armor = 2f,
                Damage = 110f, Interval = 0.7f, Range = 1.8f,
                Speed = 11.0f, Mass = 900f,
                BodySize = new Vector3(0.8f, 1.0f, 2.0f),
                Recolor = true, TintStrength = 0.80f,
                Accent = new Color(0.98f, 0.85f, 0.35f),
                Tint = new Color(0.80f, 0.20f, 0.16f),
            },
            // Stegosaurus is deliberately absent. The pack's attack clip swings its tail in a way
            // that does not read as a strike, and no amount of tuning on our side fixes a clip. Its
            // model and animator are still imported, so restoring the entry is all that is needed if
            // the animation is ever replaced.
            new()
            {
                // Herbivore: quick and cheap, poor damage. The "cheap body" slot above raptors.
                Name = "Parasaurolophus", Model = "Parasaurolophus",
                Cost = 200, Health = 2600f, Armor = 6f,
                Damage = 180f, Interval = 1.6f, Range = 3.4f,
                Speed = 8.2f, Mass = 3200f,
                BodySize = new Vector3(1.6f, 2.6f, 5.0f),
                Recolor = true, TintStrength = 0.80f,
                Accent = new Color(0.20f, 0.55f, 0.85f),
                Tint = new Color(0.78f, 0.72f, 0.12f),
            },
        };
    }
}
