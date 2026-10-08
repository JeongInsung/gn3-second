using System.Linq;
using GN3.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GN3.EditorTools
{
    /// <summary>
    /// 강아지 걷기 시트(4열×2행 8프레임, 오른쪽을 봄)를 화면 크기로 구워 Village 아래 "강아지" 그룹의 VillageDogs에 넣는다.
    /// 강아지는 Play 때 VillageDogs가 만든다. 그룹만 Village 프리팹에 Apply해 MainScene에도 나온다. 다시 누르면 다시 굽고 덮어쓴다.
    /// </summary>
    public static class DogBuilder
    {
        private const string SheetPath = "Assets/조정식/Animations/Dog/픽셀 강아지 걷기.png";
        private const string OutputName = "강아지 걷기";
        private const string GroupName = "강아지";
        private const string VillageName = "Village";
        private const string ShadowMaterialPath = "Assets/조정식/Shaders/ProjectedShadow.mat";
        private const float DogWidth = 0.4f; // 몸 폭(월드). 캐릭터 키 약 0.6, 꽃화분 폭 0.6과 비교해 정함
        private const int DogCount = 2;

        [MenuItem("GN3/Village/강아지 만들기")]
        public static void Build()
        {
            if (VillagePrefabBuilder.RefuseInMainScene("강아지 만들기")) return;
            var scene = SceneManager.GetActiveScene();
            var village = scene.GetRootGameObjects().FirstOrDefault(r => r.name == VillageName);
            if (village == null)
            {
                Debug.LogError($"[Dog] 활성 씬에 '{VillageName}' 오브젝트가 없습니다.");
                return;
            }
            var cam = Camera.main;
            if (cam == null || !cam.orthographic)
            {
                Debug.LogError("[Dog] 직교(Orthographic) Main Camera가 필요합니다.");
                return;
            }
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(SheetPath) == null)
            {
                Debug.LogError($"[Dog] 시트가 없습니다: {SheetPath}");
                return;
            }

            var settings = new SheetAnimationSettings
            {
                LogTag = "Dog",
                SheetPath = SheetPath,
                OutputName = OutputName,
                Columns = 4,
                Rows = 2,
                Anchor = SheetAnchor.BottomCenter, // 발밑을 루트(발 위치)에 맞춘다
            };
            var frames = SheetAnimationBuilder.BakeFramesAtWidth(settings, DogWidth);
            if (frames == null || frames.Length == 0)
            {
                Debug.LogError("[Dog] 프레임을 굽지 못했습니다.");
                return;
            }

            var existing = village.transform.Find(GroupName);
            GameObject group;
            if (existing != null) group = existing.gameObject;
            else
            {
                group = new GameObject(GroupName);
                Undo.RegisterCreatedObjectUndo(group, "강아지");
                group.transform.SetParent(village.transform, false);
            }
            if (!group.TryGetComponent<VillageDogs>(out var dogs)) dogs = group.AddComponent<VillageDogs>();
            dogs.Configure(frames, AssetDatabase.LoadAssetAtPath<Material>(ShadowMaterialPath), DogCount);
            EditorUtility.SetDirty(dogs);
            PrefabUtility.RecordPrefabInstancePropertyModifications(dogs);

            WallLayoutBuilder.ApplyGroupToPrefab(village, group);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            var rect = frames[0].rect;
            Debug.Log($"[Dog] 강아지 걷기 {frames.Length}프레임({rect.width}x{rect.height}px)을 '{VillageName}/{GroupName}'에 넣었습니다. Play 때 {DogCount}마리가 돌아다닙니다.");
        }
    }
}
