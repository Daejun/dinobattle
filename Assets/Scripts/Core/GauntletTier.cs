using System.Collections.Generic;
using UnityEngine;

namespace DinoBattle.Core
{
    /// <summary>
    /// One step of the board: a flat platform, the point creatures walk to when they are on their way
    /// here, and the spots its monsters stand on.
    ///
    /// A component rather than a naming convention. The director asks a tier where its objective is on
    /// every advance, and doing that with <c>transform.Find("Objective")</c> would turn a rename in
    /// the scene builder into a null reference at runtime with no compile error.
    ///
    /// In its own file because it has to be. Unity only binds a MonoBehaviour to a script asset when
    /// the file name matches the class name, so a second MonoBehaviour sharing a file cannot be
    /// serialized — it was silently dropped from the scene on save, and the symptom was a tier list
    /// with the right length and ten null entries.
    /// </summary>
    public class GauntletTier : MonoBehaviour
    {
        [Tooltip("Where creatures head for while climbing to this tier. Set a third of the way onto " +
                 "the platform so they finish the ramp and step clear of it before stopping.")]
        [SerializeField] private Transform objective;

        [Tooltip("Where this tier's monsters stand. The builder scatters these across the platform.")]
        [SerializeField] private List<Transform> spawnPoints = new();

        public Transform Objective => objective;
        public IReadOnlyList<Transform> SpawnPoints => spawnPoints;

        public Vector3 ObjectivePosition => objective != null ? objective.position : transform.position;

        /// <summary>Editor-only wiring, called by the scene builder.</summary>
        public void Configure(Transform objectivePoint, List<Transform> points)
        {
            objective = objectivePoint;
            spawnPoints = points;
        }
    }
}
