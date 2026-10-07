using UnityEngine;
using UnityEngine.UI;

namespace GN3.UI
{
    /// <summary>
    /// 화면 오른쪽 아래 구석의 우편함 아이콘. 누르면 그 바로 위에 우편함 창(MailboxPanel)을 열고 닫는다.
    /// 안 읽은 편지가 있으면 오른쪽 위에 빨간 "!", 자동/직접 선택을 기다리는 도착이 있으면 주황 숫자 배지.
    /// 그림은 Resources/UIIcons/mailbox. 자기 그림이 있어 UIThemeApplier가 건드리지 않는다.
    /// </summary>
    public class MailboxButton : MonoBehaviour
    {
        private const string IconPath = "UIIcons/mailbox";
        public const float Size = 64f;
        public const float Margin = 16f; // 화면 가장자리에서 띄우는 거리(알림 기록 창도 같이 쓴다)
        private const float BadgeSize = 20f;

        private static readonly Color UnreadColor = new Color(0.85f, 0.15f, 0.15f, 1f);
        private static readonly Color PendingColor = new Color(0.95f, 0.55f, 0.1f, 1f);

        private GameObject _badge;
        private Image _badgeImage;
        private Text _badgeText;

        public static MailboxButton Create(Transform parent)
        {
            var go = new GameObject("MailboxButton", typeof(RectTransform), typeof(Image), typeof(Button), typeof(TooltipTrigger), typeof(MailboxButton));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 0f); // 화면 오른쪽 아래
            rect.anchoredPosition = new Vector2(-Margin, Margin);
            rect.sizeDelta = new Vector2(Size, Size);
            go.GetComponent<TooltipTrigger>().Text = "우편함";
            go.GetComponent<Button>().onClick.AddListener(MailboxPanel.Toggle);

            var image = go.GetComponent<Image>();
            var icon = UIIcons.Load(IconPath);
            if (icon != null)
            {
                image.sprite = icon;
                image.preserveAspect = true;
            }
            else
            {
                // 그림이 없으면 예전처럼 글자 버튼(테마가 버튼 모양을 입힌다)
                image.color = new Color(0.3f, 0.3f, 0.35f, 0.9f);
                var label = CreateText(go.transform, "알림", 15);
                Stretch(label.rectTransform);
            }

            var mailbox = go.GetComponent<MailboxButton>();
            mailbox._badge = CreateBadge(go.transform);
            mailbox._badgeImage = mailbox._badge.GetComponent<Image>();
            mailbox._badgeText = mailbox._badge.GetComponentInChildren<Text>();
            return mailbox;
        }

        private void Update()
        {
            // 자동/직접을 기다리는 도착이 있으면 주황 숫자, 아니면 안 읽은 편지가 있을 때 빨간 "!"
            int pending = Mailbox.PendingChoiceCount;
            bool show = pending > 0 || Mailbox.HasUnread;
            if (_badge.activeSelf != show) _badge.SetActive(show);
            if (!show) return;
            _badgeImage.color = pending > 0 ? PendingColor : UnreadColor;
            _badgeText.text = pending > 0 ? pending.ToString() : "!";
        }

        private static GameObject CreateBadge(Transform parent)
        {
            var badge = new GameObject("Badge", typeof(RectTransform), typeof(Image));
            badge.transform.SetParent(parent, false);
            var rect = badge.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one; // 오른쪽 위
            rect.anchoredPosition = new Vector2(4f, 4f);
            rect.sizeDelta = new Vector2(BadgeSize, BadgeSize);
            var image = badge.GetComponent<Image>();
            image.sprite = Resources.GetBuiltinResource<Sprite>("UI/Skin/Knob.psd"); // 동그라미(자기 그림이라 테마가 안 바꾼다)
            image.color = UnreadColor;
            image.raycastTarget = false;

            var mark = CreateText(badge.transform, "!", 15);
            mark.fontStyle = FontStyle.Bold;
            Stretch(mark.rectTransform);
            badge.SetActive(false);
            return badge;
        }

        private static Text CreateText(Transform parent, string content, int size)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.text = content;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.raycastTarget = false;
            return text;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }
}
