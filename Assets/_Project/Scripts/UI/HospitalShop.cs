using System.Collections.Generic;
using System.Linq;
using GN3.Economy;
using GN3.Mercenaries;
using GN3.Quests;
using GN3.World;
using UnityEngine;
using UnityEngine.UI;

namespace GN3.UI
{
    /// <summary>
    /// 의약품 상점 건물 패널의 상품 칸을 치료 아이템(HealingItemCatalog)으로 채운다. 사는 즉시 치료한다.
    /// 한 명용 아이템은 마을에 있는 용병 중 대상을 고르는 화면으로 바뀌고, 전원용은 다친 사람 모두를 치료한다.
    /// 치료할 사람이 없거나 골드가 모자라면 돈을 받지 않고 알림만 띄운다. MainMenuBootstrapper가 만든다.
    /// </summary>
    public class HospitalShop : MonoBehaviour
    {
        private const string ShopKeyword = "의약품";
        private const float RowHeight = 64f;
        private const float IconSize = 48f;

        private BuildingPanel _panel;
        private Font _font;

        private void Awake()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            BuildingPanel.Opened += HandleOpened;
        }

        private void OnDestroy() => BuildingPanel.Opened -= HandleOpened;

        private void HandleOpened(BuildingPanel panel, SelectableBuilding building)
        {
            if (building == null || !building.DisplayName.Contains(ShopKeyword)) return;
            _panel = panel;
            ShowItems();
        }

        private static IEnumerable<Mercenary> MercsInVillage() =>
            PlayerParty.Instance.Members.Where(m => m.IsAlive && !ExpeditionLog.Instance.IsOnExpedition(m));

        // ---------- 아이템 목록 ----------

        private void ShowItems()
        {
            BeginList();
            foreach (var item in HealingItemCatalog.All)
                CreateItemRow(item);
            EndList();
        }

        private void CreateItemRow(HealingItem item)
        {
            var row = CreateRow("Item_" + item.Name);

            var icon = new GameObject("Icon", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            icon.transform.SetParent(row.transform, false);
            var iconImage = icon.GetComponent<Image>();
            iconImage.sprite = item.Icon;
            iconImage.preserveAspect = true;
            iconImage.raycastTarget = false;
            var iconLayout = icon.GetComponent<LayoutElement>();
            iconLayout.minWidth = iconLayout.preferredWidth = IconSize;
            iconLayout.minHeight = iconLayout.preferredHeight = IconSize;

            CreateTwoLineLabel(row.transform, item.Name, item.Effect);

            var price = CreateText(row.transform, $"{item.Price}G", 16, TextAnchor.MiddleRight, UITheme.TitleText);
            price.fontStyle = FontStyle.Bold;
            var priceLayout = price.gameObject.AddComponent<LayoutElement>();
            priceLayout.minWidth = 52f;

            CreateButton(row.transform, "구매", () =>
            {
                if (item.AllParty) BuyForEveryone(item);
                else ShowTargets(item);
            });
        }

        private void BuyForEveryone(HealingItem item)
        {
            var wounded = MercsInVillage().Where(item.CanHeal).ToList();
            if (wounded.Count == 0)
            {
                ToastLog.Show("치료할 용병이 없습니다 (마을에 다친 용병 없음)");
                return;
            }
            if (!Wallet.TrySpend(item.Price))
            {
                ToastLog.Show($"골드가 부족합니다 (필요 {item.Price}G, 보유 {Wallet.Gold}G)");
                return;
            }
            foreach (var merc in wounded) item.Apply(merc);
            ToastLog.Show($"{item.Name}: {wounded.Count}명 회복 (-{item.Price}G)");
        }

        // ---------- 대상 고르기 ----------

        private void ShowTargets(HealingItem item)
        {
            BeginList();

            var header = CreateRow("TargetHeader");
            CreateButton(header.transform, "← 돌아가기", ShowItems, 110f);
            var title = CreateText(header.transform, $"{item.Name} ({item.Price}G) — 누구에게?", 16, TextAnchor.MiddleLeft, UITheme.BodyText);
            title.fontStyle = FontStyle.Bold;
            title.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            var mercs = MercsInVillage().ToList();
            if (mercs.Count == 0)
            {
                var none = CreateRow("NoTarget");
                CreateText(none.transform, "마을에 있는 용병이 없습니다.", 16, TextAnchor.MiddleCenter, UITheme.MutedText)
                    .gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            }

            foreach (var merc in mercs)
            {
                var row = CreateRow("Target_" + merc.Name);
                CharacterPortraitUI.Create(row.transform, merc.Appearance, 40);
                int max = merc.CurrentStats.MaxHealth;
                CreateTwoLineLabel(row.transform, merc.Name, $"체력 {merc.CurrentHealth} / {max}");
                var target = merc;
                var button = CreateButton(row.transform, "치료", () => Treat(item, target));
                button.interactable = item.CanHeal(merc);
            }

            EndList();
        }

        private void Treat(HealingItem item, Mercenary merc)
        {
            if (!item.CanHeal(merc))
            {
                ToastLog.Show($"{merc.Name}은(는) 다친 곳이 없습니다.");
                return;
            }
            if (!Wallet.TrySpend(item.Price))
            {
                ToastLog.Show($"골드가 부족합니다 (필요 {item.Price}G, 보유 {Wallet.Gold}G)");
                return;
            }
            int healed = item.Apply(merc);
            ToastLog.Show($"{merc.Name}에게 {item.Name} 사용 (체력 +{healed}) -{item.Price}G");
            ShowItems();
        }

        // ---------- 목록 칸 공용 ----------

        private void BeginList()
        {
            var content = _panel.ItemListContent;
            // Destroy는 프레임 끝에 일어나서, 바로 레이아웃을 다시 계산하면 옛 줄까지 셈에 들어간다 → 먼저 떼어 낸다.
            var old = new List<Transform>();
            foreach (Transform child in content) old.Add(child);
            foreach (var child in old)
            {
                child.SetParent(null, false);
                Destroy(child.gameObject);
            }
            if (_panel.EmptyLabel != null) _panel.EmptyLabel.gameObject.SetActive(false);

            var layout = content.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(6, 6, 6, 6);
            layout.spacing = 6f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
        }

        private void EndList()
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(_panel.ItemListContent);
            var scroll = _panel.ItemListContent.GetComponentInParent<ScrollRect>();
            if (scroll != null) scroll.verticalNormalizedPosition = 1f;
        }

        private GameObject CreateRow(string name)
        {
            var row = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            row.transform.SetParent(_panel.ItemListContent, false);
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

        private void CreateTwoLineLabel(Transform parent, string title, string subtitle)
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

        private Button CreateButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick, float width = 72f)
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

        private Text CreateText(Transform parent, string content, int size, TextAnchor alignment, Color color)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.text = content;
            text.font = _font;
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
