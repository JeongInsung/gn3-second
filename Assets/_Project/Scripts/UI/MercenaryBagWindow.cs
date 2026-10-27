using System.Linq;
using GN3.Economy;
using GN3.Mercenaries;
using UnityEngine;
using UnityEngine.UI;

namespace GN3.UI
{
    /// <summary>
    /// 마을에서 보는 용병 한 명의 가방 창(용병 정보의 가방 칸을 누르면): 큰 칸 가방 카드(BagSlotsUI.CreateMercBagCard) +
    /// 안내 + [창고에 넣기]. 원정에서 주운 전리품은 가방에 남고(Mercenary.BagContents), 팔거나 쓰려면 창고(LootBag)로 옮긴다.
    /// 처음 열 때 자기 캔버스를 만들고(씬마다 새로), X·ESC로 닫는다.
    /// </summary>
    public class MercenaryBagWindow : MonoBehaviour
    {
        private const float Width = 900f;
        private const int SortingOrder = 2; // 용병 정보 창(1) 위
        private static MercenaryBagWindow _instance;

        private GameObject _panel;

        public static void Open(Mercenary merc)
        {
            if (merc == null) return;
            if (_instance == null) Create();
            _instance.Build(merc);
        }

        public static void Close()
        {
            if (_instance != null && _instance._panel != null) Destroy(_instance._panel);
        }

        private static void Create()
        {
            var go = new GameObject("MercenaryBagWindow", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(MercenaryBagWindow));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            _instance = go.GetComponent<MercenaryBagWindow>();
        }

        private void Build(Mercenary merc)
        {
            if (_panel != null) Destroy(_panel);
            _panel = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            _panel.transform.SetParent(transform, false);
            // ESC는 EscapeCloser에 맡긴다(따로 받으면 아래 용병 정보 창까지 한 번에 닫힌다)
            EscapeCloser.Register(_panel, Close, WindowRole.Linked);
            var rect = (RectTransform)_panel.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(Width, 0f);
            var background = _panel.GetComponent<Image>();
            background.sprite = UITheme.Panel;
            background.type = Image.Type.Sliced;
            var layout = _panel.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(20, 20, 16, 18);
            layout.spacing = 10f;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            _panel.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // 제목 줄: "이름의 가방" + X
            var header = new GameObject("Header", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            header.transform.SetParent(_panel.transform, false);
            header.GetComponent<LayoutElement>().minHeight = 32f;
            var h = header.GetComponent<HorizontalLayoutGroup>();
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = false;
            var title = ShopListUI.CreateText(header.transform, $"<b>{merc.Name}의 가방</b>", 22, TextAnchor.MiddleLeft, UITheme.TitleText);
            title.supportRichText = true;
            title.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            ShopListUI.CreateButton(header.transform, "X", Close, 40f);

            BagSlotsUI.CreateMercBagCard(_panel.transform, merc, merc.BagContents,
                BagSlotsUI.LargeSlot, BagSlotsUI.LargePerLine, BagSlotsUI.LargeGap);

            var bag = merc.Bag;
            var note = ShopListUI.CreateText(_panel.transform,
                $"원정에서 전리품을 <color=#ffd36b>{bag.Slots}개</color>까지 들고 온다 (칸 하나에 하나) · 건설 재료·동전 주머니는 칸을 쓰지 않는다\n" +
                "주운 전리품은 원정이 끝나도 가방에 남는다 — 남은 만큼 다음 원정에서 덜 줍는다 · 팔거나 쓰려면 창고에 넣는다",
                14, TextAnchor.MiddleLeft, UITheme.BodyText);
            note.supportRichText = true;

            // [창고에 넣기]: 가방을 비워 길드 창고(LootBag)로
            var buttons = new GameObject("Buttons", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            buttons.transform.SetParent(_panel.transform, false);
            buttons.GetComponent<LayoutElement>().minHeight = 38f;
            var h2 = buttons.GetComponent<HorizontalLayoutGroup>();
            h2.childAlignment = TextAnchor.MiddleRight;
            h2.childControlWidth = h2.childControlHeight = true;
            h2.childForceExpandWidth = h2.childForceExpandHeight = false;
            var stash = ShopListUI.CreateButton(buttons.transform, "창고에 넣기", () => Stash(merc), 140f);
            string block = StashBlock(merc);
            stash.interactable = block == null;
            if (block != null) stash.gameObject.AddComponent<TooltipTrigger>().Text = block;

            UIThemeApplier.ApplyNow(_panel.transform);
            _panel.transform.SetAsLastSibling();
        }

        /// <summary>지금 창고에 넣을 수 없으면 이유, 되면 null.</summary>
        private static string StashBlock(Mercenary merc)
        {
            if (merc.BagContents.Count == 0) return "가방이 비어 있습니다.";
            if (!PlayerParty.Instance.Members.Contains(merc)) return "고용한 용병의 가방만 창고에 넣을 수 있습니다.";
            if (GN3.Quests.ExpeditionLog.Instance.IsOnExpedition(merc)) return "파견 중이라 창고에 넣을 수 없습니다.";
            return null;
        }

        private void Stash(Mercenary merc)
        {
            if (StashBlock(merc) != null) return;
            int count = LootBag.StashFrom(merc);
            ToastLog.Show($"{merc.Name}의 가방에서 전리품 {count}개를 창고에 넣었습니다");
            Build(merc);
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }
    }
}
