using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Tilemaps;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

namespace GN3.EditorTools
{
    /// <summary>
    /// Assets/조정식/Tiles 아래 타일셋 PNG(128x128 타일 격자)를 Sprite(Multiple)로 잘라 임포트하고,
    /// 메뉴 한 번으로 Tile 에셋 / Tile Palette / DesignScene 바닥 Tilemap 을 만들어 둔다.
    ///
    /// 사용법: GN3/Tilemap/TILE 1 바닥 세팅 → Tile Palette 창에서 "TILE 1 Palette" 선택,
    /// Active Tilemap 을 "Floor" 로 두고 브러시(B)로 칠한다. 다시 실행해도 중복 생성되지 않는다.
    /// </summary>
    public class FloorTileSetup : AssetPostprocessor
    {
        private const string RootFolder = "Assets/조정식/Tiles";
        private const string TileSetName = "TILE 1";
        private const string TileSetFolder = RootFolder + "/" + TileSetName;
        private const string TexturePath = TileSetFolder + "/" + TileSetName + ".png";
        private const string TileAssetFolder = TileSetFolder + "/Tiles";
        private const string PalettePath = TileSetFolder + "/" + TileSetName + " Palette.prefab";
        private const string ScenePath = "Assets/조정식/DesignScene.unity";
        private const string GridObjectName = "Floor Grid";
        private const string TilemapObjectName = "Floor";
        private const int TileSize = 128;
        private const int PixelsPerUnit = 256; // 1칸 = 0.5유닛
        private const float CellSize = (float)TileSize / PixelsPerUnit; // 스프라이트 크기와 같아야 칸 사이 틈이 없다
        private const int FloorSortingOrder = -100; // 캐릭터 파츠(0부터)보다 뒤
        private const int SeamFixPixels = 4;        // 타일에 구워진 테두리 그림자 두께(실측 3~4px) - OnPostprocessTexture 참고
        private const int SeamFixThreshold = 10;    // 이만큼 어두운 변만 보정(이미 매끄러운 타일셋은 안 건드림)

        private static bool IsTileAsset(string path) =>
            path.Replace('\\', '/').StartsWith(RootFolder + "/");

        private void OnPreprocessTexture()
        {
            if (!IsTileAsset(assetPath) || !assetPath.EndsWith(".png")) return;

            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.spritePixelsPerUnit = PixelsPerUnit;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;

            if (!TryReadPngSize(assetPath, out int width, out int height))
            {
                Debug.LogWarning($"[FloorTileSetup] PNG 크기를 읽지 못해 슬라이스를 건너뜀: {assetPath}");
                return;
            }

            int cols = Mathf.Max(1, width / TileSize);
            int rows = Mathf.Max(1, height / TileSize);
            string baseName = Path.GetFileNameWithoutExtension(assetPath);

            var rects = new List<SpriteRect>(cols * rows);
            int index = 0;
            for (int row = 0; row < rows; row++)
            {
                for (int col = 0; col < cols; col++)
                {
                    // 텍스처 좌표는 좌하단 기준이라 위쪽 행부터 순서를 매기려면 y를 뒤집는다.
                    rects.Add(new SpriteRect
                    {
                        name = $"{baseName}_{index:00}",
                        spriteID = GUID.Generate(),
                        rect = new Rect(col * TileSize, height - (row + 1) * TileSize, TileSize, TileSize),
                        alignment = SpriteAlignment.Center,
                        pivot = new Vector2(0.5f, 0.5f),
                    });
                    index++;
                }
            }

            var factory = new SpriteDataProviderFactories();
            factory.Init();
            var provider = factory.GetSpriteEditorDataProviderFromObject(importer);
            provider.InitSpriteEditorDataProvider();
            provider.SetSpriteRects(rects.ToArray());
            provider.Apply();
        }

        /// <summary>
        /// 타일마다 바깥쪽에 구워져 있는 테두리 그림자를 지운다. 이 타일셋은 칸 가장자리 3~4px이 안쪽보다
        /// 훨씬 어둡게 칠해져 있어(실측: 풀 타일 가장자리 밝기 27~94 vs 안쪽 134) 칸을 이어 붙이면
        /// 경계마다 검은 격자선이 생겨 바닥이 "끊겨" 보였다.
        /// 바깥 SeamFixPixels 줄을 안쪽 텍스처의 거울상으로 덮어 자연스럽게 잇는다
        /// (한두 줄만 늘리면 그라데이션이 남고, 밝기 보정은 밝은 선/얼룩이 생겨서 이 방식을 골랐다).
        /// 원본 PNG는 건드리지 않고 임포트된 텍스처만 고치므로, 되돌리려면 이 메서드만 지우면 된다.
        /// </summary>
        private void OnPostprocessTexture(Texture2D texture)
        {
            if (!IsTileAsset(assetPath) || !assetPath.EndsWith(".png")) return;
            if (texture.width < TileSize || texture.height < TileSize) return;

            var pixels = texture.GetPixels32();
            bool changed = false;
            for (int ty = 0; ty + TileSize <= texture.height; ty += TileSize)
                for (int tx = 0; tx + TileSize <= texture.width; tx += TileSize)
                    changed |= FixTileSeam(pixels, texture.width, tx, ty);

            if (!changed) return;
            texture.SetPixels32(pixels);
            texture.Apply(false);
        }

        /// <summary>타일 한 칸의 네 변을 검사해 어두운 변만 안쪽 거울상으로 덮는다. 고친 변이 있으면 true.</summary>
        private static bool FixTileSeam(Color32[] pixels, int stride, int ox, int oy)
        {
            int k = SeamFixPixels;
            float inner = AverageLuma(pixels, stride, ox + k, oy + k, TileSize - 2 * k, TileSize - 2 * k);
            bool changed = false;

            // 아래/위: 행 단위. 이미 이음새가 없는 타일셋을 나중에 넣어도 손상되지 않도록 어두운 변만 고친다.
            if (AverageLuma(pixels, stride, ox, oy, TileSize, k) < inner - SeamFixThreshold)
            {
                for (int i = 0; i < k; i++)
                    CopyRow(pixels, stride, ox, oy + 2 * k - 1 - i, oy + i);
                changed = true;
            }
            if (AverageLuma(pixels, stride, ox, oy + TileSize - k, TileSize, k) < inner - SeamFixThreshold)
            {
                for (int i = 0; i < k; i++)
                    CopyRow(pixels, stride, ox, oy + TileSize - 2 * k + i, oy + TileSize - 1 - i);
                changed = true;
            }
            // 좌/우: 열 단위. 모서리는 위에서 행을 덮은 결과를 다시 쓰므로 두 방향이 모두 반영된다.
            if (AverageLuma(pixels, stride, ox, oy, k, TileSize) < inner - SeamFixThreshold)
            {
                for (int i = 0; i < k; i++)
                    CopyColumn(pixels, stride, oy, ox + 2 * k - 1 - i, ox + i);
                changed = true;
            }
            if (AverageLuma(pixels, stride, ox + TileSize - k, oy, k, TileSize) < inner - SeamFixThreshold)
            {
                for (int i = 0; i < k; i++)
                    CopyColumn(pixels, stride, oy, ox + TileSize - 2 * k + i, ox + TileSize - 1 - i);
                changed = true;
            }
            return changed;
        }

        private static float AverageLuma(Color32[] pixels, int stride, int x, int y, int w, int h)
        {
            double sum = 0;
            for (int j = 0; j < h; j++)
                for (int i = 0; i < w; i++)
                {
                    var c = pixels[(y + j) * stride + x + i];
                    sum += 0.299 * c.r + 0.587 * c.g + 0.114 * c.b;
                }
            return (float)(sum / (w * h));
        }

        private static void CopyRow(Color32[] pixels, int stride, int x, int fromY, int toY)
        {
            System.Array.Copy(pixels, fromY * stride + x, pixels, toY * stride + x, TileSize);
        }

        private static void CopyColumn(Color32[] pixels, int stride, int y, int fromX, int toX)
        {
            for (int j = 0; j < TileSize; j++)
                pixels[(y + j) * stride + toX] = pixels[(y + j) * stride + fromX];
        }

        [MenuItem("GN3/Tilemap/TILE 1 바닥 세팅")]
        public static void Setup()
        {
            if (!File.Exists(Path.GetFullPath(TexturePath)))
            {
                Debug.LogError($"[FloorTileSetup] 타일셋 이미지가 없습니다: {TexturePath}");
                return;
            }

            // 이 스크립트가 컴파일되기 전에 들어온 PNG일 수 있으니 전처리를 다시 태운다.
            AssetDatabase.ImportAsset(TexturePath, ImportAssetOptions.ForceUpdate);

            var tiles = CreateTiles(out int cols, out int rows);
            if (tiles.Count == 0)
            {
                Debug.LogError($"[FloorTileSetup] {TexturePath} 에서 스프라이트를 찾지 못했습니다.");
                return;
            }

            CreatePalette(tiles, cols, rows);
            EnsureSceneTilemap();

            AssetDatabase.SaveAssets();
            Debug.Log($"[FloorTileSetup] 완료: 타일 {tiles.Count}개, 팔레트 {PalettePath}, {ScenePath} 의 {GridObjectName}/{TilemapObjectName}");

            if (!Application.isBatchMode)
                EditorApplication.ExecuteMenuItem("Window/2D/Tile Palette");
        }

        /// <summary>스프라이트마다 Tile 에셋을 만든다(있으면 스프라이트만 갱신). 반환 순서 = 위쪽 행부터.</summary>
        private static List<Tile> CreateTiles(out int cols, out int rows)
        {
            TryReadPngSize(TexturePath, out int width, out int height);
            cols = Mathf.Max(1, width / TileSize);
            rows = Mathf.Max(1, height / TileSize);

            var sprites = new Dictionary<string, Sprite>();
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(TexturePath))
                if (asset is Sprite sprite) sprites[sprite.name] = sprite;

            EnsureFolder(TileAssetFolder);

            var tiles = new List<Tile>();
            for (int i = 0; i < cols * rows; i++)
            {
                string name = $"{TileSetName}_{i:00}";
                if (!sprites.TryGetValue(name, out var sprite)) continue;

                string tilePath = $"{TileAssetFolder}/{name}.asset";
                var tile = AssetDatabase.LoadAssetAtPath<Tile>(tilePath);
                if (tile == null)
                {
                    tile = ScriptableObject.CreateInstance<Tile>();
                    tile.sprite = sprite;
                    AssetDatabase.CreateAsset(tile, tilePath);
                }
                else if (tile.sprite != sprite)
                {
                    tile.sprite = sprite;
                    EditorUtility.SetDirty(tile);
                }
                tiles.Add(tile);
            }
            return tiles;
        }

        /// <summary>원본 이미지와 같은 배치의 Tile Palette 프리팹을 만든다(매번 새로 써서 타일 변경을 반영).</summary>
        private static void CreatePalette(List<Tile> tiles, int cols, int rows)
        {
            var cellSize = new Vector3(CellSize, CellSize, 0f);

            // Tile Palette 창은 GridPalette 서브에셋이 있는 프리팹만 팔레트로 인식한다. 직접 만든 GameObject를
            // PrefabUtility.SaveAsPrefabAsset으로 저장하면 그 서브에셋이 날아가고, AddObjectToAsset으로 다시 붙여도
            // 프리팹에는 남지 않아 팔레트 목록에 아예 안 떴다(실측). 그래서 공식 API로 팔레트를 만들고,
            // 타일은 프리팹 에셋의 Tilemap에 직접 찍는다(프리팹을 다시 쓰지 않으므로 서브에셋이 보존된다).
            if (!HasGridPalette(PalettePath))
            {
                if (AssetDatabase.LoadAssetAtPath<GameObject>(PalettePath) != null)
                    AssetDatabase.DeleteAsset(PalettePath);

                // Automatic은 스프라이트 크기에서 셀 크기를 다시 계산해 Floor Grid(0.5)와 어긋날 수 있어 Manual로 고정한다.
                GridPaletteUtility.CreateNewPalette(TileSetFolder, Path.GetFileNameWithoutExtension(PalettePath),
                    GridLayout.CellLayout.Rectangle, GridPalette.CellSizing.Manual, cellSize, GridLayout.CellSwizzle.XYZ);
            }

            var paletteRoot = AssetDatabase.LoadAssetAtPath<GameObject>(PalettePath);
            if (paletteRoot == null)
            {
                Debug.LogError($"[FloorTileSetup] 팔레트를 만들지 못했습니다: {PalettePath}");
                return;
            }

            var paletteGrid = paletteRoot.GetComponent<Grid>();
            if (paletteGrid != null && paletteGrid.cellSize != cellSize)
            {
                paletteGrid.cellSize = cellSize;
                EditorUtility.SetDirty(paletteGrid);
            }

            var tilemap = paletteRoot.GetComponentInChildren<Tilemap>(true);
            if (tilemap == null)
            {
                Debug.LogError($"[FloorTileSetup] 팔레트 안에 Tilemap이 없습니다: {PalettePath}");
                return;
            }

            // 타일 수가 줄었을 때 옛 칸이 남지 않도록 비우고 다시 찍는다(원본 이미지와 같은 배치).
            foreach (var cell in tilemap.cellBounds.allPositionsWithin)
                tilemap.SetTile(cell, null);

            for (int i = 0; i < tiles.Count; i++)
            {
                int col = i % cols, row = i / cols;
                tilemap.SetTile(new Vector3Int(col, rows - 1 - row, 0), tiles[i]);
            }

            EditorUtility.SetDirty(tilemap);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(PalettePath, ImportAssetOptions.ForceUpdate);
        }

        /// <summary>프리팹 안에 GridPalette 서브에셋이 실제로 들어 있는지 확인한다.</summary>
        private static bool HasGridPalette(string path)
        {
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                if (asset is GridPalette) return true;
            return false;
        }

        /// <summary>DesignScene 에 Floor Grid / Floor Tilemap 이 없으면 추가하고 저장한다. 칠한 타일은 건드리지 않는다.</summary>
        private static void EnsureSceneTilemap()
        {
            var scene = SceneManager.GetSceneByPath(ScenePath);
            bool openedHere = !scene.isLoaded;
            if (openedHere) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);

            GameObject gridObject = null;
            foreach (var go in scene.GetRootGameObjects())
                if (go.name == GridObjectName) gridObject = go;

            bool changed = false;
            if (gridObject == null)
            {
                gridObject = new GameObject(GridObjectName);
                SceneManager.MoveGameObjectToScene(gridObject, scene);
                gridObject.AddComponent<Grid>();
                changed = true;
            }

            // 타일 크기를 바꾼 뒤 다시 실행하면 기존 Grid도 맞춘다. 칠한 타일은 셀 좌표 그대로 따라온다.
            var grid = gridObject.GetComponent<Grid>();
            var cellSize = new Vector3(CellSize, CellSize, 0f);
            if (grid.cellSize != cellSize)
            {
                Undo.RecordObject(grid, "Floor cell size");
                grid.cellSize = cellSize;
                EditorUtility.SetDirty(grid);
                changed = true;
            }

            var floor = gridObject.transform.Find(TilemapObjectName);
            if (floor == null)
            {
                var floorObject = new GameObject(TilemapObjectName);
                floorObject.transform.SetParent(gridObject.transform, false);
                floorObject.AddComponent<Tilemap>();
                floorObject.AddComponent<TilemapRenderer>().sortingOrder = FloorSortingOrder;
                floor = floorObject.transform;
                changed = true;
            }

            if (RemoveStrayGrid(scene, floor.GetComponent<Tilemap>())) changed = true;

            if (changed) EditorSceneManager.SaveScene(scene);
            if (openedHere) EditorSceneManager.CloseScene(scene, true);
        }

        /// <summary>
        /// Tile Palette 창에서 "Create New Tilemap"으로 생긴 루트 "Grid"(셀 1, sortingOrder 0)를 지운다.
        /// 거기 칠한 타일은 같은 셀 좌표로 Floor 에 옮긴다(Floor 에 이미 있는 셀은 유지).
        /// </summary>
        private static bool RemoveStrayGrid(Scene scene, Tilemap floor)
        {
            bool removed = false;
            foreach (var go in scene.GetRootGameObjects())
            {
                if (go.name != "Grid" || go.GetComponent<Grid>() == null) continue;

                int moved = 0;
                Undo.RecordObject(floor, "Move stray tiles to Floor");
                foreach (var source in go.GetComponentsInChildren<Tilemap>(true))
                {
                    foreach (var cell in source.cellBounds.allPositionsWithin)
                    {
                        var tile = source.GetTile(cell);
                        if (tile == null || floor.HasTile(cell)) continue;
                        floor.SetTile(cell, tile);
                        moved++;
                    }
                }

                Undo.DestroyObjectImmediate(go);
                removed = true;
                Debug.Log($"[FloorTileSetup] 중복 Grid 삭제, 타일 {moved}개를 {TilemapObjectName} 로 옮김");
            }
            return removed;
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }

        /// <summary>PNG IHDR 청크(오프셋 16~23)에서 폭/높이를 읽는다. 텍스처 임포트 전이라 Texture2D를 쓸 수 없다.</summary>
        private static bool TryReadPngSize(string path, out int width, out int height)
        {
            width = height = 0;
            try
            {
                using var stream = File.OpenRead(path);
                var header = new byte[24];
                if (stream.Read(header, 0, 24) < 24) return false;
                if (header[0] != 0x89 || header[1] != 'P' || header[2] != 'N' || header[3] != 'G') return false;

                width = (header[16] << 24) | (header[17] << 16) | (header[18] << 8) | header[19];
                height = (header[20] << 24) | (header[21] << 16) | (header[22] << 8) | header[23];
                return width > 0 && height > 0;
            }
            catch
            {
                return false;
            }
        }
    }
}
