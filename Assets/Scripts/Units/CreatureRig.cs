namespace DinoBattle.Units
{
    /// <summary>
    /// Names of the child objects every creature prefab is built with.
    ///
    /// This is the contract between the editor-side factory that CREATES the hierarchy and the
    /// runtime components that LOOK IT UP by name. It lives in the runtime assembly so both sides
    /// share one symbol: held as bare string literals in two files, renaming one would silently
    /// break the other — no compile error, and nothing check-project.sh can see either, since it
    /// only validates SerializedObject field names.
    /// </summary>
    public static class CreatureRig
    {
        /// <summary>Flat disc under the creature, recoloured per team at spawn.</summary>
        public const string TeamRing = "TeamRing";

        /// <summary>Chest/head marker used for range checks and as the damage origin.</summary>
        public const string AimPoint = "AimPoint";

        /// <summary>Imported model, present once real art has been brought in.</summary>
        public const string ModelVisual = "Visual_Model";

        /// <summary>Blocked-out capsule, used when no model is available.</summary>
        public const string PlaceholderBody = "Visual_Body";

        /// <summary>Blocked-out snout cube, showing which way the placeholder faces.</summary>
        public const string PlaceholderHead = "Visual_Head";

        /// <summary>Billboarded health bar root.</summary>
        public const string HealthBar = "HealthBar";
    }
}
