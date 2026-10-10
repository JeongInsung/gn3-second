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
    /// 성벽·울타리는 이어 붙인 조각이라 납작 그림자(통째로 밀기 + 쓸기 사본)로 한 띠처럼 이어지게 한다.
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
        private const string WallFolder = "Assets/조정식/성벽/";
        private const float WallLift = 0.6f;   // 성벽(가로·세로·모서리) 높이
        private const float GateLift = 0.9f;
        private const float TowerLift = 1.0f;
        private const string FenceFolder = "Assets/조정식/Decorations/울타리/";
        private const float FenceLift = 0.4f;  // 울타리 높이(가로 울타리 그림이 월드 약 0.5 — 위에서 본 윗면 빼고 0.4)
        private const int SweepSteps = 3;  // 납작 그림자를 몇 장으로 쓸어 채울지(간격 ≤ 1.0×1.5/3 < 세로벽 폭 0.69)
        private const string SweepChildName = "Shadow Sweep";

        [MenuItem("GN3/Shadow/그림자 만들기 (현재 씬)")]
        private static void BuildFromMenu()
        {
            if (VillagePrefabBuilder.RefuseInMainScene("그림자 만들기")) return;
            Build();
        }

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
            // 울타리는 예전에 장식 기본값(기울인 그림자)으로 만들어진 것도 성벽처럼 납작 그림자로 맞춘다.
            if (isNew || IsFencePiece(owner))
            {
                Undo.RecordObject(shadow, "Shadow Height");
                ApplyDefaults(shadow, owner);
            }
            if (shadow.heightScale <= 0f && shadow.liftHeight > 0f) BuildSweep(owner, shadow.liftHeight, material);
            else RemoveSweep(owner);
        }

        /// <summary>
        /// 납작 그림자(성벽·벤치·분수 등)는 실루엣을 lift만큼 통째로 밀어서, 미는 거리가 물체 두께보다 크면 본체와 그림자 사이가 떴다.
        /// lift의 1/N … (N-1)/N만큼 민 그림자를 더 깔아 발밑부터 끝까지 쓸어 채운다
        /// (셰이더 스텐실이 한 픽셀을 한 번만 칠해 겹쳐도 진하기는 같다). 다시 실행하면 이름으로 찾아 갱신만 한다.
        /// </summary>
        private static void BuildSweep(SpriteRenderer owner, float lift, Material material)
        {
            for (int k = 1; k < SweepSteps; k++)
            {
                string name = $"{SweepChildName} {k}";
                var existing = owner.transform.Find(name);
                GameObject sweepObject;
                if (existing != null) sweepObject = existing.gameObject;
                else
                {
                    sweepObject = new GameObject(name, typeof(SpriteRenderer), typeof(ProjectedShadow));
                    Undo.RegisterCreatedObjectUndo(sweepObject, "Create Shadow Sweep");
                    sweepObject.transform.SetParent(owner.transform, false);
                }
                var renderer = sweepObject.GetComponent<SpriteRenderer>();
                Undo.RecordObject(renderer, "Shadow Sweep");
                renderer.sharedMaterial = material;
                renderer.sprite = owner.sprite;
                var sweep = sweepObject.GetComponent<ProjectedShadow>();
                Undo.RecordObject(sweep, "Shadow Sweep");
                sweep.heightScale = 0f;
                sweep.liftHeight = lift * k / SweepSteps;
            }
        }

        /// <summary>납작 모드가 아니게 된 그림자에 남은 쓸기 사본을 지운다(기울인 그림자 위에 납작 사본이 겹치지 않게).</summary>
        private static void RemoveSweep(SpriteRenderer owner)
        {
            for (int k = 1; k < SweepSteps; k++)
            {
                var existing = owner.transform.Find($"{SweepChildName} {k}");
                if (existing != null) Undo.DestroyObjectImmediate(existing.gameObject);
            }
        }

        /// <summary>성벽 조각(원본이 성벽 폴더, 또는 구운 성문 열림 프레임처럼 "성벽_…" 정렬 부모 아래 그림).</summary>
        private static bool IsWallPiece(SpriteRenderer owner)
        {
            string source = PixelBaker.FindSourcePath(owner.sprite) ?? AssetDatabase.GetAssetPath(owner.sprite);
            return source.StartsWith(WallFolder) || owner.transform.parent != null && owner.transform.parent.name.StartsWith("성벽_");
        }

        /// <summary>가축 우리 울타리 조각(가로·세로·문·부러짐). 같은 폴더의 고삐 기둥·여물통은 울타리가 아니다.</summary>
        private static bool IsFencePiece(SpriteRenderer owner)
        {
            string source = PixelBaker.FindSourcePath(owner.sprite) ?? AssetDatabase.GetAssetPath(owner.sprite);
            return source.StartsWith(FenceFolder) && System.IO.Path.GetFileName(source).Contains("울타리");
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
            // 성벽은 조각을 이어 붙여 쌓아서, 조각마다 자기 발밑에서 기울이면 그림자가 톱니처럼 끊긴다.
            // 납작 모드로 모든 조각을 같은 방향·거리로 밀면 붙은 조각들 그림자가 한 띠로 이어진다.
            // (구운 성문 열림 프레임은 Baked/Animated라 원본 경로가 없어 이름으로 본다)
            if (IsWallPiece(owner))
            {
                shadow.heightScale = 0f;
                shadow.liftHeight = owner.name.Contains("탑") ? TowerLift : owner.name.Contains("성문") ? GateLift : WallLift;
                return;
            }
            // 울타리도 성벽처럼 조각을 이어 놓아서, 기울이면 조각마다 끊기고 세로 울타리(바닥에 길게 누운 그림)는 끝이 엉뚱하게 늘어난다.
            if (IsFencePiece(owner))
            {
                shadow.heightScale = 0f;
                shadow.liftHeight = FenceLift;
                return;
            }
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
