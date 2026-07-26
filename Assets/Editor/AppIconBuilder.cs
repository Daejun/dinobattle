using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace DinoBattle.EditorTools
{
    /// <summary>
    /// The Android launcher icon: the game's own T-Rex, photographed.
    ///
    /// Rendered from the real creature prefab rather than drawn by hand. The button icons in
    /// ButtonIconBuilder are hand-rolled polygons because they are 128px pictograms that only have
    /// to say "play" or "quit"; a launcher icon is the game's face, and a hand-drawn dinosaur would
    /// promise an animal the game does not contain. Pointing a camera at the actual model means the
    /// icon updates itself whenever the creature's colours or proportions change.
    ///
    /// Three sets, because Android wants all three:
    ///   Adaptive (API 26+) — two layers, and the launcher masks them to whatever shape it likes.
    ///                        Everything important has to sit inside the middle two thirds.
    ///   Round / Legacy     — one flat layer, already composited over the background.
    ///
    /// Menu: Dino Battle > 8. Generate App Icon
    /// </summary>
    public static class AppIconBuilder
    {
        private const string Folder = "Assets/Art/UI/AppIcon";
        private const string Prefab = "Assets/Prefabs/Creatures/Creature_TRex.prefab";

        /// <summary>Rendered once at the largest size Android asks for, then downscaled per slot.</summary>
        private const int Master = 512;

        /// <summary>
        /// How much of the frame the head fills on the ADAPTIVE foreground.
        ///
        /// Small, and that is not a mistake. An adaptive icon is masked to a circle, a squircle or a
        /// rounded square depending on the launcher, and only the central 66% is guaranteed to
        /// survive. A head sized to fill the canvas loses its snout on a round mask.
        /// </summary>
        private const float AdaptiveFill = 0.55f;

        /// <summary>Legacy icons are not masked, so the head can be bigger.</summary>
        private const float LegacyFill = 0.78f;

        /// <summary>
        /// Deep jungle green, much darker than the arena.
        ///
        /// The creature is bright yellow-green, so the backplate has to go a long way down to give it
        /// any separation — at the arena's own green the icon was green on green and turned to mush
        /// at the 48px size Android asks for.
        /// </summary>
        private static readonly Color Background = new(0.07f, 0.15f, 0.11f);

        [MenuItem("Dino Battle/8. Generate App Icon", priority = 133)]
        public static void Generate()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab);
            if (prefab == null)
            {
                Debug.LogError($"[AppIconBuilder] {Prefab} missing — run 'Dino Battle > 1. Generate Sample Content'.");
                return;
            }

            SampleContentBuilder.EnsureFolder("Assets/Art");
            SampleContentBuilder.EnsureFolder("Assets/Art/UI");
            SampleContentBuilder.EnsureFolder(Folder);

            Texture2D head = RenderHead(prefab, AdaptiveFill, transparent: true);
            Texture2D headLarge = RenderHead(prefab, LegacyFill, transparent: true);

            var foreground = Save(head, "icon_fg");
            var background = Save(Flat(Background), "icon_bg");
            var composited = Save(Composite(headLarge, Background), "icon_legacy");

            Apply(foreground, background, composited);

            Object.DestroyImmediate(head);
            Object.DestroyImmediate(headLarge);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[AppIconBuilder] App icon set from the T-Rex model (adaptive, round, legacy).");
        }

        /// <summary>
        /// Point a camera at the creature's head bone and photograph it against nothing.
        ///
        /// Three-quarter view rather than straight on. Head-on, a theropod skull is a narrow wedge
        /// and reads as an indistinct blob at 48px; turned, the snout and jawline give it the
        /// profile that makes it a dinosaur.
        /// </summary>
        private static Texture2D RenderHead(GameObject prefab, float fill, bool transparent)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.transform.position = Vector3.zero;
            instance.transform.rotation = Quaternion.identity;
            instance.hideFlags = HideFlags.HideAndDontSave;

            // The team ring and health bar are gameplay furniture, not part of the animal.
            foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer is SkinnedMeshRenderer) continue;
                renderer.enabled = false;
            }

            Transform head = FindBone(instance.transform, "Head");
            Transform neck = FindBone(instance.transform, "Neck");

            // Everything onto a spare layer, and the camera restricted to it.
            //
            // Without this the render picks up whatever scene happens to be open — the first attempt
            // came out as a dinosaur standing in the arena, complete with ground, hills and fog,
            // because a camera with no culling mask sees the world it was created in and "solid
            // colour, alpha zero" only clears what nothing else has drawn over.
            const int IconLayer = 31;
            foreach (var transform in instance.GetComponentsInChildren<Transform>(true))
                transform.gameObject.layer = IconLayer;

            // Size the shot from the head itself. Deriving it from the whole creature's bounds framed
            // the entire animal, which is a dinosaur photograph rather than a face.
            float headLength = head != null && neck != null
                ? Vector3.Distance(head.position, neck.position)
                : 0.6f;
            // Deliberately generous. The exact zoom does not matter because the render is re-framed
            // from its own pixels afterwards — see Fit. Two attempts at deriving it from bone
            // positions both missed, first cropping the snout and then showing half the body, for
            // the same reason: the gap between the Head and Neck joints says very little about how
            // much space the skull occupies on screen.
            float headRadius = Mathf.Max(0.25f, headLength * 2.6f);
            Vector3 focus = head != null ? head.position : instance.transform.position + Vector3.up * 2f;

            // Aim slightly ahead of the head bone, which sits at the back of the skull near the jaw
            // hinge — centring on it alone pushes the snout to the edge of the frame.
            if (head != null) focus += head.forward * headLength * 0.35f;

            var cameraObject = new GameObject("IconCamera") { hideFlags = HideFlags.HideAndDontSave };
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = transparent ? CameraClearFlags.SolidColor : CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            camera.orthographic = true;
            camera.orthographicSize = headRadius;
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 100f;
            camera.cullingMask = 1 << IconLayer;

            // Looking down the creature's front-left, slightly above the eyeline.
            Vector3 direction = (instance.transform.forward * -1.6f + instance.transform.right * -1.0f
                                 + Vector3.up * 0.42f).normalized;
            cameraObject.transform.position = focus + direction * 10f;
            cameraObject.transform.LookAt(focus);

            // Its own light: the scene's sun is not loaded when this runs from the menu.
            var lightObject = new GameObject("IconLight") { hideFlags = HideFlags.HideAndDontSave };
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.25f;
            light.color = new Color(1f, 0.97f, 0.88f);
            lightObject.transform.rotation = Quaternion.LookRotation(
                Quaternion.Euler(28f, 35f, 0f) * Vector3.forward);

            var previousAmbientMode = RenderSettings.ambientMode;
            var previousAmbient = RenderSettings.ambientLight;
            bool previousFog = RenderSettings.fog;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.45f, 0.48f, 0.44f);
            RenderSettings.fog = false;      // fog would wash the icon out exactly as it does the arena

            var target = new RenderTexture(Master, Master, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 8,
            };
            camera.targetTexture = target;
            camera.Render();

            var previousActive = RenderTexture.active;
            RenderTexture.active = target;
            var texture = new Texture2D(Master, Master, TextureFormat.RGBA32, false);
            texture.ReadPixels(new Rect(0, 0, Master, Master), 0, 0);
            texture.Apply();
            RenderTexture.active = previousActive;

            camera.targetTexture = null;
            target.Release();
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(cameraObject);
            Object.DestroyImmediate(lightObject);
            Object.DestroyImmediate(instance);

            RenderSettings.ambientMode = previousAmbientMode;
            RenderSettings.ambientLight = previousAmbient;
            RenderSettings.fog = previousFog;

            var framed = Fit(texture, fill);
            Object.DestroyImmediate(texture);
            return framed;
        }

        /// <summary>
        /// Re-frame a render on what it actually drew: crop to the opaque pixels, then centre that
        /// crop so its longest side is <paramref name="fill"/> of the canvas.
        ///
        /// Doing this in pixels rather than by positioning the camera is what finally made the
        /// framing reliable. Aiming at the head bone centres the JOINT, not the visible silhouette,
        /// and a theropod's head sits at the top-left of a mass that continues down into the neck and
        /// chest — so the subject kept landing off-centre no matter what the zoom was. Half the
        /// silhouette fell outside an adaptive icon's safe circle at two quite different zoom levels,
        /// which is the signal that the zoom was never the problem.
        /// </summary>
        private static Texture2D Fit(Texture2D source, float fill)
        {
            var pixels = source.GetPixels32();
            int size = source.width;

            int minX = size, minY = size, maxX = -1, maxY = -1;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                if (pixels[y * size + x].a <= 8) continue;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }

            // Nothing rendered — hand back a copy rather than dividing by zero.
            if (maxX < minX || maxY < minY) return Object.Instantiate(source);

            int cropWidth = maxX - minX + 1;
            int cropHeight = maxY - minY + 1;
            float scale = size * fill / Mathf.Max(cropWidth, cropHeight);

            var result = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var blank = new Color32[size * size];
            result.SetPixels32(blank);

            int targetWidth = Mathf.Max(1, Mathf.RoundToInt(cropWidth * scale));
            int targetHeight = Mathf.Max(1, Mathf.RoundToInt(cropHeight * scale));
            int offsetX = (size - targetWidth) / 2;
            int offsetY = (size - targetHeight) / 2;

            for (int y = 0; y < targetHeight; y++)
            for (int x = 0; x < targetWidth; x++)
            {
                // Point sampling from the crop. The source is 512px of an already anti-aliased
                // render being shrunk, so the edges stay soft without a filter kernel.
                int sourceX = minX + Mathf.Min(cropWidth - 1, Mathf.FloorToInt(x / scale));
                int sourceY = minY + Mathf.Min(cropHeight - 1, Mathf.FloorToInt(y / scale));
                result.SetPixel(offsetX + x, offsetY + y, pixels[sourceY * size + sourceX]);
            }

            result.Apply();
            return result;
        }

        private static Transform FindBone(Transform root, string boneName)
        {
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                if (transform.name == boneName) return transform;

            return null;
        }

        private static Texture2D Flat(Color color)
        {
            var texture = new Texture2D(Master, Master, TextureFormat.RGBA32, false);
            var pixels = new Color32[Master * Master];
            Color32 packed = color;
            for (int i = 0; i < pixels.Length; i++) pixels[i] = packed;
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        /// <summary>Flatten the transparent head over a solid colour, for the unmasked icon kinds.</summary>
        private static Texture2D Composite(Texture2D head, Color background)
        {
            var result = new Texture2D(Master, Master, TextureFormat.RGBA32, false);
            var top = head.GetPixels();

            for (int i = 0; i < top.Length; i++)
            {
                float a = top[i].a;
                top[i] = new Color(
                    Mathf.Lerp(background.r, top[i].r, a),
                    Mathf.Lerp(background.g, top[i].g, a),
                    Mathf.Lerp(background.b, top[i].b, a),
                    1f);
            }

            result.SetPixels(top);
            result.Apply();
            return result;
        }

        private static Texture2D Save(Texture2D texture, string fileName)
        {
            string path = $"{Folder}/{fileName}.png";
            File.WriteAllBytes(path, texture.EncodeToPNG());
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                // Readable and uncompressed: PlayerSettings copies the pixels at build time, and a
                // compressed or unreadable source is silently rejected.
                importer.textureType = TextureImporterType.Default;
                importer.isReadable = true;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.maxTextureSize = 512;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static void Apply(Texture2D foreground, Texture2D background, Texture2D composited)
        {
            var android = NamedBuildTarget.Android;

            foreach (var kind in PlayerSettings.GetSupportedIconKinds(android))
            {
                var icons = PlayerSettings.GetPlatformIcons(android, kind);

                foreach (var icon in icons)
                {
                    // Adaptive takes two layers, foreground over background. The others take one
                    // already-flattened image, because nothing composites them for us.
                    if (icon.maxLayerCount >= 2) icon.SetTextures(foreground, background);
                    else icon.SetTextures(composited);
                }

                PlayerSettings.SetPlatformIcons(android, kind, icons);
            }
        }
    }
}
