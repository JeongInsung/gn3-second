using System.Collections.Generic;
using GN3.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace GN3.World
{
    /// <summary>
    /// [건설] 창의 [옮기기] → 옮기기 모드. 지은 건물을 클릭하면 들리고(BuildPlacement.BeginMove) 놓을 곳을 클릭하면 옮겨진다(무료).
    /// 모드 동안은 시간이 멈추고 건물 창·용병 클릭이 막힌다. 화면 아래 막대의 [완료]는 그대로 확정, [옮기기 취소]는 이번 모드에서
    /// 옮긴 건물을 모두 원래 자리로 되돌린다. 대상은 VillageLayoutApplier.Slots의 지은 건물·놓은 장식품 전부다.
    /// 장식품을 들고 있으면 [치우기]가 나온다: 치운 것도 [옮기기 취소]면 되살아나고 [완료]면 아주 없어진다.
    /// MainMenuBootstrapper가 씬마다 하나 만든다.
    /// </summary>
    public class BuildingMover : MonoBehaviour
    {
        private const string PauseKey = "move-mode";
        private const float BarBottom = 70f;

        private static BuildingMover _instance;

        /// <summary>옮기기 모드인가(SelectableBuilding·VillagePartyPresenter가 클릭을 막는다).</summary>
        public static bool Active => _instance != null && _instance._active;

        private bool _active;
        private readonly Dictionary<string, Vector2> _before = new Dictionary<string, Vector2>(); // 모드를 켤 때의 자리
        private readonly List<KeyValuePair<VillageLayoutApplier.Slot, Vector2>> _removed =
            new List<KeyValuePair<VillageLayoutApplier.Slot, Vector2>>(); // 이번 모드에서 치운 장식품(치울 때 자리)
        private GameObject _bar;
        private GameObject _removeButton;

        public static void Create(Transform hud)
        {
            var go = new GameObject("BuildingMover", typeof(BuildingMover));
            _instance = go.GetComponent<BuildingMover>();
            _instance._bar = CreateBar(hud, out _instance._removeButton);
        }

        /// <summary>옮기기 모드를 켠다([건설] 창). pick이 있으면 그 건물을 바로 든다(한 채짜리 건물 줄의 [옮기기]).</summary>
        public static void Enter(VillageLayoutApplier.Slot pick = null)
        {
            if (_instance == null) return;
            if (!_instance._active) _instance.Begin();
            if (pick != null && pick.Built && pick.Renderer != null) BuildPlacement.BeginMove(pick, pick.Renderer.bounds.center);
        }

        /// <summary>모드를 끝낸다. commit false면 이번에 옮긴 건물을 모두 되돌린다. 모드가 아니면 아무것도 안 한다.</summary>
        public static void Finish(bool commit)
        {
            if (Active) _instance.Exit(commit);
        }

        private void Begin()
        {
            BuildPlacement.Cancel();
            _before.Clear();
            foreach (var kv in VillageLayout.Built) _before[kv.Key] = kv.Value;
            VillageLayoutApplier.BeginHiddenLog(); // 옮기다 치운 나무·풀꽃도 [옮기기 취소]면 되살린다
            _active = true;
            GamePause.Push(PauseKey);
            if (_bar != null) _bar.SetActive(true);
        }

        private void Exit(bool commit)
        {
            BuildPlacement.Cancel();
            if (!commit)
            {
                int restored = 0;
                foreach (var removed in _removed) // 치운 장식품 먼저 되살린다(옮긴 뒤 치웠으면 아래에서 처음 자리로)
                {
                    VillageLayoutApplier.RestoreDecor(removed.Key, removed.Value);
                    restored++;
                }
                foreach (var slot in VillageLayoutApplier.Slots)
                {
                    if (!_before.TryGetValue(slot.Key, out var original) || !VillageLayout.Built.TryGetValue(slot.Key, out var now) || now == original) continue;
                    VillageLayout.Add(slot.Key, original);
                    VillageLayoutApplier.MoveLive(slot, false);
                    if (!_removed.Exists(r => r.Key == slot)) restored++;
                }
                int regrown = VillageLayoutApplier.EndHiddenLog(true);
                if (restored > 0 || regrown > 0) VillageLayoutApplier.NotifyRebuilt();
                if (restored > 0) ToastLog.Show($"옮기거나 치운 것 {restored}개를 원래대로 되돌렸습니다.", false);
            }
            else
            {
                VillageLayoutApplier.EndHiddenLog(false);
                foreach (var removed in _removed) VillageLayoutApplier.DestroyDecor(removed.Key);
            }
            _removed.Clear();
            _before.Clear();
            _active = false;
            GamePause.Pop(PauseKey);
            if (_bar != null) _bar.SetActive(false);
        }

        /// <summary>[치우기]: 들고 있는 장식품을 확인받고 치운다. [옮기기 취소]면 되살아나고 [완료]면 아주 없어진다(비용은 돌려받지 않음).</summary>
        private void AskRemove()
        {
            var slot = BuildPlacement.HeldSlot;
            if (slot == null || !slot.Entry.Decor) return;
            BuildPlacement.CloseConfirmIfAny();
            var b = slot.Renderer != null ? slot.Renderer.bounds : new Bounds(slot.Transform.position, Vector3.zero);
            PlaceConfirmPanel.Show($"{Josa.EulReul(slot.Entry.Name)} 치우겠습니까?", "비용은 돌려받지 않습니다", "치우기",
                new Vector3(b.center.x, b.max.y, b.center.z), () =>
                {
                    PlaceConfirmPanel.Hide();
                    if (!VillageLayout.Built.TryGetValue(slot.Key, out var position)) return;
                    BuildPlacement.Cancel();
                    _removed.Add(new KeyValuePair<VillageLayoutApplier.Slot, Vector2>(slot, position));
                    VillageLayoutApplier.RemoveDecor(slot);
                    ToastLog.Show($"{Josa.EulReul(slot.Entry.Name)} 치웠습니다.", false);
                }, PlaceConfirmPanel.Hide);
        }

        private void Update()
        {
            if (_removeButton != null)
            {
                var held = _active ? BuildPlacement.HeldSlot : null;
                bool show = held != null && held.Entry.Decor;
                if (_removeButton.activeSelf != show) _removeButton.SetActive(show);
            }
            if (!_active || BuildPlacement.Active) return;
            var mouse = Mouse.current;
            var cam = Camera.main;
            if (mouse == null || cam == null || !mouse.leftButton.wasPressedThisFrame) return;
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
            Vector2 world = cam.ScreenToWorldPoint(mouse.position.ReadValue());
            var slot = Pick(world);
            if (slot != null) BuildPlacement.BeginMove(slot, world);
        }

        private void OnDestroy()
        {
            if (_active)
            {
                GamePause.Pop(PauseKey);
                VillageLayoutApplier.EndHiddenLog(false);
            }
            if (_instance == this) _instance = null;
        }

        /// <summary>그 점을 덮는 지은 건물 중 맨 앞(그림 아래끝이 가장 낮은) 것. 클릭 기능이 있으면 실루엣으로, 없으면 그림 범위로 본다.</summary>
        private static VillageLayoutApplier.Slot Pick(Vector2 world)
        {
            VillageLayoutApplier.Slot best = null;
            float bestBottom = float.MaxValue;
            foreach (var slot in VillageLayoutApplier.Slots)
            {
                if (!slot.Built || slot.Renderer == null || !slot.Renderer.gameObject.activeInHierarchy) continue;
                var selectable = slot.Renderer.GetComponent<SelectableBuilding>();
                bool hit = selectable != null ? selectable.ContainsPoint(world) : slot.Renderer.bounds.Contains(new Vector3(world.x, world.y, slot.Renderer.bounds.center.z));
                if (!hit) continue;
                float bottom = slot.Renderer.bounds.min.y;
                if (bottom < bestBottom)
                {
                    best = slot;
                    bestBottom = bottom;
                }
            }
            return best;
        }

        // ---------- 화면 아래 막대 ----------

        /// <summary>"옮기기: …" 안내 + [완료] + [옮기기 취소]. ESC는 확인 창이 없으면 [완료]처럼 바로 끝낸다.</summary>
        private static GameObject CreateBar(Transform hud, out GameObject removeButton)
        {
            var bar = new GameObject("MoveModeBar", typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
            bar.transform.SetParent(hud, false);
            var rect = (RectTransform)bar.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, BarBottom);
            var image = bar.GetComponent<Image>();
            image.sprite = UITheme.Panel;
            image.type = Image.Type.Sliced;
            var layout = bar.GetComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(18, 12, 8, 8);
            layout.spacing = 12f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            var fitter = bar.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var text = ShopListUI.CreateText(bar.transform, "옮기기: 건물을 클릭해 들고, 놓을 곳을 클릭하세요 · ESC 끝내기", 17, TextAnchor.MiddleLeft, UITheme.BodyText);
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            // 장식품을 들고 있을 때만 보인다(Update)
            removeButton = ShopListUI.CreateButton(bar.transform, "치우기", () => { if (_instance != null) _instance.AskRemove(); }, 90f).gameObject;
            removeButton.SetActive(false);
            ShopListUI.CreateButton(bar.transform, "완료", () => Finish(true), 90f);
            ShopListUI.CreateButton(bar.transform, "옮기기 취소", () => Finish(false), 130f);

            // HUD 캔버스라 ESC를 들고 있는 건물(월드 미리보기)보다 먼저 받는다 → 확인 창이 떠 있으면 그것만 닫고,
            // 아니면 들고 있어도 바로 [완료]처럼 끝낸다(든 건물은 제자리, 옮긴 것은 확정). 들고 있는 것만 놓기는 오른쪽 클릭.
            EscapeCloser.Register(bar, () =>
            {
                if (PlaceConfirmPanel.Active) BuildPlacement.CloseConfirmIfAny();
                else Finish(true);
            }, WindowRole.Popup);
            UIThemeApplier.ApplyNow(bar.transform);
            bar.SetActive(false);
            return bar;
        }
    }
}
