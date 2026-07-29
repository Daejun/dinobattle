using UnityEngine;

namespace DinoBattle.UI
{
    /// <summary>
    /// Keeps a world-space <see cref="TextMesh"/> on a depth-tested material pointed at the live font
    /// atlas.
    ///
    /// The tier numbers on the gauntlet deck cannot use the font's own material — that is
    /// "GUI/Text Shader", which is ZTest Always, so the numbers drew straight through the creatures
    /// standing on them. They use DinoBattle/DeckText instead, which is the same shader with depth
    /// testing switched on.
    ///
    /// The catch is that <c>LegacyRuntime.ttf</c> is a DYNAMIC font: its atlas is rasterised on
    /// demand and rebuilt whenever a glyph it has not seen before is asked for. The HUD draws Korean
    /// with the same font, so rebuilds genuinely happen mid-run — and on a rebuild Unity repoints its
    /// own font material at the new texture and leaves every other material holding the old one. The
    /// symptom would be the deck numbers turning into fragments of Korean, which is a bizarre enough
    /// bug to be worth this component's existence.
    ///
    /// One material instance shared by every number, so the ten of them still batch into one draw
    /// call. Created from the authored asset rather than written to it: a texture assigned to a
    /// shared material asset at runtime would dirty that asset in the editor.
    /// </summary>
    [RequireComponent(typeof(TextMesh))]
    [RequireComponent(typeof(MeshRenderer))]
    public class DeckNumber : MonoBehaviour
    {
        [Tooltip("How tall the number should be, in world units along the deck.")]
        [SerializeField] private float targetHeight = 7f;

        [Tooltip("How wide it is allowed to get. Two-digit numbers are shrunk to respect this — " +
                 "which is the whole reason it exists.")]
        [SerializeField] private float maxWidth = 17f;

        private static Material shared;

        private TextMesh text;
        private MeshRenderer meshRenderer;
        private bool fitted;

        private void Awake()
        {
            text = GetComponent<TextMesh>();
            meshRenderer = GetComponent<MeshRenderer>();
        }

        private void OnEnable()
        {
            Font.textureRebuilt += HandleFontRebuilt;

            // Make sure there IS an atlas before reading its texture. A dynamic font rasterises on
            // demand, so on a board that has been switched on before anything asked this font for a
            // digit, font.material.mainTexture is still null and the numbers would come up blank.
            //
            // Deliberately not called from HandleFontRebuilt — requesting characters is what causes
            // a rebuild, and doing it from inside the rebuild callback invites recursion.
            var font = text != null ? text.font : null;
            if (font != null) font.RequestCharactersInTexture(text.text, text.fontSize, text.fontStyle);

            Apply();
        }

        private void OnDisable()
        {
            Font.textureRebuilt -= HandleFontRebuilt;
        }

        /// <summary>
        /// Scale the number to the deck, once, as soon as there is a mesh to measure.
        ///
        /// MEASURED rather than calculated. TextMesh turns fontSize and characterSize into world
        /// units through the font's own metrics, and working out that constant from the outside is
        /// exactly the kind of guess that put the number off the edge of the board in the first
        /// place: it was parented to a cube scaled to (26, 1, 22) and inherited it, so its size was
        /// decided by the slab's dimensions rather than by anyone's intent. One digit fitted by luck
        /// and "10" did not.
        ///
        /// Reading the mesh that actually came out cannot be wrong about the font, and it handles
        /// the two-digit case for free.
        ///
        /// Height first, width as a ceiling: every tier should carry the same size of number, so
        /// height sets it, and only "10" is pulled in to stop it reaching the edges. Fitting purely
        /// to width would draw "1" enormous and "10" small.
        ///
        /// In LateUpdate because TextMesh builds its mesh during the frame, not in Awake — bounds
        /// read any earlier are empty.
        /// </summary>
        private void LateUpdate()
        {
            if (fitted || meshRenderer == null) return;

            // World-space, and therefore already including the scale being solved for.
            Vector3 size = meshRenderer.bounds.size;

            // The label lies flat, turned 90 degrees about X, so the glyph's height runs along the
            // board (Z) and its width across it (X).
            if (size.x <= 0.0001f || size.z <= 0.0001f) return;

            float scale = Mathf.Min(targetHeight / size.z, maxWidth / size.x);
            transform.localScale *= scale;
            fitted = true;
        }

        private void HandleFontRebuilt(Font font)
        {
            if (text == null || font != text.font) return;

            Apply();
        }

        private void Apply()
        {
            if (text == null || meshRenderer == null) return;

            var font = text.font;
            if (font == null || font.material == null) return;

            // Fake-null aware: a domain reload clears this, and a play session with reload disabled
            // leaves a destroyed material behind, which compares equal to null.
            if (shared == null)
            {
                var authored = meshRenderer.sharedMaterial;
                if (authored == null) return;

                shared = new Material(authored) { name = "DeckText (runtime)" };
            }

            shared.mainTexture = font.material.mainTexture;
            meshRenderer.sharedMaterial = shared;
        }
    }
}
