using System.Collections.Generic;
using System.Linq;
using GN3.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace GN3.EditorTools
{
    /// <summary>
    /// 소 걷기 시트(4열×2행 8프레임, 오른쪽을 봄)를 화면 크기로 구워 Village/가축 울타리 아래 "소" 그룹의 VillagePenCows에 넣는다.
    /// 소가 거닐 범위는 우리 울타리 조각(SortingGroup 부모)들의 위치로 계산한다. 소는 Play 때 VillagePenCows가 만든다.
    /// 그룹은 Village 프리팹에 Apply해 MainScene에도 나온다. 다시 누르면 다시 굽고 덮어쓴다.
    /// </summary>
    public static class CowBuilder
    {
        private const string SheetPath = "Assets/조정식/Animations/Cow/갈색 점박이 소 걷기.png";
        private const string OutputName = "소 걷기";
        private const string IdlePath = "Assets/조정식/Decorations/농장 동물/젖소.png"; // 다리를 모은 서 있는 그림(오른쪽을 봄)
        private const string IdleOutputName = "소 서기";
        private const float AlphaThreshold = 0.5f; // SheetAnimationBuilder와 같은 불투명 기준
        private const string VillageName = "Village";
        private const string PenName = "가축 울타리";
        private const string GroupName = "소";
        private const string TroughName = "여물통";
        private const string ShadowMaterialPath = "Assets/조정식/Shaders/ProjectedShadow.mat";
        private const float CowWidth = 0.9f;        // 몸 폭(월드). 우리에 놓았던 정적 젖소 그림과 같은 크기
        private const int CowCount = 3;
        private const float TopGap = 0.1f;          // 위 울타리 밑동보다 이만큼 아래까지
        private const float BottomGap = 0.3f;       // 아래 울타리 밑동보다 이만큼 위부터(발이 울타리 뒤에 살짝 가려진다)
        private const float SideGap = 0.05f;

        [MenuItem("GN3/Village/소 만들기")]
        public static void Build()
        {
            if (VillagePrefabBuilder.RefuseInMainScene("소 만들기")) return;
            var scene = SceneManager.GetActiveScene();
            var village = scene.GetRootGameObjects().FirstOrDefault(r => r.name == VillageName);
            var pen = village != null ? village.transform.Find(PenName) : null;
            if (pen == null)
            {
                Debug.LogError($"[Cow] 활성 씬에 '{VillageName}/{PenName}'이 없습니다.");
                return;
            }
            var cam = Camera.main;
            if (cam == null || !cam.orthographic)
            {
                Debug.LogError("[Cow] 직교(Orthographic) Main Camera가 필요합니다.");
                return;
            }
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(SheetPath) == null)
            {
                Debug.LogError($"[Cow] 시트가 없습니다: {SheetPath}");
                return;
            }
            if (!TryGetPenArea(pen, out var area, out var avoid))
            {
                Debug.LogError($"[Cow] '{PenName}' 안에서 위·아래·좌우 울타리를 찾지 못했습니다.");
                return;
            }

            var settings = new SheetAnimationSettings
            {
                LogTag = "Cow",
                SheetPath = SheetPath,
                OutputName = OutputName,
                Columns = 4,
                Rows = 2,
                Anchor = SheetAnchor.BottomCenter, // 발밑을 루트(발 위치)에 맞춘다
            };
            var frames = SheetAnimationBuilder.BakeFramesAtWidth(settings, CowWidth);
            if (frames == null || frames.Length == 0)
            {
                Debug.LogError("[Cow] 프레임을 굽지 못했습니다.");
                return;
            }

            var idle = BakeIdle();
            if (idle == null) Debug.LogWarning($"[Cow] 서 있는 그림({IdlePath})을 굽지 못해 걷기 0번 프레임으로 서 있게 합니다.");

            var existing = pen.Find(GroupName);
            GameObject group;
            if (existing != null) group = existing.gameObject;
            else
            {
                group = new GameObject(GroupName);
                Undo.RegisterCreatedObjectUndo(group, "소");
                group.transform.SetParent(pen, false);
            }
            if (!group.TryGetComponent<VillagePenCows>(out var cows)) cows = group.AddComponent<VillagePenCows>();
            cows.Configure(frames, idle, AssetDatabase.LoadAssetAtPath<Material>(ShadowMaterialPath), CowCount, area, avoid);
            EditorUtility.SetDirty(cows);
            PrefabUtility.RecordPrefabInstancePropertyModifications(cows);

            WallLayoutBuilder.ApplyGroupToPrefab(village, pen.gameObject);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            var rect = frames[0].rect;
            Debug.Log($"[Cow] 소 걷기 {frames.Length}프레임({rect.width}x{rect.height}px)을 '{VillageName}/{PenName}/{GroupName}'에 넣었습니다. " +
                      $"Play 때 {CowCount}마리가 x {area.xMin:0.##}~{area.xMax:0.##}, y {area.yMin:0.##}~{area.yMax:0.##} 안을 거닙니다.");
        }

        /// <summary>
        /// 서 있는 그림을 걷기 프레임과 같은 발밑 피벗으로 굽는다. 걷는 소는 다리를 벌려 폭이 넓으므로 폭이 아니라 키(발굽~뿔)로 크기를 맞춘다:
        /// 걷기는 "CowWidth / 걷기 최대 bbox 폭"(월드/원본px)으로 구워지므로 걷는 소 키 = 평균 bbox 높이 × 그 비율.
        /// </summary>
        private static Sprite BakeIdle()
        {
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(IdlePath) == null) return null;

            var walkSheet = SheetAnimationBuilder.LoadReadable(SheetPath);
            int cellWidth = walkSheet.width / 4, cellHeight = walkSheet.height / 2;
            int maxWidth = 0;
            float totalHeight = 0f;
            for (int row = 0; row < 2; row++)
            for (int col = 0; col < 4; col++)
            {
                var bbox = OpaqueBounds(walkSheet.GetPixels(col * cellWidth, row * cellHeight, cellWidth, cellHeight), cellWidth, cellHeight);
                maxWidth = Mathf.Max(maxWidth, bbox.width);
                totalHeight += bbox.height;
            }
            Object.DestroyImmediate(walkSheet);
            float walkHeight = totalHeight / 8f * CowWidth / maxWidth;

            var idleTexture = SheetAnimationBuilder.LoadReadable(IdlePath);
            var idleBox = OpaqueBounds(idleTexture.GetPixels(), idleTexture.width, idleTexture.height);
            Object.DestroyImmediate(idleTexture);
            if (maxWidth == 0 || idleBox.height == 0) return null;
            float idleWidth = idleBox.width * walkHeight / idleBox.height;

            var settings = new SheetAnimationSettings
            {
                LogTag = "Cow",
                SheetPath = IdlePath,
                OutputName = IdleOutputName,
                Columns = 1,
                Rows = 1,
                Anchor = SheetAnchor.BottomCenter,
            };
            var frames = SheetAnimationBuilder.BakeFramesAtWidth(settings, idleWidth);
            return frames != null && frames.Length > 0 ? frames[0] : null;
        }

        private static RectInt OpaqueBounds(Color[] pixels, int width, int height)
        {
            int minX = width, minY = height, maxX = -1, maxY = -1;
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                if (pixels[y * width + x].a < AlphaThreshold) continue;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
            return maxX < 0 ? new RectInt(0, 0, 0, 0) : new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
        }

        /// <summary>
        /// 우리 안쪽 범위(소 발 위치). 울타리 조각은 SortingGroup 부모(위치 = 밑동)이고 order로 위(1)·좌우(2)·아래(3, 4)를 가른다.
        /// 여물통 자리는 소가 서지 않게 avoid로 돌려준다(소 몸 반폭만큼 넓힌다).
        /// </summary>
        private static bool TryGetPenArea(Transform pen, out Rect area, out Rect[] avoid)
        {
            area = default;
            var top = new List<Bounds>();
            var sides = new List<Bounds>();
            var bottom = new List<Bounds>();
            foreach (Transform child in pen)
            {
                if (!child.TryGetComponent<SortingGroup>(out var group)) continue;
                var renderer = child.GetComponentInChildren<SpriteRenderer>();
                if (renderer == null) continue;
                if (group.sortingOrder == 1) top.Add(renderer.bounds);
                else if (group.sortingOrder == 2) sides.Add(renderer.bounds);
                else bottom.Add(renderer.bounds);
            }

            var avoidList = new List<Rect>();
            var trough = pen.Find(TroughName);
            if (trough != null && trough.TryGetComponent<SpriteRenderer>(out var troughRenderer))
            {
                var b = troughRenderer.bounds;
                float pad = CowWidth / 2f;
                avoidList.Add(Rect.MinMaxRect(b.min.x - pad, b.min.y - 0.15f, b.max.x + pad, b.max.y));
            }
            avoid = avoidList.ToArray();

            if (top.Count == 0 || bottom.Count == 0 || sides.Count == 0) return false;
            float centerX = sides.Average(b => b.center.x);
            float leftInner = sides.Where(b => b.center.x < centerX).Select(b => b.max.x).DefaultIfEmpty(float.NaN).Max();
            float rightInner = sides.Where(b => b.center.x >= centerX).Select(b => b.min.x).DefaultIfEmpty(float.NaN).Min();
            if (float.IsNaN(leftInner) || float.IsNaN(rightInner)) return false;

            float half = CowWidth / 2f;
            float yMax = top.Min(b => b.min.y) - TopGap;
            float yMin = bottom.Max(b => b.min.y) + BottomGap;
            area = Rect.MinMaxRect(leftInner + half + SideGap, yMin, rightInner - half - SideGap, yMax);
            return area.width > 0f && area.height > 0f;
        }
    }
}
