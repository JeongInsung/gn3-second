using System.Collections.Generic;
using System.IO;
using System.Linq;
using GN3.World;
using UnityEditor;
using UnityEngine;

namespace GN3.EditorTools
{
    /// <summary>
    /// Village 프리팹 안에 원본 PNG 그대로 들어간 건물(DesignScene에 새로 놓고 "마을 프리팹 만들기"만 누른 경우)을
    /// 다른 건물과 똑같이 맞춘다: 고품질 축소본으로 굽고(PixelBaker) → 그림자 자식 그림을 바꾸고 → 창문 불빛(WindowGlowBuilder, WindowRects.asset에 창이 있을 때).
    /// 이미 구운 건물도 창 불빛이 그림 크기와 안 맞거나(옛 원본 크기 마스크가 남아 2배로 떠 보임) 창 정보가 있는데 불빛이 없으면 불빛만 다시 만든다.
    /// 씬을 열지 않고 프리팹 파일을 직접 고쳐 저장하므로 DesignScene·MainScene의 Village가 그대로 따라간다.
    /// 메뉴로도 부를 수 있고, 스크립트를 다시 읽을 때(Play 중이 아닐 때) 고칠 건물이 있으면 한 번 자동으로 실행한다.
    /// </summary>
    public static class VillagePrefabFixer
    {
        private const string PrefabPath = "Assets/조정식/Prefabs/Village.prefab";
        private const string BuildingFolder = "Assets/조정식/Buildings/";
        private const string ShadowChildName = "Shadow";
        private const string SweepChildPrefix = "Shadow Sweep";

        [MenuItem("GN3/Village/프리팹의 안 구운 건물 정리")]
        private static void FixFromMenu()
        {
            int fixedCount = Fix();
            if (fixedCount == 0)
                EditorUtility.DisplayDialog("프리팹 정리", "Village 프리팹에 원본 그림 그대로 들어간 건물이 없습니다.", "확인");
        }

        [InitializeOnLoadMethod]
        private static void FixAfterReload()
        {
            // 에디터가 다 뜬 뒤(에셋 임포트가 끝난 뒤) 한 번. Play 중이거나 Play로 들어가는 중이면 건드리지 않는다.
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
                Fix();
            };
        }

        /// <summary>고친 건물 수. 고칠 게 없으면 프리팹을 저장하지 않는다.</summary>
        public static int Fix()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null) return 0;
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var renderers = root.GetComponentsInChildren<SpriteRenderer>(true);
                var unbaked = renderers.Where(IsUnbakedBuilding).ToList();
                // 굽고 나서 창 불빛이 옛 그림 크기로 남았거나(크기를 바꾼 뒤 불빛만 안 맞춘 경우), 창 정보가 있는데 불빛이 없는 건물
                var glowOnly = renderers.Where(r => !unbaked.Contains(r) && WindowGlowBuilder.NeedsRebuild(r)).ToList();
                if (unbaked.Count == 0 && glowOnly.Count == 0) return 0;

                var log = new List<string>();
                if (unbaked.Count > 0)
                {
                    float? screenPixelsPerUnit = BakedPixelsPerUnit();
                    if (screenPixelsPerUnit == null)
                    {
                        Debug.LogWarning("[VillagePrefabFixer] 이미 구운 건물이 없어 축소 비율을 정하지 못했습니다. DesignScene에서 'GN3/Pixel/고품질 축소본 굽기'를 눌러 주세요.");
                        return 0;
                    }
                    foreach (var renderer in unbaked)
                    {
                        PixelBaker.Bake(renderer, PixelBaker.FindSourcePath(renderer.sprite), screenPixelsPerUnit.Value);
                        UpdateShadowSprites(renderer);
                        log.Add($"굽기: {renderer.name}");
                    }
                }
                foreach (var renderer in glowOnly) log.Add($"창 불빛 다시: {renderer.name}");
                WindowGlowBuilder.Build(unbaked.Concat(glowOnly).ToList());

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                Debug.Log($"[VillagePrefabFixer] Village 프리팹 건물 {log.Count}개를 다른 건물처럼 맞췄습니다(축소본·그림자 그림·창문 불빛):\n- "
                          + string.Join("\n- ", log));
                return log.Count;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>Buildings 폴더 원본 PNG를 그대로 쓰는 건물 본체(그림자·창 불빛 자식은 같은 그림을 써서 뺀다).</summary>
        private static bool IsUnbakedBuilding(SpriteRenderer renderer)
        {
            if (renderer.sprite == null || renderer.GetComponent<ProjectedShadow>() != null || renderer.GetComponent<WindowGlow>() != null) return false;
            string path = AssetDatabase.GetAssetPath(renderer.sprite);
            return path.StartsWith(BuildingFolder) && path == PixelBaker.FindSourcePath(renderer.sprite);
        }

        /// <summary>이미 구운 건물·장식(Baked 바로 아래 PNG)이 가장 많이 쓰는 유닛당 픽셀 수. 모두 같은 화면 기준으로 구워져 있다.</summary>
        private static float? BakedPixelsPerUnit()
        {
            var values = new List<float>();
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { PixelBaker.BakedFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetDirectoryName(path).Replace('\\', '/') != PixelBaker.BakedFolder) continue; // 하위 폴더(WindowGlow 등) 제외
                if (AssetImporter.GetAtPath(path) is TextureImporter importer) values.Add(importer.spritePixelsPerUnit);
            }
            if (values.Count == 0) return null;
            return values.GroupBy(v => Mathf.Round(v * 100f) / 100f).OrderByDescending(g => g.Count()).First().Key;
        }

        /// <summary>그림자·쓸기 사본은 주인 그림을 그대로 쓰므로 구운 그림으로 바꾼다(ShadowBuilder가 하는 일과 같음).</summary>
        private static void UpdateShadowSprites(SpriteRenderer owner)
        {
            foreach (Transform child in owner.transform)
            {
                if (child.name != ShadowChildName && !child.name.StartsWith(SweepChildPrefix)) continue;
                var shadowRenderer = child.GetComponent<SpriteRenderer>();
                if (shadowRenderer != null) shadowRenderer.sprite = owner.sprite;
            }
        }
    }
}
