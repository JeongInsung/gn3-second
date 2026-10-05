using GN3.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace GN3.EditorTools
{
    /// <summary>
    /// 건물·장식마다 자식 "Shadow"(ProjectedShadow)를 붙여, 시간에 따라 길이·방향이 바뀌는 그림자를 만든다.
    /// 대상은 Buildings/Decorations 그림(원본 또는 PixelBaker가 구운 것)을 쓰는 오브젝트, ShadowCaster2D가 있는 것,
    /// 이미 Shadow 자식이 있는 것. 2D 조명 그림자는 끝없이 늘어나 길이를 못 정해서, ShadowCaster2D는 지운다.
    /// 다시 실행하면 갱신만 한다. 새 건물·장식은 놓고 이 메뉴(또는 "마을 프리팹 만들기")만 누르면 된다.
    /// </summary>
    public static class ShadowBuilder
    {
        private const string MaterialPath = "Assets/조정식/Shaders/ProjectedShadow.mat";
        private const string ShaderName = "GN3/ProjectedShadow";
        private const string ShadowChildName = "Shadow";
        private const float BuildingHeightScale = 0.5f;
        private const float DecorationHeightScale = 0.8f;
        private const float BenchLift = 0.12f;    // 월드 유닛. 가로등(약 0.9) 대비 앉는 높이 정도
        private const float FountainLift = 0.15f; // 대야 테두리 높이

        [MenuItem("GN3/Shadow/그림자 만들기 (현재 씬)")]
        public static void Build()
        {
            var material = EnsureMaterial();
            if (material == null) return;

            int built = 0;
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            foreach (var renderer in root.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (renderer.GetComponent<ProjectedShadow>() != null) continue; // 그림자 자신
                var caster = renderer.GetComponent<ShadowCaster2D>();
                var existing = renderer.transform.Find(ShadowChildName);
                // Buildings/Decorations 그림(원본 또는 구운 것)이면 ShadowCaster2D 없이도 그림자를 만든다(새로 놓은 건물 자동 처리).
                bool isProp = PixelBaker.FindSourcePath(renderer.sprite) != null;
                if (caster == null && existing == null && !isProp) continue;

                BuildShadow(renderer, existing, material);
                if (caster != null) Undo.DestroyObjectImmediate(caster);
                built++;
            }

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log($"[ShadowBuilder] 그림자 {built}개를 만들었습니다.");
        }

        private static void BuildShadow(SpriteRenderer owner, Transform existing, Material material)
        {
            GameObject shadowObject;
            bool isNew = existing == null;
            if (!isNew)
            {
                shadowObject = existing.gameObject;
            }
            else
            {
                shadowObject = new GameObject(ShadowChildName, typeof(SpriteRenderer), typeof(ProjectedShadow));
                Undo.RegisterCreatedObjectUndo(shadowObject, "Create Shadow");
                shadowObject.transform.SetParent(owner.transform, false);
            }

            var renderer = shadowObject.GetComponent<SpriteRenderer>();
            Undo.RecordObject(renderer, "Shadow");
            renderer.sharedMaterial = material;
            renderer.sprite = owner.sprite;
            var shadow = shadowObject.GetComponent<ProjectedShadow>();
            if (shadow == null) shadow = Undo.AddComponent<ProjectedShadow>(shadowObject);

            // 새로 만든 그림자에만 기본 높이 비율을 넣는다(손으로 조절한 값 보호).
            if (isNew)
            {
                Undo.RecordObject(shadow, "Shadow Height");
                ApplyDefaults(shadow, owner);
            }
        }

        /// <summary>
        /// 물체 종류별 기본값.
        /// - 건물: 위에서 내려다본 지붕이 그림의 절반쯤이라 높이 비율 0.5
        /// - 나무·가로등 같은 선 장식: 그림 대부분이 높이라 0.8
        /// - 벤치·분수: 위에서 똑바로 본 납작한 물체라 그림 세로가 바닥 위 길이다. 기울이면 위쪽 끝(벤치 위 다리)
        ///   그림자가 떨어져 나가서, 높이 비율 0 + 실제 높이(Lift)만큼 통째로 옮긴다.
        /// </summary>
        internal static void ApplyDefaults(ProjectedShadow shadow, SpriteRenderer owner)
        {
            if (owner.name.Contains("벤치"))
            {
                shadow.heightScale = 0f;
                shadow.liftHeight = BenchLift;
                return;
            }
            if (owner.name.Contains("분수"))
            {
                shadow.heightScale = 0f;
                shadow.liftHeight = FountainLift;
                return;
            }
            string source = PixelBaker.FindSourcePath(owner.sprite) ?? AssetDatabase.GetAssetPath(owner.sprite);
            shadow.heightScale = source.StartsWith("Assets/조정식/Buildings/") ? BuildingHeightScale : DecorationHeightScale;
            shadow.liftHeight = 0f;
        }

        private static Material EnsureMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material != null) return material;

            var shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                Debug.LogError($"[ShadowBuilder] 셰이더 '{ShaderName}'를 찾지 못했습니다.");
                return null;
            }
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, MaterialPath);
            return material;
        }
    }
}
