using System.Collections.Generic;
using System.Linq;
using GN3.Economy;
using GN3.Mercenaries;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
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
        private const float IconPad = 3f;          // 작은 칸 아이콘 여백(큰 칸은 칸 크기의 IconPadRatio)
        private const float IconPadRatio = 0.08f;

        // 큰 칸(용병별 가방 카드 — 전투 가방 창·마을 가방 창): 가장 큰 가방(10칸)도 한 줄에 든다
        public const float LargeSlot = 60f, LargeGap = 6f;
        public const int LargePerLine = 10;
        private const float CardPad = 8f, CardSpacing = 10f, CardInfoWidth = 112f;
        private static readonly Color CardColor = new Color(1f, 1f, 1f, 0.05f);

        /// <summary>(아이템, 개수) → 칸 단위 목록(칸을 쓰는 것만, 등급 높은 것·값 높은 것 먼저).</summary>
        public static List<LootItem> Units(IEnumerable<(LootItem item, int count)> loot) => BagCatalog.Units(loot);

        /// <summary>칸을 안 쓰는 것만(재료·동전 주머니).</summary>
        public static List<(LootItem item, int count)> Outside(IEnumerable<(LootItem item, int count)> loot) =>
            loot.Where(l => l.item != null && !BagCatalog.UsesSlot(l.item) && l.count > 0).ToList();

        /// <summary>등급 색 테두리(없으면 기본 테두리).</summary>
        public static Color BorderOf(LootItem item) =>
            item == null ? EmptyBorder : item.HasGrade ? LootGrades.ColorOf(item.Grade) : new Color(0.75f, 0.68f, 0.55f);

        /// <summary>
        /// 칸 하나. item null이면 빈 칸. onClick이 있으면 눌린다(onRightClick은 우클릭). 돌려주는 Image는 테두리(골랐을 때 색을 바꾸려고).
        /// </summary>
        public static Image CreateSlot(Transform parent, LootItem item, float size, UnityAction onClick = null, UnityAction onRightClick = null)
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
                Inset((RectTransform)icon.transform, Border + Mathf.Max(IconPad, size * IconPadRatio));
                var image = icon.GetComponent<Image>();
                image.sprite = item.Icon;
                image.preserveAspect = true;
                image.raycastTarget = false;
                if (item.Icon == null) image.color = new Color(1f, 0.8f, 0.3f);
                string hint = onClick != null && onRightClick != null ? "\n클릭: 고르기 · 우클릭: 버리기"
                    : onClick != null ? "\n눌러서 고른다" : onRightClick != null ? "\n우클릭: 버리기" : "";
                go.AddComponent<TooltipTrigger>().Text = Tooltip(item) + hint;
            }
            if (onClick != null && item != null)
            {
                var button = go.AddComponent<Button>();
                button.transition = Selectable.Transition.None;
                button.onClick.AddListener(onClick);
            }
            if (onRightClick != null && item != null) go.AddComponent<SlotRightClick>().OnRightClick = onRightClick;
            return border;
        }

        /// <summary>칸 우클릭(Button은 왼쪽 클릭만 받는다).</summary>
        private class SlotRightClick : MonoBehaviour, IPointerClickHandler
        {
            public UnityAction OnRightClick;

            public void OnPointerClick(PointerEventData eventData)
            {
                if (eventData.button == PointerEventData.InputButton.Right) OnRightClick?.Invoke();
            }
        }

        /// <summary>
        /// capacity칸 격자(채운 칸 → 빈 칸). perLine칸마다 줄을 바꾼다. onClick(i): i번째 채운 칸을 눌렀을 때. 돌려주는 목록 = 채운 칸 테두리들.
        /// </summary>
        public static List<Image> CreateGrid(Transform parent, IReadOnlyList<LootItem> units, int capacity, int perLine, float size, float gap,
            System.Action<int> onClick = null, System.Action<int> onRightClick = null)
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
                filled.Add(CreateSlot(go.transform, item, size,
                    item != null && onClick != null ? () => onClick(index) : (UnityAction)null,
                    item != null && onRightClick != null ? () => onRightClick(index) : (UnityAction)null));
            }
            return filled.Take(units.Count).ToList();
        }

        /// <summary>격자 높이(CreateGrid와 같은 계산).</summary>
        public static float GridHeight(int count, int perLine, float size, float gap)
        {
            int lines = Mathf.Max(1, (count + perLine - 1) / Mathf.Max(1, perLine));
            return lines * size + (lines - 1) * gap;
        }

        /// <summary>칸 목록을 대원 순서대로 각자 빈 칸 수만큼 나눈다(BagCatalog.Split).</summary>
        public static List<List<LootItem>> Split(IReadOnlyList<LootItem> units, IReadOnlyList<Mercenary> members) => BagCatalog.Split(units, members);

        /// <summary>용병 가방 카드 높이(CreateMercBagCard와 같은 계산).</summary>
        public static float CardHeight(Mercenary merc, int itemCount, float slotSize, int perLine, float gap) =>
            Mathf.Max(PortraitSize(slotSize), GridHeight(Mathf.Max(merc.Bag.Slots, itemCount), perLine, slotSize, gap)) + CardPad * 2f;

        private static int PortraitSize(float slotSize) => Mathf.RoundToInt(Mathf.Clamp(slotSize, 40f, 56f));

        /// <summary>
        /// 용병 한 명의 가방 카드: [초상] [이름 / 가방 이름 · n/m칸] [칸 격자(채운 칸 → 빈 칸)].
        /// onClick(i): i번째 채운 칸을 눌렀을 때. selected: 흰 테두리로 보일 칸(-1 없음).
        /// </summary>
        public static GameObject CreateMercBagCard(Transform parent, Mercenary merc, IReadOnlyList<LootItem> items,
            float slotSize, int perLine, float gap, System.Action<int> onClick = null, int selected = -1, System.Action<int> onRightClick = null)
        {
            var card = new GameObject("BagCard_" + merc.Name, typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            card.transform.SetParent(parent, false);
            var background = card.GetComponent<Image>();
            background.color = CardColor;
            background.raycastTarget = false;
            var h = card.GetComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset((int)CardPad, (int)CardPad, (int)CardPad, (int)CardPad);
            h.spacing = CardSpacing;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = false;
            card.GetComponent<LayoutElement>().minHeight = CardHeight(merc, items.Count, slotSize, perLine, gap);

            CharacterPortraitUI.Create(card.transform, merc.Appearance, PortraitSize(slotSize));

            var info = new GameObject("Info", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            info.transform.SetParent(card.transform, false);
            var infoLayout = info.GetComponent<LayoutElement>();
            infoLayout.minWidth = infoLayout.preferredWidth = CardInfoWidth;
            infoLayout.flexibleWidth = 0f; // 글자 줄이 늘어나려 해도 칸 격자가 정보 바로 옆에 붙게
            var v = info.GetComponent<VerticalLayoutGroup>();
            v.spacing = 2f;
            v.childAlignment = TextAnchor.MiddleLeft;
            v.childControlWidth = v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            var name = ShopListUI.CreateText(info.transform, $"<b>{merc.Name}</b>", 16, TextAnchor.MiddleLeft, UITheme.TitleText);
            name.supportRichText = true;
            name.horizontalOverflow = HorizontalWrapMode.Overflow;
            var bag = merc.Bag;
            int used = Mathf.Min(items.Count, bag.Slots);
            var bagText = ShopListUI.CreateText(info.transform, $"{bag.Name}\n<color=#ffd36b>{used}/{bag.Slots}칸</color>", 13, TextAnchor.MiddleLeft, UITheme.MutedText);
            bagText.supportRichText = true;
            bagText.horizontalOverflow = HorizontalWrapMode.Overflow;

            var borders = CreateGrid(card.transform, items, bag.Slots, perLine, slotSize, gap, onClick, onRightClick);
            if (selected >= 0 && selected < borders.Count) borders[selected].color = SelectedBorder;
            return card;
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

        /// <summary>
        /// 장비칸 하나(용병 정보의 무기·가방): 칸 네모(CreateSlot과 같은 모양) + 아래 부위 이름. icon null이면 빈 칸.
        /// onClick이 있으면 칸이 눌린다.
        /// </summary>
        public static GameObject CreateEquipSlot(Transform parent, string label, Sprite icon, string tooltip, float size, UnityAction onClick = null)
        {
            var column = new GameObject("Equip_" + label, typeof(RectTransform), typeof(VerticalLayoutGroup));
            column.transform.SetParent(parent, false);
            var v = column.GetComponent<VerticalLayoutGroup>();
            v.spacing = 2f;
            v.childAlignment = TextAnchor.UpperCenter;
            v.childControlWidth = v.childControlHeight = true;
            v.childForceExpandWidth = v.childForceExpandHeight = false;

            var slot = new GameObject("Slot", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            slot.transform.SetParent(column.transform, false);
            var layout = slot.GetComponent<LayoutElement>();
            layout.minWidth = layout.preferredWidth = size;
            layout.minHeight = layout.preferredHeight = size;
            slot.GetComponent<Image>().color = icon != null ? new Color(1f, 1f, 1f, 0.6f) : EmptyBorder;
            slot.AddComponent<TooltipTrigger>().Text = tooltip;
            if (onClick != null)
            {
                var button = slot.AddComponent<Button>();
                button.transition = Selectable.Transition.None;
                button.onClick.AddListener(onClick);
            }

            var back = new GameObject("Back", typeof(RectTransform), typeof(Image));
            back.transform.SetParent(slot.transform, false);
            Inset((RectTransform)back.transform, Border);
            var backImage = back.GetComponent<Image>();
            backImage.color = SlotColor;
            backImage.raycastTarget = false;

            if (icon != null)
            {
                var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
                iconGo.transform.SetParent(slot.transform, false);
                Inset((RectTransform)iconGo.transform, Border + Mathf.Max(IconPad, size * IconPadRatio));
                var image = iconGo.GetComponent<Image>();
                image.sprite = icon;
                image.preserveAspect = true;
                image.raycastTarget = false;
            }

            var text = ShopListUI.CreateText(column.transform, label, size >= LargeSlot ? 14 : 12, TextAnchor.MiddleCenter, UITheme.BodyText);
            text.font = UITheme.Font;
            text.raycastTarget = false;
            return column;
        }

        private static void Inset(RectTransform rect, float inset)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
        }
    }
}
