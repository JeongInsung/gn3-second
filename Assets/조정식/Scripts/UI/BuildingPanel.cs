using GN3.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace GN3.UI
{
    /// <summary>
    /// 클릭한 건물(SelectableBuilding)의 이름·설명을 보여주는 가운데 패널. 지금은 상품 목록 칸만 있는 틀이다.
    /// 닫기 버튼, Esc, 바깥 어두운 배경 클릭으로 닫힌다. 패널이 열려 있는 동안 마우스가 UI 위에 있으므로 건물은 눌리지 않는다.
    /// 화면 구성은 Create()가 코드로 만든다(DesignScene·MainScene 부트스트래퍼가 같이 쓴다).
    /// </summary>
    public class BuildingPanel : MonoBehaviour
    {
        public Text Title;
        public Text Description;
        public RectTransform ItemListContent;
        public Image Portrait;
        /// <summary>상품이 없을 때 보이는 "상품 준비 중". 상품을 채우는 쪽이 숨긴다.</summary>
        public Text EmptyLabel;

        public SelectableBuilding Current { get; private set; }

        /// <summary>패널이 열릴 때마다 불린다. 상점(예: HospitalShop)이 이걸 받아 상품 칸을 채운다.</summary>
        public static event System.Action<BuildingPanel, SelectableBuilding> Opened;

        // Domain Reload가 꺼져 있어도 Play 진입마다 구독을 비운다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession() => Opened = null;

        /// <summary>씬의 클릭 가능한 건물(SelectableBuilding)을 누르면 건물 패널이 열리게 연결한다.</summary>
        public static void ConnectAll(BuildingPanel panel)
        {
            foreach (var building in Object.FindObjectsByType<SelectableBuilding>(FindObjectsSortMode.None))
                building.onClicked.AddListener(panel.Open);
        }

        /// <summary>
        /// 가운데 건물 패널(처음엔 숨김): 화면 전체 어두운 배경(누르면 닫힘) + 520x420 판에 제목·설명·상품 목록 칸·닫기 버튼.
        /// 목록 칸은 기본으로 "상품 준비 중"이고, 상점(HospitalShop 등)이 Opened 때 채운다. 상인 그림은 쓰지 않는다.
        /// </summary>
        public static BuildingPanel Create(Transform parent)
        {
            var root = new GameObject("BuildingPanel", typeof(RectTransform), typeof(Image), typeof(Button), typeof(BuildingPanel));
            root.transform.SetParent(parent, false);
            Stretch(root.GetComponent<RectTransform>());
            root.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);
            var panel = root.GetComponent<BuildingPanel>();
            root.GetComponent<Button>().onClick.AddListener(panel.Close);

            // 판 안 클릭이 부모(배경 닫기 버튼)로 올라가 패널이 닫히지 않게, 판에도 아무 일 안 하는 버튼을 둬 클릭을 받는다.
            var box = new GameObject("Box", typeof(RectTransform), typeof(Image), typeof(Button));
            box.transform.SetParent(root.transform, false);
            box.GetComponent<Button>().transition = Selectable.Transition.None;
            var boxRect = box.GetComponent<RectTransform>();
            boxRect.sizeDelta = new Vector2(520f, 420f);
            boxRect.anchoredPosition = Vector2.zero; // 화면 가운데(상인 그림은 빼서 Portrait는 null)
            DraggablePanel.Attach(boxRect); // 판을 잡고 끌어 옮길 수 있다(뒤 어두운 막은 고정)
            box.GetComponent<Image>().color = new Color(0.13f, 0.12f, 0.15f, 0.96f);

            panel.Title = CreateText(box.transform, "", 28, TextAnchor.MiddleLeft);
            Place(panel.Title.rectTransform, new Vector2(24f, -20f), new Vector2(400f, 40f));

            panel.Description = CreateText(box.transform, "", 18, TextAnchor.UpperLeft);
            panel.Description.color = new Color(0.85f, 0.85f, 0.85f);
            Place(panel.Description.rectTransform, new Vector2(24f, -66f), new Vector2(472f, 48f));

            // 상품 목록 칸(빈 스크롤 영역). 나중에 ItemListContent 아래에 상품 줄을 추가하면 된다.
            var list = new GameObject("ItemList", typeof(RectTransform), typeof(Image), typeof(ScrollRect), typeof(RectMask2D));
            list.transform.SetParent(box.transform, false);
            Place(list.GetComponent<RectTransform>(), new Vector2(24f, -124f), new Vector2(472f, 272f));
            list.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.05f);
            var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            content.transform.SetParent(list.transform, false);
            var contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.sizeDelta = Vector2.zero;
            content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = list.GetComponent<ScrollRect>();
            scroll.content = contentRect;
            scroll.horizontal = false;
            SmoothWheelScroll.Attach(scroll); // 휠로 한 줄씩 부드럽게(끝에서 튕기지 않게 Clamped)
            panel.ItemListContent = contentRect;

            var empty = CreateText(list.transform, "상품 준비 중", 18, TextAnchor.MiddleCenter);
            empty.color = new Color(1f, 1f, 1f, 0.4f);
            Stretch(empty.rectTransform);
            panel.EmptyLabel = empty;

            var close = new GameObject("CloseButton", typeof(RectTransform), typeof(Image), typeof(Button));
            close.transform.SetParent(box.transform, false);
            var closeRect = close.GetComponent<RectTransform>();
            closeRect.anchorMin = closeRect.anchorMax = closeRect.pivot = new Vector2(1f, 1f);
            closeRect.anchoredPosition = new Vector2(-14f, -14f);
            closeRect.sizeDelta = new Vector2(40f, 40f);
            close.GetComponent<Image>().color = new Color(0.3f, 0.28f, 0.33f, 1f);
            close.GetComponent<Button>().onClick.AddListener(panel.Close);
            Stretch(CreateText(close.transform, "X", 22, TextAnchor.MiddleCenter).rectTransform);

            root.transform.SetAsLastSibling(); // 같은 Canvas의 다른 메뉴 패널보다 위에 뜬다
            root.SetActive(false);
            return panel;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        /// <summary>부모 왼쪽 위 기준으로 놓는다.</summary>
        private static void Place(RectTransform rect, Vector2 topLeft, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = topLeft;
            rect.sizeDelta = size;
        }

        private static Text CreateText(Transform parent, string content, int fontSize, TextAnchor alignment)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);

            var text = go.GetComponent<Text>();
            text.text = content;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = Color.white;
            return text;
        }

        public void Open(SelectableBuilding building)
        {
            Current = building;
            Title.text = building.DisplayName;
            Description.text = building.Description;
            if (Portrait != null)
            {
                Portrait.sprite = building.Portrait;
                Portrait.gameObject.SetActive(building.Portrait != null);
            }
            building.SetHover(false);
            gameObject.SetActive(true);
            Opened?.Invoke(this, building);
            // 처음 열 때 판·제목·버튼이 다음 테마 스캔까지 기본색으로 보여 한 번 반짝이지 않게, 레이아웃을 잡고 바로 테마를 입힌다.
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)transform);
            UIThemeApplier.ApplyNow(transform);
        }

        public void Close()
        {
            Current = null;
            gameObject.SetActive(false);
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) Close();
        }
    }
}
