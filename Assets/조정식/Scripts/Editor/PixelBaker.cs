using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace GN3.EditorTools
{
    /// <summary>
    /// 건물·장식 그림을 "1080p 화면에 실제로 그려지는 크기"로 미리 고품질 축소해 Baked 폴더에 굽고, 씬 스프라이트를 교체한다.
    /// 원본 PNG는 1000px 이상 AI 도트풍이라 화면에서 5~16배 축소되는데, 실시간 축소(밉맵)는 윤곽이 흐려졌다.
    /// 영역 평균으로 줄이고 약하게 샤픈해 1:1(Point)로 찍으면 윤곽과 디테일이 또렷하게 남는다.
    ///
    /// - 원본은 그대로 두고 Assets/조정식/Baked/{이름}@{폭}x{높이}.png 를 만든다.
    /// - 오브젝트 크기(scale)는 구운 그림에 반영하고 scale은 1(부호만 유지)로 바꾼다 → 월드 크기 동일.
    /// - 이미 구운 오브젝트도 원본을 다시 찾아 굽는다. 크기를 바꾸거나 새 장식을 놓은 뒤 메뉴를 다시 누르면 된다.
    /// </summary>
    public static class PixelBaker
    {
        internal const string BakedFolder = "Assets/조정식/Baked";
        private static readonly string[] SourceFolders = { "Assets/조정식/Buildings/", "Assets/조정식/Decorations/", "Assets/조정식/성벽/" };
        internal const int ReferenceScreenHeight = 1080;
        private const float SharpenAmount = 0.6f;

        [MenuItem("GN3/Pixel/고품질 축소본 굽기 (현재 씬)")]
        public static void BakeActiveScene()
        {
            var cam = Camera.main;
            if (cam == null || !cam.orthographic)
            {
                Debug.LogError("[PixelBaker] 직교(Orthographic) Main Camera가 필요합니다.");
                return;
            }
            float screenPixelsPerUnit = ReferenceScreenHeight / (cam.orthographicSize * 2f);
            EnsureFolder();

            int baked = 0;
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            foreach (var renderer in root.GetComponentsInChildren<SpriteRenderer>(true))
            {
                string sourcePath = FindSourcePath(renderer.sprite);
                if (sourcePath == null) continue;
                Bake(renderer, sourcePath, screenPixelsPerUnit);
                baked++;
            }

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log($"[PixelBaker] {baked}개 스프라이트를 {screenPixelsPerUnit:0.#}px/유닛 기준으로 구웠습니다.");
        }

        /// <summary>원본(Buildings/Decorations) 경로. 이미 구운 스프라이트면 이름으로 원본을 찾는다. 대상이 아니면 null.</summary>
        internal static string FindSourcePath(Sprite sprite)
        {
            if (sprite == null) return null;
            string path = AssetDatabase.GetAssetPath(sprite);
            foreach (var folder in SourceFolders)
                if (path.StartsWith(folder)) return path;

            if (!path.StartsWith(BakedFolder + "/")) return null;
            // Baked 바로 아래 파일만 구운 건물·장식이다. 하위 폴더(Animated, WindowGlow)는 다른 도구가 만든 것이라 건드리지 않는다.
            if (path.Substring(BakedFolder.Length + 1).Contains("/")) return null;
            string name = Path.GetFileNameWithoutExtension(path);
            int at = name.LastIndexOf('@');
            if (at > 0) name = name.Substring(0, at);
            foreach (var folder in SourceFolders)
            {
                string candidate = folder + name + ".png";
                if (File.Exists(candidate)) return candidate;
            }
            // 하위 폴더(Buildings/뒷골목 등)에 있는 원본. 바로 아래에서 못 찾으면 그 안까지 찾는다.
            foreach (var folder in SourceFolders)
            {
                if (!Directory.Exists(folder)) continue;
                string found = Directory.GetFiles(folder, name + ".png", SearchOption.AllDirectories).FirstOrDefault();
                if (found != null) return found.Replace('\\', '/');
            }
            return null;
        }

        internal static void Bake(SpriteRenderer renderer, string sourcePath, float screenPixelsPerUnit)
        {
            var source = new Texture2D(2, 2);
            source.LoadImage(File.ReadAllBytes(sourcePath));
            var sourceSprite = AssetDatabase.LoadAssetAtPath<Sprite>(sourcePath);

            // 지금 스프라이트의 월드 크기 × 현재 scale = 화면에 그려지는 크기
            // (구운 스프라이트는 scale 1로 두므로, 그 뒤에 크기를 바꿔도 다시 구우면 반영된다)
            var transform = renderer.transform;
            Vector3 scale = transform.lossyScale;
            Vector2 worldSize = renderer.sprite.bounds.size;
            worldSize = new Vector2(worldSize.x * Mathf.Abs(scale.x), worldSize.y * Mathf.Abs(scale.y));

            int width = Mathf.Max(1, Mathf.RoundToInt(worldSize.x * screenPixelsPerUnit));
            int height = Mathf.Max(1, Mathf.RoundToInt(worldSize.y * screenPixelsPerUnit));
            var pixels = Sharpen(AreaDownscale(source, width, height), width, height);
            Object.DestroyImmediate(source);

            string name = Path.GetFileNameWithoutExtension(sourcePath);
            string bakedPath = $"{BakedFolder}/{name}@{width}x{height}.png";
            var output = new Texture2D(width, height, TextureFormat.RGBA32, false);
            output.SetPixels(pixels);
            output.Apply();
            File.WriteAllBytes(bakedPath, output.EncodeToPNG());
            Object.DestroyImmediate(output);

            AssetDatabase.ImportAsset(bakedPath, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(bakedPath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.alphaIsTransparency = true;
            importer.spritePixelsPerUnit = screenPixelsPerUnit;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.Custom;
            settings.spritePivot = new Vector2(sourceSprite.pivot.x / sourceSprite.rect.width, sourceSprite.pivot.y / sourceSprite.rect.height);
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();

            Undo.RecordObject(renderer, "Bake Pixel Sprite");
            Undo.RecordObject(transform, "Bake Pixel Sprite");
            renderer.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(bakedPath);
            var local = transform.localScale;
            transform.localScale = new Vector3(Mathf.Sign(local.x), Mathf.Sign(local.y), local.z);

            // 그림자 모양을 새 스프라이트 실루엣으로 다시 만든다.
            var caster = renderer.GetComponent<ShadowCaster2D>();
            if (caster != null && caster.enabled)
            {
                caster.enabled = false;
                caster.enabled = true;
            }
        }

        /// <summary>영역 평균 축소(알파 가중). 큰 배율 축소에서 도트가 끊기지 않게 원본 픽셀을 빠짐없이 섞는다.</summary>
        internal static Color[] AreaDownscale(Texture2D source, int width, int height)
        {
            int sw = source.width, sh = source.height;
            var src = source.GetPixels();
            var result = new Color[width * height];
            float fx = (float)sw / width, fy = (float)sh / height;

            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float x0 = x * fx, x1 = (x + 1) * fx, y0 = y * fy, y1 = (y + 1) * fy;
                float r = 0, g = 0, b = 0, a = 0, total = 0;
                for (int sy = (int)y0; sy < Mathf.Min(Mathf.CeilToInt(y1), sh); sy++)
                {
                    float wy = Mathf.Min(y1, sy + 1) - Mathf.Max(y0, sy);
                    for (int sx = (int)x0; sx < Mathf.Min(Mathf.CeilToInt(x1), sw); sx++)
                    {
                        float w = (Mathf.Min(x1, sx + 1) - Mathf.Max(x0, sx)) * wy;
                        var c = src[sy * sw + sx];
                        r += c.r * c.a * w; g += c.g * c.a * w; b += c.b * c.a * w; a += c.a * w; total += w;
                    }
                }
                a /= total;
                result[y * width + x] = a > 0.0001f ? new Color(r / total / a, g / total / a, b / total / a, a) : Color.clear;
            }
            return result;
        }

        /// <summary>약한 언샤프 마스크로 윤곽선을 살리고, 알파는 0/1로 잘라 도트 가장자리를 딱 떨어지게 한다.</summary>
        internal static Color[] Sharpen(Color[] pixels, int width, int height)
        {
            var result = new Color[pixels.Length];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                var center = pixels[y * width + x];
                var blur = Color.clear;
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    var n = pixels[Mathf.Clamp(y + dy, 0, height - 1) * width + Mathf.Clamp(x + dx, 0, width - 1)];
                    blur += n.a < 0.05f ? center : n; // 투명 바깥은 섞지 않아 테두리에 후광이 생기지 않게
                }
                blur /= 9f;
                var s = center + (center - blur) * SharpenAmount;
                result[y * width + x] = new Color(Mathf.Clamp01(s.r), Mathf.Clamp01(s.g), Mathf.Clamp01(s.b), center.a < 0.5f ? 0f : 1f);
            }
            return result;
        }

        private static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder(BakedFolder))
                AssetDatabase.CreateFolder(Path.GetDirectoryName(BakedFolder).Replace('\\', '/'), Path.GetFileName(BakedFolder));
        }
    }
}
