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
                Tint = new Color(0.45f, 0.32f, 0.22f),
            },
            new()
            {
                // Bio-engineered variant: the T-Rex model in a different skin, costed above it.
                Name = "Bio T-Rex", Model = "Trex", Recolor = true,
                Cost = 520, Health = 4800f, Armor = 20f,
                Damage = 560f, Interval = 1.5f, Range = 4.2f,
                Speed = 7.0f, Mass = 8600f,
                BodySize = new Vector3(2.1f, 2.5f, 5.2f),
                Tint = new Color(0.20f, 0.42f, 0.28f),
            },
            new()
            {
                Name = "Triceratops", Model = "Triceratops",
                Cost = 300, Health = 4400f, Armor = 22f,
                Damage = 300f, Interval = 1.9f, Range = 3.4f,
                Speed = 5.8f, Mass = 7400f,
                BodySize = new Vector3(2.2f, 1.9f, 4.4f),
                Tint = new Color(0.52f, 0.44f, 0.30f),
            },
            new()
            {
                // Cheap and fast. Four of these cost less than one T-Rex and should nearly beat it.
                Name = "Velociraptor", Model = "Velociraptor",
                Cost = 90, Health = 600f, Armor = 2f,
                Damage = 110f, Interval = 0.7f, Range = 1.8f,
                Speed = 11.0f, Mass = 900f,
                BodySize = new Vector3(0.8f, 1.0f, 2.0f),
                Tint = new Color(0.62f, 0.50f, 0.28f),
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
                Tint = new Color(0.50f, 0.43f, 0.33f),
            },
        };
    }
}
