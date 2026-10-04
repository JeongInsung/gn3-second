using System.IO;
using GN3.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GN3.EditorTools
{
    /// <summary>
    /// 선택한 건물을 마우스로 고를 수 있게 만든다: 실루엣 콜라이더 + SelectableBuilding + 흰 테두리 자식.
    /// 테두리는 셰이더로 매 프레임 계산하지 않고, 건물 그림의 알파를 몇 px 넓힌 뒤 원래 모양을 빼서 "바깥 테두리만" 그린
    /// 그림을 미리 굽는다. 조명을 받지 않는 Sprite-Unlit 머티리얼이라 밤에도 선명하다.
    /// 건물 그림을 다시 굽거나 바꾸면 이 메뉴를 다시 누르면 된다.
    /// </summary>
    public static class SelectableBuildingBuilder
    {
        private const string OutlineFolder = PixelBaker.BakedFolder + "/Outline";
        private const string OutlineChildName = "Outline";
        private const string UnlitMaterialPath = "Packages/com.unity.render-pipelines.universal/Runtime/Materials/Sprite-Unlit-Default.mat";
        private const int Thickness = 2; // 화면 px (구운 그림은 1080p에서 1텍셀 = 1px)

        [MenuItem("GN3/Interaction/선택한 건물을 클릭 가능하게")]
        public static void MakeSelectedClickable()
        {
            var target = Selection.activeGameObject;
            var renderer = target != null ? target.GetComponent<SpriteRenderer>() : null;
            if (renderer == null || renderer.sprite == null)
            {
                Debug.LogError("[SelectableBuilding] 씬에서 건물 오브젝트(SpriteRenderer)를 하나 선택하세요.");
                return;
            }
            Apply(renderer, null);
        }

        [MenuItem("GN3/Interaction/선택한 건물을 클릭 가능하게", true)]
        private static bool CanMakeSelectedClickable() =>
            Selection.activeGameObject != null && Selection.activeGameObject.GetComponent<SpriteRenderer>() != null;

        internal static SelectableBuilding Apply(SpriteRenderer building, string displayName)
        {
            var go = building.gameObject;
            if (go.GetComponent<Collider2D>() == null) Undo.AddComponent<PolygonCollider2D>(go);

            var selectable = go.GetComponent<SelectableBuilding>() ?? Undo.AddComponent<SelectableBuilding>(go);
            if (!string.IsNullOrEmpty(displayName))
            {
                var so = new SerializedObject(selectable);
                so.FindProperty("displayName").stringValue = displayName;
                so.ApplyModifiedProperties();
            }

            var outlineSprite = BakeOutline(building.sprite);
            var existing = go.transform.Find(OutlineChildName);
            if (existing != null) Undo.DestroyObjectImmediate(existing.gameObject);
            var outlineObject = new GameObject(OutlineChildName, typeof(SpriteRenderer));
            Undo.RegisterCreatedObjectUndo(outlineObject, "Building Outline");
            outlineObject.transform.SetParent(go.transform, false);

            var outline = outlineObject.GetComponent<SpriteRenderer>();
            outline.sprite = outlineSprite;
            outline.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(UnlitMaterialPath);
            outline.sortingLayerID = building.sortingLayerID;
            outline.sortingOrder = building.sortingOrder + 2; // 건물·창 불빛 위(테두리는 그림 바깥에만 있어 건물을 가리지 않는다)
            outline.enabled = false;

            Undo.RecordObject(selectable, "Building Outline");
            selectable.SetOutline(outline);
            EditorUtility.SetDirty(selectable);
            EditorSceneManager.MarkSceneDirty(go.scene);
            Debug.Log($"[SelectableBuilding] '{go.name}'을(를) 클릭 가능하게 만들었습니다.");
            return selectable;
        }

        /// <summary>건물 그림 바깥으로 Thickness px 두께의 흰 테두리만 있는 그림. 테두리가 잘리지 않게 사방을 넓혀 굽는다.</summary>
        private static Sprite BakeOutline(Sprite sprite)
        {
            var source = new Texture2D(2, 2);
            source.LoadImage(File.ReadAllBytes(AssetDatabase.GetAssetPath(sprite)));
            int sw = source.width, sh = source.height;
            var src = source.GetPixels();
            Object.DestroyImmediate(source);

            int pad = Thickness, w = sw + pad * 2, h = sh + pad * 2;
            var result = new Color[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int sx = x - pad, sy = y - pad;
                if (Opaque(src, sw, sh, sx, sy)) continue; // 원래 그림 자리는 비운다
                bool near = false;
                for (int dy = -Thickness; dy <= Thickness && !near; dy++)
                for (int dx = -Thickness; dx <= Thickness && !near; dx++)
                    if (dx * dx + dy * dy <= Thickness * Thickness + 1 && Opaque(src, sw, sh, sx + dx, sy + dy)) near = true;
                if (near) result[y * w + x] = Color.white;
            }

            SheetAnimationBuilder.EnsureFolder(OutlineFolder);
            string name = Path.GetFileNameWithoutExtension(PixelBaker.FindSourcePath(sprite) ?? AssetDatabase.GetAssetPath(sprite));
            string path = $"{OutlineFolder}/{name}.png";
            var texture = new Texture2D(w, h, TextureFormat.RGBA32, false);
            texture.SetPixels(result);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.alphaIsTransparency = true;
            importer.spritePixelsPerUnit = sprite.pixelsPerUnit;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.Custom;
            // 넓힌 만큼 pivot을 옮겨 건물과 정확히 겹치게 한다.
            settings.spritePivot = new Vector2((sprite.pivot.x + pad) / w, (sprite.pivot.y + pad) / h);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static bool Opaque(Color[] pixels, int width, int height, int x, int y) =>
            x >= 0 && y >= 0 && x < width && y < height && pixels[y * width + x].a >= 0.5f;
    }
}
