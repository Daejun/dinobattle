using System;
using UnityEditor;
using UnityEngine;

namespace DinoBattle.EditorTools
{
    /// <summary>
    /// Draws the HUD button icons as textures, in code.
    ///
    /// From a four-year-old's playtest: "글씨야. 나 글씨 못 읽어. 그림도 없어." Every button is Korean
    /// text, and the tester picked one by pressing the leftmost because it was the biggest. A button
    /// a non-reader cannot identify is a button they press at random, which is exactly what happened
    /// — including pressing the one that quits.
    ///
    /// Drawn rather than downloaded. These are silhouettes at 128px on a phone, an icon set is a
    /// licence and an attribution entry each, and the whole project already generates its art from
    /// code. Shapes are built from polygons and circles so they stay crisp at any size and diff as
    /// numbers rather than as binary.
    ///
    /// The shapes chosen are the ones a four-year-old already knows from other apps — a play
    /// triangle, a circular arrow, a cross — plus dinosaur and spider silhouettes for the things
    /// that are specific to this game.
    ///
    /// Menu: Dino Battle > 7. Generate Button Icons
    /// </summary>
    public static class ButtonIconBuilder
    {
        private const int Size = 128;
        private const string Folder = "Assets/Art/UI";

        public const string AutoFill = "icon_autofill";
        public const string Boss = "icon_boss";
        public const string Start = "icon_start";
        public const string Replay = "icon_replay";
        public const string Quit = "icon_quit";
        public const string Shuffle = "icon_shuffle";

        [MenuItem("Dino Battle/7. Generate Button Icons", priority = 132)]
        public static void Generate()
        {
            SampleContentBuilder.EnsureFolder("Assets/Art");
            SampleContentBuilder.EnsureFolder(Folder);

            Write(AutoFill, DrawAutoFill);
            Write(Boss, DrawBoss);
            Write(Start, DrawStart);
            Write(Replay, DrawReplay);
            Write(Quit, DrawQuit);
            Write(Shuffle, DrawShuffle);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[ButtonIconBuilder] Wrote 6 icons into {Folder}.");
        }

        private static void Write(string iconName, Action<Canvas> draw)
        {
            var canvas = new Canvas(Size);
            draw(canvas);

            string path = $"{Folder}/{iconName}.png";
            System.IO.File.WriteAllBytes(path, canvas.ToTexture().EncodeToPNG());
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            // Sprite, point-filtered, no compression: these are flat silhouettes and any block
            // compression puts coloured fringes around the edges at this size.
            if (AssetImporter.GetAtPath(path) is not TextureImporter importer) return;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }

        // ------------------------------------------------------------------ the icons

        /// <summary>Three dinosaurs: "fill the field for me".</summary>
        private static void DrawAutoFill(Canvas c)
        {
            c.Theropod(0.30f, 0.42f, 0.40f);
            c.Theropod(0.62f, 0.38f, 0.32f);
            c.Theropod(0.46f, 0.70f, 0.26f);
        }

        /// <summary>One big thing against a small one: the boss fight, stated as a size difference.</summary>
        private static void DrawBoss(Canvas c)
        {
            c.Theropod(0.40f, 0.50f, 0.78f);
            c.Theropod(0.83f, 0.30f, 0.24f);
        }

        /// <summary>A play triangle. Understood by anyone who has ever seen a video.</summary>
        private static void DrawStart(Canvas c)
        {
            c.Polygon(new[]
            {
                new Vector2(0.30f, 0.16f), new Vector2(0.30f, 0.84f), new Vector2(0.84f, 0.50f),
            });
        }

        /// <summary>A circular arrow: do it again.</summary>
        private static void DrawReplay(Canvas c)
        {
            c.Arc(0.5f, 0.5f, 0.30f, 0.10f, 55f, 340f);
            c.Polygon(new[]
            {
                new Vector2(0.60f, 0.90f), new Vector2(0.86f, 0.78f), new Vector2(0.60f, 0.62f),
            });
        }

        /// <summary>A cross. The one shape that already means "stop" to a small child.</summary>
        private static void DrawQuit(Canvas c)
        {
            c.Bar(0.5f, 0.5f, 0.52f, 0.13f, 45f);
            c.Bar(0.5f, 0.5f, 0.52f, 0.13f, -45f);
        }

        /// <summary>Two crossing arrows: give me a different set.</summary>
        private static void DrawShuffle(Canvas c)
        {
            c.Bar(0.5f, 0.5f, 0.62f, 0.10f, 28f);
            c.Bar(0.5f, 0.5f, 0.62f, 0.10f, -28f);
            c.Polygon(new[]
            {
                new Vector2(0.72f, 0.80f), new Vector2(0.92f, 0.70f), new Vector2(0.72f, 0.60f),
            });
            c.Polygon(new[]
            {
                new Vector2(0.72f, 0.40f), new Vector2(0.92f, 0.30f), new Vector2(0.72f, 0.20f),
            });
        }

        // ------------------------------------------------------------------ drawing

        /// <summary>
        /// A tiny software rasteriser. Coordinates are 0-1 across the icon so the shapes are written
        /// in proportions rather than pixels, and the size constant can change without redrawing.
        /// </summary>
        private sealed class Canvas
        {
            private readonly int size;
            private readonly float[] coverage;

            public Canvas(int size)
            {
                this.size = size;
                coverage = new float[size * size];
            }

            /// <summary>A side-on theropod: body, neck, head, tail, two legs.</summary>
            public void Theropod(float x, float y, float scale)
            {
                float s = scale;
                Ellipse(x, y, 0.26f * s, 0.17f * s);                                   // body
                Ellipse(x - 0.26f * s, y + 0.13f * s, 0.09f * s, 0.09f * s);            // head
                Bar(x - 0.15f * s, y + 0.09f * s, 0.20f * s, 0.09f * s, 35f);           // neck
                Polygon(new[]                                                            // tail
                {
                    new Vector2(x + 0.18f * s, y + 0.07f * s),
                    new Vector2(x + 0.52f * s, y + 0.16f * s),
                    new Vector2(x + 0.18f * s, y - 0.06f * s),
                });
                Bar(x - 0.04f * s, y - 0.20f * s, 0.22f * s, 0.075f * s, 78f);          // near leg
                Bar(x + 0.10f * s, y - 0.20f * s, 0.22f * s, 0.075f * s, 100f);         // far leg
            }

            public void Ellipse(float cx, float cy, float rx, float ry)
            {
                For((u, v) =>
                {
                    float dx = (u - cx) / Mathf.Max(1e-4f, rx);
                    float dy = (v - cy) / Mathf.Max(1e-4f, ry);
                    return dx * dx + dy * dy <= 1f;
                });
            }

            /// <summary>A rounded bar of <paramref name="length"/> at <paramref name="degrees"/>.</summary>
            public void Bar(float cx, float cy, float length, float thickness, float degrees)
            {
                float rad = degrees * Mathf.Deg2Rad;
                Vector2 dir = new(Mathf.Cos(rad), Mathf.Sin(rad));
                Vector2 a = new Vector2(cx, cy) - dir * (length * 0.5f);
                Vector2 b = new Vector2(cx, cy) + dir * (length * 0.5f);
                Capsule(a, b, thickness * 0.5f);
            }

            public void Capsule(Vector2 a, Vector2 b, float radius)
            {
                Vector2 ab = b - a;
                float lengthSquared = Mathf.Max(1e-6f, ab.sqrMagnitude);

                For((u, v) =>
                {
                    Vector2 p = new(u, v);
                    float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / lengthSquared);
                    return (p - (a + ab * t)).sqrMagnitude <= radius * radius;
                });
            }

            /// <summary>A ring segment, for the replay arrow.</summary>
            public void Arc(float cx, float cy, float radius, float thickness, float fromDeg, float toDeg)
            {
                For((u, v) =>
                {
                    float dx = u - cx, dy = v - cy;
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);
                    if (Mathf.Abs(distance - radius) > thickness * 0.5f) return false;

                    float angle = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
                    if (angle < 0f) angle += 360f;
                    return angle >= fromDeg && angle <= toDeg;
                });
            }

            /// <summary>Convex polygon fill, by half-plane test.</summary>
            public void Polygon(Vector2[] points)
            {
                For((u, v) =>
                {
                    Vector2 p = new(u, v);
                    bool positive = false, negative = false;

                    for (int i = 0; i < points.Length; i++)
                    {
                        Vector2 a = points[i];
                        Vector2 b = points[(i + 1) % points.Length];
                        float cross = (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);
                        if (cross > 0f) positive = true;
                        if (cross < 0f) negative = true;
                        if (positive && negative) return false;
                    }

                    return true;
                });
            }

            /// <summary>
            /// Rasterise a shape with 3x3 supersampling.
            ///
            /// Silhouettes at this size have long diagonal edges — a theropod's tail, the cross —
            /// and hard-edged sampling turns those into visible staircases on a phone screen.
            /// </summary>
            private void For(Func<float, float, bool> inside)
            {
                const int Samples = 3;

                for (int py = 0; py < size; py++)
                for (int px = 0; px < size; px++)
                {
                    float hits = 0f;

                    for (int sy = 0; sy < Samples; sy++)
                    for (int sx = 0; sx < Samples; sx++)
                    {
                        float u = (px + (sx + 0.5f) / Samples) / size;
                        float v = (py + (sy + 0.5f) / Samples) / size;
                        if (inside(u, v)) hits++;
                    }

                    float value = hits / (Samples * Samples);
                    int index = py * size + px;
                    if (value > coverage[index]) coverage[index] = value;
                }
            }

            /// <summary>White silhouette on transparent, so the UI can tint it per button.</summary>
            public Texture2D ToTexture()
            {
                var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
                var pixels = new Color32[size * size];

                for (int i = 0; i < pixels.Length; i++)
                {
                    byte alpha = (byte)Mathf.RoundToInt(Mathf.Clamp01(coverage[i]) * 255f);
                    pixels[i] = new Color32(255, 255, 255, alpha);
                }

                texture.SetPixels32(pixels);
                texture.Apply();
                return texture;
            }
        }
    }
}
