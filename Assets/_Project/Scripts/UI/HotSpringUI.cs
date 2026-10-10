using System.Collections.Generic;
using GN3.Mercenaries;
using GN3.World;
using UnityEngine;
using UnityEngine.UI;

namespace GN3.UI
{
    /// <summary>
    /// 온천 건물 패널의 목록 칸을 채운다(보기 전용): 지금 온천에 있는 용병과 피로·남은 시간.
    /// 피로가 쌓인 용병들이 스스로 무작위로 찾아오고(VillagePartyPresenter) 시간이 다 되면 나간다. 규칙은 HotSpring.
    /// 갱신 방식은 TrainingHallUI와 같다(구성이 바뀔 때만 새로 만들고, 1초마다 글자만 바꾼다). MainMenuBootstrapper가 만든다.
    /// </summary>
    public class HotSpringUI : MonoBehaviour
    {
        public const string SpringKeyword = "온천";
        private const float RefreshSeconds = 1f;

        private BuildingPanel _panel;
        private float _nextRefresh;
        private readonly Dictionary<string, (Mercenary merc, Text title, Text progress)> _labels =
            new Dictionary<string, (Mercenary, Text, Text)>();

        private void Awake()
        {
            BuildingPanel.Opened += HandleOpened;
            HotSpring.OnChanged += RedrawIfOpen;
        }

        private void OnDestroy()
        {
            BuildingPanel.Opened -= HandleOpened;
            HotSpring.OnChanged -= RedrawIfOpen;
        }

        private void HandleOpened(BuildingPanel panel, SelectableBuilding building)
        {
            if (building == null || !building.DisplayName.Contains(SpringKeyword)) return;
            _panel = panel;
            Draw();
        }

        private bool IsShowing =>
            _panel != null && _panel.gameObject.activeInHierarchy && _panel.Current != null && _panel.Current.DisplayName.Contains(SpringKeyword);

        private void RedrawIfOpen()
        {
            if (IsShowing) Draw();
        }

        private void Update()
        {
            if (!IsShowing || Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + RefreshSeconds;
            foreach (var label in _labels.Values)
            {
                if (label.title != null) label.title.text = Title(label.merc);
                if (label.progress != null) label.progress.text = Progress(label.merc);
            }
        }

        private void Draw()
        {
            _nextRefresh = Time.unscaledTime + RefreshSeconds;
            _labels.Clear();
            ShopListUI.BeginList(_panel);

            var bathers = HotSpring.Bathers;
            CreateHeaderRow($"온천 ({bathers.Count}/{HotSpring.Capacity}) · 1시간마다 피로 -{HotSpring.FatigueReliefPerHour:0}");
            if (bathers.Count == 0) ShopListUI.CreateEmptyRow(_panel, "지금 온천에 있는 용병이 없습니다. 지친 용병들이 가끔 스스로 쉬러 옵니다.");
            foreach (var merc in bathers)
            {
                var row = ShopListUI.CreateRow(_panel, "Bather_" + merc.Name);
                CharacterPortraitUI.Create(row.transform, merc.Appearance, 40);
                ShopListUI.CreateTwoLineLabel(row.transform, Title(merc), Progress(merc));
                Remember(row, merc);
            }

            ShopListUI.EndList(_panel);
        }

        /// <summary>CreateTwoLineLabel이 만든 "Label" 칸의 제목·부제 글자를 기억해 둔다.</summary>
        private void Remember(GameObject row, Mercenary merc)
        {
            var label = row.transform.Find("Label");
            if (label == null) return;
            var texts = label.GetComponentsInChildren<Text>();
            if (texts.Length < 2) return;
            _labels[merc.Id] = (merc, texts[0], texts[1]);
        }

        private static string Title(Mercenary merc) => $"[{merc.Grade}] {merc.Name} · {merc.Class.ClassName} Lv {merc.Level}";

        private static string Progress(Mercenary merc) =>
            $"피로 {merc.Fatigue} · 사기 {merc.Morale} · 남은 약 {HotSpring.RemainingHours(merc):0.#}시간";

        private void CreateHeaderRow(string title)
        {
            var row = ShopListUI.CreateRow(_panel, "Header");
            var text = ShopListUI.CreateText(row.transform, title, 16, TextAnchor.MiddleLeft, UITheme.TitleText);
            text.fontStyle = FontStyle.Bold;
            text.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        }
    }
}
