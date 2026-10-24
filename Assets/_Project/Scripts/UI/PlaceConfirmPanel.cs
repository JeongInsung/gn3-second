using System;
using UnityEngine;
using UnityEngine.UI;

namespace GN3.UI
{
    /// <summary>
    /// 건물을 놓으려고 클릭하면(BuildPlacement) 미리보기 위에 뜨는 확인 창: "…을 여기에 짓겠습니까?" + 비용 + [짓기]·[취소].
    /// 옮기기도 같은 창([옮기기]·[취소]). [취소]·ESC는 창만 닫고 미리보기는 계속 들고 있다. MainMenuBootstrapper가 씬마다 HUD에 만든다.
    /// </summary>
    public class PlaceConfirmPanel : MonoBehaviour
    {
        private const float AnchorGap = 8f;     // 미리보기 윗변과 창 사이
        private const float ScreenMargin = 12f; // 화면 가장자리와 창 사이

        private static PlaceConfirmPanel _instance;

        /// <summary>창이 떠 있는가.</summary>
        public static bool Active => _instance != null && _instance.gameObject.activeSelf;

        private RectTransform _hud;
        private Text _title;
        private Text _detail;
        private Text _okLabel;
        private Action _ok;
        private Action _cancel;

        public static void Create(Transform hud)
        {
            var go = new GameObject("PlaceConfirmPanel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter), typeof(PlaceConfirmPanel));
            go.transform.SetParent(hud, false);
            var panel = go.GetComponent<PlaceConfirmPanel>();
            panel._hud = (RectTransform)hud;
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0f);
            var image = go.GetComponent<Image>();
            image.sprite = UITheme.Panel;
            image.type = Image.Type.Sliced;
            var layout = go.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(18, 18, 12, 12);
            layout.spacing = 6f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            var fitter = go.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            panel._title = ShopListUI.CreateText(go.transform, "", 17, TextAnchor.MiddleCenter, UITheme.BodyText);
            panel._title.horizontalOverflow = HorizontalWrapMode.Overflow;
            panel._detail = ShopListUI.CreateText(go.transform, "", 15, TextAnchor.MiddleCenter, UITheme.MutedText);
            panel._detail.horizontalOverflow = HorizontalWrapMode.Overflow;

            var buttons = new GameObject("Buttons", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            buttons.transform.SetParent(go.transform, false);
            var row = buttons.GetComponent<HorizontalLayoutGroup>();
            row.spacing = 12f;
            row.padding = new RectOffset(0, 0, 4, 0);
            row.childAlignment = TextAnchor.MiddleCenter;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = row.childForceExpandHeight = false;
            var ok = ShopListUI.CreateButton(buttons.transform, "짓기", () => panel._ok?.Invoke(), 90f);
            panel._okLabel = ok.GetComponentInChildren<Text>();
            ShopListUI.CreateButton(buttons.transform, "취소", () => panel._cancel?.Invoke(), 90f);

            EscapeCloser.Register(go, () => panel._cancel?.Invoke(), WindowRole.Popup);
            UIThemeApplier.ApplyNow(go.transform);
            go.SetActive(false);
            _instance = panel;
        }

        /// <summary>worldAnchor(미리보기 윗변 가운데) 위에 창을 띄운다. ok·cancel은 버튼(cancel은 ESC도).</summary>
        public static void Show(string title, string detail, string okLabel, Vector3 worldAnchor, Action ok, Action cancel)
        {
            if (_instance == null) return;
            var panel = _instance;
            panel._title.text = title;
            panel._detail.text = detail;
            panel._detail.gameObject.SetActive(!string.IsNullOrEmpty(detail));
            panel._okLabel.text = okLabel;
            panel._ok = ok;
            panel._cancel = cancel;
            panel.gameObject.SetActive(true);
            panel.transform.SetAsLastSibling();
            panel.Place(worldAnchor);
        }

        public static void Hide()
        {
            if (_instance == null) return;
            _instance._ok = _instance._cancel = null;
            _instance.gameObject.SetActive(false);
        }

        /// <summary>미리보기 위, 화면 밖으로 나가지 않게.</summary>
        private void Place(Vector3 worldAnchor)
        {
            var rect = (RectTransform)transform;
            LayoutRebuilder.ForceRebuildLayoutImmediate(rect); // 크기를 알아야 화면 안으로 맞춘다
            var cam = Camera.main;
            var canvas = _hud.GetComponentInParent<Canvas>();
            var uiCam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            Vector2 screen = cam != null ? (Vector2)cam.WorldToScreenPoint(worldAnchor) : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_hud, screen, uiCam, out var local);

            Vector2 size = rect.rect.size;
            Rect area = _hud.rect;
            float x = Mathf.Clamp(local.x, area.xMin + ScreenMargin + size.x * 0.5f, area.xMax - ScreenMargin - size.x * 0.5f);
            float y = Mathf.Clamp(local.y + AnchorGap, area.yMin + ScreenMargin, area.yMax - ScreenMargin - size.y);
            rect.anchoredPosition = new Vector2(x, y);
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession() => _instance = null;
    }
}
