using UnityEngine;
using UnityEngine.UI;

namespace GN3.UI
{
    public static class ScrollListWrapper
    {
        private const float ScrollbarWidth = 14f;

        /// <summary>
        /// 부모(패널) 안의 offsetMin~offsetMax 고정 영역에 ScrollView(뷰포트 + 오른쪽 스크롤바)를 만들고,
        /// content를 그 안으로 옮긴다. content에는 이미 VerticalLayoutGroup + ContentSizeFitter(세로 PreferredSize)가
        /// 붙어있다고 가정한다.
        ///
        /// content 자신의 anchorMin/anchorMax/sizeDelta는 ContentSizeFitter가 계속 덮어써서(비어있으면 높이 0)
        /// 그대로 재사용할 수 없기 때문에, 스크롤 영역의 크기는 반드시 별도로 지정해야 한다.
        /// </summary>
        public static void Wrap(RectTransform content, Vector2 offsetMin, Vector2 offsetMax)
        {
            var parent = content.parent;
            int siblingIndex = content.GetSiblingIndex();

            // 패널 안의 고정된 영역을 차지하는 ScrollView (content 크기와 무관하게 항상 동일)
            var scrollGO = new GameObject("ScrollView", typeof(RectTransform), typeof(ScrollRect));
            var scrollRect3D = (RectTransform)scrollGO.transform;
            scrollRect3D.SetParent(parent, false);
            scrollRect3D.SetSiblingIndex(siblingIndex);
            scrollRect3D.anchorMin = Vector2.zero;
            scrollRect3D.anchorMax = Vector2.one;
            scrollRect3D.pivot = new Vector2(0.5f, 0.5f);
            scrollRect3D.offsetMin = offsetMin;
            scrollRect3D.offsetMax = offsetMax;

            // 뷰포트: 오른쪽에 스크롤바가 들어갈 자리만큼 안쪽으로 좁힘
            var viewportGO = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            var viewportRect = (RectTransform)viewportGO.transform;
            viewportRect.SetParent(scrollRect3D, false);
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.pivot = new Vector2(0f, 1f);
            viewportRect.offsetMin = Vector2.zero;
            viewportRect.offsetMax = new Vector2(-ScrollbarWidth, 0f);

            content.SetParent(viewportRect, false);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = Vector2.zero; // 이전 anchor 기준의 sizeDelta는 의미가 없으므로 버리고 ContentSizeFitter가 새로 계산하게 함
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);

            // 오른쪽 스크롤바 (마우스로 잡고 드래그하는 실제 바)
            var scrollbar = CreateBar(scrollRect3D, ScrollbarWidth, 0f);

            var scrollRect = scrollGO.GetComponent<ScrollRect>();
            scrollRect.content = content;
            scrollRect.viewport = viewportRect;
            scrollRect.verticalScrollbar = scrollbar;
            scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 20f;
            SmoothWheelScroll.Attach(scrollRect); // 휠로 부드럽게(기본 휠 처리는 끈다)
        }

        /// <summary>
        /// 손수 만든 ScrollRect(뷰포트 = 자기 자신, RectMask2D)에 같은 모양 스크롤바를 붙인다. 목록 오른쪽 안쪽에 얇게 겹쳐 그리고,
        /// 내용이 짧아 스크롤할 게 없으면 숨는다. 내용(content)을 다시 채운 뒤에도 바가 위에 오게 맨 뒤 형제로 둔다.
        /// </summary>
        public static Scrollbar AddOverlayScrollbar(ScrollRect scroll)
        {
            if (scroll == null || scroll.verticalScrollbar != null) return scroll != null ? scroll.verticalScrollbar : null;
            var bar = CreateBar(scroll.transform, OverlayWidth, 2f);
            bar.transform.SetAsLastSibling();
            scroll.verticalScrollbar = bar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            return bar;
        }

        private const float OverlayWidth = 10f;

        /// <summary>parent 오른쪽 끝(안쪽 margin)에 세로 스크롤바: 옅은 바탕 + 잡는 손잡이.</summary>
        private static Scrollbar CreateBar(Transform parent, float width, float margin)
        {
            var scrollbarGO = new GameObject("Scrollbar", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
            var scrollbarRect = (RectTransform)scrollbarGO.transform;
            scrollbarRect.SetParent(parent, false);
            scrollbarRect.anchorMin = new Vector2(1f, 0f);
            scrollbarRect.anchorMax = new Vector2(1f, 1f);
            scrollbarRect.pivot = new Vector2(1f, 1f);
            scrollbarRect.offsetMin = new Vector2(-width - margin, margin);
            scrollbarRect.offsetMax = new Vector2(-margin, -margin);
            scrollbarGO.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.08f);

            var handleGO = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            var handleRect = (RectTransform)handleGO.transform;
            handleRect.SetParent(scrollbarRect, false);
            handleRect.anchorMin = Vector2.zero;
            handleRect.anchorMax = Vector2.one;
            handleRect.offsetMin = new Vector2(2f, 2f);
            handleRect.offsetMax = new Vector2(-2f, -2f);
            var handleImage = handleGO.GetComponent<Image>();
            handleImage.color = new Color(1f, 1f, 1f, 0.35f);

            var scrollbar = scrollbarGO.GetComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.handleRect = handleRect;
            scrollbar.targetGraphic = handleImage;
            return scrollbar;
        }
    }
}
