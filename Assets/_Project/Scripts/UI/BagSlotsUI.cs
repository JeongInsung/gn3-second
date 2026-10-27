using System.Collections.Generic;
using System.Linq;
using GN3.Economy;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace GN3.UI
{
    /// <summary>
    /// 가방 칸 그리기(전투 중 가방 창·결과 화면·용병 정보). 칸 1개 = 아이템 1개(BagCatalog).
    /// 칸 = 테두리(등급 색, 빈 칸은 옅은 색) 안에 불투명 바탕 + 아이콘. 칸을 안 쓰는 것(재료·동전 주머니)은 "가방 밖" 칩 줄로 따로.
    /// 모두 레이아웃 그룹 안에 넣어 쓰도록 LayoutElement로 크기를 정한다.
    /// </summary>
    public static class BagSlotsUI
    {
        public static readonly Color SlotColor = new Color(0.058f, 0.058f, 0.072f, 1f);   // 칸 바탕(불투명: 테두리만 등급 색으로 보이게)
        public static readonly Color EmptyBorder = new Color(1f, 1f, 1f, 0.2f);
        public static readonly Color SelectedBorder = Color.white; // 고른 칸(최상급 금빛 테두리와 헷갈리지 않게 흰색)
        private const float Border = 2f;
        private const float IconPad = 3f;

        /// <summary>(아이템, 개수) → 칸 단위 목록(칸을 쓰는 것만, 등급 높은 것·값 높은 것 먼저).</summary>
        public static List<LootItem> Units(IEnumerable<(LootItem item, int count)> loot) =>
            loot.Where(l => BagCatalog.UsesSlot(l.item))
                .OrderByDescending(l => l.item.HasGrade ? (int)l.item.Grade : -1).ThenByDescending(l => l.item.Price)
                .SelectMany(l => Enumerable.Repeat(l.item, Mathf.Max(0, l.count))).ToList();

        /// <summary>칸을 안 쓰는 것만(재료·동전 주머니).</summary>
        public static List<(LootItem item, int count)> Outside(IEnumerable<(LootItem item, int count)> loot) =>
            loot.Where(l => l.item != null && !BagCatalog.UsesSlot(l.item) && l.count > 0).ToList();

        /// <summary>등급 색 테두리(없으면 기본 테두리).</summary>
        public static Color BorderOf(LootItem item) =>
            item == null ? EmptyBorder : item.HasGrade ? LootGrades.ColorOf(item.Grade) : new Color(0.75f, 0.68f, 0.55f);

        /// <summary>
        /// 칸 하나. item null이면 빈 칸. onClick이 있으면 눌린다. 돌려주는 Image는 테두리(골랐을 때 색을 바꾸려고).
        /// </summary>
        public static Image CreateSlot(Transform parent, LootItem item, float size, UnityAction onClick = null)
        {
            var go = new GameObject(item != null ? "Slot_" + item.Name : "Slot_Empty", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var layout = go.GetComponent<LayoutElement>();
            layout.minWidth = layout.preferredWidth = size;
            layout.minHeight = layout.preferredHeight = size;
            ((RectTransform)go.transform).sizeDelta = new Vector2(size, size);
            var border = go.GetComponent<Image>();
            border.color = BorderOf(item);
            border.raycastTarget = item != null;

            var back = new GameObject("Back", typeof(RectTransform), typeof(Image));
            back.transform.SetParent(go.transform, false);
            Inset((RectTransform)back.transform, Border);
            var backImage = back.GetComponent<Image>();
            backImage.color = SlotColor;
            backImage.raycastTarget = false;

            if (item != null)
            {
                var icon = new GameObject("Icon", typeof(RectTransform), typeof(Image));
                icon.transform.SetParent(go.transform, false);
                Inset((RectTransform)icon.transform, Border + IconPad);
                var image = icon.GetComponent<Image>();
                image.sprite = item.Icon;
                image.preserveAspect = true;
                image.raycastTarget = false;
                if (item.Icon == null) image.color = new Color(1f, 0.8f, 0.3f);
                go.AddComponent<TooltipTrigger>().Text = Tooltip(item) + (onClick != null ? "\n눌러서 고른다" : "");
            }
            if (onClick != null && item != null)
            {
                var button = go.AddComponent<Button>();
                button.transition = Selectable.Transition.None;
                button.onClick.AddListener(onClick);
            }
            return border;
        }

        /// <summary>
        /// capacity칸 격자(채운 칸 → 빈 칸). perLine칸마다 줄을 바꾼다. onClick(i): i번째 채운 칸을 눌렀을 때. 돌려주는 목록 = 채운 칸 테두리들.
        /// </summary>
        public static List<Image> CreateGrid(Transform parent, IReadOnlyList<LootItem> units, int capacity, int perLine, float size, float gap,
            System.Action<int> onClick = null)
        {
            var go = new GameObject("BagGrid", typeof(RectTransform), typeof(GridLayoutGroup), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var grid = go.GetComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(size, size);
            grid.spacing = new Vector2(gap, gap);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = Mathf.Max(1, perLine);
            grid.childAlignment = TextAnchor.UpperLeft;
            int total = Mathf.Max(capacity, units.Count);
            int columns = Mathf.Min(Mathf.Max(1, perLine), Mathf.Max(1, total));
            int lines = Mathf.Max(1, (total + perLine - 1) / Mathf.Max(1, perLine));
            var layout = go.GetComponent<LayoutElement>();
            layout.minWidth = layout.preferredWidth = columns * size + (columns - 1) * gap;
            layout.minHeight = layout.preferredHeight = lines * size + (lines - 1) * gap;
            ((RectTransform)go.transform).sizeDelta = new Vector2(layout.preferredWidth, layout.preferredHeight);

            var filled = new List<Image>();
            for (int i = 0; i < total; i++)
            {
                var item = i < units.Count ? units[i] : null;
                int index = i;
                filled.Add(CreateSlot(go.transform, item, size, item != null && onClick != null ? () => onClick(index) : (UnityAction)null));
            }
            return filled.Take(units.Count).ToList();
        }

        /// <summary>격자 높이(CreateGrid와 같은 계산).</summary>
        public static float GridHeight(int count, int perLine, float size, float gap)
        {
            int lines = Mathf.Max(1, (count + perLine - 1) / Mathf.Max(1, perLine));
            return lines * size + (lines - 1) * gap;
        }

        /// <summary>"가방 밖" 칩 줄: 아이콘 ×n(재료·동전 주머니). 없으면 null.</summary>
        public static GameObject CreateOutsideRow(Transform parent, IEnumerable<(LootItem item, int count)> loot, float iconSize)
        {
            var outside = Outside(loot);
            if (outside.Count == 0) return null;
            var row = new GameObject("BagOutside", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            row.transform.SetParent(parent, false);
            row.GetComponent<LayoutElement>().minHeight = iconSize + 4f;
            var h = row.GetComponent<HorizontalLayoutGroup>();
            h.spacing = 8f;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = false;
            var label = ShopListUI.CreateText(row.transform, "가방 밖", 14, TextAnchor.MiddleLeft, UITheme.MutedText);
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.gameObject.AddComponent<LayoutElement>().minWidth = 52f;
            foreach (var (item, count) in outside)
            {
                var chip = new GameObject("Chip_" + item.Name, typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup));
                chip.transform.SetParent(row.transform, false);
                chip.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.001f); // 툴팁용 레이캐스트(보이지 않음)
                var c = chip.GetComponent<HorizontalLayoutGroup>();
                c.spacing = 2f;
                c.childAlignment = TextAnchor.MiddleLeft;
                c.childControlWidth = c.childControlHeight = true;
                c.childForceExpandWidth = c.childForceExpandHeight = false;
                var icon = new GameObject("Icon", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
                icon.transform.SetParent(chip.transform, false);
                var image = icon.GetComponent<Image>();
                image.sprite = item.Icon;
                image.preserveAspect = true;
                image.raycastTarget = false;
                var il = icon.GetComponent<LayoutElement>();
                il.preferredWidth = il.preferredHeight = iconSize;
                var text = ShopListUI.CreateText(chip.transform, $"×{count}", 15, TextAnchor.MiddleLeft, UITheme.BodyText);
                text.horizontalOverflow = HorizontalWrapMode.Overflow;
                chip.AddComponent<TooltipTrigger>().Text = Tooltip(item) + "\n가방 칸을 쓰지 않는다";
            }
            return row;
        }

        public static string Tooltip(LootItem item) =>
            $"<b>{item.Name}</b>" + (item.HasGrade ? $" <color={LootGrades.HexOf(item.Grade)}>{item.GradeTag}</color>" : "")
            + $"\n팔면 개당 {item.Price}G";

        private static void Inset(RectTransform rect, float inset)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
        }
    }
}
