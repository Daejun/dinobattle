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
    /// NAMING. Two of these were originally called after Jurassic World creatures, which are
    /// Universal trademarks that Docs/legal.md and CLAUDE.md both rule out. That was tolerable while
    /// the build only ever went on the owner's own phone — trademark bites on use in commerce, and
    /// there was none — but the source is public now, so they carry their own names.
    ///
    /// Only the names changed. The models are not Universal's either way: both are the CC0 T-Rex
    /// reshaped in code, and what each one is modelled on is described where its BodyShape is
    /// defined, since that is engineering rationale rather than branding.
    /// </summary>
    internal static class BossBlueprints
    {
        /// <summary>
        /// A note on why these health values look wrong next to the armour values.
        ///
        /// Armour is flat subtraction, not a percentage — see Health.TakeDamage. So it is worth far
        /// more against the light hunters than the heavy ones: 78 armour takes 71% off a
        /// Velociraptor's 110-damage bite and 14% off a Bio T-Rex's 560. Health and armour are
        /// therefore not independent dials, and the number that actually decides a boss fight is
        /// neither of them on its own but how long ten hunters need to chew through the pair.
        ///
        /// Measured against the current roster, that quantity predicts the outcome almost exactly,
        /// while raw health does not. At 8 battles each: 31.9s survived -> won 6/8, 30.1s -> 6/8,
        /// 26.5s -> 7/8, 22.1s -> 1/8. The boss that killed hunters FASTEST was the one that lost
        /// almost every fight, because a pack that is still ten strong out-damages anything, and
        /// living longer is what lets a boss thin it.
        ///
        /// So these are all tuned to roughly the same time-to-kill, around 24 seconds, and the
        /// health figures fall out of that. The lightly armoured spider needs the most health to
        /// survive as long as the heavily armoured Malformed Rex on much less. Change armour here and
        /// the health has to move with it, in the opposite direction.
        /// </summary>
        public static readonly CreatureBlueprint[] All =
        {
            new()
            {
                Name = "Alpha Hybrid", Model = "Trex", Recolor = true,
                Cost = 3000, Health = 40000f, Armor = 70f,
                Damage = 1150f, Interval = 1.9f, Range = 7.5f,
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
                Cost = 3000, Health = 41500f, Armor = 60f,
                Damage = 1050f, Interval = 1.7f, Range = 7f,
                Speed = 6.2f, Mass = 52000f,
                BodySize = new Vector3(5.0f, 5.6f, 10.5f),
                TintStrength = 0.85f,
                Accent = new Color(0.98f, 0.72f, 0.18f),
                Tint = new Color(0.62f, 0.10f, 0.42f),
            },
            new()
            {
                // A real genus, not an invention: Megarachne was described as the largest spider
                // that ever lived before being reclassified as a sea scorpion. Natural names carry no
                // trademark, so this one needed no renaming when the repository went public.
                //
                // The third boss exists to fight differently, not just to look different. The others
                // are armoured and hit like a truck at a long reach; this one is the fastest thing on
                // the field with the shortest reach and the least armour, and it wins by rate of fire
                // — 687 damage per second, the highest of the four, against the least health. Whether
                // that trade is even is not something a stat block can tell you, so it was probed.
                //
                // Sourced rather than reshaped from a dinosaur, because eight legs are not something
                // vertex displacement can add to a theropod.
                //
                // Health sits on the same equal-time-to-kill line as the others, at 44500. It was
                // briefly raised to 49000 on the theory that flat armour leaves a lightly armoured
                // boss more exposed to which hunters the pack happens to draw. Re-probing killed
                // that theory: the raise did move this boss from 2/8 to 6/8, but in the same run the
                // three bosses whose stats had not been touched at all moved 4/8 -> 2/8, 5/8 -> 1/8
                // and 3/8 -> 1/8. An effect that size on unchanged configurations means the harness
                // cannot resolve what was being tuned, so the adjustment was backed out rather than
                // kept on the strength of a result that had already been shown to be noise.
                //
                // Two things it is NOT, both ruled out by measuring rather than assuming: reach and
                // attack opportunity. Despite the shortest range on the widest body, it has a target
                // within reach 97% of the time — the same as the others — and it swings more often
                // than any of them, 21 times to their 10-13 over the same window.
                Name = "Megarachne", Model = "Spider", Recolor = true,
                Cost = 3000, Health = 44500f, Armor = 45f,
                Damage = 790f, Interval = 1.15f, Range = 6.0f,
                Speed = 7.4f, Mass = 46000f,

                // The model scales off BodySize.z, and this rig is 1.18x as wide as it is long, so
                // 9.5 puts the leg span at about 11 units — wider than the Alpha Hybrid is long.
                // BodySize.x is NOT that span: it drives the footprint and the physics capsule, and
                // those should describe the solid body in the middle, not the reach of the legs.
                BodySize = new Vector3(5.0f, 3.4f, 9.5f),

                TintStrength = 0.88f,
                Accent = new Color(0.95f, 0.20f, 0.10f),
                Tint = new Color(0.17f, 0.11f, 0.24f),
                Shape = BodyShape.Broodmother,
            },
            new()
            {
                // The failed-experiment boss. Slowest and shortest-reaching of the four, but the most
                // health, the heaviest single hit and the most armour: something that has to be worn
                // down rather than out-traded. It is the mirror of the Megarachne, which is fast and
                // fragile and wins on rate of fire.
                Name = "Malformed Rex", Model = "Trex", Recolor = true,
                Cost = 3000, Health = 38500f, Armor = 78f,
                Damage = 1330f, Interval = 2.35f, Range = 6.8f,
                Speed = 4.8f, Mass = 64000f,
                BodySize = new Vector3(5.2f, 7.0f, 12.5f),

                // Pale and grey, with the tint doing nearly all the work — the film design reads as
                // bleached lab-grown flesh, and the pack's own body colour is dark enough to fight
                // that unless it is almost fully overridden. Same reason the Alpha Hybrid sits at 0.93.
                TintStrength = 0.94f,
                Tint = new Color(0.80f, 0.79f, 0.76f),
                Accent = new Color(0.72f, 0.36f, 0.36f),
                Shape = BodyShape.Malformed,
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
