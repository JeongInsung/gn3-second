using System.Collections.Generic;
using System.Linq;
using GN3.Economy;
using GN3.Mercenaries;
using GN3.Quests;
using GN3.World;
using UnityEngine;
using UnityEngine.UI;

namespace GN3.UI
{
    /// <summary>
    /// 쉼터(온천·도박장·마법 연구소·성당) 건물 패널의 목록 칸을 채운다: 지금 안에 있는 용병과 피로·사기·남은 시간("빼기"),
    /// 그 아래 직접 보낼 수 있는 파티원("넣기", 연구소·성당은 직업이 맞는 사람만).
    /// 용병들은 스스로도 무작위로 찾아오고(VillagePartyPresenter) 시간이 다 되면 나간다. 규칙은 RestVenue.
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
            int facility = _venue == RestVenues.HotSpring ? GuildFacilities.Level(Facility.HotSpring) : 0; // 길드 시설(온천만)
            CreateHeaderRow($"{_venue.Name} {guests.Count}/{_venue.Capacity} · {_venue.Summary}" +
                            (facility > 0 ? $" · 시설 {facility}단계(피로 회복 ×{GuildFacilities.HotSpringReliefMultiplier:0.0#})" : ""));
            if (guests.Count == 0) ShopListUI.CreateEmptyRow(_panel, $"{_venue.Name}에 있는 용병이 없습니다.");
            var venue = _venue;
            foreach (var merc in guests)
            {
                var row = ShopListUI.CreateRow(_panel, "Guest_" + merc.Name);
                CharacterPortraitUI.Create(row.transform, merc.Appearance, 40);
                ShopListUI.CreateTwoLineLabel(row.transform, Title(merc), Progress(merc));
                Remember(row, merc);
                var target = merc;
                ShopListUI.CreateButton(row.transform, "빼기", () => venue.Remove(target));
            }

            // 직접 넣기: 마을에 있는 파티원(파견 중·이미 여기 있는 사람 제외). 연구소·성당은 직업이 맞는 사람만.
            var candidates = PlayerParty.Instance.Members
                .Where(m => m.IsAlive && !ExpeditionLog.Instance.IsOnExpedition(m) && !venue.IsInside(m) && ClassFits(venue, m)).ToList();
            if (candidates.Count > 0) CreateHeaderRow($"{venue.Name}에 보내기 · {venue.MaxSessionHours:0}시간");
            foreach (var merc in candidates)
            {
                var row = ShopListUI.CreateRow(_panel, "Candidate_" + merc.Name);
                CharacterPortraitUI.Create(row.transform, merc.Appearance, 40);
                string reason = BlockReason(venue, merc);
                ShopListUI.CreateTwoLineLabel(row.transform, Title(merc), reason ?? $"피로 {merc.Fatigue} · 사기 {merc.Morale}");
                var target = merc;
                ShopListUI.CreateButton(row.transform, "넣기", () => Send(venue, target)).interactable = reason == null;
            }

            ShopListUI.EndList(_panel);
        }

        /// <summary>연구소는 마법사만, 성당은 힐러만 후보로 보인다(온천·도박장은 누구나).</summary>
        private static bool ClassFits(RestVenue venue, Mercenary merc) =>
            venue == RestVenues.MagicLab ? merc.Class.Kind == MercenaryClassKind.Mage
            : venue == RestVenues.Cathedral ? merc.Class.Kind == MercenaryClassKind.Healer
            : true;

        private static void Send(RestVenue venue, Mercenary merc)
        {
            if (venue.TryAdd(merc, venue.MaxSessionHours))
                ToastLog.Show($"{merc.Name} {venue.Name} 입장 ({venue.MaxSessionHours:0}시간)");
            else
                ToastLog.Show($"{Josa.EunNeun(merc.Name)} 지금 {venue.Name}에 들어갈 수 없습니다.");
        }

        /// <summary>지금 이 쉼터에 넣을 수 없는 이유(넣을 수 있으면 null). RestVenue.CanEnter·TryAdd와 같은 조건.</summary>
        private static string BlockReason(RestVenue venue, Mercenary merc)
        {
            if (Hospital.IsAdmitted(merc)) return "입원 중";
            if (TrainingHall.IsTraining(merc)) return "훈련 중";
            var other = RestVenues.Find(merc);
            if (other != null) return $"{other.Name}에 있음";
            if (RestVenues.IsStudyVenue(venue) && !merc.CanResearchMore) return $"{merc.ResearchLabel} 최대 단계";
            if (RestVenues.IsStudyVenue(venue) && merc.IsExhausted) return $"너무 지침 (피로 {merc.Fatigue})";
            if (!venue.IsEligible(merc)) return "들어갈 수 없음";
            if (!venue.IsOpen) return "밤에는 닫혀 있음";
            if (venue.IsFull) return "정원이 찼음";
            return null;
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

        private static string Title(Mercenary merc) => $"[{merc.Grade}] {merc.Name} · {merc.Class.ClassName} {merc.Age}세";

        private string Progress(Mercenary merc)
        {
            string line = $"피로 {merc.Fatigue} · 사기 {merc.Morale} · 남은 {_venue.RemainingHours(merc):0.#}시간";
            if (!RestVenues.IsStudyVenue(_venue)) return line;
            // 연구소·성당: 지금까지 오른 능력치와 다음 단계까지 남은 시간
            string bonus = merc.ResearchLevel > 0 ? merc.ResearchBonusText() : "없음";
            string research = merc.CanResearchMore
                ? $"{merc.ResearchLabel} {bonus} · 다음 단계 {Mercenary.ResearchHoursPerBonus - merc.ResearchHours:0.#}시간"
                : $"{merc.ResearchLabel} {bonus} (최대)";
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
