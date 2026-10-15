using System.Collections.Generic;
using GN3.World;
using UnityEngine;
using UnityEngine.UI;

namespace GN3.Battle
{
    /// <summary>
    /// 전투 필드(UI 판) 바닥에 잔디 타일을 깐다. 필드가 Screen Space Overlay UI라 씬 Tilemap은 가려지므로 UI 이미지 격자로 깐다.
    /// 그림은 Resources/Battle/Tiles/tile1·tile2(조정식/Tiles의 TILE 1·TILE 2 복사본, 4×4 칸 128px)를 런타임에 잘라 쓴다.
    /// 칸은 클릭을 받지 않아 필드의 땅 클릭(BattleClickTarget)은 그대로 동작한다. 같은 seed면 늘 같은 무늬.
    /// 판 바깥(캔버스 루트, Backdrop 바로 위)에도 같은 격자로 잔디를 넓혀 깔고, 판 위쪽에는 흔들리는 참나무 숲을 심는다
    /// (Resources/Battle/oak_sway = 조정식/Baked/Animated/참나무 바람, 148×146 8프레임 가로 한 줄).
    /// 꽃·풀·돌과 나비·빛 가루·구름 그림자는 BattleDecor가 같은 rng로 얹는다.
    /// </summary>
    public static class BattleGroundTiles
    {
        private const float CellSize = 80f; // 1760×560 필드 = 22×7칸
        private const int SheetColumns = 4;
        private const float Shade = 0.85f;  // 유닛이 잘 보이게 바닥을 살짝 어둡게
        private const float EdgeCrop = 4f;   // 칸 가장자리에 구워진 어두운 테두리(128px 칸 기준 3~4px, FloorTileSetup.SeamFixPixels)를 잘라 격자선이 안 보이게

        // 판 바깥 잔디 범위(판 좌표, 화면 비율이 바뀌어도 빈 곳이 안 남게 넉넉히)
        private const int OuterColumns = 6;    // 좌우 각 480
        private const int OuterRowsBelow = 4;  // 아래 320(아래 패널이 덮는다)
        private const int OuterRowsAbove = 9;  // 위 720

        // 판 위쪽 숲
        private const string TreeSheetPath = "Battle/oak_sway";
        private const int TreeFrameCount = 8;
        private const float TreeFps = 1.5f;        // 마을 참나무와 같은 속도(TreeAnimationBuilder)
        private const float TreeWidth = 210f, TreeHeight = 207f;
        private const int TreeAttempts = 3000;          // 무작위로 던져 볼 자리 수
        private const float TreeBandBottom = 4f;        // 밑동 높이 범위(판 위 가장자리 기준). 0보다 아래면 판이 기둥 밑을 가린다(그림 기둥이 맨 아래까지 닿음)
        private const float TreeBandTop = 300f;
        private const float TreeMinScale = 0.8f, TreeMaxScale = 1.25f;
        private const float TreeSpacingFactor = 0.38f;  // 두 나무 평균 폭 × 이 값보다 가까우면 버린다(빼곡함 조절)
        private const float TreeBackShrink = 0.15f;     // 맨 뒤 나무는 최대 15% 작게
        private const float TreeBackDarken = 0.24f;     // 맨 뒤 나무는 최대 24% 어둡게
        private const float TreeMinLight = 0.88f;       // 나무마다 밝기 0.88~1
        private const float TitleShadeHeight = 110f, TitleShadeAlpha = 0.65f; // 제목이 나무 위에서도 읽히게

        private class Theme
        {
            public string Sheet;
            public int[] Cells; // 시트 칸 번호(왼쪽 위 0부터 가로로)
            public Color Tint;
        }

        // 지금은 모든 지역이 잔디. 지역별 바닥을 넣으려면 region으로 나눠 여기만 바꾸면 된다.
        private static Theme For(Region region) =>
            new Theme { Sheet = "tile1", Cells = new[] { 2, 3 }, Tint = Color.white };

        private static readonly Dictionary<string, Sprite[]> _sheets = new Dictionary<string, Sprite[]>();
        private static Sprite[] _treeFrames;

        // Domain Reload가 꺼져 있어도 Play마다 비운다(Sprite.Create로 만든 스프라이트는 Play가 끝나면 파괴된다).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession()
        {
            _sheets.Clear();
            _treeFrames = null;
        }

        public static void Build(RectTransform field, Region region, int seed)
        {
            var theme = For(region);
            var cells = Sheet(theme.Sheet);
            if (cells == null) return; // 그림이 없으면 원래 판 그대로

            var layer = new GameObject("GroundTiles", typeof(RectTransform));
            layer.transform.SetParent(field, false);
            layer.transform.SetAsFirstSibling();
            var layerRect = (RectTransform)layer.transform;
            layerRect.anchorMin = Vector2.zero;
            layerRect.anchorMax = Vector2.one;
            layerRect.offsetMin = layerRect.offsetMax = Vector2.zero;
            layerRect.pivot = Vector2.zero;

            Vector2 size = field.sizeDelta;
            int columns = Mathf.CeilToInt(size.x / CellSize);
            int rows = Mathf.CeilToInt(size.y / CellSize);
            var rng = new System.Random(seed);
            Color color = theme.Tint * Shade;
            color.a = 1f;

            for (int y = 0; y < rows; y++)
            for (int x = 0; x < columns; x++)
                AddTile(layer.transform, x, y, cells[theme.Cells[rng.Next(theme.Cells.Length)]], color);
            // 판 안에만 따로 얹는 그늘·색은 두지 않는다: 판 밖(나무 밑) 잔디와 한 장으로 이어져 보이게.
            var flowerSpots = BattleDecor.BuildField(layer.transform, size, rng); // 꽃밭·풀·바위

            BuildSurroundings(field, theme, cells, color, columns, rows, rng);
            var atmosphere = BattleFx.BuildAtmosphere(field);
            BattleDecor.BuildLife(atmosphere, field, flowerSpots); // 나비·빛 가루·구름 그림자
        }

        /// <summary>판 바깥 잔디 + 위쪽 숲 + 제목 그늘. 캔버스 루트의 Backdrop 바로 다음에 둬서 제목·판·아래 패널보다 뒤에 그린다.</summary>
        private static void BuildSurroundings(RectTransform field, Theme theme, Sprite[] cells, Color color, int columns, int rows, System.Random rng)
        {
            var root = field.parent;
            if (root == null) return;

            var surroundings = new GameObject("Surroundings", typeof(RectTransform));
            surroundings.transform.SetParent(root, false);
            var backdrop = root.Find("Backdrop");
            surroundings.transform.SetSiblingIndex(backdrop != null ? backdrop.GetSiblingIndex() + 1 : 0);
            var surroundRect = (RectTransform)surroundings.transform;
            surroundRect.anchorMin = Vector2.zero;
            surroundRect.anchorMax = Vector2.one;
            surroundRect.offsetMin = surroundRect.offsetMax = Vector2.zero;

            // 판과 같은 자리·피벗의 층: 자식 좌표 = 판 좌표라 격자가 판 안 타일과 이어진다.
            var area = new GameObject("Outskirts", typeof(RectTransform));
            area.transform.SetParent(surroundings.transform, false);
            var areaRect = (RectTransform)area.transform;
            areaRect.anchorMin = field.anchorMin;
            areaRect.anchorMax = field.anchorMax;
            areaRect.pivot = field.pivot;
            areaRect.sizeDelta = field.sizeDelta;
            areaRect.anchoredPosition = field.anchoredPosition;

            for (int y = -OuterRowsBelow; y < rows + OuterRowsAbove; y++)
            for (int x = -OuterColumns; x < columns + OuterColumns; x++)
            {
                if (x >= 0 && x < columns && y >= 0 && y < rows) continue; // 판 자리는 판 안 타일이 덮는다
                AddTile(area.transform, x, y, cells[theme.Cells[rng.Next(theme.Cells.Length)]], color);
            }

            BattleDecor.BuildOutskirts(area.transform, field.sizeDelta, OuterColumns * CellSize, rng);
            BuildForest(area.transform, field.sizeDelta, rng);

            var shade = new GameObject("TitleShade", typeof(RectTransform), typeof(Image));
            shade.transform.SetParent(surroundings.transform, false);
            var shadeRect = (RectTransform)shade.transform;
            shadeRect.anchorMin = new Vector2(0f, 1f);
            shadeRect.anchorMax = Vector2.one;
            shadeRect.pivot = new Vector2(0.5f, 1f);
            shadeRect.sizeDelta = new Vector2(0f, TitleShadeHeight);
            shadeRect.anchoredPosition = Vector2.zero;
            var shadeImage = shade.GetComponent<Image>();
            shadeImage.color = new Color(0f, 0f, 0f, TitleShadeAlpha);
            shadeImage.raycastTarget = false;
        }

        /// <summary>판 위쪽에 참나무를 무작위로 빼곡히(크기·좌우·밝기도 나무마다 다르게). 뒤(위)부터 그려 앞 나무가 앞에 보인다.</summary>
        private static void BuildForest(Transform parent, Vector2 fieldSize, System.Random rng)
        {
            var frames = TreeFrames();
            if (frames == null) return;

            var shadows = BattleDecor.CreateLayer(parent, "ForestShadows"); // 나무 해 그림자(나무보다 먼저 그린다)
            var forest = new GameObject("Forest", typeof(RectTransform));
            forest.transform.SetParent(parent, false);
            var forestRect = (RectTransform)forest.transform;
            forestRect.anchorMin = forestRect.anchorMax = forestRect.pivot = Vector2.zero;
            var sway = forest.AddComponent<BattleTreeSway>();

            // 무작위로 던진 자리 중 이미 심은 나무와 너무 가깝지 않은 곳만 남긴다 → 빼곡하지만 줄이 맞지 않는다.
            float left = -OuterColumns * CellSize, right = fieldSize.x + OuterColumns * CellSize;
            var trees = new List<(Vector2 pos, float scale)>();
            for (int attempt = 0; attempt < TreeAttempts; attempt++)
            {
                float depth = (float)rng.NextDouble(); // 0 = 맨 앞(판 가장자리), 1 = 맨 뒤
                var pos = new Vector2(Mathf.Lerp(left, right, (float)rng.NextDouble()),
                                      fieldSize.y + Mathf.Lerp(TreeBandBottom, TreeBandTop, depth));
                float scale = Mathf.Lerp(TreeMinScale, TreeMaxScale, (float)rng.NextDouble()) * (1f - TreeBackShrink * depth);
                bool crowded = false;
                foreach (var other in trees)
                    if (Vector2.Distance(other.pos, pos) < TreeWidth * (scale + other.scale) * 0.5f * TreeSpacingFactor) { crowded = true; break; }
                if (!crowded) trees.Add((pos, scale));
            }
            trees.Sort((a, b) => b.pos.y.CompareTo(a.pos.y)); // 뒤(위)부터 그려 앞 나무가 가린다

            foreach (var (pos, scale) in trees)
            {
                var go = new GameObject("Tree", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(forest.transform, false);
                var rect = (RectTransform)go.transform;
                rect.anchorMin = rect.anchorMax = Vector2.zero;
                rect.pivot = new Vector2(0.5f, 0f);
                rect.anchoredPosition = pos;
                rect.sizeDelta = new Vector2(TreeWidth, TreeHeight) * scale;
                if (rng.NextDouble() < 0.5) rect.localScale = new Vector3(-1f, 1f, 1f); // 좌우 뒤집어 같은 모양 반복을 줄인다

                float depth = Mathf.InverseLerp(fieldSize.y + TreeBandBottom, fieldSize.y + TreeBandTop, pos.y);
                float light = Mathf.Lerp(TreeMinLight, 1f, (float)rng.NextDouble()) * (1f - TreeBackDarken * depth);
                var image = go.GetComponent<Image>();
                image.color = new Color(light, light, light, 1f);
                image.raycastTarget = false;
                sway.Add(image, rng.Next(TreeFrameCount));
                BattleShadow.Attach(image, shadows, 0.8f); // 마을 나무와 같은 높이 비율
            }
            sway.Init(frames, TreeFps);
        }

        private static void AddTile(Transform parent, int x, int y, Sprite sprite, Color color)
        {
            var go = new GameObject($"Tile {x},{y}", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
            rect.anchoredPosition = new Vector2(x * CellSize, y * CellSize);
            rect.sizeDelta = new Vector2(CellSize, CellSize);
            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
        }

        /// <summary>시트를 4×4 칸으로 자른다(시트마다 한 번). 시트가 없으면 null.</summary>
        private static Sprite[] Sheet(string name)
        {
            if (_sheets.TryGetValue(name, out var cached) && cached[0] != null) return cached; // 파괴됐으면 다시 자른다
            var texture = Resources.Load<Texture2D>("Battle/Tiles/" + name);
            if (texture == null) return null;
            int rows = SheetColumns;
            float w = texture.width / (float)SheetColumns, h = texture.height / (float)rows;
            var sprites = new Sprite[SheetColumns * rows];
            for (int i = 0; i < sprites.Length; i++)
            {
                int col = i % SheetColumns, row = i / SheetColumns;
                // 텍스처 좌표는 아래가 0이라 윗줄이 가장 큰 y.
                float crop = EdgeCrop * w / 128f;
                var rect = new Rect(col * w + crop, (rows - 1 - row) * h + crop, w - 2f * crop, h - 2f * crop);
                sprites[i] = Sprite.Create(texture, rect, new Vector2(0.5f, 0.5f));
            }
            _sheets[name] = sprites;
            return sprites;
        }

        /// <summary>참나무 시트(가로 8프레임)를 자른다(한 번). 없으면 null.</summary>
        private static Sprite[] TreeFrames()
        {
            if (_treeFrames != null && _treeFrames[0] != null) return _treeFrames; // 파괴됐으면 다시 자른다
            var texture = Resources.Load<Texture2D>(TreeSheetPath);
            if (texture == null) return null;
            float w = texture.width / (float)TreeFrameCount;
            _treeFrames = new Sprite[TreeFrameCount];
            for (int i = 0; i < TreeFrameCount; i++)
                _treeFrames[i] = Sprite.Create(texture, new Rect(i * w, 0f, w, texture.height), new Vector2(0.5f, 0f));
            return _treeFrames;
        }
    }

    /// <summary>전투 화면 숲의 참나무들을 한꺼번에 넘긴다. 나무마다 시작 프레임이 달라 같이 움직이지 않는다.
    /// Time.deltaTime이라 전투 일시정지·배속을 그대로 따른다.</summary>
    public class BattleTreeSway : MonoBehaviour
    {
        private readonly List<Image> _trees = new List<Image>();
        private readonly List<int> _offsets = new List<int>();
        private Sprite[] _frames;
        private float _fps;
        private float _time;
        private int _shown = -1;

        public void Add(Image tree, int offset)
        {
            _trees.Add(tree);
            _offsets.Add(offset);
        }

        public void Init(Sprite[] frames, float fps)
        {
            _frames = frames;
            _fps = fps;
            Show(0);
        }

        private void Update()
        {
            if (_frames == null) return;
            _time += Time.deltaTime;
            int step = Mathf.FloorToInt(_time * _fps);
            if (step != _shown) Show(step);
        }

        private void Show(int step)
        {
            _shown = step;
            for (int i = 0; i < _trees.Count; i++)
                if (_trees[i] != null) _trees[i].sprite = _frames[(step + _offsets[i]) % _frames.Length];
        }
    }
}
