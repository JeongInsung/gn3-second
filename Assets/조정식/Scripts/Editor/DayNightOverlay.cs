using GN3.World;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;
using UnityEngine.UIElements;

namespace GN3.EditorTools
{
    /// <summary>
    /// Scene 뷰에 항상 떠 있는 "낮/밤 시간" 패널. 슬라이더나 버튼으로 시간을 바꾸면
    /// Play 없이도 Scene/Game 뷰 조명이 바로 바뀐다(Play 중에도 동작).
    /// 패널이 안 보이면 Scene 뷰에서 ` 키(오버레이 메뉴) → "낮/밤 시간" 체크.
    /// </summary>
    [Overlay(typeof(SceneView), "gn3-day-night", "낮/밤 시간", true)]
    public class DayNightOverlay : Overlay
    {
        private static readonly (string label, float hour)[] Presets =
        {
            ("새벽", 6f), ("아침", 9f), ("정오", 12f), ("노을", 18f), ("밤", 22f),
        };

        private Label _timeLabel;
        private Slider _slider;

        public override VisualElement CreatePanelContent()
        {
            var root = new VisualElement { style = { width = 260, paddingLeft = 4, paddingRight = 4, paddingTop = 2, paddingBottom = 4 } };

            _timeLabel = new Label { style = { fontSize = 16, unityFontStyleAndWeight = FontStyle.Bold, unityTextAlign = TextAnchor.MiddleCenter } };
            root.Add(_timeLabel);

            _slider = new Slider(0f, 24f) { style = { marginTop = 2 } };
            _slider.RegisterValueChangedCallback(evt => SetTime(evt.newValue));
            root.Add(_slider);

            var buttons = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 2 } };
            foreach (var (label, hour) in Presets)
            {
                float h = hour;
                buttons.Add(new Button(() => SetTime(h)) { text = label, style = { flexGrow = 1 } });
            }
            root.Add(buttons);

            Refresh();
            root.RegisterCallback<AttachToPanelEvent>(_ => EditorApplication.update += Refresh);
            root.RegisterCallback<DetachFromPanelEvent>(_ => EditorApplication.update -= Refresh);
            return root;
        }

        private static DayNightCycle FindCycle()
        {
            return DayNightCycle.Instance != null ? DayNightCycle.Instance : Object.FindFirstObjectByType<DayNightCycle>();
        }

        private static void SetTime(float hour)
        {
            var cycle = FindCycle();
            if (cycle == null || Mathf.Approximately(cycle.TimeOfDay, hour)) return;

            Undo.RecordObject(cycle, "Change Time Of Day");
            cycle.TimeOfDay = hour;
            if (!Application.isPlaying) EditorUtility.SetDirty(cycle);
            SceneView.RepaintAll();
            EditorApplication.QueuePlayerLoopUpdate();
        }

        // 인스펙터·Play 중 UI·autoAdvance로 바뀐 값도 패널에 따라오게 한다.
        private void Refresh()
        {
            if (_timeLabel == null) return;
            var cycle = FindCycle();
            if (cycle == null)
            {
                _timeLabel.text = "DayNight 없음";
                _slider.SetEnabled(false);
                return;
            }

            _slider.SetEnabled(true);
            _timeLabel.text = DayNightCycle.FormatTime(cycle.TimeOfDay);
            if (!Mathf.Approximately(_slider.value, cycle.TimeOfDay))
                _slider.SetValueWithoutNotify(cycle.TimeOfDay);
        }
    }
}
