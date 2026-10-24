using System.Collections.Generic;
using System.Linq;
using GN3.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.Tilemaps;

namespace GN3.EditorTools
{
    /// <summary>
    /// 마을을 처음 화면(가운데 칸)의 3×3으로 넓힌다(Village 프리팹):
    /// - 바닥 타일맵을 넓힌 사각형 + 여백까지 같은 타일로 채운다(VillageBounds가 이 바닥을 마을 범위로 쓴다).
    /// - 바깥 8칸에 지금 숲(나무·덤불·돌)과 숲 바닥(풀·들꽃·꽃무더기)을 칸 크기만큼 옮겨 복제한다. 조금씩 흔들고 좌우를 뒤집어
    ///   반복이 티 나지 않게 하고, 가운데 칸 안·바닥 밖·다른 나무에 붙는 것은 버린다. 이름 끝 OuterSuffix로 표시해 다시 돌리면 갈아 끼운다.
    /// - 해·달 빛 반경을 넓힌 모서리까지 닿게 늘린다.
    /// </summary>
    public static class VillageExpandBuilder
    {
        private const string PrefabPath = "Assets/조정식/Prefabs/Village.prefab";
        private const string ForestGroupName = "숲";
        private const string GroundCoverGroupName = "숲 바닥";
        public const string OuterSuffix = " (바깥)";

        // 처음 화면(카메라 크기 5, 16:9) = 가운데 칸
        private static readonly Vector2 CellCenter = new Vector2(0.19f, 0.54f);
        private static readonly Vector2 CellSize = new Vector2(17.78f, 10f);
        private const int FloorMarginCells = 1;   // 넓힌 사각형 밖으로 더 까는 바닥 칸
        private const float EdgeInset = 0.5f;     // 바닥 끝에서 이만큼 안쪽에만 숲을 둔다
        private const float Jitter = 0.6f;
        private const float TreeSpacing = 0.5f;   // 나무 밑동끼리 이보다 가까우면 버린다
        private const float CoverSpacing = 0.2f;
        private const float SunMoonRadius = 80f;  // 넓힌 모서리(가운데에서 약 31) + 궤도 14보다 넉넉히
        private const int Seed = 20261024;

        [MenuItem("GN3/Village/마을 3×3으로 넓히기")]
        public static void Build()
        {
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var full = new Rect(CellCenter - CellSize * 1.5f, CellSize * 3f);
                var center = new Rect(CellCenter - CellSize * 0.5f, CellSize);
                var rng = new System.Random(Seed);

                int tiles = FillFloor(root, full);
                int trees = CopyOutward(root.transform.Find(ForestGroupName), full, center, TreeSpacing, rng);
                int cover = CopyOutward(root.transform.Find(GroundCoverGroupName), full, center, CoverSpacing, rng);
                int lights = WidenSunMoon(root);

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                Debug.Log($"[VillageExpandBuilder] 바닥 {tiles}칸 추가 · 바깥 숲 {trees}개 · 바깥 풀꽃 {cover}개 · 해·달 빛 {lights}개 반경 {SunMoonRadius}");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>가장 넓은 타일맵(바깥 바닥)의 빈칸을 넓힌 사각형 + 여백까지 지금 쓰는 타일로 채운다.</summary>
        private static int FillFloor(GameObject root, Rect full)
        {
            var floor = root.GetComponentsInChildren<Tilemap>(true).OrderByDescending(t => { t.CompressBounds(); return t.cellBounds.size.x * t.cellBounds.size.y; }).FirstOrDefault();
            if (floor == null) return 0;
            TileBase tile = null;
            foreach (var pos in floor.cellBounds.allPositionsWithin)
                if ((tile = floor.GetTile(pos)) != null) break;
            if (tile == null) return 0;

            var min = floor.WorldToCell(new Vector3(full.xMin, full.yMin, 0f));
            var max = floor.WorldToCell(new Vector3(full.xMax, full.yMax, 0f));
            int added = 0;
            for (int y = min.y - FloorMarginCells; y <= max.y + FloorMarginCells; y++)
            for (int x = min.x - FloorMarginCells; x <= max.x + FloorMarginCells; x++)
            {
                var cell = new Vector3Int(x, y, 0);
                if (floor.HasTile(cell)) continue;
                floor.SetTile(cell, tile);
                added++;
            }
            floor.CompressBounds();
            return added;
        }

        /// <summary>그룹의 원래 자식들을 바깥 8칸에 한 벌씩 복제한다(지난번 복제본은 먼저 지운다).</summary>
        private static int CopyOutward(Transform group, Rect full, Rect center, float spacing, System.Random rng)
        {
            if (group == null) return 0;
            foreach (var old in group.Cast<Transform>().Where(t => t.name.EndsWith(OuterSuffix)).ToList())
                Object.DestroyImmediate(old.gameObject);

            var sources = group.Cast<Transform>().ToList();
            var taken = sources.Select(t => (Vector2)t.position).ToList();
            var inner = Rect.MinMaxRect(full.xMin + EdgeInset, full.yMin + EdgeInset, full.xMax - EdgeInset, full.yMax - EdgeInset);
            int added = 0;
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                var offset = new Vector2(dx * CellSize.x, dy * CellSize.y);
                foreach (var source in sources)
                {
                    var jitter = new Vector2((float)(rng.NextDouble() * 2 - 1), (float)(rng.NextDouble() * 2 - 1)) * Jitter;
                    var pos = (Vector2)source.position + offset + jitter;
                    if (center.Contains(pos) || !inner.Contains(pos)) continue;
                    if (taken.Any(p => (p - pos).sqrMagnitude < spacing * spacing)) continue;

                    var copy = Object.Instantiate(source.gameObject, group);
                    copy.name = source.name + OuterSuffix;
                    copy.transform.position = new Vector3(pos.x, pos.y, source.position.z);
                    var renderer = copy.GetComponent<SpriteRenderer>();
                    if (renderer != null && rng.NextDouble() < 0.5) renderer.flipX = !renderer.flipX; // 그림자는 원본 flip을 따라간다(ProjectedShadow)
                    taken.Add(pos);
                    added++;
                }
            }
            return added;
        }

        /// <summary>DayNightCycle의 해·달 점광원 반경을 넓힌 마을 모서리까지 닿게 늘린다.</summary>
        private static int WidenSunMoon(GameObject root)
        {
            int count = 0;
            foreach (var cycle in root.GetComponentsInChildren<DayNightCycle>(true))
            {
                var so = new SerializedObject(cycle);
                foreach (var field in new[] { "sunLight", "moonLight" })
                {
                    var light = so.FindProperty(field)?.objectReferenceValue as Light2D;
                    if (light == null) continue;
                    light.pointLightOuterRadius = Mathf.Max(light.pointLightOuterRadius, SunMoonRadius);
                    EditorUtility.SetDirty(light);
                    count++;
                }
            }
            return count;
        }
    }
}
