using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GN3.EditorTools
{
    /// <summary>프레임을 맞출 기준점. 분수처럼 제자리에서 움직이면 Center, 나무처럼 밑동이 땅에 붙어야 하면 BottomCenter.</summary>
    internal enum SheetAnchor
    {
        Center,
        BottomCenter,
    }

    internal class SheetAnimationSettings
    {
        public string LogTag;
        public string SheetPath;
        public string AnimationFolder;
        public string OutputName;      // 구운 스트립·스프라이트 이름 (예: "분수 루프")
        public string ClipName;        // 예: "FountainLoop"
        public string ControllerName;  // 예: "Fountain"
        public string TargetKeyword;   // 씬에서 이 글자가 이름에 든 SpriteRenderer에 적용
        public int Columns = 4;
        public int Rows = 2;
        public float FramesPerSecond = 8f;
        public SheetAnchor Anchor = SheetAnchor.Center;
        /// <summary>
        /// 1보다 작으면 그림 아래쪽 이 비율(몸체)의 불투명 영역만으로 가로 중심·폭을 정한다.
        /// 굴뚝 연기처럼 프레임마다 위로 다르게 뻗는 부분이 정렬을 흔들지 않게 한다. 1이면 그림 전체 기준.
        /// </summary>
        public float AlignBottomRatio = 1f;
        /// <summary>구운 프레임(화면 크기)을 받아 고정할 부분을 고치는 후처리. null이면 그대로.</summary>
        public System.Action<Color[][], int, int> PostProcess;
        /// <summary>true면 키워드에 맞는 모든 오브젝트에 적용(화분처럼 같은 그림이 여러 개). false면 처음 찾은 하나만.</summary>
        public bool ApplyToAll;
    }

    /// <summary>
    /// AI가 만든 격자형 애니메이션 시트(예: 4열×2행 8프레임)를 씬 오브젝트의 루프 애니메이션으로 만든다.
    ///
    /// AI 시트는 프레임마다 그림 위치·크기가 몇 px씩 달라 그대로 자르면 떨린다. 그래서 칸마다 불투명 영역(bbox)을
    /// 찾아 기준점(중앙 또는 밑동)을 공통 캔버스의 같은 자리에 맞추고, PixelBaker와 같은 방식(영역 평균 축소 + 샤픈)으로
    /// 1080p 화면 크기에 맞춰 구운 뒤 8조각 스프라이트 시트 + AnimationClip + AnimatorController를 만들어 연결한다.
    /// 크기·위치는 기존 정지 그림의 불투명 영역에 맞추고 placement.json에 남겨, 다시 실행해도 같은 결과가 된다.
    /// </summary>
    internal static class SheetAnimationBuilder
    {
        internal const string BakedAnimatedFolder = PixelBaker.BakedFolder + "/Animated";
        private const float AlphaThreshold = 0.5f;
        private const int CanvasMargin = 2;

        public static void Build(SheetAnimationSettings settings)
        {
            var cam = Camera.main;
            if (cam == null || !cam.orthographic)
            {
                Debug.LogError($"[{settings.LogTag}] 직교(Orthographic) Main Camera가 필요합니다.");
                return;
            }
            var targets = FindTargets(settings.TargetKeyword);
            var target = targets.FirstOrDefault();
            if (target == null)
            {
                Debug.LogError($"[{settings.LogTag}] 이름에 '{settings.TargetKeyword}'가 들어간 SpriteRenderer를 씬에서 찾지 못했습니다.");
                return;
            }

            var sprites = BakeFrames(settings, target, out Vector2 anchorPoint);
            var controller = CreateAnimation(settings, sprites);
            ApplyToScene(target, sprites[0], controller, anchorPoint);

            int applied = 1;
            if (settings.ApplyToAll)
            {
                foreach (var other in targets.Skip(1))
                {
                    // 이미 애니메이션이면 스프라이트 피벗이 곧 기준점이라 지금 위치를 그대로 쓴다(다시 재면 조금씩 밀린다).
                    Vector2 otherAnchor = other.transform.position;
                    if (!AssetDatabase.GetAssetPath(other.sprite).StartsWith(BakedAnimatedFolder + "/"))
                        MeasureOpaqueArea(other, settings.Anchor, out otherAnchor, out _);
                    ApplyToScene(other, sprites[0], controller, otherAnchor);
                    applied++;
                }
            }

            var rect = sprites[0].rect;
            Debug.Log($"[{settings.LogTag}] {sprites.Length}프레임 {rect.width}x{rect.height}px, {settings.FramesPerSecond}fps 루프를 '{target.name}' 등 {applied}개에 적용했습니다.");
        }

        /// <summary>이미 구워 둔 "{outputName}@WxH.png" 스트립의 프레임들(순서대로). 없으면 null.</summary>
        internal static Sprite[] LoadBakedFrames(string outputName)
        {
            if (!AssetDatabase.IsValidFolder(BakedAnimatedFolder)) return null;
            var path = Directory.GetFiles(BakedAnimatedFolder, outputName + "@*.png").FirstOrDefault();
            if (path == null) return null;
            var sprites = AssetDatabase.LoadAllAssetsAtPath(path.Replace('\\', '/')).OfType<Sprite>()
                .OrderBy(s => int.Parse(s.name.Substring(s.name.LastIndexOf('_') + 1)))
                .ToArray();
            return sprites.Length > 0 ? sprites : null;
        }

        /// <summary>
        /// 시트를 잘라 정렬하고 target 그림의 자리·크기에 맞춰 1080p 크기로 구운 뒤 프레임 스프라이트들을 돌려준다(애니메이션·씬 적용은 안 함).
        /// anchorPoint = 첫 프레임을 놓을 월드 기준점(스프라이트 피벗이 이 점에 오게 놓으면 기존 그림과 겹친다).
        /// </summary>
        internal static Sprite[] BakeFrames(SheetAnimationSettings settings, SpriteRenderer target, out Vector2 anchorPoint)
        {
            var cam = Camera.main;
            float screenPixelsPerUnit = PixelBaker.ReferenceScreenHeight / (cam.orthographicSize * 2f);
            GetPlacement(settings, target, out anchorPoint, out float targetWidth);

            var sheet = LoadReadable(settings.SheetPath);
            var frames = ExtractAlignedFrames(sheet, settings, out int canvasWidth, out int canvasHeight, out int maxBboxWidth);
            Object.DestroyImmediate(sheet);

            // 새 그림 폭(화면 px) = 기존 그림 불투명 폭(월드) × 화면 px/유닛
            float scale = targetWidth * screenPixelsPerUnit / maxBboxWidth;
            int frameWidth = Mathf.Max(1, Mathf.RoundToInt(canvasWidth * scale));
            int frameHeight = Mathf.Max(1, Mathf.RoundToInt(canvasHeight * scale));

            var pivot = settings.Anchor == SheetAnchor.Center
                ? new Vector2(0.5f, 0.5f)
                : new Vector2(0.5f, (float)CanvasMargin / canvasHeight);
            return BakeStrip(frames, settings.OutputName, frameWidth, frameHeight, screenPixelsPerUnit, pivot, settings.PostProcess);
        }

        /// <summary>
        /// 원본 해상도 프레임들을 화면 크기(frameWidth×frameHeight)로 축소·샤픈해 가로 스트립 하나로 굽고 프레임 스프라이트로 임포트한다.
        /// 넘긴 프레임 텍스처는 여기서 지운다. 결과 파일은 "{outputName}@WxH.png"(LoadBakedFrames가 찾는 이름).
        /// </summary>
        internal static Sprite[] BakeStrip(IList<Texture2D> frames, string outputName, int frameWidth, int frameHeight,
            float screenPixelsPerUnit, Vector2 pivot, System.Action<Color[][], int, int> postProcess = null)
        {
            var bakedFrames = new Color[frames.Count][];
            for (int i = 0; i < frames.Count; i++)
            {
                bakedFrames[i] = PixelBaker.Sharpen(PixelBaker.AreaDownscale(frames[i], frameWidth, frameHeight), frameWidth, frameHeight);
                Object.DestroyImmediate(frames[i]);
            }
            postProcess?.Invoke(bakedFrames, frameWidth, frameHeight);

            var strip = new Texture2D(frameWidth * frames.Count, frameHeight, TextureFormat.RGBA32, false);
            for (int i = 0; i < bakedFrames.Length; i++)
                strip.SetPixels(i * frameWidth, 0, frameWidth, frameHeight, bakedFrames[i]);
            strip.Apply();

            EnsureFolder(BakedAnimatedFolder);
            foreach (var old in Directory.GetFiles(BakedAnimatedFolder, outputName + "@*.png"))
                AssetDatabase.DeleteAsset(old.Replace('\\', '/'));
            string stripPath = $"{BakedAnimatedFolder}/{outputName}@{frameWidth}x{frameHeight}.png";
            File.WriteAllBytes(stripPath, strip.EncodeToPNG());
            Object.DestroyImmediate(strip);

            return ImportStrip(stripPath, outputName, frameWidth, frameHeight, bakedFrames.Length, screenPixelsPerUnit, pivot);
        }

        /// <summary>8프레임의 픽셀별 중앙값. 몇 프레임에만 나타나는 변화는 빠지고 대표 모양 하나가 남는다(고정할 부분에 쓴다).</summary>
        public static Color[] Median(Color[][] frames, int width, int height)
        {
            int count = frames.Length;
            var result = new Color[width * height];
            var channel = new float[count];
            for (int p = 0; p < result.Length; p++)
            {
                var c = new float[4];
                for (int ch = 0; ch < 4; ch++)
                {
                    for (int i = 0; i < count; i++) channel[i] = frames[i][p][ch];
                    System.Array.Sort(channel);
                    c[ch] = count % 2 == 0 ? (channel[count / 2 - 1] + channel[count / 2]) * 0.5f : channel[count / 2];
                }
                result[p] = new Color(c[0], c[1], c[2], c[3] >= 0.5f ? 1f : 0f);
            }
            return result;
        }

        private static List<SpriteRenderer> FindTargets(string keyword)
        {
            var result = new List<SpriteRenderer>();
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            foreach (var renderer in root.GetComponentsInChildren<SpriteRenderer>(true))
                if (renderer.name.Contains(keyword) && renderer.GetComponent<GN3.World.ProjectedShadow>() == null)
                    result.Add(renderer);
            return result;
        }

        /// <summary>
        /// 그림을 놓을 기준점(월드)과 폭. 처음엔 씬의 정지 그림에서 재고 placement.json에 남긴다.
        /// 이미 애니메이션을 적용한 뒤 다시 실행하면 저장된 값을 써서, 다시 잴 때마다 크기가 1px씩 늘던 문제를 막는다.
        /// </summary>
        private static void GetPlacement(SheetAnimationSettings settings, SpriteRenderer target, out Vector2 anchorPoint, out float width)
        {
            string placementPath = settings.AnimationFolder + "/placement.json";
            bool animated = AssetDatabase.GetAssetPath(target.sprite).StartsWith(BakedAnimatedFolder + "/");
            if (animated && File.Exists(placementPath))
            {
                var saved = JsonUtility.FromJson<Placement>(File.ReadAllText(placementPath));
                anchorPoint = saved.center;
                width = saved.width;
                return;
            }

            MeasureOpaqueArea(target, settings.Anchor, out anchorPoint, out width);
            File.WriteAllText(placementPath, JsonUtility.ToJson(new Placement { center = anchorPoint, width = width }, true));
        }

        [System.Serializable]
        private class Placement
        {
            public Vector2 center; // 기준점(Center면 불투명 영역 중심, BottomCenter면 밑동)
            public float width;
        }

        /// <summary>지금 그림에서 실제로 보이는(불투명) 부분의 기준점(월드)과 폭.</summary>
        private static void MeasureOpaqueArea(SpriteRenderer renderer, SheetAnchor anchor, out Vector2 anchorPoint, out float width)
        {
            var sprite = renderer.sprite;
            var texture = LoadReadable(AssetDatabase.GetAssetPath(sprite));
            var rect = sprite.rect;
            var bbox = OpaqueBounds(texture.GetPixels((int)rect.x, (int)rect.y, (int)rect.width, (int)rect.height), (int)rect.width, (int)rect.height);
            Object.DestroyImmediate(texture);

            Vector2 local = anchor == SheetAnchor.Center ? bbox.center : new Vector2(bbox.center.x, bbox.yMin);
            Vector3 scale = renderer.transform.lossyScale;
            float ppu = sprite.pixelsPerUnit;
            Vector2 offset = (local - sprite.pivot) / ppu;
            anchorPoint = (Vector2)renderer.transform.position + new Vector2(offset.x * scale.x, offset.y * scale.y);
            width = bbox.width / ppu * Mathf.Abs(scale.x);
        }

        /// <summary>칸마다 불투명 bbox를 찾아 기준점을 공통 캔버스의 같은 자리에 맞춰 옮겨 담는다(프레임 간 떨림·미끄러짐 제거).</summary>
        private static List<Texture2D> ExtractAlignedFrames(Texture2D sheet, SheetAnimationSettings settings,
            out int canvasWidth, out int canvasHeight, out int maxBboxWidth)
        {
            int cellWidth = sheet.width / settings.Columns, cellHeight = sheet.height / settings.Rows;
            var cells = new List<(Color[] pixels, RectInt bbox)>();
            for (int row = 0; row < settings.Rows; row++)
            for (int col = 0; col < settings.Columns; col++)
            {
                // 텍스처 좌표는 좌하단 기준이라 위쪽 행부터 순서를 매기려면 y를 뒤집는다.
                var pixels = sheet.GetPixels(col * cellWidth, sheet.height - (row + 1) * cellHeight, cellWidth, cellHeight);
                KeepLargestPiece(pixels, cellWidth, cellHeight);
                cells.Add((pixels, OpaqueBounds(pixels, cellWidth, cellHeight)));
            }

            if (settings.AlignBottomRatio < 1f)
                return ExtractAlignedByBody(cells, cellWidth, settings, out canvasWidth, out canvasHeight, out maxBboxWidth);

            maxBboxWidth = cells.Max(c => c.bbox.width);
            canvasWidth = maxBboxWidth + CanvasMargin * 2;
            canvasHeight = cells.Max(c => c.bbox.height) + CanvasMargin * 2;

            var frames = new List<Texture2D>();
            foreach (var (pixels, bbox) in cells)
            {
                var canvas = new Color[canvasWidth * canvasHeight];
                int offsetX = canvasWidth / 2 - (bbox.x + bbox.width / 2);
                int offsetY = settings.Anchor == SheetAnchor.Center
                    ? canvasHeight / 2 - (bbox.y + bbox.height / 2)
                    : CanvasMargin - bbox.y; // 밑동을 캔버스 바닥 여백 위에 붙인다
                for (int y = bbox.yMin; y < bbox.yMax; y++)
                for (int x = bbox.xMin; x < bbox.xMax; x++)
                {
                    int cx = x + offsetX, cy = y + offsetY;
                    if (cx < 0 || cy < 0 || cx >= canvasWidth || cy >= canvasHeight) continue;
                    canvas[cy * canvasWidth + cx] = pixels[y * cellWidth + x];
                }
                var frame = new Texture2D(canvasWidth, canvasHeight, TextureFormat.RGBA32, false);
                frame.SetPixels(canvas);
                frame.Apply();
                frames.Add(frame);
            }
            return frames;
        }

        /// <summary>
        /// 몸체(그림 아래쪽 AlignBottomRatio 부분) 기준 정렬: 몸체 bbox의 가로 중심과 밑동을 캔버스의 같은 자리에 맞춘다.
        /// 캔버스는 연기처럼 몸체 밖으로 나온 부분까지 모든 프레임이 들어가게 넉넉히 잡는다. 크기 맞춤 폭도 몸체 폭이다.
        /// </summary>
        private static List<Texture2D> ExtractAlignedByBody(List<(Color[] pixels, RectInt bbox)> cells, int cellWidth,
            SheetAnimationSettings settings, out int canvasWidth, out int canvasHeight, out int maxBodyWidth)
        {
            int cellHeight = cells[0].pixels.Length / cellWidth;
            var bodies = new List<RectInt>();
            foreach (var (pixels, bbox) in cells)
            {
                int bodyTop = bbox.yMin + Mathf.RoundToInt(bbox.height * settings.AlignBottomRatio);
                var lower = new Color[pixels.Length];
                System.Array.Copy(pixels, lower, Mathf.Min(pixels.Length, bodyTop * cellWidth));
                bodies.Add(OpaqueBounds(lower, cellWidth, cellHeight));
            }

            maxBodyWidth = bodies.Max(b => b.width);
            int halfWidth = 0, height = 0;
            for (int i = 0; i < cells.Count; i++)
            {
                int centerX = bodies[i].x + bodies[i].width / 2;
                halfWidth = Mathf.Max(halfWidth, Mathf.Max(centerX - cells[i].bbox.xMin, cells[i].bbox.xMax - centerX));
                height = Mathf.Max(height, cells[i].bbox.yMax - bodies[i].yMin);
            }
            canvasWidth = halfWidth * 2 + CanvasMargin * 2;
            canvasHeight = height + CanvasMargin * 2;

            var frames = new List<Texture2D>();
            for (int i = 0; i < cells.Count; i++)
            {
                var (pixels, bbox) = cells[i];
                var canvas = new Color[canvasWidth * canvasHeight];
                int offsetX = canvasWidth / 2 - (bodies[i].x + bodies[i].width / 2);
                int offsetY = settings.Anchor == SheetAnchor.Center
                    ? canvasHeight / 2 - (bodies[i].y + bodies[i].height / 2)
                    : CanvasMargin - bodies[i].yMin;
                for (int y = bbox.yMin; y < bbox.yMax; y++)
                for (int x = bbox.xMin; x < bbox.xMax; x++)
                {
                    int cx = x + offsetX, cy = y + offsetY;
                    if (cx < 0 || cy < 0 || cx >= canvasWidth || cy >= canvasHeight) continue;
                    canvas[cy * canvasWidth + cx] = pixels[y * cellWidth + x];
                }
                var frame = new Texture2D(canvasWidth, canvasHeight, TextureFormat.RGBA32, false);
                frame.SetPixels(canvas);
                frame.Apply();
                frames.Add(frame);
            }
            return frames;
        }

        /// <summary>
        /// 칸 안에서 가장 큰 불투명 덩어리만 남긴다. AI 시트는 옆 프레임 그림이 칸 경계를 넘어와
        /// bbox가 커지고 프레임 가장자리에 조각이 붙는 일이 있었다(참나무 3번 프레임).
        /// 칸 가장자리에 닿은 작은 조각(본체의 2% 미만)만 지우고, 칸 안에 떠 있는 조각(연기·물방울)은 남긴다.
        /// </summary>
        private static void KeepLargestPiece(Color[] pixels, int width, int height)
        {
            var label = new int[pixels.Length];
            var sizes = new List<int> { 0 };
            var touchesEdge = new List<bool> { false };
            var stack = new Stack<int>();
            for (int start = 0; start < pixels.Length; start++)
            {
                if (label[start] != 0 || pixels[start].a < AlphaThreshold) continue;
                int id = sizes.Count, size = 0;
                bool edge = false;
                label[start] = id;
                stack.Push(start);
                while (stack.Count > 0)
                {
                    int p = stack.Pop();
                    size++;
                    int x = p % width, y = p / width;
                    if (x == 0 || y == 0 || x == width - 1 || y == height - 1) edge = true;
                    for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                        int q = ny * width + nx;
                        if (label[q] != 0 || pixels[q].a < AlphaThreshold) continue;
                        label[q] = id;
                        stack.Push(q);
                    }
                }
                sizes.Add(size);
                touchesEdge.Add(edge);
            }
            if (sizes.Count <= 2) return;

            // 옆 칸에서 넘어온 조각은 항상 칸 가장자리에 닿아 있다. 칸 안에 떠 있는 작은 조각(연기 한 줄기·물방울)은 남긴다.
            int largest = sizes.Max();
            for (int p = 0; p < pixels.Length; p++)
                if (label[p] != 0 && touchesEdge[label[p]] && sizes[label[p]] < largest * 0.02f) pixels[p] = Color.clear;
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
            return maxX < 0 ? new RectInt(0, 0, width, height) : new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
        }

        private static Sprite[] ImportStrip(string path, string spriteName, int frameWidth, int frameHeight, int frameCount,
            float pixelsPerUnit, Vector2 pivot)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.alphaIsTransparency = true;
            importer.spritePixelsPerUnit = pixelsPerUnit;
            // 프레임을 가로로 이어 붙여 2048px(기본 최대)을 넘으면 Unity가 텍스처를 줄여 프레임이 작아졌다(성문 2384px → 2048px).
            importer.maxTextureSize = 8192;

            var factory = new SpriteDataProviderFactories();
            factory.Init();
            var provider = factory.GetSpriteEditorDataProviderFromObject(importer);
            provider.InitSpriteEditorDataProvider();
            var rects = new SpriteRect[frameCount];
            for (int i = 0; i < frameCount; i++)
            {
                rects[i] = new SpriteRect
                {
                    name = $"{spriteName}_{i}",
                    spriteID = GUID.Generate(),
                    rect = new Rect(i * frameWidth, 0, frameWidth, frameHeight),
                    alignment = SpriteAlignment.Custom,
                    pivot = pivot,
                };
            }
            provider.SetSpriteRects(rects);
            provider.Apply();
            importer.SaveAndReimport();

            return AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>()
                .OrderBy(s => int.Parse(s.name.Substring(s.name.LastIndexOf('_') + 1)))
                .ToArray();
        }

        private static AnimatorController CreateAnimation(SheetAnimationSettings settings, Sprite[] sprites)
        {
            string clipPath = $"{settings.AnimationFolder}/{settings.ClipName}.anim";
            string controllerPath = $"{settings.AnimationFolder}/{settings.ControllerName}.controller";

            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            if (clip == null)
            {
                clip = new AnimationClip();
                AssetDatabase.CreateAsset(clip, clipPath);
            }
            clip.frameRate = settings.FramesPerSecond;

            // 스프라이트 클립은 Unity가 마지막 키 뒤에 한 칸(1/fps)을 자동으로 붙여 루프 길이가 프레임 수만큼 된다.
            // 끝에 0번 키를 더 넣으면 0번이 두 번 보여 루프마다 한 박자 멈칫했다(실측 1.125초).
            var keys = new ObjectReferenceKeyframe[sprites.Length];
            for (int i = 0; i < sprites.Length; i++)
                keys[i] = new ObjectReferenceKeyframe { time = i / settings.FramesPerSecond, value = sprites[i] };
            var binding = EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite");
            AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);

            var clipSettings = AnimationUtility.GetAnimationClipSettings(clip);
            clipSettings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(clip, clipSettings);
            EditorUtility.SetDirty(clip);

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null)
                controller = AnimatorController.CreateAnimatorControllerAtPathWithClip(controllerPath, clip);
            AssetDatabase.SaveAssets();
            return controller;
        }

        private static void ApplyToScene(SpriteRenderer target, Sprite firstFrame, AnimatorController controller, Vector2 anchorPoint)
        {
            var transform = target.transform;
            Undo.RecordObject(target, "Sheet Animation");
            Undo.RecordObject(transform, "Sheet Animation");
            target.sprite = firstFrame;
            var local = transform.localScale;
            transform.localScale = new Vector3(Mathf.Sign(local.x), Mathf.Sign(local.y), local.z);
            transform.position = new Vector3(anchorPoint.x, anchorPoint.y, transform.position.z);

            var animator = target.GetComponent<Animator>();
            if (animator == null) animator = Undo.AddComponent<Animator>(target.gameObject);
            Undo.RecordObject(animator, "Sheet Animation");
            animator.runtimeAnimatorController = controller;
            EditorSceneManager.MarkSceneDirty(target.gameObject.scene);
        }

        internal static Texture2D LoadReadable(string path)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            texture.LoadImage(File.ReadAllBytes(path));
            return texture;
        }

        internal static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }
    }
}
