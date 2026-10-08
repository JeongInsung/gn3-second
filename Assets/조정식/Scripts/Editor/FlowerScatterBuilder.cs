using System.Collections.Generic;
using System.IO;
using System.Linq;
using GN3.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

namespace GN3.EditorTools
{
    /// <summary>
    /// 꽃무더기 12종(Assets/조정식/Decorations/꽃무더기_01~12)을 바닥 잔디 칸에 무작위로 흩뿌린다.
    /// 잔디 칸 = 타일 그림의 90% 이상이 초록 픽셀인 칸(돌길·흙길·경계석 타일 제외). 꽃 그림이 칸보다 커서
    /// 주변 8칸도 전부 잔디인 칸만 쓰고, Village 안 건물·나무·성벽 등 다른 그림과 겹치는 칸, 바닥 가장자리 칸은 뺀다.
    /// 그룹은 Village 아래 "꽃무더기"로 만들고 그 부분만 Village 프리팹에 Apply해 MainScene에도 나온다.
    /// 다시 누르면 비우고 새 seed로 다시 뿌린다(seed는 로그에 남는다).
    /// </summary>
    public static class FlowerScatterBuilder
    {
        private const string SpritePathFormat = "Assets/조정식/Decorations/꽃무더기_{0:00}.png";
        private const int Kinds = 12;
        private const float FlowerPixelsPerUnit = 2800f; // 덤불 폭 0.08~0.12 유닛(처음 700에서 사용자 요청으로 1/4로 줄임)
        private const string GroupName = "꽃무더기";
        private const string VillageName = "Village";
        private const string AmbienceName = "Ambience";
        private const string ShadowMaterialPath = "Assets/조정식/Shaders/ProjectedShadow.mat";
        private const int Count = 54;               // 바닥 전체 기준(바닥이 화면보다 넓어 시작 화면엔 일부만 보인다)
        private const float MinSpacing = 0.5f;      // 꽃끼리 최소 거리(월드 유닛)
        private const float Jitter = 0.18f;         // 칸 가운데에서 흔드는 범위(셀 0.5의 절반 미만)
        private const float ObstacleMargin = 0.15f; // 다른 그림 경계에서 띄울 거리
        private const float MaxObstacleArea = 20f;  // 이보다 넓은 그림은 오버레이로 보고 장애물에서 뺀다
        private const float GrassRatio = 0.9f;      // 타일 그림 중 초록 픽셀 비율이 이 이상이면 잔디
        private const float ShadowHeightScale = 0.4f; // 낮은 덤불이라 그림 높이의 일부만 실제 높이
        private const int FlowerOrder = 1;          // 다른 장식과 같은 order(같은 order끼리는 발밑 높이로 앞뒤)

        [MenuItem("GN3/Village/꽃무더기 뿌리기 (잔디)")]
        public static void Build()
        {
            if (VillagePrefabBuilder.RefuseInMainScene("꽃무더기 뿌리기")) return;

            var scene = SceneManager.GetActiveScene();
            var floors = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Tilemap>(true))
                .Where(t => t.transform.parent != null && t.transform.parent.name == "Floor Grid").ToList();
            if (floors.Count == 0)
            {
                Debug.LogError("[FlowerScatter] 'Floor Grid' 아래 Tilemap(바닥)을 찾지 못했습니다.");
                return;
            }
            var sprites = LoadSprites();
            if (sprites == null) return;

            var village = scene.GetRootGameObjects().FirstOrDefault(r => r.name == VillageName);
            var group = ResetGroup(scene, village);
            var obstacles = CollectObstacles(scene, group);
            var candidates = GrassCandidates(floors, obstacles, out int grassCells);

            int seed = System.Environment.TickCount & 0x7fffffff;
            var random = new System.Random(seed);
            Shuffle(candidates, random);

            var material = AssetDatabase.LoadAssetAtPath<Material>(ShadowMaterialPath);
            var placedPositions = new List<Vector2>();
            var placed = new List<SpriteRenderer>();
            foreach (var center in candidates)
            {
                if (placed.Count >= Count) break;
                var pos = center + new Vector2(Range(random, -Jitter, Jitter), Range(random, -Jitter, Jitter));
                if (placedPositions.Any(p => (p - pos).sqrMagnitude < MinSpacing * MinSpacing)) continue;

                var sprite = sprites[random.Next(sprites.Length)];
                var renderer = Place(group, sprite, pos, random.Next(2) == 0);
                placed.Add(renderer);
                placedPositions.Add(pos);
            }

            Bake(placed);
            if (material != null)
                foreach (var renderer in placed) AddShadow(renderer, material);

            if (village != null) WallLayoutBuilder.ApplyGroupToPrefab(village, group);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[FlowerScatter] 잔디 {grassCells}칸 중 후보 {candidates.Count}칸에 꽃무더기 {placed.Count}개를 놓았습니다. (seed {seed})");
        }

        private static float Range(System.Random random, float min, float max) => min + (float)random.NextDouble() * (max - min);

        private static void Shuffle<T>(List<T> list, System.Random random)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        /// <summary>12장을 불러온다. 처음 들어온 그림은 PPU를 FlowerPixelsPerUnit으로 맞춰 다시 임포트한다.</summary>
        private static Sprite[] LoadSprites()
        {
            var sprites = new Sprite[Kinds];
            for (int i = 0; i < Kinds; i++)
            {
                string path = string.Format(SpritePathFormat, i + 1);
                if (AssetImporter.GetAtPath(path) is TextureImporter importer && !Mathf.Approximately(importer.spritePixelsPerUnit, FlowerPixelsPerUnit))
                {
                    importer.spritePixelsPerUnit = FlowerPixelsPerUnit;
                    importer.SaveAndReimport();
                }
                sprites[i] = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sprites[i] == null)
                {
                    Debug.LogError($"[FlowerScatter] {path} 스프라이트가 없습니다.");
                    return null;
                }
            }
            return sprites;
        }

        /// <summary>꽃무더기 그룹을 비워서 다시 만든다(WallLayoutBuilder.ResetGroup과 같은 규칙).</summary>
        private static GameObject ResetGroup(Scene scene, GameObject village)
        {
            foreach (var root in scene.GetRootGameObjects())
                if (root.name == GroupName) Object.DestroyImmediate(root);

            var existing = village != null ? village.transform.Find(GroupName) : null;
            if (existing != null)
            {
                // 이미 프리팹에 들어간 그룹은 인스턴스에서 지울 수 없어 프리팹 원본에서 지운다.
                if (PrefabUtility.IsPartOfPrefabInstance(existing) && !PrefabUtility.IsAddedGameObjectOverride(existing.gameObject))
                    RemoveGroupFromPrefab(village);
                else
                    Object.DestroyImmediate(existing.gameObject);
            }

            var group = new GameObject(GroupName);
            if (village != null) group.transform.SetParent(village.transform, false);
            else SceneManager.MoveGameObjectToScene(group, scene);
            return group;
        }

        private static void RemoveGroupFromPrefab(GameObject village)
        {
            string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(village);
            var contents = PrefabUtility.LoadPrefabContents(path);
            var old = contents.transform.Find(GroupName);
            if (old != null)
            {
                Object.DestroyImmediate(old.gameObject);
                PrefabUtility.SaveAsPrefabAsset(contents, path);
            }
            PrefabUtility.UnloadPrefabContents(contents);
        }

        /// <summary>바닥 말고 씬에 그려지는 그림(건물·나무·성벽·장식)의 영역. 그림자·분위기 효과·꽃무더기 그룹은 뺀다.</summary>
        private static List<Bounds> CollectObstacles(Scene scene, GameObject group)
        {
            var result = new List<Bounds>();
            foreach (var root in scene.GetRootGameObjects())
            foreach (var renderer in root.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (renderer.sprite == null || renderer.GetComponent<ProjectedShadow>() != null) continue;
                var t = renderer.transform;
                if (t.IsChildOf(group.transform)) continue;
                if (t.GetComponentsInParent<Transform>(true).Any(p => p.name == AmbienceName)) continue;
                var bounds = renderer.bounds;
                // 화면을 덮는 오버레이류(밤 어둠 등)는 장애물이 아니다. 가장 큰 건물도 약 2.9×2.6 유닛.
                if (bounds.size.x * bounds.size.y > MaxObstacleArea) continue;
                bounds.Expand(ObstacleMargin * 2f);
                result.Add(bounds);
            }
            return result;
        }

        /// <summary>꽃을 놓을 수 있는 잔디 칸 가운데(월드). 주변 8칸까지 잔디이고 가장자리·다른 그림과 겹치지 않는 칸.</summary>
        private static List<Vector2> GrassCandidates(List<Tilemap> floors, List<Bounds> obstacles, out int grassCells)
        {
            var cells = new Dictionary<Vector2Int, TileBase>();
            // 바닥이 바깥(-3)·광장(-2)·흙길 칠하기 층(-1)으로 겹친다. 아래 층부터 덮어써 화면 맨 위에 보이는 타일로 판정한다.
            foreach (var tilemap in floors.OrderBy(t => t.GetComponent<TilemapRenderer>().sortingOrder))
            foreach (var p in tilemap.cellBounds.allPositionsWithin)
            {
                var tile = tilemap.GetTile(p);
                if (tile != null) cells[new Vector2Int(p.x, p.y)] = tile;
            }

            var grassCache = new Dictionary<TileBase, bool>();
            bool IsGrass(Vector2Int c)
            {
                if (!cells.TryGetValue(c, out var tile)) return false; // 바닥 밖(가장자리 바깥)도 아님으로
                if (!grassCache.TryGetValue(tile, out bool grass)) grassCache[tile] = grass = IsGrassTile(tile);
                return grass;
            }

            grassCells = cells.Keys.Count(IsGrass);
            var grid = floors[0];
            Vector2 cellSize = grid.layoutGrid.cellSize;
            var result = new List<Vector2>();
            foreach (var c in cells.Keys)
            {
                bool allGrass = true;
                for (int dy = -1; dy <= 1 && allGrass; dy++)
                for (int dx = -1; dx <= 1 && allGrass; dx++)
                    allGrass = IsGrass(c + new Vector2Int(dx, dy));
                if (!allGrass) continue;

                Vector2 center = (Vector2)grid.CellToWorld(new Vector3Int(c.x, c.y, 0)) + cellSize * 0.5f;
                var probe = new Bounds(center, cellSize);
                if (obstacles.Any(b => b.Intersects(probe))) continue;
                result.Add(center);
            }
            return result;
        }

        /// <summary>타일 스프라이트 영역에서 초록 픽셀(g가 r·b보다 뚜렷이 큼) 비율이 GrassRatio 이상이면 잔디.</summary>
        private static bool IsGrassTile(TileBase tileBase)
        {
            if (tileBase is not Tile tile || tile.sprite == null) return false;
            string path = AssetDatabase.GetAssetPath(tile.sprite.texture);
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;

            var texture = new Texture2D(2, 2);
            texture.LoadImage(File.ReadAllBytes(path)); // 원본 PNG(임포트 텍스처는 읽기 불가일 수 있다)
            var rect = tile.sprite.rect;
            var pixels = texture.GetPixels32();
            int green = 0, total = 0;
            for (int y = (int)rect.yMin; y < (int)rect.yMax; y++)
            for (int x = (int)rect.xMin; x < (int)rect.xMax; x++)
            {
                var p = pixels[y * texture.width + x];
                total++;
                if (p.g > p.r + 10 && p.g > p.b + 10) green++;
            }
            Object.DestroyImmediate(texture);
            return total > 0 && green >= total * GrassRatio;
        }

        /// <summary>발밑 가운데(pos)에 SortingGroup 부모를 두고 그 위에 그림을 세운다(WallLayoutBuilder.Place와 같은 구조).</summary>
        private static SpriteRenderer Place(GameObject group, Sprite sprite, Vector2 pos, bool flip)
        {
            var anchor = new GameObject(sprite.name, typeof(SortingGroup));
            anchor.transform.SetParent(group.transform, false);
            anchor.transform.position = new Vector3(pos.x, pos.y, 0f);
            anchor.GetComponent<SortingGroup>().sortingOrder = FlowerOrder;

            var art = new GameObject(sprite.name, typeof(SpriteRenderer));
            art.transform.SetParent(anchor.transform, false);
            var renderer = art.GetComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.flipX = flip;
            renderer.sortingOrder = FlowerOrder;
            art.transform.localPosition = new Vector3(-sprite.bounds.center.x, sprite.bounds.extents.y - sprite.bounds.center.y, 0f);
            return renderer;
        }

        /// <summary>1080p 화면 크기로 굽는다(같은 그림은 한 번만 굽고 나머지는 결과를 같이 쓴다).</summary>
        private static void Bake(List<SpriteRenderer> renderers)
        {
            var cam = Camera.main;
            if (cam == null || !cam.orthographic) return;
            float screenPixelsPerUnit = PixelBaker.ReferenceScreenHeight / (cam.orthographicSize * 2f);
            foreach (var bySprite in renderers.GroupBy(r => r.sprite))
            {
                var first = bySprite.First();
                PixelBaker.Bake(first, PixelBaker.FindSourcePath(first.sprite), screenPixelsPerUnit);
                foreach (var other in bySprite.Skip(1)) other.sprite = first.sprite;
            }
        }

        private static void AddShadow(SpriteRenderer owner, Material material)
        {
            var shadowObject = new GameObject("Shadow", typeof(SpriteRenderer), typeof(ProjectedShadow));
            shadowObject.transform.SetParent(owner.transform, false);
            var renderer = shadowObject.GetComponent<SpriteRenderer>();
            renderer.sharedMaterial = material;
            renderer.sprite = owner.sprite;
            var shadow = shadowObject.GetComponent<ProjectedShadow>();
            shadow.heightScale = ShadowHeightScale;
            shadow.liftHeight = 0f;
        }
    }
}
