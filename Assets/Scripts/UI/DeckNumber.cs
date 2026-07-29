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
        private static Material shared;

        private TextMesh text;
        private MeshRenderer meshRenderer;

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
