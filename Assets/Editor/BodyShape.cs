using UnityEngine;

namespace DinoBattle.EditorTools
{
    /// <summary>
    /// Proportion changes applied to a base model's mesh, so one rig can produce more than one animal.
    ///
    /// This exists because scaling a creature up does not make a new creature — it makes the same
    /// creature standing closer to the camera. What reads as a different species is different
    /// proportions: which parts are oversized relative to the rest. Moving vertices per bone group is
    /// enough to get there, and it costs nothing at runtime because the result is baked into a mesh
    /// asset at build time.
    /// </summary>
    internal sealed class BodyShape
    {
        internal readonly struct Part
        {
            /// <summary>Bone names to match on, by substring. Catches left and right in one entry.</summary>
            public readonly string[] Bones;

            /// <summary>Per-axis multiplier, in mesh space, about each vertex's driving bone.</summary>
            public readonly Vector3 Scale;

            /// <summary>
            /// How much to concentrate the effect toward the TOP of this part, 0 for none.
            ///
            /// Exists because the rig has one Head bone and no separate brow. Scaling it evenly
            /// inflates the jaw as much as the skull roof, which reads as a swollen face rather than
            /// a heavy brow. Weighting by height within the part's own vertex extent raises the
            /// forehead and leaves the snout alone, with no extra bones required.
            /// </summary>
            public readonly float UpperBias;

            public Part(string[] bones, Vector3 scale, float upperBias = 0f)
            {
                Bones = bones;
                Scale = scale;
                UpperBias = upperBias;
            }
        }

        public Part[] Parts = System.Array.Empty<Part>();

        /// <summary>
        /// Alpha Hybrid: the silhouette of the Jurassic World hybrid, from descriptions of the film
        /// design. Named for what it is modelled on, which is reference, not branding — the creature
        /// itself carries an original name. See BossBlueprints.
        ///
        /// Three things separate it from the T-Rex it is built on, and only the first two are
        /// reachable by moving existing vertices:
        ///
        /// Arms. The single biggest difference and the one people actually recognise — the design
        /// gives it long, heavy, Therizinosaurus-like forelimbs it can put weight on, where a T-Rex
        /// has famously little ones. Tripled.
        ///
        /// Skull. Described as Giganotosaurus-shaped with the width of a Majungasaurus, so it is
        /// widened more than it is lengthened rather than scaled evenly.
        ///
        /// Not reachable: the osteoderms and neck spines need geometry that is not in the source
        /// mesh. Vertex movement cannot add spikes to a back that has none, and faking them by
        /// stretching the existing back would just produce a lumpy dinosaur.
        /// </summary>
        public static BodyShape Hybrid => new()
        {
            Parts = new[]
            {
                // Skull: broad, but not evenly. The width is the Majungasaurus note from the design
                // description; the separate biased pass above it is the brow ridge and horns.
                new Part(new[] { "Head" }, new Vector3(1.7f, 1.25f, 1.45f)),
                new Part(new[] { "Head" }, new Vector3(1.15f, 1.5f, 1.1f), upperBias: 0.85f),

                // Arms long and heavy — the Therizinosaurus-like forelimbs are the silhouette people
                // recognise. The hand is deliberately NOT in this group: inheriting the full arm
                // scale gave it shovels for claws.
                new Part(new[] { "FrontLeg", "FrontUpLeg", "FrontLowLeg" }, new Vector3(2.4f, 2.4f, 3.0f)),
                new Part(new[] { "FrontFoot" }, new Vector3(1.25f, 1.25f, 1.25f)),

                new Part(new[] { "Neck" }, new Vector3(1.3f, 1.3f, 1.15f)),

                // Tail thickened, not lengthened. It was left at T-Rex proportions under a much
                // bulkier body, which made the animal look like it was wearing someone else's tail.
                new Part(new[] { "Tail1", "Tail2", "Tail3" }, new Vector3(1.55f, 1.55f, 1f)),
                new Part(new[] { "Tail4", "Tail5" }, new Vector3(1.3f, 1.3f, 1f)),
            }
        };

        /// <summary>
        /// Malformed Rex: a T-Rex that went wrong in the tank, after the Jurassic World Rebirth
        /// mutant. Same note as above — the reference is here, the name is not.
        ///
        /// Almost the opposite brief to <see cref="Hybrid"/>. That one is a well-made animal
        /// exaggerated in the directions a predator is already built; this one is a failed one, and
        /// what sells it is proportions that look like a mistake rather than an upgrade.
        ///
        /// The skull carries it. The design is described as a head that never developed past the
        /// embryonic stage — brachycephalic, hugely swollen and SHORT, with the cranium ballooning
        /// over small eyes. So this is the one part scaled DOWN along its length while being blown up
        /// across the other two axes, with a second biased pass raising the dome. Every other
        /// creature here gets a longer snout when it grows; this one gets a shorter one, and that
        /// inversion is the whole silhouette.
        ///
        /// Then the limbs. Polymelia in the film, six of them, and vertex displacement cannot add a
        /// pair of arms any more than it could add osteoderms to the hybrid. What it can do is the
        /// other half of the description — long, heavy, disproportionate forelimbs and a hunched
        /// ape-like bulk through the shoulders — so the arms go further than the hybrid's and the
        /// torso is thickened to match. Four-armed is out of reach; wrong-looking is not.
        /// </summary>
        public static BodyShape Malformed => new()
        {
            Parts = new[]
            {
                // Swollen and stunted at once: wider and taller than a T-Rex skull, and notably
                // shorter front to back. 0.8 rather than lower because past that the snout starts
                // pulling back into the neck instead of reading as a blunt face.
                new Part(new[] { "Head" }, new Vector3(2.0f, 1.9f, 0.8f)),

                // The cranial dome over the eyes. Biased to the top of the head's own extent, the
                // same trick the hybrid uses for its brow, pushed much further.
                new Part(new[] { "Head" }, new Vector3(1.15f, 1.5f, 1.05f), upperBias: 0.75f),

                // Short thick neck — a head that heavy cannot sit on a graceful one.
                new Part(new[] { "Neck" }, new Vector3(1.6f, 1.55f, 0.85f)),

                // Arms longer and heavier even than the hybrid's. Hand excluded for the same reason:
                // inheriting the full arm scale gives it shovels.
                new Part(new[] { "FrontLeg", "FrontUpLeg", "FrontLowLeg" }, new Vector3(2.7f, 2.7f, 3.5f)),
                new Part(new[] { "FrontFoot" }, new Vector3(1.45f, 1.45f, 1.45f)),

                // Hunched bulk. Shoulders heaviest, falling off down the back, so the mass reads as
                // piled onto the front of the animal rather than as a uniformly fatter dinosaur.
                new Part(new[] { "Shoulders" }, new Vector3(1.5f, 1.5f, 1.25f)),
                new Part(new[] { "Torso" }, new Vector3(1.35f, 1.35f, 1.15f)),
                new Part(new[] { "Hips" }, new Vector3(1.2f, 1.2f, 1.1f)),

                // Tail thickened to carry the extra body, not lengthened.
                new Part(new[] { "Tail1", "Tail2", "Tail3" }, new Vector3(1.45f, 1.45f, 1f)),
            }
        };

        /// <summary>
        /// A giant spider turned into something worth being a boss.
        ///
        /// The source is from a pack of small, ordinary enemies, and scaling one of those up to boss
        /// size gives away exactly what it is — the proportions stay those of a creature meant to be
        /// stepped on. What separates a big spider from a monstrous one is where the mass sits: a
        /// heavy, swollen abdomen dragging behind a comparatively small body, and a head that has
        /// grown to carry fangs rather than in step with the rest.
        ///
        /// The legs are thickened only slightly. They are already long relative to the body — that
        /// is what a spider is — and pushing them further turned it into a daddy-long-legs, which
        /// reads as fragile rather than dangerous.
        ///
        /// Bone names come from the rig itself: Abdomen, Thorax, Head, and four leg pairs whose
        /// names all contain "Leg", which the substring match picks up in one entry.
        /// </summary>
        public static BodyShape Broodmother => new()
        {
            Parts = new[]
            {
                new Part(new[] { "Abdomen" }, new Vector3(1.55f, 1.5f, 1.6f)),
                new Part(new[] { "Head" }, new Vector3(1.35f, 1.3f, 1.35f)),
                new Part(new[] { "Thorax" }, new Vector3(1.15f, 1.15f, 1.1f)),
                new Part(new[] { "Leg" }, new Vector3(1.18f, 1.18f, 1.18f)),
            }
        };
    }
}
