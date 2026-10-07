using System.Collections.Generic;
using GN3.Mercenaries;
using GN3.World;
using UnityEngine;
using UnityEngine.UI;

namespace GN3.UI
{
    /// <summary>
    /// 훈련소 건물 패널의 목록 칸을 채운다(보기 전용): 지금 훈련 중인 용병과 경험치·남은 훈련 시간.
    /// 용병들은 스스로 무작위로 훈련하러 오고(VillagePartyPresenter) 시간이 다 되면 나간다. 훈련 규칙은 TrainingHall.
    /// 목록은 창이 열릴 때와 훈련 구성이 바뀔 때만 새로 만들고, 열려 있는 동안 1초(실제 시간)마다는 글자만 바꾼다
    /// (매번 새로 만들면 UIThemeApplier가 테마를 입히기 전 기본색이 잠깐 보여 깜빡였다). MainMenuBootstrapper가 만든다.
    /// </summary>
    public class TrainingHallUI : MonoBehaviour
    {
        public const string HallKeyword = "훈련소";
        private const float RefreshSeconds = 1f;

        private BuildingPanel _panel;
        private float _nextRefresh;
        // 1초 갱신 때 글자만 바꿀 줄들(용병 id → 제목/진행 글자, 훈련 중인지)
        private readonly Dictionary<string, (Mercenary merc, Text title, Text progress, bool training)> _labels =
            new Dictionary<string, (Mercenary, Text, Text, bool)>();

        private void Awake()
        {
            BuildingPanel.Opened += HandleOpened;
            TrainingHall.OnChanged += RedrawIfOpen;
        }

        private void OnDestroy()
        {
            BuildingPanel.Opened -= HandleOpened;
            TrainingHall.OnChanged -= RedrawIfOpen;
        }

        private void HandleOpened(BuildingPanel panel, SelectableBuilding building)
        {
            if (building == null || !building.DisplayName.Contains(HallKeyword)) return;
            _panel = panel;
            Draw();
        }

        private bool IsShowing =>
            _panel != null && _panel.gameObject.activeInHierarchy && _panel.Current != null && _panel.Current.DisplayName.Contains(HallKeyword);

        private void RedrawIfOpen()
        {
            if (IsShowing) Draw();
        }

        private void Update()
        {
            if (!IsShowing || Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + RefreshSeconds;
            UpdateLabels();
        }

        /// <summary>이미 만든 줄의 글자만 바꾼다(경험치·남은 시간·레벨). 오브젝트를 새로 만들지 않아 테마가 그대로 유지된다.</summary>
        private void UpdateLabels()
        {
            foreach (var label in _labels.Values)
            {
                if (label.title != null) label.title.text = Title(label.merc);
                if (label.progress != null) label.progress.text = Progress(label.merc, label.training);
            }
        }

        private void Draw()
        {
            _nextRefresh = Time.unscaledTime + RefreshSeconds;
            _labels.Clear();
            ShopListUI.BeginList(_panel);

            var trainees = TrainingHall.Trainees;
            CreateHeaderRow($"훈련 중 ({trainees.Count}/{TrainingHall.Capacity}) · 1시간마다 경험치 +{TrainingHall.XpPerHour:0}");
            if (trainees.Count == 0) ShopListUI.CreateEmptyRow(_panel, "지금 훈련 중인 용병이 없습니다. 용병들이 가끔 스스로 훈련하러 옵니다.");
            foreach (var merc in trainees)
            {
                var row = ShopListUI.CreateRow(_panel, "Trainee_" + merc.Name);
                CharacterPortraitUI.Create(row.transform, merc.Appearance, 40);
                ShopListUI.CreateTwoLineLabel(row.transform, Title(merc), Progress(merc, true));
                Remember(row, merc, true);
            }

            ShopListUI.EndList(_panel);
        }

        /// <summary>CreateTwoLineLabel이 만든 "Label" 칸의 제목·부제 글자를 기억해 둔다.</summary>
        private void Remember(GameObject row, Mercenary merc, bool training)
        {
            var label = row.transform.Find("Label");
            if (label == null) return;
            var texts = label.GetComponentsInChildren<Text>();
            if (texts.Length < 2) return;
            _labels[merc.Id] = (merc, texts[0], texts[1], training);
        }

        private static string Title(Mercenary merc) => $"[{merc.Grade}] {merc.Name} · {merc.Class.ClassName} Lv {merc.Level}";

        private static string Progress(Mercenary merc, bool training)
        {
            if (merc.Level >= Mercenary.MaxLevel) return "최대 레벨";
            string xp = $"경험치 {merc.Experience} / {merc.XpToNext}";
            return training ? $"{xp} · 남은 훈련 약 {TrainingHall.RemainingHours(merc):0.#}시간" : $"{xp} · 전투력 {merc.CombatPower}";
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
