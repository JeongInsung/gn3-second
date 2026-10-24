using System.Linq;
using GN3.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace GN3.EditorTools
{
    /// <summary>
    /// Village 프리팹 안에 [건설] 창 [장식품](BuildingCatalog.Decorations)의 원본을 만든다: 꺼 둔 "장식 원본" 그룹 아래 "장식:id"마다
    /// 빈 루트(놓는 자리 = 발밑) → 그림 → 그림자(ShadowBuilder). 놓으면 VillageLayoutApplier가 원본을 복제한다.
    /// 성벽은 꺼 둔 "성벽" 그룹에서 조각 하나씩, 소품 6종(Decorations/소품, tools/bake_prop_sheet.py)은 PixelBaker로 구워 쓴다.
    /// 다시 돌리면 그룹을 통째로 새로 만든다(놓은 장식은 원본 이름으로 다시 복제되므로 그대로).
    /// </summary>
    public static class DecorTemplateBuilder
    {
        private const string PrefabPath = "Assets/조정식/Prefabs/Village.prefab";
        private const string GroupName = "장식 원본";
        private const string WallGroupName = "성벽";
        private const string Baked = "Assets/조정식/Baked/";
        private const string Fences = "Assets/조정식/Decorations/울타리/";
        private const string Props = "Assets/조정식/Decorations/소품/";
        private const string LampReference = Baked + "팔각 석대의 호박빛 철등-4@97x97.png"; // 구운 그림의 px/유닛을 여기서 읽는다
        private const int ArtSortingOrder = 1;

        private static readonly Vector3 FenceScale = new Vector3(0.2996f, 0.3f, 1f);         // 예전 가축 울타리(가로) 배치 값
        private static readonly Vector3 SideFenceScale = new Vector3(0.16f, 0.1485f, 1f);    // 세로
        private static readonly Vector3 TroughScale = new Vector3(0.22f, 0.22f, 1f);

        // 예전 철등의 불빛(Lamp Light 넓게 · Lamp Glow 등불 둘레) — DayNightCycle이 이름으로 찾아 밤에 켠다
        private static readonly Color LampColor = new Color(1f, 0.72f, 0.35f);

        [MenuItem("GN3/Village/장식 원본 만들기")]
        public static void Build()
        {
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var old = root.transform.Find(GroupName);
                if (old != null) Object.DestroyImmediate(old.gameObject);
                var group = new GameObject(GroupName).transform;
                group.SetParent(root.transform, false);
                group.gameObject.SetActive(false);
                var material = FindArtMaterial(root);
                float ppu = AssetImporter.GetAtPath(LampReference) is TextureImporter lamp ? lamp.spritePixelsPerUnit : 100f;

                // 소품 6종: (id, 파일, 목표 높이 px — 철등 97px 기준, 불빛 자리(그림 안 비율))
                Prop(group, material, ppu, "crate", "나무 상자", 60f);
                Prop(group, material, ppu, "barrel", "술통", 62f);
                var post = Prop(group, material, ppu, "lanternpost", "랜턴 기둥", 104f);
                AddLamp(post, new Vector2(0.85f, 0.53f), 1.2f, 0.3f);
                var fire = Prop(group, material, ppu, "campfire", "모닥불", 50f);
                AddLamp(fire, new Vector2(0.5f, 0.45f), 1.4f, 0.4f);
                Prop(group, material, ppu, "banner", "깃발", 104f);
                Prop(group, material, ppu, "sacks", "자루", 52f);

                // 마을 소품
                var fountain = Art(group, material, "fountain", "푸른 물의 원형 석조 분수", FirstSprite(Baked + "Animated/분수 루프@82x76.png"), Vector3.one);
                AddAnimator(fountain, "Assets/조정식/Animations/Fountain/Fountain.controller");
                var lampArt = Art(group, material, "lamp", "팔각 석대의 호박빛 철등", Load(LampReference), Vector3.one);
                AddLight(lampArt.transform, "Lamp Light", new Vector2(0f, 0.15f), 0.25f, 1.2f, 0.6f);
                AddLight(lampArt.transform, "Lamp Glow", new Vector2(0f, 0.70186f), 0f, 0.35f, 0.5f);
                var planter = Art(group, material, "planter", "꽃이 핀 나무 화분", FirstSprite(Baked + "Animated/꽃화분 바람@66x56.png"), Vector3.one);
                AddAnimator(planter, "Assets/조정식/Animations/Planter/Planter.controller");
                Art(group, material, "bench", "오크 나무 벤치", Load(Baked + "오크 나무 벤치 픽셀 스프라이트-3@76x76.png"), Vector3.one);

                // 울타리류(원본 그림을 예전 배치 크기로)
                Art(group, material, "fence", "울타리_2단", Load(Fences + "울타리_2단.png"), FenceScale);
                Art(group, material, "fence_moss", "울타리_2단_이끼", Load(Fences + "울타리_2단_이끼.png"), FenceScale);
                Art(group, material, "fence_moss2", "울타리_2단_이끼2", Load(Fences + "울타리_2단_이끼2.png"), FenceScale);
                Art(group, material, "fence_tall", "울타리_3단", Load(Fences + "울타리_3단.png"), FenceScale);
                Art(group, material, "fence_broken", "울타리_부러짐", Load(Fences + "울타리_부러짐.png"), FenceScale);
                Art(group, material, "fence_gate", "울타리_문", Load(Fences + "울타리_문.png"), FenceScale);
                Art(group, material, "fence_v", "세로 울타리_기본", Load(Fences + "세로 울타리_기본.png"), SideFenceScale);
                Art(group, material, "fence_v_moss", "세로 울타리_이끼", Load(Fences + "세로 울타리_이끼.png"), SideFenceScale);
                Art(group, material, "fence_v_broken", "세로 울타리_부러짐", Load(Fences + "세로 울타리_부러짐.png"), SideFenceScale);
                Art(group, material, "trough", "여물통", Load(Fences + "여물통.png"), TroughScale);
                Art(group, material, "hitchpost", "고삐 기둥", Load(Fences + "고삐 기둥.png"), TroughScale);

                Cow(group, material);

                // 성벽: 꺼 둔 성벽 그룹에서 조각 하나씩(그림자·납작 그림자 포함 그대로)
                var walls = root.transform.Find(WallGroupName);
                Wall(group, walls, "wall_h", "성벽_가로");
                Wall(group, walls, "wall_v", "성벽_세로");
                Wall(group, walls, "wall_corner_l", "성벽_모서리_왼쪽아래");
                Wall(group, walls, "wall_corner_r", "성벽_모서리_오른쪽아래");
                Wall(group, walls, "wall_tower", "성벽_탑");
                Wall(group, walls, "wall_gate", "성벽_성문");

                // 원본은 하나하나 꺼 둔다 → 복제본도 꺼진 채 나와 자리를 잡은 뒤에 켜진다
                foreach (Transform template in group) template.gameObject.SetActive(false);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                var missing = BuildingCatalog.Decorations.Where(e => group.Find(BuildingCatalog.DecorPrefix + e.Id) == null).Select(e => e.Id).ToList();
                Debug.Log($"[DecorTemplateBuilder] 장식 원본 {group.childCount}개를 만들었습니다." + (missing.Count > 0 ? $" 빠진 것: {string.Join(", ", missing)}" : ""));
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>빈 루트 "장식:id" → 그림(발밑이 루트에 오게) → 그림자.</summary>
        private static SpriteRenderer Art(Transform group, Material material, string id, string artName, Sprite sprite, Vector3 scale)
        {
            if (sprite == null)
            {
                Debug.LogWarning($"[DecorTemplateBuilder] 그림이 없습니다: {id}");
                return null;
            }
            var root = new GameObject(BuildingCatalog.DecorPrefix + id).transform;
            root.SetParent(group, false);
            var art = new GameObject(artName, typeof(SpriteRenderer));
            art.transform.SetParent(root, false);
            art.transform.localScale = scale;
            var renderer = art.GetComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            if (material != null) renderer.sharedMaterial = material;
            renderer.sortingOrder = ArtSortingOrder;
            SitOnRoot(renderer);
            ShadowBuilder.BuildFor(renderer);
            return renderer;
        }

        /// <summary>그림 아래끝 가운데가 루트(놓는 자리)에 오게.</summary>
        private static void SitOnRoot(SpriteRenderer renderer)
        {
            var b = renderer.sprite.bounds;
            var s = renderer.transform.localScale;
            renderer.transform.localPosition = new Vector3(-b.center.x * s.x, -b.min.y * s.y, 0f);
        }

        /// <summary>소품 시트 그림: 원본(발밑 가운데 기준점)을 목표 높이로 놓고 PixelBaker로 구운 뒤 그림자를 만든다.</summary>
        private static SpriteRenderer Prop(Transform group, Material material, float ppu, string id, string file, float heightPx)
        {
            string path = Props + file + ".png";
            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                if (settings.spriteAlignment != (int)SpriteAlignment.BottomCenter || importer.textureType != TextureImporterType.Sprite)
                {
                    importer.textureType = TextureImporterType.Sprite;
                    importer.spriteImportMode = SpriteImportMode.Single;
                    importer.ReadTextureSettings(settings);
                    settings.spriteAlignment = (int)SpriteAlignment.BottomCenter;
                    importer.SetTextureSettings(settings);
                    importer.alphaIsTransparency = true;
                    importer.SaveAndReimport();
                }
            }
            var sprite = Load(path);
            if (sprite == null)
            {
                Debug.LogWarning($"[DecorTemplateBuilder] 소품 그림이 없습니다: {path} (tools/bake_prop_sheet.py)");
                return null;
            }
            float scale = heightPx / ppu / sprite.bounds.size.y;
            var renderer = Art(group, material, id, file, sprite, new Vector3(scale, scale, 1f));
            // 구운 그림으로 바꾸고(크기 1) 발밑을 다시 맞춘 뒤 그림자도 새 그림으로
            PixelBaker.Bake(renderer, path, ppu);
            SitOnRoot(renderer);
            var shadow = renderer.transform.Find("Shadow");
            if (shadow != null) Object.DestroyImmediate(shadow.gameObject);
            ShadowBuilder.BuildFor(renderer);
            return renderer;
        }

        /// <summary>그림 안 비율 자리(기준점 = 아래 가운데)에 등불 두 개.</summary>
        private static void AddLamp(SpriteRenderer art, Vector2 at, float reach, float glow)
        {
            if (art == null) return;
            var size = art.sprite.bounds.size;
            var local = new Vector2((at.x - 0.5f) * size.x, at.y * size.y);
            AddLight(art.transform, "Lamp Light", local, reach * 0.2f, reach, 0.6f);
            AddLight(art.transform, "Lamp Glow", local, 0f, glow, 0.5f);
        }

        private static void AddLight(Transform parent, string name, Vector2 local, float inner, float outer, float falloff)
        {
            var go = new GameObject(name, typeof(Light2D));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            var light = go.GetComponent<Light2D>();
            light.lightType = Light2D.LightType.Point;
            light.pointLightInnerRadius = inner;
            light.pointLightOuterRadius = outer;
            light.falloffIntensity = falloff;
            light.color = LampColor;
            light.intensity = 0f; // DayNightCycle이 밤에 올린다
        }

        private static void AddAnimator(SpriteRenderer art, string controllerPath)
        {
            if (art == null) return;
            var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(controllerPath);
            if (controller == null) Debug.LogWarning($"[DecorTemplateBuilder] 애니메이터가 없습니다: {controllerPath}");
            else art.gameObject.AddComponent<Animator>().runtimeAnimatorController = controller;
        }

        /// <summary>소: 루트(VillageDecorCow) → Home(거니는 몸) → Visual(SortingGroup) → 그림, Home 아래 그림자(길을 막지 않음).</summary>
        private static void Cow(Transform group, Material material)
        {
            var frames = AssetDatabase.LoadAllAssetsAtPath(Baked + "Animated/소 걷기@98x68.png").OfType<Sprite>().OrderBy(SpriteIndex).ToArray();
            var idle = FirstSprite(Baked + "Animated/소 서기@94x67.png");
            if (frames.Length == 0 || idle == null)
            {
                Debug.LogWarning("[DecorTemplateBuilder] 소 그림이 없습니다.");
                return;
            }
            var root = new GameObject(BuildingCatalog.DecorPrefix + "cow", typeof(VillageDecorCow)).transform;
            root.SetParent(group, false);
            var home = new GameObject("Home").transform;
            home.SetParent(root, false);
            var visual = new GameObject("Visual", typeof(SortingGroup));
            visual.transform.SetParent(home, false);
            visual.GetComponent<SortingGroup>().sortingOrder = ArtSortingOrder;
            var art = new GameObject("소", typeof(SpriteRenderer));
            art.transform.SetParent(visual.transform, false);
            var renderer = art.GetComponent<SpriteRenderer>();
            renderer.sprite = idle;
            if (material != null) renderer.sharedMaterial = material;
            SitOnRoot(renderer);

            var shadowMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/조정식/Shaders/ProjectedShadow.mat");
            var shadowGO = new GameObject("Shadow", typeof(SpriteRenderer));
            shadowGO.transform.SetParent(home, false);
            shadowGO.GetComponent<SpriteRenderer>().sharedMaterial = shadowMaterial;
            var shadow = shadowGO.AddComponent<ProjectedShadow>();
            shadow.sourceOverride = renderer;
            shadow.groundAnchor = home;
            shadow.heightScale = 0.5f; // VillagePenCow와 같음

            root.GetComponent<VillageDecorCow>().Configure(home, renderer, frames, idle);
        }

        /// <summary>꺼 둔 성벽 그룹의 조각 하나를 원본으로 복제한다(감싼 SortingGroup은 떼고 루트는 0에).</summary>
        private static void Wall(Transform group, Transform walls, string id, string pieceName)
        {
            var piece = walls == null ? null : walls.Cast<Transform>().FirstOrDefault(t => t.name == pieceName
                && !t.GetComponentsInChildren<MonoBehaviour>(true).Any(c => c != null && c.GetType().Name == "CastleGate")); // 여닫는 아래 성문은 빼고 닫힌 것
            if (piece == null)
            {
                Debug.LogWarning($"[DecorTemplateBuilder] 성벽 조각이 없습니다: {pieceName}");
                return;
            }
            var copy = Object.Instantiate(piece.gameObject, group);
            copy.name = BuildingCatalog.DecorPrefix + id;
            copy.SetActive(true);
            var sortingGroup = copy.GetComponent<SortingGroup>();
            if (sortingGroup != null) Object.DestroyImmediate(sortingGroup);
            // 그림(그림자가 아닌 첫 그림)의 발밑이 루트에 오게 — 꺼진 그룹 안이라 renderer.bounds 대신 그림 범위로 잰다
            var art = copy.GetComponentsInChildren<SpriteRenderer>(true).FirstOrDefault(r => r.GetComponent<ProjectedShadow>() == null);
            copy.transform.localPosition = Vector3.zero;
            if (art != null && art.sprite != null)
            {
                var b = art.sprite.bounds;
                var foot = art.transform.TransformPoint(new Vector3(b.center.x, b.min.y, 0f));
                var offset = new Vector3(foot.x, foot.y, 0f) - copy.transform.position;
                foreach (Transform child in copy.transform) child.position -= offset;
            }
        }

        /// <summary>그림자가 아닌 첫 그림의 재질(Sprite-Lit-Default — 낮밤 빛을 받는다).</summary>
        private static Material FindArtMaterial(GameObject village)
        {
            var renderer = village.GetComponentsInChildren<SpriteRenderer>(true)
                .FirstOrDefault(r => r.GetComponent<ProjectedShadow>() == null && r.sharedMaterial != null && r.sharedMaterial.name.Contains("Lit"));
            return renderer != null ? renderer.sharedMaterial : null;
        }

        private static Sprite Load(string path) => AssetDatabase.LoadAssetAtPath<Sprite>(path);

        private static Sprite FirstSprite(string path) =>
            AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().OrderBy(SpriteIndex).FirstOrDefault();

        /// <summary>"이름_3" → 3 (시트 조각 순서).</summary>
        private static int SpriteIndex(Sprite sprite)
        {
            int underscore = sprite.name.LastIndexOf('_');
            return underscore >= 0 && int.TryParse(sprite.name.Substring(underscore + 1), out int n) ? n : 0;
        }
    }
}
