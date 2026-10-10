using System.Collections.Generic;
using System.Linq;
using GN3.Mercenaries;
using GN3.World;
using UnityEngine;
using UnityEngine.UI;

namespace GN3.UI
{
    /// <summary>
    /// 쉼터(온천·도박장·마법 연구소) 건물 패널의 목록 칸을 채운다(보기 전용): 지금 안에 있는 용병과 피로·사기·남은 시간.
    /// 용병들이 스스로 무작위로 찾아오고(VillagePartyPresenter) 시간이 다 되면 나간다. 규칙은 RestVenue.
    /// 갱신 방식은 TrainingHallUI와 같다(구성이 바뀔 때만 새로 만들고, 1초마다 글자만 바꾼다). MainMenuBootstrapper가 만든다.
    /// </summary>
    public class RestVenueUI : MonoBehaviour
    {
        private const float RefreshSeconds = 1f;

        private BuildingPanel _panel;
        private RestVenue _venue;
        private float _nextRefresh;
        private readonly Dictionary<string, (Mercenary merc, Text title, Text progress)> _labels =
            new Dictionary<string, (Mercenary, Text, Text)>();

        private void Awake()
        {
            BuildingPanel.Opened += HandleOpened;
            RestVenues.OnAnyChanged += RedrawIfOpen;
        }

        private void OnDestroy()
        {
            BuildingPanel.Opened -= HandleOpened;
            RestVenues.OnAnyChanged -= RedrawIfOpen;
        }

        private static RestVenue VenueOf(SelectableBuilding building) =>
            building == null ? null : RestVenues.All.FirstOrDefault(v => building.DisplayName.Contains(v.Name));

        private void HandleOpened(BuildingPanel panel, SelectableBuilding building)
        {
            var venue = VenueOf(building);
            if (venue == null) return;
            _panel = panel;
            _venue = venue;
            Draw();
        }

        private bool IsShowing =>
            _panel != null && _venue != null && _panel.gameObject.activeInHierarchy && VenueOf(_panel.Current) == _venue;

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

            var guests = _venue.Guests;
            CreateHeaderRow($"{_venue.Name} ({guests.Count}/{_venue.Capacity}) · {_venue.Summary}");
            if (guests.Count == 0) ShopListUI.CreateEmptyRow(_panel, $"지금 {_venue.Name}에 있는 용병이 없습니다. 용병들이 가끔 스스로 찾아옵니다.");
            foreach (var merc in guests)
            {
                var row = ShopListUI.CreateRow(_panel, "Guest_" + merc.Name);
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

        private string Progress(Mercenary merc)
        {
            string line = $"피로 {merc.Fatigue} · 사기 {merc.Morale} · 남은 약 {_venue.RemainingHours(merc):0.#}시간";
            if (!RestVenues.IsStudyVenue(_venue)) return line;
            // 연구소·성당: 지금까지 오른 능력치와 다음 단계까지 남은 시간
            string bonus = merc.ResearchLevel > 0 ? merc.ResearchBonusText() : "없음";
            string research = merc.CanResearchMore
                ? $"{merc.ResearchLabel} 보너스 {bonus} · 다음 단계까지 약 {Mercenary.ResearchHoursPerBonus - merc.ResearchHours:0.#}시간"
                : $"{merc.ResearchLabel} 보너스 {bonus} (최대)";
            return $"{research} · {line}";
        }

        private void CreateHeaderRow(string title)
        {
            var row = ShopListUI.CreateRow(_panel, "Header");
            var text = ShopListUI.CreateText(row.transform, title, 16, TextAnchor.MiddleLeft, UITheme.TitleText);
            text.fontStyle = FontStyle.Bold;
            text.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        }
    }
}
