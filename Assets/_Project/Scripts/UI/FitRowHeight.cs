using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GN3.UI
{
    /// <summary>
    /// 목록 줄 높이를 글 내용에 맞춘다: 줄 높이 = max(기본 높이, 글 묶음마다 Σ글 높이 + 사이 간격 + 위아래 여백).
    /// 글 폭이나 내용이 바뀔 때마다(LateUpdate에서 확인) 다시 재서 늘리고 줄인다 — 목록을 그리는 순간 폭이 아직 틀려도
    /// 다음 프레임에 저절로 맞는다. 글씨 크기는 건드리지 않는다(넘치면 줄이 커진다).
    /// 줄에 LayoutElement가 있어야 하고(min/preferredHeight를 바꾼다), 부모는 세로 레이아웃 그룹이어야 한다.
    /// 묶음을 등록하지 않으면(auto) 줄의 바로 아래 글과 바로 아래 세로 묶음(VerticalLayoutGroup) 안의 글을 스스로 찾는다(버튼 글은 뺀다).
    /// </summary>
    public class FitRowHeight : MonoBehaviour
    {
        private readonly List<(Text[] texts, float spacing)> _columns = new List<(Text[], float)>();
        private readonly List<(Text[] texts, float spacing)> _autoColumns = new List<(Text[], float)>();
        private int _autoChildCount = -1;
        private LayoutElement _layout;
        private float _baseHeight;
        private float _padding;
        private float _lastSignature = float.NaN;
        private float _lastSet = -1f; // 마지막으로 이 컴포넌트가 정한 높이

        /// <summary>row에 붙인다. baseHeight = 최소 높이, verticalPadding = 위아래 여백 합.</summary>
        public static FitRowHeight Attach(GameObject row, float baseHeight, float verticalPadding)
        {
            var fit = row.GetComponent<FitRowHeight>();
            if (fit == null) fit = row.AddComponent<FitRowHeight>();
            fit._layout = row.GetComponent<LayoutElement>();
            if (fit._layout == null) fit._layout = row.AddComponent<LayoutElement>();
            fit._baseHeight = baseHeight;
            fit._padding = verticalPadding;
            fit._layout.minHeight = fit._layout.preferredHeight = baseHeight;
            fit._lastSet = baseHeight;
            return fit;
        }

        /// <summary>세로로 쌓인 글 묶음 하나(한 개여도 된다). 묶음들 중 가장 높은 것에 맞춘다.</summary>
        public FitRowHeight AddColumn(float spacing, params Text[] texts)
        {
            _columns.Add((texts, spacing));
            _lastSignature = float.NaN;
            return this;
        }

        private List<(Text[] texts, float spacing)> Columns()
        {
            if (_columns.Count > 0) return _columns;
            if (transform.childCount == _autoChildCount) return _autoColumns;
            _autoChildCount = transform.childCount;
            _autoColumns.Clear();
            foreach (Transform child in transform)
            {
                if (child.GetComponent<Button>() != null) continue;
                var text = child.GetComponent<Text>();
                if (text != null) { _autoColumns.Add((new[] { text }, 0f)); continue; }
                var column = child.GetComponent<VerticalLayoutGroup>();
                if (column == null) continue;
                var texts = new List<Text>();
                foreach (Transform inner in child)
                    if (inner.GetComponent<Button>() == null && inner.GetComponent<Text>() is Text t) texts.Add(t);
                if (texts.Count > 0) _autoColumns.Add((texts.ToArray(), column.spacing));
            }
            return _autoColumns;
        }

        private void LateUpdate()
        {
            if (_layout == null) return;
            var columns = Columns();
            if (columns.Count == 0) return;
            // 폭·글이 그대로면 다시 재지 않는다
            float signature = columns.Count;
            foreach (var (texts, _) in columns)
                foreach (var text in texts)
                    if (text != null) signature = signature * 31f + text.rectTransform.rect.width + text.text.GetHashCode() % 997;
            if (signature == _lastSignature) return;
            _lastSignature = signature;
            Fit();
        }

        /// <summary>지금 폭으로 재서 줄 높이를 맞춘다.</summary>
        public void Fit()
        {
            if (_layout == null) return;
            float needed = 0f;
            foreach (var (texts, spacing) in Columns())
            {
                float sum = 0f;
                int count = 0;
                foreach (var text in texts)
                {
                    if (text == null || !text.gameObject.activeSelf || text.rectTransform.rect.width <= 1f) continue;
                    sum += text.preferredHeight;
                    count++;
                }
                if (count > 1) sum += spacing * (count - 1);
                needed = Mathf.Max(needed, sum);
            }
            // 붙인 뒤 다른 코드가 줄 높이를 직접 바꿨으면(훈련장 줄 170 등) 그것을 새 기본 높이로 삼는다
            if (_lastSet >= 0f && Mathf.Abs(_layout.preferredHeight - _lastSet) >= 0.5f) _baseHeight = _layout.preferredHeight;
            float height = Mathf.Ceil(Mathf.Max(_baseHeight, needed + _padding));
            _lastSet = height;
            if (Mathf.Abs(height - _layout.preferredHeight) < 0.5f) return;
            _layout.minHeight = _layout.preferredHeight = height;
            if (transform.parent is RectTransform parent) LayoutRebuilder.MarkLayoutForRebuild(parent);
        }
    }
}
