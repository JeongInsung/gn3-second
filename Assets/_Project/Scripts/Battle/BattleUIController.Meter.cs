using System.Collections.Generic;
using System.Linq;
using GN3.Combat;
using GN3.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace GN3.Battle
{
    /// <summary>
    /// 전투 통계 그래프: 아군 대원별 딜량 · 힐량 · 받은 피해(Combatant가 이번 전투 동안 쌓는 값) + 적이 우리에게 준 피해.
    ///  - 전투 중: 왼쪽 위 미터 창. [딜량][힐량][받은 피해][적] 탭, 큰 순서대로 막대(1등 기준 길이). 딜량은 초당 피해도.
    ///    아군 줄에 마우스를 올리면 필드의 그 대원 발밑에 금빛 고리. D 키·[통계 (D)] 버튼으로 켜고 끈다. 끌어 옮긴 자리는 저장된다.
    ///  - 결과 화면: 아군 세 칸 나란히(BuildResultMeters). 결과가 뜨면 미터 창은 숨긴다.
    /// </summary>
    public partial class BattleUIController
    {
        private enum MeterKind { Dealt, Healed, Taken, Enemy }

        private const float MeterWidth = 300f, MeterRowHeight = 22f, MeterTabHeight = 26f, MeterPad = 10f;
        private const float MeterNameWidth = 70f, MeterValueWidth = 90f;
        private const float MeterRefreshInterval = 0.25f;
        private const int MeterEnemyRows = 6; // [적] 탭: 이름별 합계 상위 몇 개
        private const float ResultMeterRowHeight = 18f, ResultMeterTitleHeight = 26f;
        private const int ResultMeterKinds = 3;  // 결과 화면은 아군 세 칸(딜량·힐량·받은 피해)

        private static readonly string[] MeterLabels = { "딜량", "힐량", "받은 피해", "적" };
        private static readonly Color[] MeterColors =
        {
            new Color(0.92f, 0.45f, 0.3f),  // 딜량
            new Color(0.45f, 0.85f, 0.45f), // 힐량
            new Color(0.5f, 0.65f, 0.95f),  // 받은 피해
            new Color(0.75f, 0.5f, 0.9f),   // 적이 준 피해
        };
        private static readonly Color MeterTabIdle = new Color(1f, 1f, 1f, 0.08f);
        private static readonly Color MeterMarkerColor = new Color(1f, 0.82f, 0.35f);

        private static bool _meterOpen = true;          // 다음 전투도 같은 상태로
        private static MeterKind _meterKind = MeterKind.Dealt;

        private class MeterRow
        {
            public Combatant Combatant; // 아군 줄이면 그 대원(마우스 올림 표시), 적 합계 줄이면 null
            public RectTransform Root, Fill;
            public Image FillImage;
            public Text Name, Value;
        }

        private struct MeterEntry
        {
            public string Name;
            public int Value;
            public bool Dim;
            public Combatant Combatant;
        }

        private GameObject _meterWindow;
        private readonly List<MeterRow> _meterRows = new List<MeterRow>();
        private readonly List<(Image bg, Text label)> _meterTabs = new List<(Image, Text)>();
        private float _meterTimer;
        private Button _meterButton;
        private Image _meterMarker;         // 마우스를 올린 아군 발밑 고리
        private Combatant _meterHovered;

        private static int MeterValue(Combatant c, MeterKind kind) =>
            kind == MeterKind.Dealt || kind == MeterKind.Enemy ? c.DamageDealt : kind == MeterKind.Healed ? c.HealingDone : c.DamageTaken;

        /// <summary>오른쪽 위 가방 버튼 아래 [통계 (D)] 버튼.</summary>
        private void CreateMeterButton(Transform root)
        {
            _meterButton = CreateActionButton(root, Vector2.zero, out var label);
            var rect = (RectTransform)_meterButton.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-40f, -160f);
            rect.sizeDelta = new Vector2(120f, 40f);
            label.fontSize = 16;
            label.text = "통계 (D)";
            _meterButton.onClick.AddListener(ToggleMeter);
        }

        private void ToggleMeter()
        {
            if (_resultShown) return;
            _meterOpen = !_meterOpen;
            _meterTimer = 0f;
            if (_meterWindow != null) _meterWindow.SetActive(_meterOpen);
            if (!_meterOpen) _meterHovered = null;
        }

        /// <summary>매 프레임: D 키, 창 만들기(처음 한 번), 0.25초마다 값·순서 갱신, 발밑 고리. 결과 화면에선 숨긴다.</summary>
        private void UpdateMeter()
        {
            if (_session == null) return;
            if (_resultShown)
            {
                if (_meterWindow != null && _meterWindow.activeSelf) _meterWindow.SetActive(false);
                if (_meterButton != null && _meterButton.gameObject.activeSelf) _meterButton.gameObject.SetActive(false);
                _meterHovered = null;
                UpdateMeterMarker();
                return;
            }
            var kb = Keyboard.current;
            if (kb != null && kb.dKey.wasPressedThisFrame) ToggleMeter();
            UpdateMeterMarker();
            if (!_meterOpen) return;
            if (_meterWindow == null) BuildMeterWindow();
            _meterTimer -= Time.unscaledDeltaTime;
            if (_meterTimer > 0f) return;
            _meterTimer = MeterRefreshInterval;
            RefreshMeter();
        }

        private void BuildMeterWindow()
        {
            var team = _session.PlayerTeam;
            _meterWindow = CreateImage(_canvas.transform, "DamageMeter", Color.white);
            StyleGamePanel(_meterWindow);
            _meterWindow.GetComponent<Image>().raycastTarget = true; // 창 위 클릭이 땅 명령으로 새지 않게
            var rect = (RectTransform)_meterWindow.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(24f, -100f);
            rect.sizeDelta = new Vector2(MeterWidth, MeterHeight(team.Count));
            DraggablePanel.Attach(rect); // 잡고 끌어 옮길 수 있다 — 자리는 저장된다(D로 껐다 켜도, 다음 전투도)

            // 탭: [딜량][힐량][받은 피해][적]
            float tabWidth = (MeterWidth - MeterPad * 2f - 4f * (MeterLabels.Length - 1)) / MeterLabels.Length;
            _meterTabs.Clear();
            for (int i = 0; i < MeterLabels.Length; i++)
            {
                var kind = (MeterKind)i;
                var tab = CreateImage(_meterWindow.transform, "Tab", MeterTabIdle);
                var tabRect = (RectTransform)tab.transform;
                tabRect.anchorMin = tabRect.anchorMax = tabRect.pivot = new Vector2(0f, 1f);
                tabRect.anchoredPosition = new Vector2(MeterPad + i * (tabWidth + 4f), -MeterPad);
                tabRect.sizeDelta = new Vector2(tabWidth, MeterTabHeight);
                var label = CreateText(tab.transform, MeterLabels[i], 13, TextAnchor.MiddleCenter, Color.white);
                label.horizontalOverflow = HorizontalWrapMode.Overflow;
                label.raycastTarget = false;
                Stretch(label.rectTransform);
                RowClickHandler.Attach(tab, () => { _meterKind = kind; _meterTimer = 0f; _meterHovered = null; });
                _meterTabs.Add((tab.GetComponent<Image>(), label));
            }

            // 줄: 대원 수와 [적] 탭 상위 수 중 큰 만큼 미리 만들고, 탭마다 쓰는 만큼만 켠다(정렬은 순서만 바꾼다)
            var list = new GameObject("Rows", typeof(RectTransform), typeof(VerticalLayoutGroup));
            list.transform.SetParent(_meterWindow.transform, false);
            var listRect = (RectTransform)list.transform;
            listRect.anchorMin = new Vector2(0f, 0f);
            listRect.anchorMax = new Vector2(1f, 1f);
            listRect.offsetMin = new Vector2(MeterPad, MeterPad);
            listRect.offsetMax = new Vector2(-MeterPad, -(MeterPad + MeterTabHeight + 6f));
            var v = list.GetComponent<VerticalLayoutGroup>();
            v.childControlWidth = v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            v.childAlignment = TextAnchor.UpperLeft;
            _meterRows.Clear();
            int rows = Mathf.Max(team.Count, MeterEnemyRows);
            for (int i = 0; i < rows; i++)
            {
                var row = CreateMeterRow(list.transform, "", MeterRowHeight, 13);
                AddMeterHover(row);
                _meterRows.Add(row);
            }
            _meterWindow.SetActive(_meterOpen);
        }

        private static float MeterHeight(int rows) => MeterPad * 2f + MeterTabHeight + 6f + MeterRowHeight * Mathf.Max(1, rows);

        /// <summary>한 줄: 이름(왼쪽) · 막대(가운데) · 값(오른쪽).</summary>
        private MeterRow CreateMeterRow(Transform parent, string name, float height, int fontSize, float valueWidth = MeterValueWidth)
        {
            var go = new GameObject("MeterRow", typeof(RectTransform), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var layout = go.GetComponent<LayoutElement>();
            layout.minHeight = layout.preferredHeight = height;
            var row = new MeterRow { Root = (RectTransform)go.transform };

            row.Name = CreateText(go.transform, name, fontSize, TextAnchor.MiddleLeft, UITheme.BodyText);
            row.Name.horizontalOverflow = HorizontalWrapMode.Overflow;
            row.Name.raycastTarget = false;
            var nameRect = row.Name.rectTransform;
            nameRect.anchorMin = new Vector2(0f, 0f);
            nameRect.anchorMax = new Vector2(0f, 1f);
            nameRect.pivot = new Vector2(0f, 0.5f);
            nameRect.offsetMin = Vector2.zero;
            nameRect.offsetMax = new Vector2(MeterNameWidth, 0f);

            var track = CreateImage(go.transform, "Track", new Color(0f, 0f, 0f, 0.45f));
            track.GetComponent<Image>().raycastTarget = false;
            var trackRect = (RectTransform)track.transform;
            trackRect.anchorMin = new Vector2(0f, 0.2f);
            trackRect.anchorMax = new Vector2(1f, 0.8f);
            trackRect.offsetMin = new Vector2(MeterNameWidth + 4f, 0f);
            trackRect.offsetMax = new Vector2(-(valueWidth + 4f), 0f);
            row.FillImage = CreateBarFill(track.transform, MeterColors[0]);
            row.Fill = (RectTransform)row.FillImage.transform;
            SetLength(row.Fill, 0f);

            row.Value = CreateText(go.transform, "0", fontSize, TextAnchor.MiddleRight, UITheme.BodyText);
            row.Value.horizontalOverflow = HorizontalWrapMode.Overflow;
            row.Value.supportRichText = true;
            row.Value.raycastTarget = false;
            var valueRect = row.Value.rectTransform;
            valueRect.anchorMin = new Vector2(1f, 0f);
            valueRect.anchorMax = new Vector2(1f, 1f);
            valueRect.pivot = new Vector2(1f, 0.5f);
            valueRect.offsetMin = new Vector2(-valueWidth, 0f);
            valueRect.offsetMax = Vector2.zero;
            return row;
        }

        /// <summary>줄에 마우스를 올리면 그 아군 발밑 고리(줄 전체가 받도록 투명 칸). 끌기는 창으로 올라간다.</summary>
        private void AddMeterHover(MeterRow row)
        {
            var hit = row.Root.gameObject.AddComponent<Image>();
            hit.color = Color.clear;
            var hover = row.Root.gameObject.AddComponent<MeterRowHover>(); // 올림·내림만 받는다(EventTrigger는 끌기까지 먹어 창을 못 옮긴다)
            hover.Enter = () => _meterHovered = row.Combatant;
            hover.Exit = () => { if (_meterHovered == row.Combatant) _meterHovered = null; };
        }

        /// <summary>마우스를 올린 아군 발밑 금빛 고리: 위치를 따라가며 은은하게 깜빡인다. 없으면 숨긴다.</summary>
        private void UpdateMeterMarker()
        {
            UnitView view = null;
            if (_meterHovered != null)
            {
                var unit = _session.UnitOf(_meterHovered);
                if (unit != null && unit.IsAlive) _views.TryGetValue(unit, out view);
            }
            if (view == null)
            {
                if (_meterMarker != null && _meterMarker.gameObject.activeSelf) _meterMarker.gameObject.SetActive(false);
                return;
            }
            if (_meterMarker == null)
            {
                var go = CreateImage(_groundLayer, "MeterMarker", MeterMarkerColor);
                _meterMarker = go.GetComponent<Image>();
                _meterMarker.raycastTarget = false;
                _meterMarker.sprite = _views.Values.FirstOrDefault(v => v.TargetRing != null)?.TargetRing.sprite; // 적 표시 고리와 같은 그림
                var rect = (RectTransform)go.transform;
                rect.anchorMin = rect.anchorMax = Vector2.zero;
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(90f, 36f);
            }
            _meterMarker.gameObject.SetActive(true);
            ((RectTransform)_meterMarker.transform).anchoredPosition = view.Root.anchoredPosition;
            var color = MeterMarkerColor;
            color.a = 0.65f + 0.35f * Mathf.Sin(Time.unscaledTime * 5f);
            _meterMarker.color = color;
        }

        /// <summary>고른 탭의 줄 목록: 아군(대원별) 또는 적(이름별 합계 상위 MeterEnemyRows개). 큰 순서.</summary>
        private List<MeterEntry> MeterEntries(MeterKind kind)
        {
            if (kind == MeterKind.Enemy)
                return _session.AllEnemies
                    .GroupBy(c => c.Name)
                    .Select(g => new MeterEntry
                    {
                        Name = g.Count() > 1 ? $"{g.Key} ×{g.Count()}" : g.Key,
                        Value = g.Sum(c => c.DamageDealt),
                        Dim = g.All(c => c.CurrentHealth <= 0),
                    })
                    .OrderByDescending(e => e.Value).Take(MeterEnemyRows).ToList();
            return _session.PlayerTeam
                .Select(c => new MeterEntry
                {
                    Name = c.Name, Value = MeterValue(c, kind), Combatant = c,
                    Dim = !(_session.UnitOf(c)?.IsAlive ?? c.CurrentHealth > 0),
                })
                .OrderByDescending(e => e.Value).ToList();
        }

        /// <summary>고른 탭 값으로 줄 채우기 · 막대 길이(1등 기준) · 탭 강조 · 창 높이. 딜량은 초당 피해도, 쓰러진 쪽은 흐리게.</summary>
        private void RefreshMeter()
        {
            if (_meterWindow == null || !_meterWindow.activeSelf) return;
            var kind = _meterKind;
            Color color = MeterColors[(int)kind];
            for (int i = 0; i < _meterTabs.Count; i++)
            {
                bool on = i == (int)kind;
                var c = MeterColors[i];
                _meterTabs[i].bg.color = on ? new Color(c.r, c.g, c.b, 0.55f) : MeterTabIdle;
                _meterTabs[i].label.color = on ? Color.white : UITheme.MutedText;
            }
            var entries = MeterEntries(kind);
            int top = Mathf.Max(1, entries.Count > 0 ? entries[0].Value : 1);
            float seconds = Mathf.Max(1f, _session.Elapsed);
            for (int i = 0; i < _meterRows.Count; i++)
            {
                var row = _meterRows[i];
                bool used = i < entries.Count;
                row.Root.gameObject.SetActive(used);
                if (!used) continue;
                var e = entries[i];
                row.Combatant = e.Combatant;
                row.Name.text = e.Name;
                row.Name.color = e.Dim ? UITheme.MutedText : UITheme.BodyText;
                SetLength(row.Fill, (float)e.Value / top);
                row.FillImage.color = color;
                row.Value.text = kind == MeterKind.Dealt
                    ? $"{e.Value} <color=#a3937c>· {e.Value / seconds:0}/s</color>"
                    : e.Value.ToString();
            }
            ((RectTransform)_meterWindow.transform).sizeDelta = new Vector2(MeterWidth, MeterHeight(entries.Count));
        }

        /// <summary>결과 화면 세 칸 그래프(딜량 · 힐량 · 받은 피해)의 높이.</summary>
        private float ResultMeterHeight(int members) => ResultMeterTitleHeight + ResultMeterRowHeight * members + 10f;

        /// <summary>결과 화면: 위에서 y부터 세 칸 나란히. 칸마다 제목 + 큰 순서 막대 줄.</summary>
        private void BuildResultMeters(Transform panel, float y, float panelWidth)
        {
            var team = _session.PlayerTeam;
            float gap = 16f;
            float columnWidth = (panelWidth - ResultPad * 2f - gap * 2f) / ResultMeterKinds;
            for (int k = 0; k < ResultMeterKinds; k++)
            {
                var kind = (MeterKind)k;
                var column = new GameObject("ResultMeter_" + MeterLabels[k], typeof(RectTransform), typeof(VerticalLayoutGroup));
                column.transform.SetParent(panel, false);
                var rect = (RectTransform)column.transform;
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
                rect.anchoredPosition = new Vector2(ResultPad + k * (columnWidth + gap), y);
                rect.sizeDelta = new Vector2(columnWidth, ResultMeterHeight(team.Count));
                var v = column.GetComponent<VerticalLayoutGroup>();
                v.childControlWidth = v.childControlHeight = true;
                v.childForceExpandWidth = true;
                v.childForceExpandHeight = false;

                var title = CreateText(column.transform, MeterLabels[k], 15, TextAnchor.MiddleLeft, MeterColors[k]);
                title.fontStyle = FontStyle.Bold;
                title.raycastTarget = false;
                title.gameObject.AddComponent<LayoutElement>().minHeight = ResultMeterTitleHeight;

                var entries = MeterEntries(kind);
                int top = Mathf.Max(1, entries.Count > 0 ? entries[0].Value : 1);
                foreach (var e in entries)
                {
                    var row = CreateMeterRow(column.transform, e.Name, ResultMeterRowHeight, 12, 48f);
                    SetLength(row.Fill, (float)e.Value / top);
                    row.FillImage.color = MeterColors[k];
                    row.Value.text = e.Value.ToString();
                    row.Name.color = e.Dim ? UITheme.MutedText : UITheme.BodyText;
                }
            }
        }
    }

    /// <summary>통계 창 줄: 마우스 올림·내림만 알린다(끌기·클릭은 창으로 올라간다).</summary>
    public class MeterRowHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public System.Action Enter, Exit;
        public void OnPointerEnter(PointerEventData eventData) => Enter?.Invoke();
        public void OnPointerExit(PointerEventData eventData) => Exit?.Invoke();
    }
}
