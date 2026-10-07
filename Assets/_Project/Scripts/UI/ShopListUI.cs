using System.Collections.Generic;
using System.Linq;
using GN3.Mercenaries;
using GN3.Quests;
using UnityEngine;
using UnityEngine.UI;

namespace GN3.UI
{
    /// <summary>
    /// 건물 패널(BuildingPanel) 상품 칸을 채우는 상점들(HospitalShop, WeaponShop)의 공용 UI 조각.
    /// 줄 배경·버튼은 일부러 기본 색으로 만들고 UIThemeApplier가 테마(어두운 칸·진홍 버튼)를 입힌다.
    /// </summary>
    public static class ShopListUI
    {
        public const float RowHeight = 64f;
        public const float IconSize = 48f;

        private static Font _font;
        private static Font Font => _font != null ? _font : _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        public static IEnumerable<Mercenary> MercsInVillage() =>
            PlayerParty.Instance.Members.Where(m => m.IsAlive && !ExpeditionLog.Instance.IsOnExpedition(m));

        /// <summary>상품 칸을 비우고("상품 준비 중"도 숨김) 세로 목록 설정을 맞춘다.</summary>
        public static void BeginList(BuildingPanel panel)
        {
            var content = panel.ItemListContent;
            // Destroy는 프레임 끝에 일어나서, 바로 레이아웃을 다시 계산하면 옛 줄까지 셈에 들어간다 → 먼저 떼어 낸다.
            var old = new List<Transform>();
            foreach (Transform child in content) old.Add(child);
            foreach (var child in old)
            {
                child.SetParent(null, false);
                Object.Destroy(child.gameObject);
            }
            if (panel.EmptyLabel != null) panel.EmptyLabel.gameObject.SetActive(false);

            var layout = content.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(6, 6, 6, 6);
            layout.spacing = 6f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
        }

        /// <summary>레이아웃을 바로 계산하고 목록을 맨 위로 올린다.</summary>
        public static void EndList(BuildingPanel panel)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(panel.ItemListContent);
            UIThemeApplier.ApplyNow(panel.ItemListContent); // 기본색으로 만든 줄·버튼에 바로 테마(안 하면 한 번 반짝인다)
            var scroll = panel.ItemListContent.GetComponentInParent<ScrollRect>();
            if (scroll != null) scroll.verticalNormalizedPosition = 1f;
        }

        public static GameObject CreateRow(BuildingPanel panel, string name)
        {
            var row = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            row.transform.SetParent(panel.ItemListContent, false);
            row.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.06f); // UIThemeApplier가 어두운 칸으로 바꾼다
            var layoutElement = row.GetComponent<LayoutElement>();
            layoutElement.minHeight = layoutElement.preferredHeight = RowHeight;
            var h = row.GetComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(10, 10, 8, 8);
            h.spacing = 10f;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = false;
            return row;
        }

        public static void CreateIcon(Transform parent, Sprite sprite)
        {
            var icon = new GameObject("Icon", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            icon.transform.SetParent(parent, false);
            var image = icon.GetComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            var layout = icon.GetComponent<LayoutElement>();
            layout.minWidth = layout.preferredWidth = IconSize;
            layout.minHeight = layout.preferredHeight = IconSize;
        }

        public static void CreatePrice(Transform parent, int price)
        {
            var text = CreateText(parent, $"{price}G", 16, TextAnchor.MiddleRight, UITheme.TitleText);
            text.fontStyle = FontStyle.Bold;
            text.gameObject.AddComponent<LayoutElement>().minWidth = 52f;
        }

        public static void CreateTwoLineLabel(Transform parent, string title, string subtitle)
        {
            var column = new GameObject("Label", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            column.transform.SetParent(parent, false);
            column.GetComponent<LayoutElement>().flexibleWidth = 1f;
            var v = column.GetComponent<VerticalLayoutGroup>();
            v.childAlignment = TextAnchor.MiddleLeft;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            v.spacing = 2f;

            var name = CreateText(column.transform, title, 17, TextAnchor.MiddleLeft, UITheme.BodyText);
            name.fontStyle = FontStyle.Bold;
            CreateText(column.transform, subtitle, 13, TextAnchor.MiddleLeft, UITheme.MutedText);
        }

        /// <summary>대상 고르기 화면 맨 위: "← 돌아가기" + 제목.</summary>
        public static void CreateHeader(BuildingPanel panel, string title, UnityEngine.Events.UnityAction onBack)
        {
            var header = CreateRow(panel, "TargetHeader");
            CreateButton(header.transform, "← 돌아가기", onBack, 110f);
            var text = CreateText(header.transform, title, 16, TextAnchor.MiddleLeft, UITheme.BodyText);
            text.fontStyle = FontStyle.Bold;
            text.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        }

        public static void CreateEmptyRow(BuildingPanel panel, string message)
        {
            var row = CreateRow(panel, "Empty");
            CreateText(row.transform, message, 16, TextAnchor.MiddleCenter, UITheme.MutedText)
                .gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        }

        public static Button CreateButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick, float width = 72f)
        {
            var go = new GameObject(label + "Button", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = new Color(0.6f, 0.2f, 0.2f, 1f); // UIThemeApplier가 진홍 버튼으로 바꾼다
            var layout = go.GetComponent<LayoutElement>();
            layout.minWidth = layout.preferredWidth = width;
            layout.minHeight = layout.preferredHeight = 38f;

            var text = CreateText(go.transform, label, 16, TextAnchor.MiddleCenter, UITheme.BodyText);
            var rect = text.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;

            var button = go.GetComponent<Button>();
            button.onClick.AddListener(onClick);
            return button;
        }

        public static Text CreateText(Transform parent, string content, int size, TextAnchor alignment, Color color)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.text = content;
            text.font = Font;
            text.fontSize = size;
            text.alignment = alignment;
            text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.supportRichText = false;
            text.raycastTarget = false;
            return text;
        }
    }
}
