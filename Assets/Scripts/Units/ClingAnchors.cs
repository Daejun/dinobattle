using System.Collections.Generic;
using UnityEngine;

namespace DinoBattle.Units
{
    /// <summary>
    /// The set of places a smaller creature can latch onto this one, handed out one at a time.
    ///
    /// Lives on the host rather than the attacker so the bookkeeping sits with the thing being
    /// climbed: whoever owns the back knows which parts of it are taken. Without that, several
    /// raptors would all pick the same bone and occupy the same point in space.
    /// </summary>
    [DisallowMultipleComponent]
    public class ClingAnchors : MonoBehaviour
    {
        /// <summary>
        /// Bone names to offer, best first. The Quaternius rigs share this naming, and a rig missing
        /// some of them simply offers fewer slots rather than failing.
        /// </summary>
        [SerializeField]
        private string[] anchorBoneNames = { "Shoulders", "Torso", "Back", "Neck", "Hips" };

        private readonly List<Transform> anchors = new();
        private readonly HashSet<Transform> claimed = new();

        /// <summary>How many creatures can hang off this one at once.</summary>
        public int Capacity => anchors.Count;

        private void Awake()
        {
            var bones = GetComponentsInChildren<Transform>(true);

            foreach (var wanted in anchorBoneNames)
            {
                foreach (var bone in bones)
                {
                    if (!bone.name.Equals(wanted, System.StringComparison.OrdinalIgnoreCase)) continue;

                    anchors.Add(bone);
                    break;
                }
            }
        }

        /// <summary>Take a free anchor, or return false when this creature is fully occupied.</summary>
        public bool TryClaim(out Transform anchor)
        {
            foreach (var candidate in anchors)
            {
                if (candidate == null || claimed.Contains(candidate)) continue;

                claimed.Add(candidate);
                anchor = candidate;
                return true;
            }

            anchor = null;
            return false;
        }

        public void Release(Transform anchor)
        {
            if (anchor != null) claimed.Remove(anchor);
        }
    }
}
