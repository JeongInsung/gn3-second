using System.Collections.Generic;
using System.IO;
using System.Linq;
using GN3.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace GN3.EditorTools
{
    /// <summary>
    /// 씬의 건물마다 "창 유리만 칠한 마스크"를 만들어 밤에 창문만 빛나게 한다.
    /// 창 사각형은 WindowRects.asset(사람이 정함)에서 읽고, 그 안에서 유리 픽셀만 고른다:
    /// 어두운 남색·회색 유리(꺼진 창)와 원래 주황빛으로 그려진 유리. 밝은 나무틀·창살·꽃은 빠진다.
    /// 마스크는 건물 스프라이트와 같은 크기·PPU·pivot이라 자식으로 그대로 겹친다.
    /// 건물을 다시 굽거나(PixelBaker) 창 사각형을 고치면 이 메뉴를 다시 누르면 된다.
    /// </summary>
    public static class WindowGlowBuilder
    {
        private const string RectsPath = "Assets/조정식/Settings/WindowRects.asset";
        private const string MaterialPath = "Assets/조정식/Shaders/WindowGlow.mat";
        private const string MaskFolder = PixelBaker.BakedFolder + "/WindowGlow";
        private const string GlowChildName = "WindowGlow";
        private const string BuildingFolder = "Assets/조정식/Buildings/";
        private const float SpillRadius = 0.45f;
        private const float ForgeLightRadius = 1.1f;
        private const string ForgeChildName = "ForgeFire";
        private const string FireMaterialPath = "Assets/조정식/Shaders/FireGlow.mat";
        private const string UnlitSpriteMaterialPath = "Packages/com.unity.render-pipelines.universal/Runtime/Materials/Sprite-Unlit-Default.mat";
        private const float LowerWindowLimit = 0.45f; // 이보다 아래(그림 높이 비율)에 있는 창만 바닥을 비춘다

        [MenuItem("GN3/Light/창문 불빛 만들기 (현재 씬)")]
        private static void BuildFromMenu()
        {
            if (VillagePrefabBuilder.RefuseInMainScene("창문 불빛 만들기")) return;
            Build();
        }

        public static void Build() => Build(null);

        /// <summary>only가 있으면 그 건물들만 만든다(새로 옮겨 온 건물만 — 다른 건물 불빛을 지우고 다시 만들지 않는다).</summary>
        public static void Build(ICollection<SpriteRenderer> only)
        {
            var rects = EnsureRects();
            var material = EnsureMaterial();
            if (material == null) return;
            SheetAnimationBuilder.EnsureFolder(MaskFolder);

            int built = 0;
            var log = new List<string>();
            // 처리 중에 기존 WindowGlow 자식을 지우고 다시 만들므로, 대상 건물을 먼저 모아 둔 뒤 처리한다
            // (모으면서 바로 처리하면 지워진 렌더러를 다시 건드려 MissingReferenceException이 났다).
            // only가 있으면 그 렌더러만(씬 밖, 예: 열어 둔 프리팹 안의 건물이어도 된다), 없으면 지금 씬 전체.
            var candidates = only ?? SceneManager.GetActiveScene().GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<SpriteRenderer>(true)).ToList();
            var targets = new List<(SpriteRenderer renderer, string key)>();
            foreach (var candidate in candidates)
            {
                // 그림자(같은 건물 그림을 씀)와 창 불빛 자신은 건너뛴다.
                if (candidate.GetComponent<ProjectedShadow>() != null || candidate.GetComponent<WindowGlow>() != null) continue;
                string key = BuildingKey(candidate.sprite);
                if (key != null) targets.Add((candidate, key));
            }

            foreach (var (renderer, sourceName) in targets)
            {
                var entry = rects.Find(sourceName);
                if (entry == null || entry.windows.Count == 0)
                {
                    log.Add($"{sourceName}: 창 사각형 없음(WindowRects.asset에 추가하세요)");
                    continue;
                }

                var mask = BuildMask(renderer.sprite, entry, false, out int glassPixels);
                var maskSprite = SaveMask(mask, renderer.sprite, sourceName);
                BuildGlowObject(renderer, maskSprite, material, entry);
                if (entry.forgeIndices.Count > 0)
                {
                    var fireMask = BuildMask(renderer.sprite, entry, true, out int firePixels);
                    var fireSprite = SaveMask(fireMask, renderer.sprite, sourceName + "_forge");
                    BuildForgeFire(renderer, fireSprite, entry);
                    log.Add($"{sourceName}: 화덕 불꽃 {firePixels}px");
                }
                log.Add($"{sourceName}: 창 {entry.windows.Count}개, 유리 {glassPixels}px");
                built++;
            }

            if (only == null) EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log($"[WindowGlow] 건물 {built}채에 창문 불빛을 만들었습니다.\n" + string.Join("\n", log));
        }

        /// <summary>
        /// 창 불빛을 다시 만들어야 하는 건물인지: WindowRects에 창이 있는데 불빛 자식이 없거나, 불빛 마스크 크기가 건물 그림과 다르다
        /// (건물만 다시 구워 크기·scale이 바뀌면 옛 마스크가 커져 창 밖으로 떠 보인다). 그림자·불빛 자신과 애니메이션 조각은 대상이 아니다.
        /// </summary>
        public static bool NeedsRebuild(SpriteRenderer building)
        {
            if (building.sprite == null || building.GetComponent<ProjectedShadow>() != null || building.GetComponent<WindowGlow>() != null) return false;
            if (PixelBaker.FindSourcePath(building.sprite) == null) return false;
            string key = BuildingKey(building.sprite);
            var entry = key != null ? EnsureRects().Find(key) : null;
            if (entry == null || entry.windows.Count == 0) return false;
            var glow = building.transform.Find(GlowChildName);
            var glowRenderer = glow != null ? glow.GetComponent<SpriteRenderer>() : null;
            if (glowRenderer == null || glowRenderer.sprite == null) return true;
            return glowRenderer.sprite.rect.size != building.sprite.rect.size;
        }

        /// <summary>
        /// WindowRects에서 찾을 건물 이름. Buildings 폴더 원본이면 파일 이름, 애니메이션 스트립 조각(Baked/Animated)이면
        /// 조각 이름에서 "_번호"를 뗀 이름(예: "대장간 연기_0" → "대장간 연기"). 그 밖의 그림은 건물이 아니라 null.
        /// </summary>
        private static string BuildingKey(Sprite sprite)
        {
            string source = PixelBaker.FindSourcePath(sprite);
            if (source != null) return source.StartsWith(BuildingFolder) ? Path.GetFileNameWithoutExtension(source) : null;
            if (sprite == null || !AssetDatabase.GetAssetPath(sprite).StartsWith(SheetAnimationBuilder.BakedAnimatedFolder + "/")) return null;
            int underscore = sprite.name.LastIndexOf('_');
            return underscore > 0 ? sprite.name.Substring(0, underscore) : sprite.name;
        }

        /// <summary>
        /// 창 사각형 안의 유리 픽셀만 불투명(흰색)으로 칠한 마스크 텍스처. 스프라이트 조각(sprite.rect) 영역만 읽어
        /// 애니메이션 스트립(대장간)에서도 한 프레임 크기의 마스크가 된다.
        /// forge가 false면 창(유리)만, true면 화덕 사각형의 불꽃 색(흰 노랑 심지 포함)만 칠한다.
        /// </summary>
        internal static Texture2D BuildMask(Sprite sprite, WindowRectSet.Building entry, bool forgeOnly, out int glassPixels)
        {
            var source = new Texture2D(2, 2);
            source.LoadImage(File.ReadAllBytes(AssetDatabase.GetAssetPath(sprite)));
            var rect = sprite.rect;
            int width = (int)rect.width, height = (int)rect.height;
            var pixels = source.GetPixels((int)rect.x, (int)rect.y, width, height);
            Object.DestroyImmediate(source);

            var mask = new Color[pixels.Length];
            glassPixels = 0;
            for (int i = 0; i < entry.windows.Count; i++)
            {
                var window = entry.windows[i];
                bool forge = entry.forgeIndices.Contains(i);
                if (forge != forgeOnly) continue;
                int x0 = Mathf.FloorToInt(window.xMin * width), x1 = Mathf.CeilToInt(window.xMax * width);
                int y0 = Mathf.FloorToInt(window.yMin * height), y1 = Mathf.CeilToInt(window.yMax * height);
                for (int y = Mathf.Max(0, y0); y < Mathf.Min(height, y1); y++)
                for (int x = Mathf.Max(0, x0); x < Mathf.Min(width, x1); x++)
                {
                    int p = y * width + x;
                    if (!(forge ? IsFire(pixels[p]) : IsGlass(pixels[p], entry.warmGlass))) continue;
                    mask[p] = Color.white;
                    glassPixels++;
                }
            }

            var result = new Texture2D(width, height, TextureFormat.RGBA32, false);
            result.SetPixels(mask);
            result.Apply();
            return result;
        }

        /// <summary>
        /// 꺼진 창의 어두운 남색·회색 유리, 또는 원래 주황빛 불이 그려진 유리.
        /// warmGlass(건물별 설정)면 붉은 유리(뒷골목 건물의 붉은 격자·커튼 창)와 아주 어두운 갈색 유리
        /// (낡은 마을 건물: 어두운 안쪽에 주황 창살 — 창살은 밝아서 빠지고 사이 유리만 남는다)도 넣는다.
        /// 기존 건물은 이 규칙을 켜면 창틀까지 빛나 넓어지므로 건물별로만 켠다.
        /// </summary>
        private static bool IsGlass(Color c, bool warmGlass)
        {
            if (c.a < 0.5f) return false;
            float luma = 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
            bool darkCoolGlass = luma < 0.42f && c.b >= c.r * 0.85f;          // 나무(빨강 > 파랑)는 빠진다
            bool litAmberGlass = c.r > 0.7f && c.g > 0.35f && c.b < 0.45f && luma > 0.45f;
            if (darkCoolGlass || litAmberGlass) return true;
            if (!warmGlass) return false;
            bool redGlass = c.r > 0.55f && c.g < 0.3f && c.b < 0.3f;
            bool darkWarmGlass = luma < 0.2f;
            return redGlass || darkWarmGlass;
        }

        /// <summary>화덕 불꽃: 주황·노랑·흰 노랑 심지(빨강이 파랑보다 확실히 크고 밝다). 검은 석탄·벽돌은 빠진다.</summary>
        private static bool IsFire(Color c) => c.a >= 0.5f && c.r > 0.6f && c.r > c.b + 0.2f && c.g > 0.2f;

        private static Sprite SaveMask(Texture2D mask, Sprite building, string sourceName)
        {
            string path = $"{MaskFolder}/{sourceName}.png";
            File.WriteAllBytes(path, mask.EncodeToPNG());
            Object.DestroyImmediate(mask);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.alphaIsTransparency = true;
            importer.spritePixelsPerUnit = building.pixelsPerUnit;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.Custom;
            settings.spritePivot = new Vector2(building.pivot.x / building.rect.width, building.pivot.y / building.rect.height);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static void BuildGlowObject(SpriteRenderer building, Sprite mask, Material material, WindowRectSet.Building entry)
        {
            var existing = building.transform.Find(GlowChildName);
            if (existing != null) Undo.DestroyObjectImmediate(existing.gameObject);

            var glowObject = new GameObject(GlowChildName, typeof(SpriteRenderer));
            Undo.RegisterCreatedObjectUndo(glowObject, "Window Glow");
            glowObject.transform.SetParent(building.transform, false);

            var renderer = glowObject.GetComponent<SpriteRenderer>();
            renderer.sprite = mask;
            renderer.sharedMaterial = material;
            renderer.sortingLayerID = building.sortingLayerID;
            renderer.sortingOrder = building.sortingOrder + 1;

            // 아래층 창 앞 바닥을 비추는 작은 빛. 지붕 박공 창은 바닥에서 멀어 빼고, 창 바로 아래 건물 앞에 둔다.
            // 화덕은 ForgeFire가 따로 비추므로 뺀다.
            var bounds = building.sprite.bounds;
            var lights = new List<Light2D>();
            for (int i = 0; i < entry.windows.Count; i++)
            {
                var window = entry.windows[i];
                if (window.center.y > LowerWindowLimit) continue;
                if (entry.forgeIndices.Contains(i)) continue;
                var local = new Vector3(Mathf.Lerp(bounds.min.x, bounds.max.x, window.center.x), bounds.min.y + 0.12f, 0f);
                var lightObject = new GameObject("Window Spill");
                lightObject.transform.SetParent(glowObject.transform, false);
                lightObject.transform.localPosition = local;
                var light = lightObject.AddComponent<Light2D>();
                light.lightType = Light2D.LightType.Point;
                light.pointLightInnerRadius = 0f;
                light.pointLightOuterRadius = SpillRadius;
                light.falloffIntensity = 0.7f;
                light.color = new Color(1f, 0.72f, 0.4f);
                light.shadowsEnabled = false;
                DayNightCycle.RebuildLightMesh(light);
                lights.Add(light);
            }

            var glow = glowObject.AddComponent<WindowGlow>();
            // 집마다 켜지는 시점을 조금씩 다르게(이름으로 고정된 값이라 다시 만들어도 같다)
            float delay = Mathf.Abs(building.name.GetHashCode() % 100) / 100f * 0.3f;
            glow.Configure(delay, DefaultBedtime(building.name), lights);
        }

        /// <summary>
        /// 화덕 불: 불꽃 마스크(GN3/FireGlow로 도트가 위로 일렁임) + 화덕 앞 깜빡이는 조명 + 화덕 입구에서 위로 튀는 불티.
        /// 불티는 1~2px 정사각(언릿 스프라이트 머티리얼의 흰 사각형)이라 도트 그림과 어울린다.
        /// </summary>
        private static void BuildForgeFire(SpriteRenderer building, Sprite fireMask, WindowRectSet.Building entry)
        {
            var existing = building.transform.Find(ForgeChildName);
            if (existing != null) Undo.DestroyObjectImmediate(existing.gameObject);

            var fireObject = new GameObject(ForgeChildName, typeof(SpriteRenderer));
            Undo.RegisterCreatedObjectUndo(fireObject, "Forge Fire");
            fireObject.transform.SetParent(building.transform, false);
            var renderer = fireObject.GetComponent<SpriteRenderer>();
            renderer.sprite = fireMask;
            renderer.sharedMaterial = EnsureFireMaterial();
            renderer.sortingLayerID = building.sortingLayerID;
            renderer.sortingOrder = building.sortingOrder + 1;

            // 화덕 사각형들을 합친 영역(스프라이트 0~1 좌표)
            var forge = entry.windows[entry.forgeIndices[0]];
            foreach (int i in entry.forgeIndices) forge = Rect.MinMaxRect(Mathf.Min(forge.xMin, entry.windows[i].xMin),
                Mathf.Min(forge.yMin, entry.windows[i].yMin), Mathf.Max(forge.xMax, entry.windows[i].xMax), Mathf.Max(forge.yMax, entry.windows[i].yMax));
            var bounds = building.sprite.bounds;
            Vector3 ToLocal(float u, float v) => new Vector3(Mathf.Lerp(bounds.min.x, bounds.max.x, u), Mathf.Lerp(bounds.min.y, bounds.max.y, v), 0f);

            var lightObject = new GameObject("Forge Light");
            lightObject.transform.SetParent(fireObject.transform, false);
            lightObject.transform.localPosition = ToLocal(forge.center.x, forge.yMin);
            var light = lightObject.AddComponent<Light2D>();
            light.lightType = Light2D.LightType.Point;
            light.pointLightInnerRadius = 0.1f;
            light.pointLightOuterRadius = ForgeLightRadius;
            light.falloffIntensity = 0.65f;
            light.color = new Color(1f, 0.55f, 0.25f);
            light.shadowsEnabled = false;
            light.intensity = 0f;
            DayNightCycle.RebuildLightMesh(light);

            var embers = CreateEmbers(fireObject.transform, ToLocal(forge.center.x, forge.yMax),
                (forge.width * bounds.size.x) * 0.8f, building);

            var fire = fireObject.AddComponent<ForgeFire>();
            fire.Configure(light, embers, new Vector2(forge.yMin, forge.height));
        }

        private static ParticleSystem CreateEmbers(Transform parent, Vector3 localPosition, float width, SpriteRenderer building)
        {
            var go = new GameObject("Embers", typeof(ParticleSystem));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            var ps = go.GetComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            float pixel = 1f / building.sprite.pixelsPerUnit; // 화면 1px(1080p 기준)
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = true;
            main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.2f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(pixel * 1.5f, pixel * 2.5f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.9f, 0.45f), new Color(1f, 0.6f, 0.2f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 40;

            var emission = ps.emission;
            emission.rateOverTime = 6f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(width, pixel * 2f, 0f);

            var velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(-0.08f, 0.08f);
            velocity.y = new ParticleSystem.MinMaxCurve(0.25f, 0.55f);
            velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);

            var color = ps.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(new Color(1f, 0.9f, 0.5f), 0f), new GradientColorKey(new Color(1f, 0.45f, 0.1f), 0.6f), new GradientColorKey(new Color(0.8f, 0.2f, 0.05f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.9f, 0.6f), new GradientAlphaKey(0f, 1f) });
            color.color = gradient;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(UnlitSpriteMaterialPath);
            renderer.sortingLayerID = building.sortingLayerID;
            renderer.sortingOrder = building.sortingOrder + 3;
            ps.Play();
            return ps;
        }

        private static Material EnsureFireMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(FireMaterialPath);
            if (material != null) return material;
            material = new Material(Shader.Find("GN3/FireGlow"));
            AssetDatabase.CreateAsset(material, FireMaterialPath);
            return material;
        }

        /// <summary>집마다 불 끄는 시각 1:00~2:30. 이름으로 고정한 값이라 다시 만들어도 같다.</summary>
        internal static float DefaultBedtime(string buildingName)
        {
            // GetHashCode는 실행마다 달라질 수 있어 글자 코드로 직접 섞는다.
            uint hash = 2166136261;
            foreach (char c in buildingName) hash = (hash ^ c) * 16777619;
            return 1f + (hash % 1000) / 1000f * 1.5f;
        }

        private static Material EnsureMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material != null) return material;
            var shader = Shader.Find("GN3/WindowGlow");
            if (shader == null)
            {
                Debug.LogError("[WindowGlow] 셰이더 'GN3/WindowGlow'를 찾지 못했습니다.");
                return null;
            }
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, MaterialPath);
            return material;
        }

        /// <summary>창 사각형 에셋. 없으면 지금 씬의 건물 4채 값(그림을 확대해 직접 잰 값)으로 만든다.</summary>
        private static WindowRectSet EnsureRects()
        {
            var set = AssetDatabase.LoadAssetAtPath<WindowRectSet>(RectsPath);
            if (set != null) return set;

            set = ScriptableObject.CreateInstance<WindowRectSet>();
            set.buildings.Add(Building("붉은 기와 지붕의 작은 오두막-1",
                new Rect(0.2831f, 0.2593f, 0.0708f, 0.1019f),   // 왼쪽 창
                new Rect(0.6462f, 0.2778f, 0.0677f, 0.0833f))); // 오른쪽 창(아래 꽃상자는 유리가 아니라 자동 제외)
            set.buildings.Add(Building("쇠락한 모험가 길드 홀 (1)",       // 판자로 막힌 창은 일부러 뺐다(버려진 길드)
                new Rect(0.2772f, 0.2772f, 0.0351f, 0.0702f),
                new Rect(0.7404f, 0.4632f, 0.0316f, 0.0632f),
                new Rect(0.7930f, 0.2772f, 0.0281f, 0.0667f),
                new Rect(0.2772f, 0.1298f, 0.0351f, 0.0702f),
                new Rect(0.7930f, 0.1298f, 0.0421f, 0.0702f)));
            set.buildings.Add(Building("중세 마을의 소박한 2층 주택-3",
                new Rect(0.4457f, 0.4000f, 0.0375f, 0.0667f),   // 박공 창 왼쪽
                new Rect(0.5169f, 0.4000f, 0.0375f, 0.0667f),   // 박공 창 오른쪽
                new Rect(0.5131f, 0.1852f, 0.0524f, 0.0556f),   // 아래층 창
                new Rect(0.6180f, 0.1852f, 0.0524f, 0.0556f)));
            set.buildings.Add(Building("이끼 지붕의 중세 마을 잡화점",
                new Rect(0.4754f, 0.5041f, 0.0528f, 0.0785f),   // 박공 창
                new Rect(0.6831f, 0.2562f, 0.0599f, 0.0785f))); // 오른쪽 창
            AssetDatabase.CreateAsset(set, RectsPath);
            return set;
        }

        private static WindowRectSet.Building Building(string sourceName, params Rect[] windows) =>
            new WindowRectSet.Building { sourceName = sourceName, windows = new List<Rect>(windows) };
    }
}
