using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

namespace GN3.EditorTools
{
    /// <summary>
    /// 성벽 조각(Assets/조정식/성벽)으로 활성 씬 바닥 타일(Floor Grid)의 가장자리를 둘러싼다.
    /// 벽은 바닥 바깥쪽에 가장자리에 딱 붙여 놓는다(위 변 = 벽 아래면이 가장자리, 아래 변 = 벽 윗면이 가장자리, 좌우 = 벽 안쪽 면이 가장자리).
    /// 아래 두 모서리는 꺾인 조각, 위 두 모서리는 탑(세트에 위쪽 꺾임이 없다), 길이 바닥 밖으로 나가는 곳은
    /// 위·아래 변 = 성문, 좌우 변 = 막힌 성벽(SideExits를 켜면 길 폭만큼 비우고 양옆에 탑).
    ///
    /// 조각마다 발밑(아래 가운데)에 SortingGroup 부모를 두어 그림 높이가 달라도 발밑 높이로 앞뒤가 정해지게 한다
    /// (스프라이트 피벗이 가운데라 그대로 두면 키 큰 탑·성문이 옆 벽 뒤로 숨었다). 위 변은 탑·성문이 벽 앞, 아래 변은 벽이 성문 앞·모서리 탑 뒤, 좌우 변은 세로벽·탑·모서리를 같은 order로 둬 발밑 높이(아래일수록 앞)로 정한다.
    /// 바닥 타일을 바꾼 뒤 다시 누르면 새 가장자리에 맞춰 다시 놓는다. 마지막에 성벽 조각만 1080p 크기로 구워 선명하게 한다.
    /// 그룹은 Village 아래에 만들고 그 부분만 Village 프리팹에 Apply해 MainScene에도 나온다. 아래 성문에는 구워 둔 열림 프레임(CastleGate)을 붙인다.
    /// </summary>
    public static class WallLayoutBuilder
    {
        private const string Folder = "Assets/조정식/성벽/";
        private const string GroupName = "성벽";
        private const string VillageName = "Village";
        private const int WallOrder = 1;
        private const int FeatureOrder = 2;    // 탑·성문·모서리·좌우 세로벽(같은 order끼리는 발밑이 낮을수록 앞)
        private const int BottomWallOrder = 3; // 아래 변은 벽이 성문 위로(위 변은 기둥이 벽 위로)
        private const int BottomCornerOrder = 4; // 아래 변 모서리 탑은 가로벽 앞
        private const float TowerFootOverlap = 0.3f; // 아래에서 올라와 탑에 닿는 세로벽이 탑 발밑(받침)을 덮는 높이

        // 조각 그림에서 잰 값(원본 px). 원본 PPU로 나눠 월드 크기로 쓴다.
        private const float VerticalFrontPx = 120f;    // 세로벽 아래쪽 돌 정면 높이 → 위로 쌓을 때 이만큼 겹쳐 아래 조각이 덮게
        private const float HorizontalPostPx = 40f;    // 가로벽 양끝 기둥 폭 → 옆으로 이을 때 이만큼 겹침
        private const float CornerArmPx = 125f;        // 꺾인 모서리 조각의 세로 팔 두께(바깥 끝에서)
        private const float CornerTopPx = 130f;        // 모서리 조각 세로 팔 위쪽 중 세로벽이 겹쳐 올라탈 수 있는 부분
        private const float Gap = 0.15f;               // 길 양옆 탑과 길 사이 여유
        private static readonly bool SideExits = false;         // 좌우 변은 길이 닿아도 출구 없이 막는다(true면 길 폭만큼 비우고 양옆에 탑)

        private class Pieces
        {
            public Sprite Horizontal, Vertical, CornerBottomLeft, CornerBottomRight, Gate, Tower;
        }

        [MenuItem("GN3/Village/성벽 둘러치기 (바닥 가장자리)")]
        public static void Build()
        {
            var scene = SceneManager.GetActiveScene();
            var floors = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Tilemap>(true))
                .Where(t => t.transform.parent != null && t.transform.parent.name == "Floor Grid").ToList();
            if (floors.Count == 0)
            {
                Debug.LogError("[WallLayout] 'Floor Grid' 아래 Tilemap(바닥)을 찾지 못했습니다.");
                return;
            }
            var pieces = LoadPieces();
            if (pieces == null) return;

            var floor = FloorRect(floors, out var roads);
            var village = scene.GetRootGameObjects().FirstOrDefault(r => r.name == VillageName);
            var group = ResetGroup(scene, village);
            var placed = new List<SpriteRenderer>();

            float ppu = pieces.Vertical.pixelsPerUnit;
            float columnWidth = pieces.Vertical.bounds.size.x;
            float wallHeight = pieces.Horizontal.bounds.size.y;
            float bottomBase = floor.yMin - wallHeight;   // 아래 변: 벽 윗면 = 바닥 아래 가장자리
            float topBase = floor.yMax;                   // 위 변: 벽 아래면 = 바닥 위 가장자리
            float leftColumnX = floor.xMin - columnWidth * 0.5f;
            float rightColumnX = floor.xMax + columnWidth * 0.5f;

            // 아래 모서리: 세로 팔의 안쪽 면이 바닥 옆 가장자리에 오게
            float cornerWidth = pieces.CornerBottomLeft.bounds.size.x;
            float arm = CornerArmPx / ppu;
            float cornerLeftX = floor.xMin - arm + cornerWidth * 0.5f;
            float cornerRightX = floor.xMax + arm - cornerWidth * 0.5f;
            placed.Add(Place(group, pieces.CornerBottomLeft, cornerLeftX, bottomBase, BottomCornerOrder));
            placed.Add(Place(group, pieces.CornerBottomRight, cornerRightX, bottomBase, BottomCornerOrder));
            float cornerTop = bottomBase + pieces.CornerBottomLeft.bounds.size.y;

            // 위 모서리: 탑을 좌우 벽 줄 가운데에
            float towerHalf = pieces.Tower.bounds.size.x * 0.5f;
            placed.Add(Place(group, pieces.Tower, leftColumnX, topBase, FeatureOrder));
            placed.Add(Place(group, pieces.Tower, rightColumnX, topBase, FeatureOrder));

            // 위·아래 변: 가운데 길에 성문, 나머지는 가로벽
            float gateHalf = pieces.Gate.bounds.size.x * 0.5f;
            float overlap = HorizontalPostPx / ppu;
            float topGateX = roads.Top ?? floor.center.x;
            float bottomGateX = roads.Bottom ?? floor.center.x;
            placed.Add(Place(group, pieces.Gate, topGateX, topBase, FeatureOrder));
            var bottomGate = Place(group, pieces.Gate, bottomGateX, bottomBase, FeatureOrder);
            placed.Add(bottomGate);
            FillRow(group, pieces.Horizontal, leftColumnX + towerHalf - overlap, topGateX - gateHalf + overlap, topBase, overlap, WallOrder, placed);
            FillRow(group, pieces.Horizontal, topGateX + gateHalf - overlap, rightColumnX - towerHalf + overlap, topBase, overlap, WallOrder, placed);
            FillRow(group, pieces.Horizontal, cornerLeftX + cornerWidth * 0.5f - overlap, bottomGateX - gateHalf + overlap, bottomBase, overlap, BottomWallOrder, placed);
            FillRow(group, pieces.Horizontal, bottomGateX + gateHalf - overlap, cornerRightX - cornerWidth * 0.5f + overlap, bottomBase, overlap, BottomWallOrder, placed);

            // 좌·우 변: 모서리 위에서 위 탑 밑까지 세로벽, 길이 나가는 곳은 비우고 양옆에 탑
            float front = VerticalFrontPx / ppu;
            float columnStart = cornerTop - CornerTopPx / ppu - front; // 첫 세로벽의 앞면이 모서리 조각 뒤로 숨게
            float towerHeight = pieces.Tower.bounds.size.y;
            // 아래에서 올라와 탑에 닿는 세로벽은 탑 발밑 조금 위에서 끝낸다 → 발밑이 탑보다 낮아 벽이 앞, 탑이 뒤.
            // 탑 꼭대기까지 올려 두면 앞에 그려질 때 탑 가운데를 벽 띠가 세로로 가린다.
            float endBelowTower = TowerFootOverlap - pieces.Vertical.bounds.size.y; // 탑 발밑 기준, 마지막 세로벽 발밑 위치
            foreach (var (x, road) in new[] { (leftColumnX, roads.Left), (rightColumnX, roads.Right) })
            {
                if (SideExits && road.HasValue)
                {
                    var (roadMin, roadMax) = road.Value;
                    float lowerTowerBase = roadMin - Gap - towerHeight; // 탑 꼭대기가 길 아래 가장자리 밑 → 길이 가려지지 않는다
                    float upperTowerBase = roadMax + Gap;
                    placed.Add(Place(group, pieces.Tower, x, lowerTowerBase, FeatureOrder));
                    placed.Add(Place(group, pieces.Tower, x, upperTowerBase, FeatureOrder));
                    FillColumn(group, pieces.Vertical, x, columnStart, lowerTowerBase + endBelowTower, front, placed);
                    FillColumn(group, pieces.Vertical, x, upperTowerBase + front, topBase + endBelowTower, front, placed);
                }
                else
                {
                    FillColumn(group, pieces.Vertical, x, columnStart, topBase + endBelowTower, front, placed);
                }
            }

            BakeWalls(placed);
            GateAnimationBuilder.AttachBakedFrames(bottomGate); // 성문 열림 프레임을 구워 둔 적이 있으면 아래 성문에 다시 붙인다
            if (village != null) ApplyGroupToPrefab(village, group);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[WallLayout] 바닥 {floor.xMin}~{floor.xMax} × {floor.yMin}~{floor.yMax} 가장자리에 성벽 {placed.Count}조각을 놓았습니다. " +
                      $"(길: 위 {Fmt(roads.Top)} / 아래 {Fmt(roads.Bottom)} / 왼쪽 {Fmt(roads.Left)} / 오른쪽 {Fmt(roads.Right)})");
        }

        private static string Fmt(float? v) => v.HasValue ? v.Value.ToString("0.##") : "없음";
        private static string Fmt((float, float)? v) => v.HasValue ? $"{v.Value.Item1:0.##}~{v.Value.Item2:0.##}" : "없음";

        private static Pieces LoadPieces()
        {
            Sprite Load(string name)
            {
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(Folder + name + ".png");
                if (sprite == null) Debug.LogError($"[WallLayout] {Folder}{name}.png 스프라이트가 없습니다.");
                return sprite;
            }
            var pieces = new Pieces
            {
                Horizontal = Load("성벽_가로"),
                Vertical = Load("성벽_세로"),
                CornerBottomLeft = Load("성벽_모서리_왼쪽아래"),
                CornerBottomRight = Load("성벽_모서리_오른쪽아래"),
                Gate = Load("성벽_성문"),
                Tower = Load("성벽_탑"),
            };
            bool ok = pieces.Horizontal && pieces.Vertical && pieces.CornerBottomLeft && pieces.CornerBottomRight && pieces.Gate && pieces.Tower;
            return ok ? pieces : null;
        }

        private struct Roads
        {
            public float? Top, Bottom;                 // 길 가운데 x
            public (float, float)? Left, Right;        // 길 y 범위
        }

        /// <summary>바닥 Tilemap들의 칸을 합친 월드 사각형. 가장자리 줄에서 가장 흔한 타일(풀)과 다른 타일이 이어진 곳을 길로 본다.</summary>
        private static Rect FloorRect(List<Tilemap> floors, out Roads roads)
        {
            var cells = new Dictionary<Vector2Int, TileBase>();
            foreach (var tilemap in floors)
            foreach (var p in tilemap.cellBounds.allPositionsWithin)
            {
                var tile = tilemap.GetTile(p);
                if (tile != null) cells[new Vector2Int(p.x, p.y)] = tile;
            }
            int xMin = cells.Keys.Min(c => c.x), xMax = cells.Keys.Max(c => c.x);
            int yMin = cells.Keys.Min(c => c.y), yMax = cells.Keys.Max(c => c.y);
            var grass = cells.Values.GroupBy(t => t).OrderByDescending(g => g.Count()).First().Key;

            var grid = floors[0];
            Vector2 cell = grid.layoutGrid.cellSize;
            Vector2 origin = grid.CellToWorld(new Vector3Int(xMin, yMin, 0));
            var rect = new Rect(origin.x, origin.y, (xMax - xMin + 1) * cell.x, (yMax - yMin + 1) * cell.y);

            bool IsRoad(int x, int y) => cells.TryGetValue(new Vector2Int(x, y), out var t) && t != grass;
            (int, int)? Run(IEnumerable<int> range, System.Func<int, bool> road)
            {
                // 가장 긴 연속 구간(가장자리에 섞인 잡초 타일 한두 칸은 무시)
                (int, int)? best = null; int start = int.MinValue, last = int.MinValue;
                foreach (int i in range)
                {
                    if (!road(i)) continue;
                    if (i != last + 1) start = i;
                    last = i;
                    if (best == null || last - start > best.Value.Item2 - best.Value.Item1) best = (start, last);
                }
                return best.HasValue && best.Value.Item2 - best.Value.Item1 >= 1 ? best : null;
            }

            var xs = Enumerable.Range(xMin, xMax - xMin + 1);
            var ys = Enumerable.Range(yMin, yMax - yMin + 1);
            var top = Run(xs, x => IsRoad(x, yMax));
            var bottom = Run(xs, x => IsRoad(x, yMin));
            var left = Run(ys, y => IsRoad(xMin, y));
            var right = Run(ys, y => IsRoad(xMax, y));
            float CenterX((int, int) r) => origin.x + ((r.Item1 + r.Item2) * 0.5f - xMin + 0.5f) * cell.x;
            (float, float) RangeY((int, int) r) => (origin.y + (r.Item1 - yMin) * cell.y, origin.y + (r.Item2 - yMin + 1) * cell.y);
            roads = new Roads
            {
                Top = top.HasValue ? CenterX(top.Value) : (float?)null,
                Bottom = bottom.HasValue ? CenterX(bottom.Value) : (float?)null,
                Left = left.HasValue ? RangeY(left.Value) : ((float, float)?)null,
                Right = right.HasValue ? RangeY(right.Value) : ((float, float)?)null,
            };
            return rect;
        }

        /// <summary>
        /// 성벽 그룹을 비워서 다시 만든다. Village가 있으면 그 아래에 만들어 MainScene에도 나오게 한다(프리팹에 넣는 건 ApplyGroupToPrefab).
        /// 예전에 씬 루트에 만든 그룹과 시험으로 루트에 놓은 성벽 조각도 같이 정리한다.
        /// </summary>
        private static GameObject ResetGroup(Scene scene, GameObject village)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                bool loosePiece = root.TryGetComponent<SpriteRenderer>(out var r) && r.sprite != null
                                  && AssetDatabase.GetAssetPath(r.sprite).StartsWith(Folder);
                if (root.name == GroupName || loosePiece) Object.DestroyImmediate(root);
            }

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

        /// <summary>
        /// Village 프리팹 인스턴스 안의 성벽 그룹 쪽 변경만 프리팹에 Apply한다(다른 오버라이드는 그대로 둔다).
        /// 새로 만든 그룹 전체, 또는 그룹 안 오브젝트에 붙인 컴포넌트·바뀐 값(성문 프레임 등).
        /// </summary>
        internal static void ApplyGroupToPrefab(GameObject village, GameObject group)
        {
            if (!PrefabUtility.IsPartOfPrefabInstance(village)) return;
            string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(village);
            bool InGroup(GameObject go) => go != null && go.transform.IsChildOf(group.transform);

            foreach (var added in PrefabUtility.GetAddedGameObjects(village))
                if (InGroup(added.instanceGameObject)) added.Apply(path, InteractionMode.AutomatedAction);
            foreach (var added in PrefabUtility.GetAddedComponents(village))
                if (InGroup(added.instanceComponent.gameObject)) added.Apply(path, InteractionMode.AutomatedAction);
            foreach (var changed in PrefabUtility.GetObjectOverrides(village, false))
            {
                var go = changed.instanceObject is Component c ? c.gameObject : changed.instanceObject as GameObject;
                if (InGroup(go)) changed.Apply(path, InteractionMode.AutomatedAction);
            }
        }

        /// <summary>씬에서 성벽 그룹(Village 아래 또는 씬 루트).</summary>
        internal static GameObject FindGroup(Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == GroupName) return root;
                if (root.name == VillageName && root.transform.Find(GroupName) is Transform t) return t.gameObject;
            }
            return null;
        }

        /// <summary>발밑 가운데(x, baseY)에 SortingGroup 부모를 두고 그 위에 그림을 세운다.</summary>
        private static SpriteRenderer Place(GameObject group, Sprite sprite, float x, float baseY, int order)
        {
            var anchor = new GameObject(sprite.name, typeof(SortingGroup));
            anchor.transform.SetParent(group.transform, false);
            anchor.transform.position = new Vector3(x, baseY, 0f);
            anchor.GetComponent<SortingGroup>().sortingOrder = order;

            var art = new GameObject(sprite.name, typeof(SpriteRenderer));
            art.transform.SetParent(anchor.transform, false);
            var renderer = art.GetComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            // 피벗(가운데)에서 그림 아래쪽까지 올려 발밑이 부모 위치에 오게
            art.transform.localPosition = new Vector3(-sprite.bounds.center.x, sprite.bounds.extents.y - sprite.bounds.center.y, 0f);
            return renderer;
        }

        /// <summary>[from, to] 구간을 가로벽으로 채운다. 양끝을 맞추고 남는 길이는 겹침으로 고르게 나눈다.</summary>
        private static void FillRow(GameObject group, Sprite sprite, float from, float to, float baseY, float minOverlap, int order, List<SpriteRenderer> placed)
        {
            float width = sprite.bounds.size.x, length = to - from;
            if (length <= 0f) return;
            if (length <= width)
            {
                placed.Add(Place(group, sprite, (from + to) * 0.5f, baseY, order)); // 짧으면 한 장을 가운데에(양옆 큰 조각 밑으로 숨음)
                return;
            }
            int count = Mathf.CeilToInt((length - width) / (width - minOverlap)) + 1;
            float step = (length - width) / (count - 1);
            for (int i = 0; i < count; i++)
                placed.Add(Place(group, sprite, from + width * 0.5f + step * i, baseY, order));
        }

        /// <summary>
        /// 발밑이 fromBase~toBase 사이에 오도록 세로벽을 아래에서 위로 쌓는다. 아래 조각이 위 조각의 앞면(front)을 덮게 겹친다.
        /// 탑·모서리와 같은 order라 발밑 높이로 앞뒤가 정해진다: 아래 조각이 위 조각 앞, 탑에 닿는 마지막 조각은 탑보다 발밑이 낮아 탑 앞.
        /// </summary>
        private static void FillColumn(GameObject group, Sprite sprite, float x, float fromBase, float toBase, float front, List<SpriteRenderer> placed)
        {
            float step = sprite.bounds.size.y - front;
            float length = toBase - fromBase;
            if (length < 0f) return;
            int count = Mathf.Max(1, Mathf.CeilToInt(length / step) + 1);
            float actual = count > 1 ? length / (count - 1) : 0f;
            for (int i = 0; i < count; i++)
                placed.Add(Place(group, sprite, x, fromBase + actual * i, FeatureOrder));
        }

        /// <summary>성벽 조각만 1080p 화면 크기로 굽는다(같은 그림은 한 번만 굽고 나머지는 결과를 같이 쓴다).</summary>
        private static void BakeWalls(List<SpriteRenderer> renderers)
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
    }
}
