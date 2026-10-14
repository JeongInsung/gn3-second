using System.Collections.Generic;
using System.Linq;
using GN3.Battle;
using GN3.Mercenaries;
using GN3.Quests;
using GN3.World;
using UnityEngine;
using UnityEngine.UI;

namespace GN3.UI
{
    /// <summary>
    /// 훈련소 건물 패널의 목록 칸을 채운다: 지금 훈련 중인 용병과 경험치·남은 훈련 시간("빼기"), 그 아래 직접 훈련 보낼 수 있는 파티원("넣기").
    /// 용병들은 스스로도 무작위로 훈련하러 오고(VillagePartyPresenter) 시간이 다 되면 나간다. 훈련 규칙은 TrainingHall.
    /// 목록은 창이 열릴 때와 훈련 구성이 바뀔 때만 새로 만들고, 열려 있는 동안 1초(실제 시간)마다는 글자만 바꾼다
    /// (매번 새로 만들면 UIThemeApplier가 테마를 입히기 전 기본색이 잠깐 보여 깜빡였다). MainMenuBootstrapper가 만든다.
    /// </summary>
    public class TrainingHallUI : MonoBehaviour
    {
        public const string HallKeyword = "훈련소";
        private const float RefreshSeconds = 1f;

        // 훈련장 줄(허수아비 + 용병 한 쌍씩): 3쌍 × 140 + 간격 2 × 10 = 440이 목록 폭(592 - 여백) 안에 가운데로 들어간다.
        private const float StageHeight = 170f;
        private const float StageCellWidth = 140f;
        private const float StageSpacing = 10f;
        private const int StageFigureSize = 130;   // 용병(전신 크롭 한 변)
        private const float FigureCenterX = 52f;   // 칸 왼쪽에서 용병 가운데까지
        private const float DummyHeight = 110f;
        private const float DummyCenterX = 108f;   // 칸 왼쪽에서 허수아비 가운데까지(검 끝이 닿는 자리)
        private const float DummyBottom = 4f;

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
            CreateHeaderRow($"훈련 중 {trainees.Count}/{TrainingHall.Capacity} · {1f / GrowthTuning.TrainingGrowthPerHour:0}시간마다 성장(어릴수록 빠름)");
            if (trainees.Count == 0) ShopListUI.CreateEmptyRow(_panel, "훈련 중인 용병이 없습니다.");
            foreach (var merc in trainees)
            {
                var row = ShopListUI.CreateRow(_panel, "Trainee_" + merc.Name);
                CharacterPortraitUI.Create(row.transform, merc.Appearance, 40); // 검을 휘두르는 큰 모습은 아래 훈련장 줄(CreateStageRow)
                ShopListUI.CreateTwoLineLabel(row.transform, Title(merc), Progress(merc, true));
                Remember(row, merc, true);
                var target = merc;
                ShopListUI.CreateButton(row.transform, "빼기", () => TrainingHall.Remove(target));
            }
            if (trainees.Count > 0) CreateStageRow(trainees);

            // 직접 넣기: 마을에 있는 파티원(파견 중·이미 훈련 중 제외)
            var candidates = PlayerParty.Instance.Members
                .Where(m => m.IsAlive && !ExpeditionLog.Instance.IsOnExpedition(m) && !TrainingHall.IsTraining(m)).ToList();
            if (candidates.Count > 0) CreateHeaderRow($"훈련 보내기 · {TrainingHall.MaxSessionHours:0}시간");
            foreach (var merc in candidates)
            {
                var row = ShopListUI.CreateRow(_panel, "Candidate_" + merc.Name);
                CharacterPortraitUI.Create(row.transform, merc.Appearance, 40);
                string reason = BlockReason(merc);
                ShopListUI.CreateTwoLineLabel(row.transform, Title(merc), reason ?? Progress(merc, false));
                var target = merc;
                ShopListUI.CreateButton(row.transform, "넣기", () => Send(target)).interactable = reason == null;
            }

            ShopListUI.EndList(_panel);
        }

        /// <summary>
        /// 훈련 중 용병 줄들 아래의 넓은 "훈련장" 줄: 사람마다 칸 하나에 왼쪽 용병(검을 휘두름) + 오른쪽 허수아비.
        /// 검광이 나오는 순간(BattlePortraitAnimator.Hit) 허수아비가 흔들린다. 용병의 크롭 마스크는 꺼서 검·검광이 잘리지 않는다
        /// (목록 칸 바깥으로는 목록 마스크가 막는다). 목록을 다시 그리면 같이 새로 만들어진다.
        /// </summary>
        private void CreateStageRow(IReadOnlyList<Mercenary> trainees)
        {
            var row = ShopListUI.CreateRow(_panel, "TrainingStage");
            var layout = row.GetComponent<LayoutElement>();
            layout.minHeight = layout.preferredHeight = StageHeight;
            var h = row.GetComponent<HorizontalLayoutGroup>();
            h.childAlignment = TextAnchor.LowerCenter;
            h.spacing = StageSpacing;

            foreach (var merc in trainees)
            {
                var cell = new GameObject("Pair_" + merc.Name, typeof(RectTransform), typeof(LayoutElement));
                cell.transform.SetParent(row.transform, false);
                var cellLayout = cell.GetComponent<LayoutElement>();
                cellLayout.minWidth = cellLayout.preferredWidth = StageCellWidth;
                cellLayout.minHeight = cellLayout.preferredHeight = StageHeight - 16f; // 줄 위아래 여백 8씩

                // 허수아비: 칸 오른쪽 아래(발이 바닥에).
                var dummy = TrainingDummyUI.Create(cell.transform, DummyHeight);
                var dummyRect = (RectTransform)dummy.transform;
                dummyRect.anchorMin = dummyRect.anchorMax = new Vector2(0f, 0f);
                dummyRect.pivot = new Vector2(0.5f, 0f);
                dummyRect.anchoredPosition = new Vector2(DummyCenterX, DummyBottom);

                // 용병: 칸 왼쪽에 서서 검을 휘두른다.
                var figure = BattlePortraitAnimator.Create(cell.transform, merc.Appearance, StageFigureSize);
                figure.GetComponent<LayoutElement>().ignoreLayout = true;
                figure.GetComponent<RectMask2D>().enabled = false;
                var figureRect = (RectTransform)figure.transform;
                figureRect.anchorMin = figureRect.anchorMax = new Vector2(0f, 0f);
                figureRect.pivot = new Vector2(0.5f, 0f);
                figureRect.sizeDelta = new Vector2(StageFigureSize, StageFigureSize);
                figureRect.anchoredPosition = new Vector2(FigureCenterX, 0f);
                var animator = figure.GetComponent<BattlePortraitAnimator>();
                animator.UseUnscaledTime = true;
                animator.Hit += dummy.Hit;
                animator.PlaySlashLoop();
            }
        }

        private static void Send(Mercenary merc)
        {
            if (TrainingHall.TryAdd(merc, TrainingHall.MaxSessionHours))
                ToastLog.Show($"{merc.Name} 훈련 시작 ({TrainingHall.MaxSessionHours:0}시간)");
            else
                ToastLog.Show($"{Josa.EunNeun(merc.Name)} 지금 훈련할 수 없습니다.");
        }

        /// <summary>지금 훈련소에 넣을 수 없는 이유(넣을 수 있으면 null). TrainingHall.CanTrain·TryAdd와 같은 조건.</summary>
        private static string BlockReason(Mercenary merc)
        {
            if (Hospital.IsAdmitted(merc)) return "입원 중";
            var venue = RestVenues.Find(merc);
            if (venue != null) return $"{venue.Name}에 있음";
            if (merc.HasAilment) return "부상·질병 — 병원에 먼저";
            if (merc.IsExhausted) return $"너무 지침 (피로 {merc.Fatigue})";
            if (!merc.CanGrow) return "나이 때문에 더 자라지 않음";
            if (GameClock.IsNight) return "밤에는 닫혀 있음";
            if (TrainingHall.IsFull) return "정원이 찼음";
            return null;
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

        private static string Title(Mercenary merc) => $"[{merc.Grade}] {merc.Name} · {merc.Class.ClassName} {merc.Age}세";

        private static string Progress(Mercenary merc, bool training)
        {
            if (!merc.CanGrow) return $"{GrowthTuning.RateLabel(merc.Age)} · 전투력 {merc.CombatPower}";
            string next = $"{GrowthTuning.RateLabel(merc.Age)} · 다음 성장 {TrainingHall.HoursToNextGrowth(merc):0.#}시간";
            return training ? $"{next} · 남은 {TrainingHall.RemainingHours(merc):0.#}시간" : $"{next} · 전투력 {merc.CombatPower}";
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
