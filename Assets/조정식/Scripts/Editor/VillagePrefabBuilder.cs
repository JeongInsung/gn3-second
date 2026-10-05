using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace GN3.EditorTools
{
    /// <summary>
    /// DesignScene 최상위에 흩어진 마을 오브젝트(건물·장식·바닥·낮밤 조명·후처리 Volume)를 Village 루트 하나로 묶어
    /// 프리팹으로 저장하고, MainScene에 그 인스턴스를 넣는다. DesignScene의 마을도 프리팹 인스턴스가 되므로
    /// 이후 DesignScene에서 고치고 Apply하면 MainScene에도 반영된다.
    /// </summary>
    public static class VillagePrefabBuilder
    {
        private const string DesignScenePath = "Assets/조정식/DesignScene.unity";
        private const string MainScenePath = "Assets/Scenes/MainScene.unity";
        private const string PrefabFolder = "Assets/조정식/Prefabs";
        private const string PrefabPath = PrefabFolder + "/Village.prefab";
        private const string VillageName = "Village";

        // 마을에 넣지 않는 씬 전용 오브젝트(카메라는 씬마다 따로, Triangle은 테스트용)
        private static readonly HashSet<string> ExcludedRoots = new HashSet<string> { "Main Camera", "Triangle" };

        [MenuItem("GN3/Village/마을 프리팹 만들기 → MainScene 배치")]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var designCamera = BuildPrefabFromDesignScene();
            if (designCamera == null) return;
            PlaceInMainScene(designCamera.Value);
        }

        private struct CameraSettings
        {
            public Vector3 Position;
            public float OrthographicSize;
        }

        private static CameraSettings? BuildPrefabFromDesignScene()
        {
            var scene = EditorSceneManager.OpenScene(DesignScenePath, OpenSceneMode.Single);

            var cameraSettings = new CameraSettings { Position = new Vector3(0f, 0f, -10f), OrthographicSize = 5f };
            var village = FindRoot(scene, VillageName);
            if (village != null && PrefabUtility.IsPartOfPrefabInstance(village))
            {
                // 이미 프리팹이면: 최상위에 새로 놓은 것을 Village 아래로 넣고, Village 아래 모든 변경을 프리팹에 Apply한다.
                // (MainScene의 Village 인스턴스는 프리팹을 따라 자동으로 바뀐다)
                var added = MoveRootsInto(scene, village);
                ShadowBuilder.Build(); // 새 건물·장식에 그림자가 있어야 마을 캐릭터가 피해 다니고 앞뒤 가림이 맞는다
                PrefabUtility.ApplyPrefabInstance(village, InteractionMode.AutomatedAction);
                Debug.Log(added.Count > 0
                    ? $"[VillagePrefab] 새로 놓은 {added.Count}개를 Village에 넣고 프리팹에 반영했습니다:\n- " + string.Join("\n- ", added)
                    : "[VillagePrefab] Village 아래 변경을 프리팹에 반영했습니다(새로 넣은 최상위 오브젝트 없음).");
            }
            else
            {
                if (village == null)
                {
                    village = new GameObject(VillageName);
                    SceneManager.MoveGameObjectToScene(village, scene);
                    village.transform.SetAsFirstSibling();
                }

                var moved = MoveRootsInto(scene, village);

                if (!AssetDatabase.IsValidFolder(PrefabFolder))
                    AssetDatabase.CreateFolder(Path.GetDirectoryName(PrefabFolder).Replace('\\', '/'), Path.GetFileName(PrefabFolder));

                var prefab = PrefabUtility.SaveAsPrefabAssetAndConnect(village, PrefabPath, InteractionMode.AutomatedAction, out bool success);
                if (!success || prefab == null)
                {
                    Debug.LogError($"[VillagePrefab] 프리팹 저장 실패: {PrefabPath}. DesignScene은 저장하지 않았습니다.");
                    return null;
                }
                Debug.Log($"[VillagePrefab] {moved.Count}개 오브젝트를 {PrefabPath}로 묶었습니다:\n- " + string.Join("\n- ", moved));
            }

            var designCamera = FindRoot(scene, "Main Camera");
            if (designCamera != null && designCamera.TryGetComponent(out Camera cam))
            {
                cameraSettings.Position = designCamera.transform.position;
                cameraSettings.OrthographicSize = cam.orthographicSize;
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            return cameraSettings;
        }

        private static void PlaceInMainScene(CameraSettings cameraSettings)
        {
            var scene = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);

            // 마을 프리팹 안의 Global Light 2D(DayNightCycle이 조종)만 남긴다. 둘이면 URP가 하나만 쓰고 경고를 낸다.
            var oldGlobalLight = FindRoot(scene, "Global Light 2D");
            if (oldGlobalLight != null)
            {
                Object.DestroyImmediate(oldGlobalLight);
                Debug.Log("[VillagePrefab] MainScene의 기존 Global Light 2D를 지웠습니다(마을 프리팹 안의 것을 사용).");
            }

            if (FindRoot(scene, VillageName) == null)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                instance.transform.position = Vector3.zero;
                instance.transform.SetAsFirstSibling();
                Debug.Log("[VillagePrefab] MainScene에 Village 프리팹을 넣었습니다.");
            }
            else
            {
                Debug.Log("[VillagePrefab] MainScene에 이미 Village가 있어 다시 넣지 않았습니다.");
            }

            var mainCamera = FindRoot(scene, "Main Camera");
            if (mainCamera != null)
            {
                mainCamera.transform.position = cameraSettings.Position;
                if (mainCamera.TryGetComponent(out Camera cam)) cam.orthographicSize = cameraSettings.OrthographicSize;
                // 블룸·비네트·밤 색감(Lighting Volume)이 보이려면 카메라 후처리가 켜져 있어야 한다.
                if (mainCamera.TryGetComponent(out UniversalAdditionalCameraData data)) data.renderPostProcessing = true;
                Debug.Log("[VillagePrefab] MainScene 카메라 위치·크기를 DesignScene에 맞추고 후처리를 켰습니다.");
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        /// <summary>씬 최상위 오브젝트 중 Village·제외 목록이 아닌 것을 Village 아래로 옮긴다(월드 위치 유지). 옮긴 이름 목록.</summary>
        private static List<string> MoveRootsInto(Scene scene, GameObject village)
        {
            var moved = new List<string>();
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root == village || ExcludedRoots.Contains(root.name)) continue;
                root.transform.SetParent(village.transform, true); // 순서대로 붙여 형제 순서 유지
                moved.Add(root.name);
            }
            return moved;
        }

        private static GameObject FindRoot(Scene scene, string name)
        {
            foreach (var root in scene.GetRootGameObjects())
                if (root.name == name) return root;
            return null;
        }
    }
}
