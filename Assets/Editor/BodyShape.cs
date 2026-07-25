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

            public Part(string[] bones, Vector3 scale)
            {
                Bones = bones;
                Scale = scale;
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
                new Part(new[] { "Head" }, new Vector3(1.75f, 1.45f, 1.55f)),
                new Part(new[] { "FrontLeg", "FrontUpLeg", "FrontLowLeg", "FrontFoot" }, new Vector3(2.4f, 2.4f, 3.0f)),
                new Part(new[] { "Neck" }, new Vector3(1.3f, 1.3f, 1.15f)),
            }
        };
    }
}
