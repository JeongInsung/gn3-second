using System.Collections.Generic;
using System.Linq;
using GN3.Quests;
using UnityEngine;
using UnityEngine.UI;

namespace GN3.UI
{
    /// <summary>
    /// 파견대가 목적지에 도착하면 퀘스트마다 "자동 진행 / 직접 진행"을 고르게 하는 알림창(반드시 고른다).
    /// - 자동 진행: 재생 없이 바로 결과(ExpeditionBattle) → 창이 결과 화면으로 바뀜
    /// - 직접 진행: 직접 전투는 아직 준비 중이라, 지금은 파티 패널을 열고 전투 기록을 재생(PartyUI.StartBattle)
    /// 여러 파견이 한꺼번에 도착하면 대기열로 하나씩 묻는다.
    /// </summary>
    public class ArrivalPrompt : MonoBehaviour
    {
        private const float Width = 540f;
        private const float Height = 330f;

        private readonly Queue<Expedition> _queue = new Queue<Expedition>();
        private Expedition _current;
        private GameObject _partyPanel;

        private GameObject _panel;
        private Text _title;
        private Text _body;
        private GameObject _choiceRow;
        private GameObject _confirmButton;
        private GameObject _manualNote;
        private Font _font;

        public static ArrivalPrompt Create(GameObject partyPanel)
        {
            var go = new GameObject("ArrivalPrompt", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(ArrivalPrompt));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 3; // 하루 보고서(2) 위
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            var prompt = go.GetComponent<ArrivalPrompt>();
            prompt._partyPanel = partyPanel;
            prompt.Build();
            ExpeditionLog.Instance.OnArrived += prompt.Enqueue;
            return prompt;
        }

        private void OnDestroy()
        {
            ExpeditionLog.Instance.OnArrived -= Enqueue;
        }

        // ---------- 흐름 ----------

        private void Enqueue(Expedition expedition)
        {
            if (expedition == _current || _queue.Contains(expedition)) return;
            _queue.Enqueue(expedition);
            if (_current == null) ShowNext();
        }

        private void ShowNext()
        {
            _current = null;
            while (_queue.Count > 0)
            {
                var next = _queue.Dequeue();
                if (next.IsReady && ExpeditionLog.Instance.Active.Contains(next)) { _current = next; break; }
            }
            if (_current == null)
            {
                _panel.SetActive(false);
                return;
            }

            var quest = _current.Quest;
            var members = _current.Members.Where(m => m.IsAlive).ToList();
            _title.text = "목적지 도착";
            _body.text = $"{QuestDifficulty.RichTitle(quest)}\n" +
                         $"파견대: {string.Join(", ", members.Select(m => m.Name))}\n" +
                         $"{QuestDifficulty.DescribeTeam(quest, QuestDifficulty.TeamPower(members))}\n\n" +
                         "전투를 진행하시겠습니까?";
            _choiceRow.SetActive(true);
            _manualNote.SetActive(true);
            _confirmButton.SetActive(false);
            _panel.SetActive(true);
            _panel.transform.SetAsLastSibling();
        }

        private void Fight()
        {
            var expedition = _current;
            if (_partyPanel != null) PanelActivator.Open(_partyPanel);
            var party = Object.FindFirstObjectByType<PartyUI>(FindObjectsInactive.Include);
            if (party != null) party.StartBattle(expedition);
            ShowNext();
        }

        private void AutoFight()
        {
            if (_current == null || !ExpeditionLog.Instance.Active.Contains(_current))
            {
                ShowNext();
                return;
            }
            string summary = ExpeditionBattle.Conclude(_current, ExpeditionBattle.Simulate(_current));
            _current = null;
            _title.text = "전투 결과";
            _body.text = summary.Replace(" · ", "\n");
            _choiceRow.SetActive(false);
            _manualNote.SetActive(false);
            _confirmButton.SetActive(true);
        }


        // ---------- 화면 ----------

        private void Build()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            _panel = new GameObject("ArrivalPanel", typeof(RectTransform), typeof(Image));
            _panel.transform.SetParent(transform, false);
            var rect = _panel.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(Width, Height);
            rect.anchoredPosition = new Vector2(0f, 40f);
            _panel.GetComponent<Image>().color = new Color(0.1f, 0.1f, 0.13f, 0.96f);
            DraggablePanel.Attach(rect);

            _title = CreateText(_panel.transform, "", 22, TextAnchor.MiddleLeft, UITheme.TitleText);
            _title.fontStyle = FontStyle.Bold;
            Place(_title.rectTransform, new Vector2(22f, -16f), new Vector2(Width - 44f, 34f));

            _body = CreateText(_panel.transform, "", 16, TextAnchor.UpperLeft, UITheme.BodyText);
            _body.supportRichText = true; // 판정 색
            Place(_body.rectTransform, new Vector2(22f, -58f), new Vector2(Width - 44f, 170f));

            // 선택 버튼 줄
            _choiceRow = new GameObject("Choices", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            _choiceRow.transform.SetParent(_panel.transform, false);
            var rowRect = _choiceRow.GetComponent<RectTransform>();
            rowRect.anchorMin = rowRect.anchorMax = rowRect.pivot = new Vector2(0.5f, 0f);
            rowRect.anchoredPosition = new Vector2(0f, 52f);
            rowRect.sizeDelta = new Vector2(Width - 44f, 44f);
            var h = _choiceRow.GetComponent<HorizontalLayoutGroup>();
            h.spacing = 12f;
            h.childAlignment = TextAnchor.MiddleCenter;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = true;
            CreateButton(_choiceRow.transform, "자동 진행", AutoFight);
            CreateButton(_choiceRow.transform, "직접 진행", Fight);

            // 결과 화면용 확인 버튼
            _confirmButton = CreateButton(_panel.transform, "확인", ShowNext).gameObject;
            var okRect = _confirmButton.GetComponent<RectTransform>();
            okRect.anchorMin = okRect.anchorMax = okRect.pivot = new Vector2(0.5f, 0f);
            okRect.anchoredPosition = new Vector2(0f, 52f);
            okRect.sizeDelta = new Vector2(160f, 44f);
            _confirmButton.SetActive(false);

            // 직접 진행 안내(직접 전투 화면이 생기면 지운다)
            var note = CreateText(_panel.transform, "직접 전투는 준비 중 — 지금은 파티 패널에서 전투 기록을 재생합니다", 13,
                TextAnchor.MiddleCenter, UITheme.MutedText);
            var noteRect = note.rectTransform;
            noteRect.anchorMin = noteRect.anchorMax = noteRect.pivot = new Vector2(0.5f, 0f);
            noteRect.anchoredPosition = new Vector2(0f, 18f);
            noteRect.sizeDelta = new Vector2(Width - 40f, 24f);
            _manualNote = note.gameObject;

            _panel.SetActive(false);
        }

        private Button CreateButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick, Color? color = null)
        {
            var go = new GameObject(label + "Button", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = color ?? new Color(0.6f, 0.2f, 0.2f, 1f); // UIThemeApplier가 진홍 버튼으로
            go.GetComponent<LayoutElement>().minHeight = 44f;
            var text = CreateText(go.transform, label, 17, TextAnchor.MiddleCenter, UITheme.BodyText);
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = text.rectTransform.offsetMax = Vector2.zero;
            var button = go.GetComponent<Button>();
            button.onClick.AddListener(onClick);
            return button;
        }

        private Text CreateText(Transform parent, string content, int size, TextAnchor alignment, Color color)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.text = content;
            text.font = _font;
            text.fontSize = size;
            text.alignment = alignment;
            text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.supportRichText = false;
            text.raycastTarget = false;
            return text;
        }

        private static void Place(RectTransform rect, Vector2 topLeft, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = topLeft;
            rect.sizeDelta = size;
        }
    }
}
