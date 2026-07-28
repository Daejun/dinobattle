using UnityEditor;
using UnityEngine;

namespace DinoBattle.EditorTools
{
    /// <summary>
    /// A tiling brick texture, generated rather than downloaded.
    ///
    /// Same reasoning as the button icons and the app icon: an asset that can be rebuilt from source
    /// needs no attribution entry, no licence check and no binary in the repository, and it can be
    /// retuned by changing a number instead of finding the original file.
    ///
    /// Built to tile seamlessly on both axes, because <c>EnvironmentBrick</c> samples it in world
    /// space across slabs tens of units long — a seam would repeat every few metres down the whole
    /// board.
    /// </summary>
    public static class BrickTextureBuilder
    {
        public const string BrickTexturePath = "Assets/Art/Textures/Brick.png";

        private const int Size = 256;

        /// <summary>Courses down the tile. Must divide Size for the vertical wrap to be seamless.</summary>
        private const int Rows = 8;

        /// <summary>Bricks across a course. Must divide Size for the horizontal wrap to be seamless.</summary>
        private const int Columns = 4;

        private const int MortarPixels = 4;

        [MenuItem("Dino Battle/Advanced/Rebuild Brick Texture", priority = 224)]
        public static void Rebuild()
        {
            var texture = Generate();

            SampleContentBuilder.EnsureFolder("Assets/Art");
            SampleContentBuilder.EnsureFolder("Assets/Art/Textures");
            System.IO.File.WriteAllBytes(BrickTexturePath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(BrickTexturePath, ImportAssetOptions.ForceUpdate);

            // Repeat wrapping is the entire point — clamped, the world-space sampling in the shader
            // would stretch one edge pixel across the whole board.
            var importer = (TextureImporter)AssetImporter.GetAtPath(BrickTexturePath);
            if (importer != null)
            {
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.filterMode = FilterMode.Bilinear;
                importer.mipmapEnabled = true;
                importer.textureCompression = TextureImporterCompression.CompressedLQ;
                importer.maxTextureSize = 256;
                importer.SaveAndReimport();
            }

            Debug.Log($"[BrickTextureBuilder] Wrote {BrickTexturePath} ({Size}x{Size}, " +
                      $"{Rows} courses of {Columns}).");
        }

        private static Texture2D Generate()
        {
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, true);
            var pixels = new Color32[Size * Size];

            int rowHeight = Size / Rows;
            int brickWidth = Size / Columns;

            var mortar = new Color(0.42f, 0.41f, 0.38f);
            var brickDark = new Color(0.38f, 0.20f, 0.16f);
            var brickLight = new Color(0.56f, 0.32f, 0.24f);

            // Deterministic: the same texture every rebuild, so a regenerated asset is a no-op diff
            // rather than noise in every commit that happens to touch the builder.
            var random = new System.Random(20260728);

            for (int y = 0; y < Size; y++)
            {
                int row = y / rowHeight;

                // Offset every other course by half a brick. Because Columns divides Size evenly, a
                // half-brick shift still wraps cleanly at the tile edge.
                int offset = (row % 2 == 0) ? 0 : brickWidth / 2;

                for (int x = 0; x < Size; x++)
                {
                    int localY = y - row * rowHeight;
                    int shifted = (x + offset) % Size;
                    int localX = shifted % brickWidth;

                    bool isMortar = localY < MortarPixels || localX < MortarPixels;

                    Color color;
                    if (isMortar)
                    {
                        color = mortar;
                    }
                    else
                    {
                        // One tone per brick, not per pixel, so each block reads as a solid unit.
                        // Seeded off the block index so it is stable across the tile.
                        int block = row * Columns + shifted / brickWidth;
                        var blockRandom = new System.Random(block * 7919 + 13);
                        color = Color.Lerp(brickDark, brickLight, (float)blockRandom.NextDouble());
                    }

                    // A little per-pixel grain so large flat slabs do not band.
                    float grain = 1f + ((float)random.NextDouble() - 0.5f) * 0.06f;
                    pixels[y * Size + x] = new Color(color.r * grain, color.g * grain, color.b * grain, 1f);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }
    }
}
