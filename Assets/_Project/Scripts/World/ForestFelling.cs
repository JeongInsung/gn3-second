using System.Collections.Generic;
using GN3.Economy;
using GN3.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace GN3.World
{
    /// <summary>
    /// 벌목 모드([건설] 창 머리 [벌목]): 마을 숲의 나무·덤불·바위를 클릭해 확인하면 베어 건설 재료를 얻는다(하루 ForestLayout.FellsPerDay번).
    /// 나무 → 통나무 2·나무 말뚝 1, 덤불 → 밧줄 또는 실타래 1, 바위 → 쇠못 2 또는 쇠고리 1. 벤 것은 저장된다(ForestLayout).
    /// 모드 동안은 시간이 멈추고 건물·용병 클릭이 막힌다. 마우스를 올린 것은 밝게 보인다. ESC·[완료]로 끝낸다. MainMenuBootstrapper가 씬마다 하나 만든다.
    /// </summary>
    public class ForestFelling : MonoBehaviour
    {
        private const string PauseKey = "felling-mode";
        private const float BarBottom = 70f;
        private static readonly Color HoverTint = new Color(1f, 0.85f, 0.55f);

        private static ForestFelling _instance;
        public static bool Active => _instance != null && _instance._active;

        private bool _active;
        private GameObject _bar;
        private Text _barText;
        private SpriteRenderer _hovered;
        private Color _hoveredColor;
        private readonly System.Random _rng = new System.Random();

        public static void Create(Transform hud)
        {
            var go = new GameObject("ForestFelling", typeof(ForestFelling));
            _instance = go.GetComponent<ForestFelling>();
            _instance._bar = CreateBar(hud, out _instance._barText);
        }

        public static void Enter()
        {
            if (_instance == null || _instance._active) return;
            BuildingMover.Finish(true);
            BuildPlacement.Cancel();
            _instance._active = true;
            GamePause.Push(PauseKey);
            _instance._bar.SetActive(true);
            _instance.RefreshBar();
        }

        public static void Finish()
        {
            if (!Active) return;
            _instance.SetHovered(null);
            PlaceConfirmPanel.Hide();
            _instance._active = false;
            GamePause.Pop(PauseKey);
            _instance._bar.SetActive(false);
        }

        private void RefreshBar() =>
            _barText.text = $"벌목: 나무·덤불·바위를 클릭 · 오늘 남은 {ForestLayout.FellsLeft}번 · ESC 끝내기";

        private void Update()
        {
            if (!_active || PlaceConfirmPanel.Active) return;
            var mouse = Mouse.current;
            var cam = Camera.main;
            if (mouse == null || cam == null) return;
            bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            Vector2 world = cam.ScreenToWorldPoint(mouse.position.ReadValue());
            var (target, key) = overUI ? (null, null) : Pick(world);
            SetHovered(target);
            if (target == null || !mouse.leftButton.wasPressedThisFrame) return;
            AskFell(target, key);
        }

        /// <summary>그 점을 덮는 숲 물체 중 맨 앞(그림 아래끝이 가장 낮은) 것. 나무는 줄기·잎 전체 범위로 본다.</summary>
        private static (SpriteRenderer renderer, string key) Pick(Vector2 world)
        {
            SpriteRenderer best = null;
            string bestKey = null;
            float bestBottom = float.MaxValue;
            foreach (var (renderer, key) in VillageLayoutApplier.StandingForest())
            {
                var b = renderer.bounds;
                if (world.x < b.min.x || world.x > b.max.x || world.y < b.min.y || world.y > b.max.y) continue;
                if (b.min.y >= bestBottom) continue;
                best = renderer;
                bestKey = key;
                bestBottom = b.min.y;
            }
            return (best, bestKey);
        }

        private void SetHovered(SpriteRenderer renderer)
        {
            if (_hovered == renderer) return;
            if (_hovered != null) _hovered.color = _hoveredColor;
            _hovered = renderer;
            if (_hovered == null) return;
            _hoveredColor = _hovered.color;
            _hovered.color = HoverTint;
        }

        private enum Kind { Tree, Bush, Rock }

        private static Kind KindOf(string name) =>
            name.Contains("관목") || name.Contains("덤불") ? Kind.Bush
            : name.Contains("바위") || name.Contains("돌") ? Kind.Rock
            : Kind.Tree;

        private static string YieldText(Kind kind) => kind switch
        {
            Kind.Bush => "밧줄 또는 실타래 1",
            Kind.Rock => "쇠못 2 또는 쇠고리 1",
            _ => "통나무 2 · 나무 말뚝 1",
        };

        private List<(LootItem item, int count)> RollYield(Kind kind) => kind switch
        {
            Kind.Bush => new List<(LootItem, int)> { _rng.NextDouble() < 0.5 ? (LootCatalog.Rope, 1) : (LootCatalog.Thread, 1) },
            Kind.Rock => new List<(LootItem, int)> { _rng.NextDouble() < 0.5 ? (LootCatalog.Nail, 2) : (LootCatalog.IronRing, 1) },
            _ => new List<(LootItem, int)> { (LootCatalog.Log, 2), (LootCatalog.Stake, 1) },
        };

        private void AskFell(SpriteRenderer target, string key)
        {
            string name = ShortName(key.Substring(0, key.LastIndexOf('#')));
            var kind = KindOf(name);
            if (ForestLayout.FellsLeft <= 0)
            {
                ToastLog.Show($"오늘은 더 벨 수 없습니다(하루 {ForestLayout.FellsPerDay}번).", false);
                return;
            }
            var b = target.bounds;
            string verb = kind == Kind.Rock ? "캐" : "베";
            PlaceConfirmPanel.Show($"{Josa.EulReul(name)} {verb}겠습니까?", $"얻는 것: {YieldText(kind)}", kind == Kind.Rock ? "캐기" : "베기",
                new Vector3(b.center.x, b.max.y, b.center.z), () =>
                {
                    PlaceConfirmPanel.Hide();
                    SetHovered(null);
                    var loot = RollYield(kind);
                    LootBag.Add(loot);
                    VillageLayoutApplier.Fell(target, key);
                    ToastLog.Show($"얻었습니다: {LootCatalog.Describe(loot)}", false);
                    RefreshBar();
                }, PlaceConfirmPanel.Hide);
        }

        /// <summary>"풍성한 오크 나무 (12) (바깥)" 같은 물체 이름에서 보이는 이름만(복제 번호·바깥 표시를 뗀다).</summary>
        private static string ShortName(string name) =>
            System.Text.RegularExpressions.Regex.Replace(name, @"( \((\d+|바깥)\))+$", "").Trim();

        private void OnDestroy()
        {
            if (_active) GamePause.Pop(PauseKey);
            if (_instance == this) _instance = null;
        }

        private static GameObject CreateBar(Transform hud, out Text text)
        {
            var bar = new GameObject("FellingModeBar", typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
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

            text = ShopListUI.CreateText(bar.transform, "", 17, TextAnchor.MiddleLeft, UITheme.BodyText);
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            ShopListUI.CreateButton(bar.transform, "완료", Finish, 90f);
            // 확인 창이 떠 있으면 그것만 닫고, 아니면 끝낸다
            EscapeCloser.Register(bar, () =>
            {
                if (PlaceConfirmPanel.Active) PlaceConfirmPanel.Hide();
                else Finish();
            }, WindowRole.Popup);
            UIThemeApplier.ApplyNow(bar.transform);
            bar.SetActive(false);
            return bar;
        }
    }
}
