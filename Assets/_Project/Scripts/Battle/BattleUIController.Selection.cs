using System.Collections.Generic;
using System.Linq;
using GN3.Combat;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GN3.Battle
{
    /// <summary>
    /// 여러 대원 한꺼번에 조종(바탕화면 드래그 선택처럼):
    ///  - 전투 중 빈 땅을 왼쪽 버튼으로 끌면 점선 네모(BattleSelectionBox)가 생기고, 놓으면 그 안에 걸친 아군이 모두 선택된다.
    ///    Shift를 누른 채 끌면 지금 선택에 더한다. Shift+아군 클릭은 그 대원을 넣거나 뺀다. 아무도 안 걸친 네모·Esc는 선택 해제.
    ///  - 선택이 있으면 땅 클릭 = 선택한 대원 모두 그 자리로(지금 대형을 유지, 너무 퍼졌으면 좁힌다), 적 클릭 = 모두 그 적 공격.
    ///    선택한 대원 하나를 끌어 놓아도 선택 전체가 따른다. 선택에 분대장이 없으면 분대장은 그대로.
    ///  - 선택한 대원 발밑에 고리 + 흰 테두리. 쓰러지면 선택에서 빠진다. 일시정지 중에도 된다.
    /// </summary>
    public partial class BattleUIController
    {
        private const float SelectHalfWidth = 34f;   // 대원이 네모에 걸쳤는가: 발 위치 기준 좌우 반폭
        private const float SelectHeight = 110f;     // 발에서 머리까지
        private const float GroupMaxSpread = 160f;   // 함께 이동: 대형이 이보다 넓게 퍼졌으면 이 반경 안으로 좁힌다
        private static readonly Color SelectRingColor = new Color(0.55f, 1f, 0.65f, 0.9f);

        private readonly List<FieldUnit> _selected = new List<FieldUnit>();
        private readonly Dictionary<FieldUnit, Image> _selectRings = new Dictionary<FieldUnit, Image>();
        private readonly HashSet<FieldUnit> _boxPreview = new HashSet<FieldUnit>(); // 끄는 중 네모에 걸친 아군(테두리 미리보기)
        private BattleSelectionBox _selectionBox;
        private bool _boxDragging;
        private Vector2 _boxStart;

        private bool HasSelection => _selected.Count > 0;
        private bool IsSelected(FieldUnit unit) => _selected.Contains(unit);

        private bool CanBoxSelect => _session.HasStarted && !_session.IsOver && !_resultShown && !_salvaging && !_cutIn && !_deploying;

        /// <summary>필드(땅)에 끌기 입력을 붙인다. 유닛 위에서 시작한 끌기는 유닛의 BattleDragTarget이 먼저 받는다.</summary>
        private void AttachBoxSelect(GameObject fieldGO)
        {
            var drag = fieldGO.AddComponent<BattleDragTarget>();
            drag.OnBegin = OnBoxBegin;
            drag.OnMove = OnBoxMove;
            drag.OnEnd = OnBoxEnd;
        }

        private void OnBoxBegin(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left || !CanBoxSelect || !TryFieldPoint(e, out var local)) return;
            _picking = false;
            _focusPicking = false;
            _boxDragging = true;
            _boxStart = local;
            if (_selectionBox == null) _selectionBox = BattleSelectionBox.Create(_fxLayer);
            _selectionBox.gameObject.SetActive(true);
            _selectionBox.SetCorners(local, local);
            _boxPreview.Clear();
        }

        private void OnBoxMove(PointerEventData e)
        {
            if (!_boxDragging || !TryFieldPoint(e, out var local)) return;
            if (!CanBoxSelect) { CancelBox(); return; }
            _selectionBox.SetCorners(_boxStart, local);
            _boxPreview.Clear();
            foreach (var u in UnitsInBox(_boxStart, local)) _boxPreview.Add(u);
        }

        private void OnBoxEnd(PointerEventData e)
        {
            if (!_boxDragging) return;
            var end = TryFieldPoint(e, out var local) ? local : _boxStart;
            bool ok = CanBoxSelect;
            CancelBox();
            if (!ok) return;
            var picked = UnitsInBox(_boxStart, end).ToList();
            if (!ShiftHeld) _selected.Clear();
            foreach (var u in picked)
                if (!_selected.Contains(u)) _selected.Add(u);
            if (picked.Count > 0)
                AppendLog($"<color=#8affa0>대원 {_selected.Count}명 선택 — 땅 클릭: 함께 이동 · 적 클릭: 함께 공격 · Esc: 해제</color>");
            SyncSelectRings();
        }

        /// <summary>Shift+클릭: 그 대원을 선택에 넣거나(없으면) 뺀다(있으면) — 바탕화면 Ctrl+클릭처럼.</summary>
        private void ToggleSelect(FieldUnit unit)
        {
            if (!CanBoxSelect || !unit.IsPlayerSide || !unit.IsAlive) return;
            _picking = false;
            _focusPicking = false;
            if (!_selected.Remove(unit)) _selected.Add(unit);
            SyncSelectRings();
        }

        private void CancelBox()
        {
            _boxDragging = false;
            _boxPreview.Clear();
            if (_selectionBox != null) _selectionBox.gameObject.SetActive(false);
        }

        /// <summary>네모에 몸(발~머리)이 걸친 살아 있는 아군.</summary>
        private IEnumerable<FieldUnit> UnitsInBox(Vector2 a, Vector2 b)
        {
            var min = Vector2.Min(a, b);
            var max = Vector2.Max(a, b);
            return _session.Units.Where(u => u.IsPlayerSide && u.IsAlive
                && u.Position.X + SelectHalfWidth >= min.x && u.Position.X - SelectHalfWidth <= max.x
                && u.Position.Y + SelectHeight >= min.y && u.Position.Y <= max.y);
        }

        private void ClearSelection()
        {
            _selected.Clear();
            CancelBox();
            SyncSelectRings();
        }

        /// <summary>선택한 대원 모두 그 지점으로. 지금 서 있는 모양을 유지하되 GroupMaxSpread보다 퍼졌으면 좁힌다.</summary>
        private void CommandSelectionMove(Vector2 point)
        {
            var units = _selected.Where(u => u.IsAlive).ToList();
            if (units.Count == 0) return;
            var center = units.Aggregate(System.Numerics.Vector2.Zero, (s, u) => s + u.Position) / units.Count;
            float spread = units.Max(u => System.Numerics.Vector2.Distance(u.Position, center));
            float scale = spread > GroupMaxSpread ? GroupMaxSpread / spread : 1f;
            var goal = new System.Numerics.Vector2(point.x, point.y);
            int moved = 0;
            foreach (var u in units)
            {
                var dest = FieldBattleSession.ClampToField(goal + (u.Position - center) * scale);
                if (!_session.OrderUnitMove(u, dest)) continue;
                moved++;
                StartCoroutine(MoveMarker(new Vector2(dest.X, dest.Y)));
            }
            if (moved > 0) AppendLog($"<color=#8affa0>대원 {moved}명 함께 이동 — 도착하면 그 자리를 지킵니다</color>");
        }

        /// <summary>선택한 대원 모두 이 적을 공격.</summary>
        private void CommandSelectionAttack(FieldUnit enemy)
        {
            int n = _selected.Count(u => _session.OrderUnitAttack(u, enemy));
            if (n > 0) AppendLog($"<color=#ff8a7a>대원 {n}명 {enemy.Combatant.Name} 공격 지시</color>");
        }

        /// <summary>매 프레임: 쓰러진 대원을 선택에서 빼고 발밑 고리를 따라가게 한다. 전투가 끝나면 선택을 비운다.</summary>
        private void UpdateSelection()
        {
            if (_session.IsOver || _resultShown) { if (HasSelection || _boxDragging) ClearSelection(); return; }
            if (_selected.RemoveAll(u => !u.IsAlive) > 0) SyncSelectRings();
            float pulse = 1f + 0.06f * Mathf.Sin(Time.unscaledTime * 6f);
            foreach (var kv in _selectRings)
            {
                var rect = (RectTransform)kv.Value.transform;
                rect.anchoredPosition = new Vector2(kv.Key.Position.X, kv.Key.Position.Y);
                rect.sizeDelta = new Vector2(70f, 30f) * pulse;
            }
        }

        /// <summary>발밑 고리를 선택과 맞춘다(새로 고른 대원엔 만들고, 빠진 대원 것은 지운다).</summary>
        private void SyncSelectRings()
        {
            foreach (var u in _selectRings.Keys.Where(u => !_selected.Contains(u)).ToList())
            {
                if (_selectRings[u] != null) Destroy(_selectRings[u].gameObject);
                _selectRings.Remove(u);
            }
            foreach (var u in _selected)
            {
                if (_selectRings.ContainsKey(u)) continue;
                var go = new GameObject("SelectRing", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(_groundLayer, false);
                var rect = (RectTransform)go.transform;
                rect.anchorMin = rect.anchorMax = Vector2.zero;
                rect.anchoredPosition = new Vector2(u.Position.X, u.Position.Y);
                rect.sizeDelta = new Vector2(70f, 30f);
                var img = go.GetComponent<Image>();
                img.sprite = BattlePlaceholderSprites.Ring;
                img.color = SelectRingColor;
                img.raycastTarget = false;
                _selectRings[u] = img;
            }
        }
    }
}
