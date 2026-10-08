using System.Collections.Generic;
using GN3.Mercenaries;
using GN3.World;
using UnityEngine;
using UnityEngine.UI;

namespace GN3.UI
{
    /// <summary>
    /// 병원 건물 패널의 목록 칸을 채운다(보기 전용): 입원 중인 용병의 부상·질병 진행도·남은 시간·쌓인 치료비, 자리가 없어 기다리는 환자.
    /// 환자는 스스로 입원하고 다 나으면 퇴원한다. 규칙은 Hospital. 갱신 방식은 TrainingHallUI와 같다
    /// (구성이 바뀔 때만 새로 만들고, 열려 있는 동안 1초마다 글자만 바꾼다). MainMenuBootstrapper가 만든다.
    /// </summary>
    public class HospitalUI : MonoBehaviour
    {
        public const string HospitalKeyword = "병원";
        private const float RefreshSeconds = 1f;

        private BuildingPanel _panel;
        private float _nextRefresh;
        private readonly Dictionary<string, (Mercenary merc, Text title, Text progress, bool admitted)> _labels =
            new Dictionary<string, (Mercenary, Text, Text, bool)>();

        private void Awake()
        {
            BuildingPanel.Opened += HandleOpened;
            Hospital.OnChanged += RedrawIfOpen;
        }

        private void OnDestroy()
        {
            BuildingPanel.Opened -= HandleOpened;
            Hospital.OnChanged -= RedrawIfOpen;
        }

        private void HandleOpened(BuildingPanel panel, SelectableBuilding building)
        {
            if (building == null || !building.DisplayName.Contains(HospitalKeyword)) return;
            _panel = panel;
            Draw();
        }

        private bool IsShowing =>
            _panel != null && _panel.gameObject.activeInHierarchy && _panel.Current != null && _panel.Current.DisplayName.Contains(HospitalKeyword);

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
                if (label.progress != null) label.progress.text = Progress(label.merc, label.admitted);
            }
        }

        private void Draw()
        {
            _nextRefresh = Time.unscaledTime + RefreshSeconds;
            _labels.Clear();
            ShopListUI.BeginList(_panel);

            var patients = Hospital.Patients;
            CreateHeaderRow($"입원 중 ({patients.Count}/{Hospital.Capacity}) · 1시간마다 {Hospital.ProgressPerHour * 100f:0}% 회복 · 치료비 시간당 {Hospital.FeePerHour}G");
            if (patients.Count == 0) ShopListUI.CreateEmptyRow(_panel, "입원한 용병이 없습니다. 다치거나 병든 용병은 스스로 입원합니다.");
            foreach (var merc in patients) CreateMercRow(merc, true);

            var waiting = Hospital.Waiting;
            if (waiting.Count > 0)
            {
                CreateHeaderRow($"자리가 나기를 기다리는 중 ({waiting.Count}명)");
                foreach (var merc in waiting) CreateMercRow(merc, false);
            }

            ShopListUI.EndList(_panel);
        }

        private void CreateMercRow(Mercenary merc, bool admitted)
        {
            var row = ShopListUI.CreateRow(_panel, (admitted ? "Patient_" : "Waiting_") + merc.Name);
            CharacterPortraitUI.Create(row.transform, merc.Appearance, 40);
            ShopListUI.CreateTwoLineLabel(row.transform, Title(merc), Progress(merc, admitted));
            var label = row.transform.Find("Label");
            if (label == null) return;
            var texts = label.GetComponentsInChildren<Text>();
            if (texts.Length >= 2) _labels[merc.Id] = (merc, texts[0], texts[1], admitted);
            // 떨어진 능력치는 마우스를 올리면(툴팁은 그릴 때 정해져, 진행도가 바뀌면 다음에 다시 그릴 때 갱신)
            string tip = Ailments.EffectTooltip(merc);
            if (tip != null && texts.Length >= 2)
            {
                texts[1].raycastTarget = true;
                texts[1].gameObject.AddComponent<TooltipTrigger>().Text = tip;
            }
        }

        private static string Title(Mercenary merc) => $"[{merc.Grade}] {merc.Name} · {merc.Class.ClassName} Lv {merc.Level}";

        private static string Progress(Mercenary merc, bool admitted)
        {
            if (!merc.HasAilment) return "다 나았다";
            string ailments = merc.AilmentSummary();
            return admitted
                ? $"{ailments} · 남은 약 {Hospital.RemainingHours(merc):0.#}시간 · 치료비 {Hospital.Fee(merc)}G"
                : $"{ailments} · 마을에서 쉬는 중(저절로 시간당 {Ailments.NaturalProgressPerHour * 100f:0}%)";
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
