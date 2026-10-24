using System.Collections.Generic;
using UnityEngine;

namespace GN3.World
{
    /// <summary>
    /// 마을 바닥을 작은 칸으로 나눠 건물·장식이 차지한 바닥을 막고, 그 사이로 걷는 길을 찾는다(VillageWanderer용).
    /// 장애물은 따로 등록하지 않고 마을 그림자(ProjectedShadow)가 붙은 물체를 쓴다. 그림자의 heightScale이
    /// 물체 종류(ShadowBuilder.ApplyDefaults: 건물 0.5 / 나무·가로등·화분 0.8 / 분수·벤치 0)를 알려 주고,
    /// 0.8 장식은 그림 비율로 다시 나눈다(화분 = 상자 전체, 가로등 = 받침, 나무 = 줄기).
    /// 비스듬히 내려다본 그림이라 그림 전체가 아니라 발밑 쪽 일부만 바닥을 차지한다(지붕·나뭇잎 뒤로는 지나간다).
    /// 예외로 성당은 그림 전체를 막는다(FullBlockKeyword).
    /// </summary>
    public class VillageNavGrid
    {
        private const float CellSize = 0.1f;
        private const float AgentRadius = 0.1f;

        // 건물: 폭 대부분, 그림 아래쪽 절반(벽·바닥)
        private const float BuildingWidth = 0.9f;
        private const float BuildingHeight = 0.5f;
        // 건물이 아닌 장식은 보이는 그림 비율(높이/폭)로 나눈다(그림 알파 범위 실측 기준).
        // 낮고 넓은 물체(화분 상자 65x37 → 0.57): 상자 전체가 바닥
        private const float LowPropAspect = 0.8f;
        private const float LowPropWidth = 0.95f;
        private const float LowPropHeight = 0.85f;
        // 가늘고 긴 물체(가로등 38x90 → 2.37): 아래 팔각 석대가 폭 전체, 높이 약 1/3
        private const float TallPropAspect = 1.8f;
        private const float TallPropWidth = 1f;
        private const float TallPropHeight = 0.35f;
        // 그 외(나무): 줄기만 막아 나뭇잎 뒤로는 지나간다
        private const float TrunkWidth = 0.25f;
        private const float TrunkHeight = 0.15f;
        // 분수·벤치(위에서 똑바로 본 납작한 물체): 보이는 범위 거의 전체
        private const float FlatShrink = 0.1f;
        private const float BuildingMaxHeightScale = 0.6f;
        // 그림자 없이 놓인 물체(메뉴를 아직 안 누른 새 건물 등)는 보이는 폭으로 건물/장식을 가른다.
        private const float UnshadowedBuildingMinWidth = 1.5f;
        private const float BuildingHeightScale = 0.5f;
        private const float PropHeightScale = 0.8f;
        private const string VillageRootName = "Village";
        // 종탑이 높은 성당은 위쪽 절반이 비어 그 위로 가로질러 다녔다 → 그림 이름에 이 말이 있으면 보이는 범위 전체를 막는다.
        private const string FullBlockKeyword = "성당";
        private const float FullBlockShrink = 0.05f; // 투명에 가까운 가장자리만 살짝 뺀다

        private readonly Rect _area;
        private readonly int _cols;
        private readonly int _rows;
        private readonly bool[] _blocked;
        private readonly List<Rect> _blockedRects = new List<Rect>();
        private const string SortAnchorName = "SortAnchor"; // VillageDepthSorter가 물체를 옮겨 넣는 정렬 묶음
        private const int PullLookahead = 80; // 경유점 줄이기에서 한 번에 내다보는 칸 수(긴 길에서 시선 검사가 제곱으로 늘지 않게)

        // 길찾기 버퍼(격자마다 하나, 길 찾을 때마다 다시 씀)
        private float[] _gScore;
        private int[] _cameFrom;
        private int[] _visitedStamp;
        private int[] _closedStamp;
        private int _searchStamp;
        private readonly MinHeap _open = new MinHeap();

        public IReadOnlyList<Rect> BlockedRects => _blockedRects;
        /// <summary>걷기 영역(마을 바닥 전체에서 안쪽 여백을 뺀 것 — VillageBounds).</summary>
        public Rect Area => _area;

        private VillageNavGrid(Rect area)
        {
            _area = area;
            _cols = Mathf.Max(1, Mathf.CeilToInt(area.width / CellSize));
            _rows = Mathf.Max(1, Mathf.CeilToInt(area.height / CellSize));
            _blocked = new bool[_cols * _rows];
        }

        /// <summary>마을 장애물 하나: 그림, 그 그림자, 바닥 점유 사각형(월드).</summary>
        public struct Obstacle
        {
            public SpriteRenderer Renderer;
            public ProjectedShadow Shadow;
            public Rect Footprint;
        }

        /// <summary>
        /// 그림자(ProjectedShadow)가 붙은 마을 물체를 모아 바닥 점유 영역을 계산한다. 용병 자신의 그림자는 뺀다.
        /// 그림자 없이 Village 바로 아래 놓인 그림(새로 놓고 그림자 메뉴를 아직 안 누른 건물 등)도 크기로 종류를 정해 넣는다.
        /// 이미 앞뒤 정렬된 물체(그림자가 sourceOverride로 따라감)도 다시 모은다 — 마을에 건물을 지은 뒤 격자를 새로 맞출 때(Rebuild).
        /// </summary>
        public static List<Obstacle> CollectObstacles()
        {
            var obstacles = new List<Obstacle>();
            var owners = new HashSet<SpriteRenderer>();
            var village = GameObject.Find(VillageRootName);
            foreach (var shadow in Object.FindObjectsByType<ProjectedShadow>(FindObjectsSortMode.None))
            {
                SpriteRenderer owner;
                if (shadow.sourceOverride != null)
                {
                    // 앞뒤 정렬된 마을 물체(SortAnchor 아래)의 그림자만 — 용병·강아지 자신의 그림자도 sourceOverride를 쓴다
                    owner = shadow.sourceOverride;
                    var anchor = owner.transform.parent;
                    if (anchor == null || anchor.name != SortAnchorName || !owner.gameObject.activeInHierarchy) continue;
                }
                else owner = shadow.transform.parent != null ? shadow.transform.parent.GetComponent<SpriteRenderer>() : null;
                if (owner == null || owner.sprite == null) continue;
                owners.Add(owner);
                obstacles.Add(new Obstacle { Renderer = owner, Shadow = shadow, Footprint = Footprint(owner, shadow.heightScale) });
            }

            if (village != null)
            {
                foreach (Transform top in village.transform)
                {
                    // 정렬된 물체는 SortAnchor 아래로 옮겨져 있다
                    var child = top.name == SortAnchorName && top.childCount > 0 ? top.GetChild(0) : top;
                    var renderer = child.GetComponent<SpriteRenderer>();
                    // 꺼 둔 물체(아직 안 지은 건물 — VillageLayoutApplier)는 바닥을 막지 않는다.
                    if (!child.gameObject.activeInHierarchy || renderer == null || renderer.sprite == null || owners.Contains(renderer)) continue;
                    if (child.GetComponent<ProjectedShadow>() != null) continue; // 정렬 때 SortAnchor 밖(Village 바로 아래)으로 빠진 그림자
                    float heightScale = VisibleRect(renderer).width >= UnshadowedBuildingMinWidth ? BuildingHeightScale : PropHeightScale;
                    obstacles.Add(new Obstacle { Renderer = renderer, Shadow = null, Footprint = Footprint(renderer, heightScale) });
                }
            }
            return obstacles;
        }

        public static VillageNavGrid Build(Rect area, IEnumerable<Obstacle> obstacles)
        {
            var grid = new VillageNavGrid(area);
            grid.BlockAll(obstacles);
            return grid;
        }

        /// <summary>
        /// 같은 격자를 새 장애물로 다시 막는다(마을에 건물을 지었을 때 — VillageLayoutApplier.Rebuilt). 걷는 용병·강아지가
        /// 이 격자를 그대로 들고 있어 새로 만들지 않고 고친다.
        /// </summary>
        public void Rebuild(IEnumerable<Obstacle> obstacles)
        {
            System.Array.Clear(_blocked, 0, _blocked.Length);
            _blockedRects.Clear();
            BlockAll(obstacles);
        }

        private void BlockAll(IEnumerable<Obstacle> obstacles)
        {
            var grid = this;
            foreach (var obstacle in obstacles)
            {
                grid.Block(obstacle.Footprint);
                // 앞뒤 정렬에도 쓰는 Footprint는 그대로 두고 길찾기만 그림 전체로 막는다.
                if (obstacle.Renderer != null && obstacle.Renderer.name.Contains(FullBlockKeyword))
                {
                    Rect visible = VisibleRect(obstacle.Renderer);
                    float dx = visible.width * FullBlockShrink * 0.5f, dy = visible.height * FullBlockShrink * 0.5f;
                    grid.Block(Rect.MinMaxRect(visible.xMin + dx, visible.yMin + dy, visible.xMax - dx, visible.yMax - dy));
                }
            }
            // 울타리로 둘러싼 땅(가축 우리)처럼 그림자로는 알 수 없는 막힌 바닥. 장애물 목록(앞뒤 정렬에도 쓰임)에는 넣지 않는다.
            foreach (var blocker in Object.FindObjectsByType<NavBlockArea>(FindObjectsSortMode.None))
                grid.Block(blocker.Area);
        }

        /// <summary>그 그림이 지금 자리에서 차지할 바닥(건설 미리보기·꽃 가리기용, 장애물 계산과 같은 식).</summary>
        public static Rect FootprintOf(SpriteRenderer owner, float heightScale) => Footprint(owner, heightScale);

        private static Rect Footprint(SpriteRenderer owner, float heightScale)
        {
            Rect visible = VisibleRect(owner);
            if (heightScale <= 0f)
            {
                float dx = visible.width * FlatShrink * 0.5f, dy = visible.height * FlatShrink * 0.5f;
                return Rect.MinMaxRect(visible.xMin + dx, visible.yMin + dy, visible.xMax - dx, visible.yMax - dy);
            }
            if (heightScale <= BuildingMaxHeightScale)
                return CenteredBottom(visible, visible.width * BuildingWidth, visible.height * BuildingHeight);

            float aspect = visible.height / Mathf.Max(visible.width, 0.001f);
            if (aspect < LowPropAspect)
                return CenteredBottom(visible, visible.width * LowPropWidth, visible.height * LowPropHeight);
            if (aspect > TallPropAspect)
                return CenteredBottom(visible, visible.width * TallPropWidth, visible.height * TallPropHeight);
            return CenteredBottom(visible, visible.width * TrunkWidth, visible.height * TrunkHeight);
        }

        private static Rect CenteredBottom(Rect visible, float width, float height) =>
            new Rect(visible.center.x - width * 0.5f, visible.yMin, width, height);

        /// <summary>투명 여백을 뺀, 그림이 실제로 보이는 월드 범위(스프라이트 메시 정점 기준).</summary>
        private static Rect VisibleRect(SpriteRenderer renderer)
        {
            var vertices = renderer.sprite.vertices;
            if (vertices.Length == 0)
            {
                var b = renderer.bounds;
                return Rect.MinMaxRect(b.min.x, b.min.y, b.max.x, b.max.y);
            }
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
            foreach (var v in vertices)
            {
                Vector2 w = renderer.transform.TransformPoint(v);
                min = Vector2.Min(min, w);
                max = Vector2.Max(max, w);
            }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        private void Block(Rect footprint)
        {
            var r = Rect.MinMaxRect(footprint.xMin - AgentRadius, footprint.yMin - AgentRadius,
                footprint.xMax + AgentRadius, footprint.yMax + AgentRadius);
            _blockedRects.Add(r);
            int x0 = Mathf.Max(0, Mathf.FloorToInt((r.xMin - _area.xMin) / CellSize));
            int x1 = Mathf.Min(_cols - 1, Mathf.FloorToInt((r.xMax - _area.xMin) / CellSize));
            int y0 = Mathf.Max(0, Mathf.FloorToInt((r.yMin - _area.yMin) / CellSize));
            int y1 = Mathf.Min(_rows - 1, Mathf.FloorToInt((r.yMax - _area.yMin) / CellSize));
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                _blocked[y * _cols + x] = true;
        }

        private bool InGrid(int x, int y) => x >= 0 && y >= 0 && x < _cols && y < _rows;
        private bool Open(int x, int y) => InGrid(x, y) && !_blocked[y * _cols + x];

        private Vector2Int ToCell(Vector2 p) => new Vector2Int(
            Mathf.FloorToInt((p.x - _area.xMin) / CellSize), Mathf.FloorToInt((p.y - _area.yMin) / CellSize));

        private Vector2 ToWorld(Vector2Int c) => new Vector2(
            _area.xMin + (c.x + 0.5f) * CellSize, _area.yMin + (c.y + 0.5f) * CellSize);

        public bool IsWalkable(Vector2 p)
        {
            var c = ToCell(p);
            return Open(c.x, c.y);
        }

        public Vector2 RandomWalkablePoint(System.Random rng)
        {
            for (int i = 0; i < 200; i++)
            {
                var p = new Vector2(
                    Mathf.Lerp(_area.xMin, _area.xMax, (float)rng.NextDouble()),
                    Mathf.Lerp(_area.yMin, _area.yMax, (float)rng.NextDouble()));
                if (IsWalkable(p)) return p;
            }
            return _area.center;
        }

        /// <summary>막힌 칸 안에 있으면 가장 가까운 걸을 수 있는 칸 중심으로 옮긴다(넓어지는 고리로 탐색).</summary>
        public Vector2 NearestWalkable(Vector2 p)
        {
            if (IsWalkable(p)) return p;
            var c = ToCell(p);
            for (int radius = 1; radius < Mathf.Max(_cols, _rows); radius++)
            {
                float best = float.MaxValue;
                Vector2 bestPoint = p;
                for (int y = c.y - radius; y <= c.y + radius; y++)
                for (int x = c.x - radius; x <= c.x + radius; x++)
                {
                    if (Mathf.Max(Mathf.Abs(x - c.x), Mathf.Abs(y - c.y)) != radius || !Open(x, y)) continue;
                    var w = ToWorld(new Vector2Int(x, y));
                    float d = (w - p).sqrMagnitude;
                    if (d < best) { best = d; bestPoint = w; }
                }
                if (best < float.MaxValue) return bestPoint;
            }
            return p;
        }

        /// <summary>p에서 한 칸씩 아래(화면 앞쪽)로 내려가며 처음 걸을 수 있는 칸 중심. 건물 문 앞 바깥 지점을 찾을 때 쓴다.</summary>
        public Vector2 FirstWalkableBelow(Vector2 p, float maxDistance = 2f)
        {
            for (float d = 0f; d <= maxDistance; d += CellSize)
            {
                var point = new Vector2(p.x, p.y - d);
                if (IsWalkable(point)) return ToWorld(ToCell(point));
            }
            return NearestWalkable(p);
        }

        private static readonly Vector2Int[] Directions =
        {
            new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1),
            new Vector2Int(1, 1), new Vector2Int(1, -1), new Vector2Int(-1, 1), new Vector2Int(-1, -1),
        };

        /// <summary>
        /// 8방향 A*로 길을 찾고(막힌 칸 모서리를 대각선으로 깎지 않음), 서로 보이는 지점끼리 건너뛰어 꺾임을 줄인다.
        /// path에는 출발점을 뺀 경유점들이 들어가고 마지막은 to다.
        /// </summary>
        public bool TryFindPath(Vector2 from, Vector2 to, List<Vector2> path)
        {
            path.Clear();
            var start = ToCell(from);
            var goal = ToCell(to);
            if (!Open(start.x, start.y) || !Open(goal.x, goal.y)) return false;
            if (LineWalkable(from, to))
            {
                path.Add(to);
                return true;
            }

            // 마을이 3×3으로 넓어져(약 16만 칸) 칸 배열을 길 찾을 때마다 새로 만들지 않고 세대 번호로 비운 셈 친다.
            EnsureSearchBuffers();
            if (++_searchStamp == int.MaxValue)
            {
                System.Array.Clear(_visitedStamp, 0, _visitedStamp.Length);
                System.Array.Clear(_closedStamp, 0, _closedStamp.Length);
                _searchStamp = 1;
            }
            int stamp = _searchStamp;

            int startIndex = start.y * _cols + start.x;
            int goalIndex = goal.y * _cols + goal.x;
            Visit(startIndex, 0f, -1, stamp);
            _open.Clear();
            _open.Push(startIndex, Heuristic(start, goal));

            bool found = false;
            while (_open.Count > 0)
            {
                int current = _open.Pop();
                if (_closedStamp[current] == stamp) continue;
                if (current == goalIndex) { found = true; break; }
                _closedStamp[current] = stamp;

                int cx = current % _cols, cy = current / _cols;
                foreach (var d in Directions)
                {
                    int nx = cx + d.x, ny = cy + d.y;
                    if (!Open(nx, ny)) continue;
                    if (d.x != 0 && d.y != 0 && (!Open(cx + d.x, cy) || !Open(cx, cy + d.y))) continue;
                    int next = ny * _cols + nx;
                    if (_closedStamp[next] == stamp) continue;
                    float g = _gScore[current] + (d.x != 0 && d.y != 0 ? 1.41421f : 1f);
                    if (_visitedStamp[next] == stamp && g >= _gScore[next]) continue;
                    Visit(next, g, current, stamp);
                    _open.Push(next, g + Heuristic(new Vector2Int(nx, ny), goal));
                }
            }
            if (!found) return false;

            var cells = new List<Vector2>();
            for (int i = _cameFrom[goalIndex]; i != -1 && i != startIndex; i = _cameFrom[i])
                cells.Add(ToWorld(new Vector2Int(i % _cols, i / _cols)));
            cells.Reverse();
            cells.Add(to);

            // string pulling: 지금 위치에서 보이는 가장 먼 경유점으로 바로 간다(먼 길은 앞쪽 PullLookahead칸까지만 본다).
            Vector2 anchor = from;
            int k = 0;
            while (k < cells.Count)
            {
                int farthest = k;
                for (int j = Mathf.Min(cells.Count - 1, k + PullLookahead); j > k; j--)
                    if (LineWalkable(anchor, cells[j])) { farthest = j; break; }
                path.Add(cells[farthest]);
                anchor = cells[farthest];
                k = farthest + 1;
            }
            return true;
        }

        private void EnsureSearchBuffers()
        {
            int count = _cols * _rows;
            if (_gScore != null && _gScore.Length == count) return;
            _gScore = new float[count];
            _cameFrom = new int[count];
            _visitedStamp = new int[count];
            _closedStamp = new int[count];
            _searchStamp = 0;
        }

        private void Visit(int index, float g, int from, int stamp)
        {
            _gScore[index] = g;
            _cameFrom[index] = from;
            _visitedStamp[index] = stamp;
        }

        /// <summary>A* 열린 목록: f가 가장 작은 칸을 꺼내는 이진 힙(같은 칸이 여러 번 들어가도 닫힌 칸은 꺼낼 때 버린다).</summary>
        private class MinHeap
        {
            private int[] _items = new int[256];
            private float[] _keys = new float[256];
            public int Count { get; private set; }

            public void Clear() => Count = 0;

            public void Push(int item, float key)
            {
                if (Count == _items.Length)
                {
                    System.Array.Resize(ref _items, Count * 2);
                    System.Array.Resize(ref _keys, Count * 2);
                }
                int i = Count++;
                while (i > 0)
                {
                    int parent = (i - 1) / 2;
                    if (_keys[parent] <= key) break;
                    _items[i] = _items[parent];
                    _keys[i] = _keys[parent];
                    i = parent;
                }
                _items[i] = item;
                _keys[i] = key;
            }

            public int Pop()
            {
                int top = _items[0];
                int lastItem = _items[--Count];
                float lastKey = _keys[Count];
                int i = 0;
                while (true)
                {
                    int child = i * 2 + 1;
                    if (child >= Count) break;
                    if (child + 1 < Count && _keys[child + 1] < _keys[child]) child++;
                    if (_keys[child] >= lastKey) break;
                    _items[i] = _items[child];
                    _keys[i] = _keys[child];
                    i = child;
                }
                if (Count > 0)
                {
                    _items[i] = lastItem;
                    _keys[i] = lastKey;
                }
                return top;
            }
        }

        private static float Heuristic(Vector2Int a, Vector2Int b)
        {
            int dx = Mathf.Abs(a.x - b.x), dy = Mathf.Abs(a.y - b.y);
            return Mathf.Max(dx, dy) + 0.41421f * Mathf.Min(dx, dy);
        }

        /// <summary>두 점 사이 선분이 막힌 칸을 지나지 않는지(반 칸 간격 샘플링).</summary>
        public bool LineWalkable(Vector2 a, Vector2 b)
        {
            float length = Vector2.Distance(a, b);
            int steps = Mathf.Max(1, Mathf.CeilToInt(length / (CellSize * 0.5f)));
            for (int i = 0; i <= steps; i++)
                if (!IsWalkable(Vector2.Lerp(a, b, i / (float)steps))) return false;
            return true;
        }
    }
}
