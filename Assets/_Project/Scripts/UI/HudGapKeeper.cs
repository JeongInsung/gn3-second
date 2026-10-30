using UnityEngine;

namespace GN3.UI
{
    /// <summary>
    /// 마을 윗줄의 날짜 줄(왼쪽 기준, 글자 길이만큼 늘어난다)이 오른쪽 고정 버튼([1배속])에 붙지 않게 간격을 지킨다.
    /// 간격이 MinGap보다 좁아지면 날짜 줄을 그만큼 작게(왼쪽 위 기준, 하한 MinScale) — 넓어지면 원래 크기로.
    /// MainMenuBootstrapper.CreateDayControls가 날짜 줄에 붙인다.
    /// </summary>
    public class HudGapKeeper : MonoBehaviour
    {
        private const float MinGap = 16f;   // 캔버스 기준 px
        private const float MinScale = 0.85f;

        public RectTransform Right; // 넘어가면 안 되는 오른쪽 버튼

        private readonly Vector3[] _bar = new Vector3[4];
        private readonly Vector3[] _right = new Vector3[4];

        private void LateUpdate()
        {
            var bar = (RectTransform)transform;
            if (Right == null || bar.parent == null) return;
            float unit = bar.parent.lossyScale.x; // 캔버스 px 하나의 월드 폭
            float natural = bar.rect.width * unit;
            if (natural <= 0f || unit <= 0f) return;
            bar.GetWorldCorners(_bar);
            Right.GetWorldCorners(_right);
            float available = _right[0].x - _bar[0].x - MinGap * unit;
            float scale = Mathf.Clamp(available / natural, MinScale, 1f);
            if (Mathf.Abs(bar.localScale.x - scale) > 0.001f) bar.localScale = new Vector3(scale, scale, 1f);
        }
    }
}
