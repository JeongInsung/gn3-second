using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

namespace GN3.EditorTools
{
    /// <summary>
    /// 바깥 바닥 타일맵(Floor Grid 아래 가장 큰 Tilemap)을 분수 칸 기준 좌우 대칭으로 맞춘다.
    /// 왼쪽에 타일이 있는 칸마다 오른쪽 거울 칸에 같은 타일을 넣는다(왼쪽이 빈 칸의 거울 칸은 지우지 않는다).
    /// 바깥 타일은 풀·길 윗변·길 아랫변뿐이라 좌우 방향이 없어 그대로 복사한다. 안쪽 광장 타일맵은 건드리지 않는다.
    /// 끝나면 그 타일맵 변경만 Village 프리팹에 Apply한다. 성벽은 '성벽 둘러치기'를 다시 눌러 새 가장자리에 맞춘다.
    /// </summary>
    public static class FloorMirrorTool
    {
        private const string FountainKeyword = "분수";

        [MenuItem("GN3/Village/바깥 바닥 좌우 대칭 (분수 기준)")]
        public static void Mirror()
        {
            var scene = SceneManager.GetActiveScene();
            var roots = scene.GetRootGameObjects();
            var floor = roots.SelectMany(r => r.GetComponentsInChildren<Tilemap>(true))
                .Where(t => t.transform.parent != null && t.transform.parent.name == "Floor Grid")
                .OrderByDescending(t => { t.CompressBounds(); var b = t.cellBounds; return b.size.x * b.size.y; })
                .FirstOrDefault();
            if (floor == null)
            {
                Debug.LogError("[FloorMirror] 'Floor Grid' 아래 Tilemap(바닥)을 찾지 못했습니다.");
                return;
            }

            var fountain = roots.SelectMany(r => r.GetComponentsInChildren<Transform>(true))
                .FirstOrDefault(t => t.name.Contains(FountainKeyword));
            int axis = fountain != null ? floor.WorldToCell(fountain.position).x : 0;

            Undo.RecordObject(floor, "바깥 바닥 좌우 대칭");
            var bounds = floor.cellBounds;
            int changed = 0;
            foreach (var p in bounds.allPositionsWithin)
            {
                if (p.x >= axis) continue;
                var tile = floor.GetTile(p);
                if (tile == null) continue;
                var mirror = new Vector3Int(2 * axis - p.x, p.y, p.z);
                if (floor.GetTile(mirror) == tile) continue;
                floor.SetTile(mirror, tile);
                changed++;
            }
            floor.CompressBounds();

            var village = floor.transform.root.gameObject;
            if (PrefabUtility.IsPartOfPrefabInstance(village))
            {
                string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(village);
                foreach (var o in PrefabUtility.GetObjectOverrides(village, false))
                {
                    var go = o.instanceObject is Component c ? c.gameObject : o.instanceObject as GameObject;
                    if (go == floor.gameObject) o.Apply(path, InteractionMode.AutomatedAction);
                }
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[FloorMirror] '{floor.name}' 축 x={axis} 기준으로 오른쪽 {changed}칸을 채웠습니다. 범위 {floor.cellBounds.xMin}~{floor.cellBounds.xMax - 1}. '성벽 둘러치기'를 다시 실행하세요.");
        }
    }
}
