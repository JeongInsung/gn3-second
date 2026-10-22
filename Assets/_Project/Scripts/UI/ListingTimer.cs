using UnityEngine;
using UnityEngine.UI;

namespace GN3.UI
{
    /// <summary>
    /// 게시판·시장의 씬 새로고침 버튼을 숨기고, 그 자리(오른쪽 끝을 맞춰)에 "새 ○○까지 n일" 글자를 놓는다.
    /// 목록은 게임 시간이 지나면 저절로 바뀐다(QuestBoardListings·MarketListings).
    /// </summary>
    public static class ListingTimer
    {
        public static Text ReplaceRefreshButton(Button refreshButton, RectTransform panel, Font font, float y)
        {
            var go = new GameObject("NextRefreshText", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(panel, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 1f);
            rect.sizeDelta = new Vector2(360f, 40f);
            rect.anchoredPosition = new Vector2(-40f, y);

            if (refreshButton != null)
            {
                // 버튼 오른쪽 끝에 글자 오른쪽 끝을 맞춘다
                var b = (RectTransform)refreshButton.transform;
                var corners = new Vector3[4];
                b.GetWorldCorners(corners);
                Vector3 topRight = panel.InverseTransformPoint(corners[2]);
                Vector2 panelTopRight = new Vector2(panel.rect.xMax, panel.rect.yMax);
                rect.anchoredPosition = new Vector2(topRight.x - panelTopRight.x, y);
                refreshButton.gameObject.SetActive(false);
            }

            var text = go.GetComponent<Text>();
            text.font = font;
            text.fontSize = 18;
            text.alignment = TextAnchor.MiddleRight;
            text.color = UITheme.MutedText;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        /// <summary>"새 의뢰까지 3일" / 하루 남으면 "내일 새 의뢰".</summary>
        public static string Label(string what, int days) => days <= 1 ? $"내일 새 {what}" : $"새 {what}까지 {days}일";
    }
}
