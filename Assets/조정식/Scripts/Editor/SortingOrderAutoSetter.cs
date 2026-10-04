using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace GN3.EditorTools
{
    /// <summary>
    /// 씬에 새로 놓는 오브젝트의 Order in Layer를 폴더 규칙대로 자동으로 맞춘다.
    /// PNG/Tile 에셋에는 sortingOrder가 없어서(씬의 렌더러에만 있다) 놓는 순간 값을 넣는다.
    ///
    /// - Buildings / Decorations 폴더의 스프라이트 → 1
    /// - 새로 만든 Tilemap(바닥) → -1 (FloorTileSetup.FloorSortingOrder 와 같은 값)
    ///
    /// 새로 만든 오브젝트만 바꾸고, 이미 있던 오브젝트는 order가 기본값(0)일 때만 바꾼다(손으로 정한 값 보호).
    /// </summary>
    [InitializeOnLoad]
    public static class SortingOrderAutoSetter
    {
        private const string BuildingFolder = "Assets/조정식/Buildings/";
        private const string DecorationFolder = "Assets/조정식/Decorations/";
        private const int PropOrder = 1;
        private const int FloorOrder = -1;

        static SortingOrderAutoSetter()
        {
            ObjectChangeEvents.changesPublished += OnChangesPublished;
            DragAndDrop.AddDropHandlerV2(OnSceneDrop);
        }

        /// <summary>
        /// 폴더 그림을 바닥 Tilemap "위에" 떨어뜨리면 Unity 기본 동작은 스프라이트를 만들지 않고
        /// 그 텍스처로 머티리얼을 만들어 바닥에 씌운다(장식이 안 보이고 바닥 머티리얼이 바뀜).
        /// 그래서 Buildings/Decorations 그림 드롭, 그리고 어느 폴더 그림이든 바닥(Tilemap) 위 드롭은
        /// 여기서 가로채 항상 스프라이트 오브젝트로 만든다.
        /// </summary>
        private static DragAndDropVisualMode OnSceneDrop(Object dropUpon, Vector3 worldPosition, Vector2 viewportPosition, Transform parentForDraggedObjects, bool perform)
        {
            var dragged = DragAndDrop.objectReferences;
            if (dragged == null || dragged.Length == 0) return DragAndDropVisualMode.None;

            bool onFloor = dropUpon is GameObject target && target.GetComponentInParent<TilemapRenderer>() != null;
            var sprites = new Sprite[dragged.Length];
            for (int i = 0; i < dragged.Length; i++)
            {
                sprites[i] = dragged[i] as Sprite ?? AssetDatabase.LoadAssetAtPath<Sprite>(AssetDatabase.GetAssetPath(dragged[i]));
                if (sprites[i] == null) return DragAndDropVisualMode.None;
                if (!onFloor && !IsPropSprite(sprites[i])) return DragAndDropVisualMode.None; // 다른 드래그는 기본 동작
            }
            if (!perform) return DragAndDropVisualMode.Copy;

            var created = new Object[sprites.Length];
            for (int i = 0; i < sprites.Length; i++)
            {
                var go = new GameObject(sprites[i].name);
                Undo.RegisterCreatedObjectUndo(go, "Place " + sprites[i].name);
                if (parentForDraggedObjects != null) go.transform.SetParent(parentForDraggedObjects, true);
                go.transform.position = new Vector3(worldPosition.x, worldPosition.y, 0f);
                var renderer = go.AddComponent<SpriteRenderer>();
                renderer.sprite = sprites[i];
                if (IsPropSprite(sprites[i])) renderer.sortingOrder = PropOrder;
                created[i] = go;
            }
            Selection.objects = created;
            return DragAndDropVisualMode.Copy;
        }

        private static void OnChangesPublished(ref ObjectChangeEventStream stream)
        {
            for (int i = 0; i < stream.length; i++)
            {
                switch (stream.GetEventType(i))
                {
                    case ObjectChangeKind.CreateGameObjectHierarchy:
                        stream.GetCreateGameObjectHierarchyEvent(i, out var created);
                        Apply(EditorUtility.EntityIdToObject(created.instanceId), onlyIfDefault: false, includeTilemap: true);
                        break;
                    // 빈 오브젝트에 SpriteRenderer/Tilemap을 붙인 경우
                    case ObjectChangeKind.ChangeGameObjectStructure:
                        stream.GetChangeGameObjectStructureEvent(i, out var structure);
                        Apply(EditorUtility.EntityIdToObject(structure.instanceId), onlyIfDefault: true, includeTilemap: true);
                        break;
                    // 인스펙터에서 sprite를 폴더 그림으로 지정한 경우(타일 칠하기는 여기서 건드리지 않는다)
                    case ObjectChangeKind.ChangeGameObjectOrComponentProperties:
                        stream.GetChangeGameObjectOrComponentPropertiesEvent(i, out var props);
                        Apply(EditorUtility.EntityIdToObject(props.instanceId), onlyIfDefault: true, includeTilemap: false);
                        break;
                }
            }
        }

        private static void Apply(Object target, bool onlyIfDefault, bool includeTilemap)
        {
            var go = target as GameObject ?? (target as Component)?.gameObject;
            if (go == null) return;

            foreach (var renderer in go.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (renderer.sprite == null || !IsPropSprite(renderer.sprite)) continue;
                SetOrder(renderer, PropOrder, onlyIfDefault);
            }

            if (!includeTilemap) return;
            foreach (var renderer in go.GetComponentsInChildren<TilemapRenderer>(true))
                SetOrder(renderer, FloorOrder, onlyIfDefault);
        }

        private static bool IsPropSprite(Sprite sprite)
        {
            string path = AssetDatabase.GetAssetPath(sprite);
            return path.StartsWith(BuildingFolder) || path.StartsWith(DecorationFolder);
        }

        private static void SetOrder(Renderer renderer, int order, bool onlyIfDefault)
        {
            if (renderer.sortingOrder == order) return;
            if (onlyIfDefault && renderer.sortingOrder != 0) return;
            Undo.RecordObject(renderer, "Auto Sorting Order");
            renderer.sortingOrder = order;
        }
    }

    /// <summary>
    /// Buildings / Decorations 의 PNG는 항상 통짜 스프라이트(Single)로 임포트한다.
    /// Tiles 폴더에 잘못 넣었다 옮기면 128px 조각(Multiple) 설정이 .meta에 남아, 씬에 첫 조각만 그려져 안 보였다.
    /// PPU는 처음 임포트할 때만 기본값을 넣는다(장식마다 따로 정한 PPU 보호).
    /// 이 그림들은 화면에 7~16배 축소돼 그려져서, 밉맵 없이 Point로만 찍으면 화면 픽셀마다 아무 텍셀이나 집혀
    /// 도트가 깨지고 지글거렸다. 밉맵(미리 줄인 텍스처)을 켜고 Point는 유지해 또렷함은 지킨다.
    /// </summary>
    public class PropTextureImportRules : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            string path = assetPath.Replace('\\', '/');
            if (!path.EndsWith(".png")) return;
            if (!path.StartsWith("Assets/조정식/Buildings/") && !path.StartsWith("Assets/조정식/Decorations/")) return;

            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = true;
            // Box는 검은 윤곽선이 평균에 묻혀 반투명처럼 흐려졌다. Kaiser(선명) + 바이어스 -0.5가
            // 노이즈 없이 윤곽을 살리는 중간값이었다(-1은 점 노이즈가 다시 생김). 2026-10-03 비교 후 선택.
            importer.mipmapFilter = TextureImporterMipFilter.KaiserFilter;
            importer.mipMapBias = -0.5f;
            importer.alphaIsTransparency = true;
            if (importer.importSettingsMissing) importer.spritePixelsPerUnit = 100;
        }
    }
}
