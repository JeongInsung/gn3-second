using System.Linq;
using GN3.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GN3.EditorTools
{
    /// <summary>
    /// 활성 씬의 Village 아래에 "Ambience"(꽃잎·반딧불 파티클, 구름 그림자, 새·나비)를 만들고 재질을 넣어 준다.
    /// Village가 프리팹 인스턴스면 Ambience 쪽 변경만 프리팹에 Apply한다(다른 오버라이드는 그대로 둔다) → MainScene에도 반영.
    /// 실제 파티클·구름·생물은 Play 때 각 컴포넌트가 코드로 만든다.
    /// </summary>
    public static class AmbienceBuilder
    {
        private const string VillageName = "Village";
        private const string AmbienceName = "Ambience";
        private const string LitSpriteMaterialPath = "Packages/com.unity.render-pipelines.universal/Runtime/Materials/Sprite-Lit-Default.mat";
        private const string UnlitSpriteMaterialPath = "Packages/com.unity.render-pipelines.universal/Runtime/Materials/Sprite-Unlit-Default.mat";

        [MenuItem("GN3/World/마을 분위기(파티클·구름·새) 만들기")]
        public static void Build()
        {
            var scene = SceneManager.GetActiveScene();
            var village = scene.GetRootGameObjects().FirstOrDefault(go => go.name == VillageName);
            if (village == null)
            {
                Debug.LogError($"[Ambience] 활성 씬에 '{VillageName}' 오브젝트가 없습니다.");
                return;
            }

            var lit = AssetDatabase.LoadAssetAtPath<Material>(LitSpriteMaterialPath);
            var unlit = AssetDatabase.LoadAssetAtPath<Material>(UnlitSpriteMaterialPath);

            var ambienceTransform = village.transform.Find(AmbienceName);
            GameObject ambience;
            if (ambienceTransform == null)
            {
                ambience = new GameObject(AmbienceName);
                Undo.RegisterCreatedObjectUndo(ambience, "Ambience");
                ambience.transform.SetParent(village.transform, false);
            }
            else
            {
                ambience = ambienceTransform.gameObject;
            }

            GetOrAdd<AmbientParticles>(ambience).Configure(lit, unlit);
            GetOrAdd<CloudShadows>(ambience).Configure(unlit);
            GetOrAdd<VillageCritters>(ambience).Configure(lit);
            foreach (var component in ambience.GetComponents<MonoBehaviour>()) EditorUtility.SetDirty(component);

            if (PrefabUtility.IsPartOfPrefabInstance(village)) ApplyAmbienceToPrefab(village, ambience);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[Ambience] '{VillageName}/{AmbienceName}'에 꽃잎·반딧불·구름 그림자·새·나비를 설정했습니다.");
        }

        private static T GetOrAdd<T>(GameObject go) where T : Component
        {
            var component = go.GetComponent<T>();
            return component != null ? component : Undo.AddComponent<T>(go);
        }

        private static void ApplyAmbienceToPrefab(GameObject village, GameObject ambience)
        {
            string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(village);
            foreach (var added in PrefabUtility.GetAddedGameObjects(village))
                if (added.instanceGameObject == ambience) added.Apply(path, InteractionMode.AutomatedAction);
            foreach (var added in PrefabUtility.GetAddedComponents(village))
                if (added.instanceComponent.gameObject == ambience) added.Apply(path, InteractionMode.AutomatedAction);
            foreach (var changed in PrefabUtility.GetObjectOverrides(village, false))
                if (changed.instanceObject is Component c && c.gameObject == ambience) changed.Apply(path, InteractionMode.AutomatedAction);
        }
    }
}
