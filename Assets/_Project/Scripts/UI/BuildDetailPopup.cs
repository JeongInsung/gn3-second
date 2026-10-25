using System.Collections.Generic;
using GN3.Economy;
using UnityEngine;
using UnityEngine.UI;

namespace GN3.UI
{
    /// <summary>
    /// [건설] 창에서 건물 카드를 누르면 뜨는 작은 상세 창: 이름 · 그림 · 필요한 것 칸(금화·재료 그림 + 보유/필요, 모자라면 붉게)
    /// · 그 밖의 막힌 이유 · [짓기]/[닫기]. 칸에 마우스를 올리면 이름이 뜬다(TooltipTrigger).
    /// [건설] 창 판(Box) 안에 한 번 만들어 두고(BuildMenuPanel.Create) 카드를 누를 때마다 내용을 바꿔 연다.
    /// 뒤 어두운 막은 RawImage라 UIThemeApplier가 칠하지 않는다. 막을 누르거나 ESC면 이 창만 닫힌다(WindowRole.Linked).
    /// </summary>
    public class BuildDetailPopup : MonoBehaviour
    {
        private const float Width = 360f;
        private const float IconSize = 120f;
        private static readonly Color DimColor = new Color(0f, 0f, 0f, 0.55f);
        private static readonly Color BlockColor = new Color(0.95f, 0.45f, 0.4f);
        private static readonly Vector2 NeedSlotSize = new Vector2(56f, 64f);
        private const float NeedIconSize = 34f;
        private const float NeedsSpacing = 8f;

        private static BuildDetailPopup _instance;

        private Text _title;
        private Image _icon;
        private RectTransform _needs; // 필요한 것 칸 줄(열 때마다 다시 만든다)
        private Text _block;
        private Button _ok;
        private Text _okLabel;
        private System.Action _onOk;

        public static bool Active => _instance != null && _instance.gameObject.activeSelf;

        public static void Create(Transform box)
        {
            var root = new GameObject("BuildDetail", typeof(RectTransform), typeof(BuildDetailPopup));
            root.transform.SetParent(box, false);
            Stretch((RectTransform)root.transform);
            var popup = root.GetComponent<BuildDetailPopup>();
            EscapeCloser.Register(root, Hide, WindowRole.Linked);

            // 어두운 막(누르면 닫힘)과 판은 형제로 둔다: 판 클릭이 막으로 올라가지 않아 판에 클릭 막이 버튼이 필요 없다
            // (판이 버튼이면 UIThemeApplier가 안의 글자를 버튼 글자로 보고 흰색으로 바꿔 붉은 "0/3"이 사라진다)
            var dimGo = new GameObject("Dim", typeof(RectTransform), typeof(RawImage), typeof(Button));
            dimGo.transform.SetParent(root.transform, false);
            Stretch((RectTransform)dimGo.transform);
            dimGo.GetComponent<RawImage>().color = DimColor;
            var dim = dimGo.GetComponent<Button>();
            dim.transition = Selectable.Transition.None;
            dim.onClick.AddListener(Hide);

            var panel = new GameObject("DetailPanel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            panel.transform.SetParent(root.transform, false);
            panel.GetComponent<Image>().color = new Color(0.13f, 0.12f, 0.15f, 0.98f);
            var panelRect = (RectTransform)panel.transform;
            panelRect.anchorMin = panelRect.anchorMax = panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(Width, 0f);
            var layout = panel.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(20, 20, 16, 18);
            layout.spacing = 8f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            panel.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            popup._title = ShopListUI.CreateText(panel.transform, "", 20, TextAnchor.MiddleCenter, UITheme.TitleText);
            popup._title.fontStyle = FontStyle.Bold;

            // 이름에 Portrait: 그림 없이 만든 Image를 UIThemeApplier가 목록 칸(Sliced·틴트)으로 칠해 비율이 늘어나지 않게
            var icon = new GameObject("Portrait", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            icon.transform.SetParent(panel.transform, false);
            popup._icon = icon.GetComponent<Image>();
            popup._icon.preserveAspect = true;
            popup._icon.raycastTarget = false;
            var iconLayout = icon.GetComponent<LayoutElement>();
            iconLayout.minHeight = iconLayout.preferredHeight = IconSize;

            var needs = new GameObject("Needs", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            needs.transform.SetParent(panel.transform, false);
            needs.GetComponent<LayoutElement>().minHeight = NeedSlotSize.y;
            var needsLayout = needs.GetComponent<HorizontalLayoutGroup>();
            needsLayout.spacing = NeedsSpacing;
            needsLayout.childAlignment = TextAnchor.MiddleCenter;
            needsLayout.childControlWidth = true;
            needsLayout.childControlHeight = true;
            needsLayout.childForceExpandWidth = false;
            needsLayout.childForceExpandHeight = false;
            popup._needs = (RectTransform)needs.transform;
            popup._block = ShopListUI.CreateText(panel.transform, "", 14, TextAnchor.MiddleCenter, BlockColor);

            var buttons = new GameObject("Buttons", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            buttons.transform.SetParent(panel.transform, false);
            buttons.GetComponent<LayoutElement>().minHeight = 42f;
            var h = buttons.GetComponent<HorizontalLayoutGroup>();
            h.spacing = 12f;
            h.childAlignment = TextAnchor.MiddleCenter;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = false;
            popup._ok = ShopListUI.CreateButton(buttons.transform, "짓기", () => popup._onOk?.Invoke(), 110f);
            popup._okLabel = popup._ok.GetComponentInChildren<Text>();
            ShopListUI.CreateButton(buttons.transform, "닫기", Hide, 110f);

            _instance = popup;
            root.SetActive(false);
        }

        /// <summary>
        /// 내용을 채워 연다. materials: 필요한 재료(다 지었으면 null). block: 지을 수 없는 이유(없으면 null → [짓기] 켜짐).
        /// 금화·재료가 모자란 이유는 칸의 붉은 수가 보여 주므로 글로는 쓰지 않는다.
        /// </summary>
        public static void Show(string title, Sprite icon, int cost, IReadOnlyList<(LootItem item, int count)> materials, string block, string okLabel, System.Action onOk)
        {
            if (_instance == null) return;
            var p = _instance;
            p._title.text = title;
            p._icon.sprite = icon;
            p._icon.type = Image.Type.Simple; // preserveAspect는 Simple에서만 듣는다
            p._icon.color = Color.white;
            p._icon.preserveAspect = true;
            p._icon.gameObject.SetActive(icon != null);
            bool lacking = p.FillNeeds(cost, materials);
            string reason = lacking ? null : block; // 모자란 것은 칸이 붉게 보여 준다
            p._block.text = reason ?? "";
            p._block.gameObject.SetActive(reason != null);
            p._okLabel.text = okLabel;
            p._ok.interactable = block == null;
            p._onOk = onOk;
            p.transform.SetAsLastSibling(); // 판 안 목록·버튼보다 위
            p.gameObject.SetActive(true);
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)p.transform);
            UIThemeApplier.ApplyNow(p.transform);
        }

        /// <summary>필요한 것 칸 줄을 다시 만든다(금화 → 재료 순). 하나라도 모자라면 true.</summary>
        private bool FillNeeds(int cost, IReadOnlyList<(LootItem item, int count)> materials)
        {
            // Destroy는 프레임 끝이라 바로 레이아웃을 잡으면 옛 칸까지 셈에 든다 → 먼저 떼어 낸다
            var old = new List<Transform>();
            foreach (Transform child in _needs) old.Add(child);
            foreach (var child in old)
            {
                child.SetParent(null, false);
                Destroy(child.gameObject);
            }

            bool lacking = false;
            if (materials != null && cost > 0)
            {
                bool enough = Wallet.Gold >= cost;
                lacking |= !enough;
                CreateNeedSlot(UIIcons.Load("UIIcons/gold"), $"{cost}", enough, $"금화 {cost}G (보유 {Wallet.Gold}G)");
            }
            if (materials != null)
                foreach (var (item, count) in materials)
                {
                    int have = LootBag.Count(item);
                    lacking |= have < count;
                    CreateNeedSlot(item.Icon, $"{have}/{count}", have >= count, $"{item.Name} {have}/{count}");
                }
            _needs.gameObject.SetActive(_needs.childCount > 0);
            return lacking;
        }

        /// <summary>칸 하나: 어두운 칸 안에 그림 + 수(모자라면 붉게). 마우스를 올리면 이름.</summary>
        private void CreateNeedSlot(Sprite sprite, string amount, bool enough, string tooltip)
        {
            var slot = new GameObject("NeedSlot", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(LayoutElement), typeof(TooltipTrigger));
            slot.transform.SetParent(_needs, false);
            slot.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.06f); // UIThemeApplier가 어두운 칸으로 바꾼다
            var layout = slot.GetComponent<LayoutElement>();
            layout.minWidth = layout.preferredWidth = NeedSlotSize.x;
            layout.minHeight = layout.preferredHeight = NeedSlotSize.y;
            var v = slot.GetComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(2, 2, 5, 3);
            v.spacing = 1f;
            v.childAlignment = TextAnchor.UpperCenter;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            slot.GetComponent<TooltipTrigger>().Text = tooltip;

            var icon = new GameObject("Icon", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            icon.transform.SetParent(slot.transform, false);
            var image = icon.GetComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            image.enabled = sprite != null;
            var iconLayout = icon.GetComponent<LayoutElement>();
            iconLayout.minHeight = iconLayout.preferredHeight = NeedIconSize;

            var text = ShopListUI.CreateText(slot.transform, amount, 13, TextAnchor.MiddleCenter, enough ? UITheme.BodyText : BlockColor);
            text.fontStyle = FontStyle.Bold;
            text.raycastTarget = false;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        public static void Hide()
        {
            if (_instance != null) _instance.gameObject.SetActive(false);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession() => _instance = null;
    }
}
