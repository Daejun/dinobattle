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
        /// The Jurassic World hybrid's silhouette, from the descriptions of the film design.
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
    }
}
