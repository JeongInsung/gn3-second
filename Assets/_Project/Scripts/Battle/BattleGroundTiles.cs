using GN3.World;
using UnityEngine;
using UnityEngine.UI;

namespace GN3.Battle
{
    /// <summary>
    /// 전투 필드(UI 판) 바닥에 지역별 타일을 깐다. 필드가 Screen Space Overlay UI라 씬 Tilemap은 가려지므로 UI 이미지 격자로 깐다.
    /// 그림은 Resources/Battle/Tiles/tile1·tile2(조정식/Tiles의 TILE 1·TILE 2 복사본, 4×4 칸 128px)를 런타임에 잘라 쓴다.
    /// 칸은 클릭을 받지 않아 필드의 땅 클릭(BattleClickTarget)은 그대로 동작한다. 같은 seed면 늘 같은 무늬.
    /// </summary>
    public static class BattleGroundTiles
    {
        private const float CellSize = 80f; // 1760×560 필드 = 22×7칸
        private const int SheetColumns = 4;
        private const float Shade = 0.85f;  // 유닛이 잘 보이게 바닥을 살짝 어둡게

        private class Theme
        {
            public string Sheet;
            public int[] Cells; // 시트 칸 번호(왼쪽 위 0부터 가로로)
            public Color Tint;
        }

        // 전용 타일이 생기면 여기만 바꾸면 된다.
        private static Theme For(Region region) => region switch
        {
            Region.North => new Theme { Sheet = "tile1", Cells = new[] { 0, 1 }, Tint = new Color(0.85f, 0.92f, 1f) },  // 눈·빙하: 차가운 돌
            Region.South => new Theme { Sheet = "tile2", Cells = new[] { 0, 1 }, Tint = new Color(1f, 0.95f, 0.8f) },   // 해안: 밝은 모래
            Region.East => new Theme { Sheet = "tile2", Cells = new[] { 0, 1 }, Tint = new Color(1f, 0.85f, 0.6f) },     // 사막: 진한 모래
            _ => new Theme { Sheet = "tile1", Cells = new[] { 2, 3 }, Tint = Color.white },                              // 서쪽 숲: 잔디
        };

        private static readonly System.Collections.Generic.Dictionary<string, Sprite[]> _sheets =
            new System.Collections.Generic.Dictionary<string, Sprite[]>();

        // Domain Reload가 꺼져 있어도 Play마다 비운다(Sprite.Create로 만든 스프라이트는 Play가 끝나면 파괴된다).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession() => _sheets.Clear();

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
            {
                var go = new GameObject($"Tile {x},{y}", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(layer.transform, false);
                var rect = (RectTransform)go.transform;
                rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
                rect.anchoredPosition = new Vector2(x * CellSize, y * CellSize);
                rect.sizeDelta = new Vector2(CellSize, CellSize);
                var image = go.GetComponent<Image>();
                image.sprite = cells[theme.Cells[rng.Next(theme.Cells.Length)]];
                image.color = color;
                image.raycastTarget = false;
            }
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
                sprites[i] = Sprite.Create(texture, new Rect(col * w, (rows - 1 - row) * h, w, h), new Vector2(0.5f, 0.5f));
            }
            _sheets[name] = sprites;
            return sprites;
        }
    }
}
