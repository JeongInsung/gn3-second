using UnityEngine;
using UnityEngine.UI;

namespace GN3.UI
{
    /// <summary>
    /// 마을 화면의 떠 있는 작은 글 하나: 마우스를 올린 건물 이름(SelectableBuilding), 놓을 수 없는 자리의 이유(BuildPlacement).
    /// 보여 줄 쪽이 매 프레임 Show를 부르고, 그 프레임에 아무도 안 부르면 숨는다(따로 끄지 않아도 된다).
    /// 화면 UI(Overlay)라 건물·나무에 가려지지 않는다(VillageNameTag와 같은 모양).
    /// </summary>
    public class WorldHoverLabel : MonoBehaviour
    {
        private const int SortingOrder = 0; // 마을 이름표와 같은 높이(창들 아래)

        private static WorldHoverLabel _instance;
        private Text _text;
        private int _shownFrame = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession() => _instance = null;

        /// <summary>이번 프레임에 world 위(화면 좌표로 바꿔)에 글을 띄운다. warn이면 붉은 글.</summary>
        public static void Show(string message, Vector3 world, bool warn = false)
        {
            var cam = Camera.main;
            if (cam == null || string.IsNullOrEmpty(message)) return;
            if (_instance == null) _instance = Create();
            var label = _instance;
            label._text.text = message;
            label._text.color = warn ? new Color(1f, 0.55f, 0.5f) : Color.white;
            label._text.transform.position = cam.WorldToScreenPoint(world);
            label._text.enabled = true;
            label._shownFrame = Time.frameCount;
        }

        private static WorldHoverLabel Create()
        {
            var go = new GameObject("WorldHoverLabel", typeof(Canvas), typeof(CanvasScaler), typeof(WorldHoverLabel));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            var textGo = new GameObject("Text", typeof(RectTransform), typeof(Text), typeof(Outline));
            textGo.transform.SetParent(go.transform, false);
            var rect = (RectTransform)textGo.transform;
            rect.sizeDelta = new Vector2(400f, 26f);
            rect.pivot = new Vector2(0.5f, 0f); // 아래 가운데를 건물 위쪽 점에 맞춘다
            var text = textGo.GetComponent<Text>();
            text.font = UITheme.Font;
            text.fontSize = 18;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.LowerCenter;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.raycastTarget = false;
            var outline = textGo.GetComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
            outline.effectDistance = new Vector2(1.5f, -1.5f);

            var label = go.GetComponent<WorldHoverLabel>();
            label._text = text;
            return label;
        }

        private void LateUpdate()
        {
            if (_text != null && _shownFrame != Time.frameCount) _text.enabled = false;
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }
    }
}
