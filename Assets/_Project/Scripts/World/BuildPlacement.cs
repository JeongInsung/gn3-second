using GN3.Economy;
using GN3.Quests;
using GN3.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace GN3.World
{
    /// <summary>
    /// 건설 목록에서 [짓기]를 누르면 그 건물의 반투명 그림이 마우스를 따라다니고, 빈 땅이면 초록·겹치면 빨강으로 보인다.
    /// 왼쪽 클릭으로 그 자리에 짓고(금화·재료를 내고 VillageLayout에 기록 → VillageLayoutApplier.ApplyLive로 그 자리에 바로 세움),
    /// 오른쪽 클릭이나 ESC로 그만둔다. 빈 땅 판정은 마을 길찾기 격자의 막힌 바닥(다른 건물·숲의 나무·덤불·바위)과 겹치지 않는지,
    /// 걷기 영역 안인지, 파견대가 드나드는 마을 출구를 막지 않는지다.
    /// [옮기기] 모드(BuildingMover)에서 지은 건물을 클릭하면 같은 미리보기로 옮기기가 된다: 놓을 곳을 클릭하면 옮겨진다(무료).
    /// 장식품은 그림이 놓는 자리(루트)의 자식이라 그 차이(_artOffset)만큼 미리보기를 옮겨 그리고, 하나 놓으면 같은 것을 바로 다시 든다.
    /// </summary>
    public class BuildPlacement : MonoBehaviour
    {
        private const float Snap = 0.1f;
        private const float ExitClearance = 0.6f; // 마을 출구 둘레로 비워 둘 거리
        private const float BuildingHeightScale = 0.5f;
        private const int GhostSortingOrder = 30000;
        private const float MovingSourceAlpha = 0.35f;  // 옮기는 동안 원래 자리 건물을 흐리게
        private const float OwnAreaPadding = 0.4f;      // 옮기는 건물의 원래 막힌 바닥을 무시할 범위(그림 둘레 여유)
        private static readonly Color ValidTint = new Color(0.85f, 1f, 0.85f, 0.5f);
        private static readonly Color InvalidTint = new Color(1f, 0.72f, 0.72f, 0.5f);

        private static BuildPlacement _instance;

        /// <summary>건물을 놓는 중이면 마을 건물·용병 클릭을 막는다(SelectableBuilding·VillagePartyPresenter·BuildingMover).</summary>
        public static bool Active => _instance != null;

        /// <summary>옮기려고 들고 있는 건물·장식품(없으면 null) — 옮기기 막대의 [치우기].</summary>
        public static VillageLayoutApplier.Slot HeldSlot => _instance != null && _instance._moving ? _instance._slot : null;

        private BuildingCatalog.Entry _entry;
        private VillageLayoutApplier.Slot _slot;
        private SpriteRenderer _ghost;
        private Vector2 _grabOffset; // 놓는 자리(루트) → 마우스가 잡는 곳(바닥 가운데)
        private Vector3 _artOffset;  // 놓는 자리(루트) → 그림(건물은 0, 장식품은 자식 그림 위치)
        private float _heightScale;
        private string _blockReason;
        private bool _moving;        // 지은 건물 옮기기(짓기가 아님)
        private Rect _ownArea;       // 옮기는 건물이 지금 막고 있는 바닥 — 겹침 판정에서 뺀다
        private Color _sourceColor;
        private int _startFrame;     // 집어 든 그 클릭으로 바로 놓지 않게
        private bool _confirming;    // 놓을 곳을 클릭해 확인 창(PlaceConfirmPanel)이 떠 있다 — 미리보기는 그 자리에 멈춘다

        public static void Begin(BuildingCatalog.Entry entry)
        {
            Cancel();
            var slot = VillageLayoutApplier.FirstUnbuilt(entry);
            var placement = Create(slot);
            if (placement == null) return;
            placement._entry = entry;
            var b = placement._ghost.sprite.bounds;
            Vector3 scale = placement.transform.localScale;
            placement._grabOffset = (Vector2)placement._artOffset + new Vector2(b.center.x * scale.x, (b.min.y + b.size.y * 0.25f) * scale.y);
            ToastLog.Show($"{entry.Name}: 왼쪽 클릭으로 {(entry.Decor ? "놓기" : "짓기")} · 오른쪽 클릭/ESC로 취소", false);
        }

        /// <summary>지은 건물을 들어 올린다. grabWorld: 누른 곳(그 자리를 잡은 채 따라온다).</summary>
        public static void BeginMove(VillageLayoutApplier.Slot slot, Vector2 grabWorld)
        {
            Cancel();
            if (slot == null || !slot.Built) return;
            var placement = Create(slot);
            if (placement == null) return;
            placement._entry = slot.Entry;
            placement._moving = true;
            placement._startFrame = Time.frameCount;
            placement._grabOffset = grabWorld - (Vector2)slot.Transform.position;
            var b = slot.Renderer.bounds;
            placement._ownArea = Rect.MinMaxRect(b.min.x - OwnAreaPadding, b.min.y - OwnAreaPadding, b.max.x + OwnAreaPadding, b.max.y + OwnAreaPadding);
            placement._sourceColor = slot.Renderer.color;
            var faded = slot.Renderer.color;
            faded.a *= MovingSourceAlpha;
            slot.Renderer.color = faded;
            ToastLog.Show($"{slot.Entry.Name}: 놓을 곳을 클릭 · 오른쪽 클릭으로 내려놓기 취소 · ESC로 옮기기 끝내기", false);
        }

        /// <summary>그 건물 그림의 미리보기를 만든다(짓기·옮기기 같이).</summary>
        private static BuildPlacement Create(VillageLayoutApplier.Slot slot)
        {
            if (slot == null || slot.Renderer == null || slot.Renderer.sprite == null) return null;

            var go = new GameObject("BuildPlacement", typeof(SpriteRenderer), typeof(BuildPlacement));
            go.transform.localScale = slot.Renderer.transform.lossyScale;
            var placement = go.GetComponent<BuildPlacement>();
            placement._slot = slot;
            placement._artOffset = slot.Renderer.transform.position - slot.Transform.position;
            var shadow = FindShadow(slot);
            placement._heightScale = shadow != null ? shadow.heightScale : BuildingHeightScale;

            var ghost = go.GetComponent<SpriteRenderer>();
            ghost.sprite = slot.Renderer.sprite;
            ghost.flipX = slot.Renderer.flipX;
            ghost.sortingLayerID = slot.Renderer.sortingLayerID;
            ghost.sortingOrder = GhostSortingOrder;
            var unlit = Shader.Find("Sprites/Default"); // 밤에도 미리보기 색이 보이게 조명을 받지 않는 기본 스프라이트
            if (unlit != null) ghost.sharedMaterial = new Material(unlit);
            placement._ghost = ghost;
            go.transform.position = slot.Renderer.transform.position;

            EscapeCloser.Register(go, Cancel, WindowRole.Popup); // ESC는 메뉴 대신 건설 취소
            _instance = placement;
            return placement;
        }

        /// <summary>건물 그림자: 아직 정렬 전이면 자식, 앞뒤 정렬 뒤엔 밖으로 빠져 sourceOverride로 따라간다(VillageDepthSorter).</summary>
        private static ProjectedShadow FindShadow(VillageLayoutApplier.Slot slot)
        {
            var child = slot.Transform.GetComponentInChildren<ProjectedShadow>(true);
            if (child != null) return child;
            foreach (var shadow in FindObjectsByType<ProjectedShadow>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (shadow.sourceOverride == slot.Renderer) return shadow;
            return null;
        }

        public static void Cancel()
        {
            if (_instance == null) return;
            PlaceConfirmPanel.Hide();
            // 바로 끈다: Destroy는 프레임 끝이라, 그 사이 Rebuilt의 건물 찾기(같은 그림)가 미리보기를 건물로 잡았다
            _instance.gameObject.SetActive(false);
            Destroy(_instance.gameObject);
            _instance = null;
        }

        private void Update()
        {
            var mouse = Mouse.current;
            var cam = Camera.main;
            if (mouse == null || cam == null) return;
            if (_confirming || PlaceConfirmPanel.Active) // 다른 확인 창(옮기기 막대 [치우기])이 떠 있어도 멈춘다
            {
                if (mouse.rightButton.wasPressedThisFrame) CloseConfirm(); // 창만 닫고 계속 들고 있는다
                return;
            }
            if (mouse.rightButton.wasPressedThisFrame)
            {
                Cancel();
                return;
            }

            Vector2 world = cam.ScreenToWorldPoint(mouse.position.ReadValue());
            Vector2 position = world - _grabOffset;
            position = new Vector2(Mathf.Round(position.x / Snap) * Snap, Mathf.Round(position.y / Snap) * Snap);
            transform.position = new Vector3(position.x, position.y, _slot.Transform.position.z) + _artOffset;

            _blockReason = BlockReason();
            _ghost.color = _blockReason == null ? ValidTint : InvalidTint;

            bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            if (overUI || !mouse.leftButton.wasPressedThisFrame || (_moving && Time.frameCount == _startFrame)) return;
            if (_blockReason != null) ToastLog.Show(_blockReason, false); // 안 되는 자리면 계속 들고 있는다
            else AskConfirm(position);
        }

        /// <summary>놓을 곳을 클릭했다 → 미리보기를 멈추고 그 위에 확인 창. [짓기]/[옮기기]면 그 자리에 놓는다.</summary>
        private void AskConfirm(Vector2 position)
        {
            if (_moving && (Vector2)_slot.Transform.position == position)
            {
                Cancel(); // 제자리 — 묻지 않고 내려놓는다
                return;
            }
            _confirming = true;
            var b = _ghost.bounds;
            var anchor = new Vector3(b.center.x, b.max.y, b.center.z);
            System.Action ok = () =>
            {
                CloseConfirm();
                if (_moving) Move(position);
                else Place(position);
            };
            if (_moving)
                PlaceConfirmPanel.Show($"{Josa.EulReul(_entry.Name)} 여기로 옮기겠습니까?", "무료", "옮기기", anchor, ok, CloseConfirm);
            else
            {
                string materials = BuildMaterials.Text(_entry.Materials);
                string cost = materials.Length > 0 ? $"{_entry.Cost}G · {materials}" : $"{_entry.Cost}G";
                string verb = _entry.Decor ? "놓기" : "짓기";
                PlaceConfirmPanel.Show($"{Josa.EulReul(_entry.Name)} 여기에 {(_entry.Decor ? "놓겠습니까" : "짓겠습니까")}?", cost, verb, anchor, ok, CloseConfirm);
            }
        }

        private void CloseConfirm()
        {
            _confirming = false;
            PlaceConfirmPanel.Hide();
        }

        /// <summary>확인 창이 떠 있으면 창만 닫는다(옮기기 막대의 ESC).</summary>
        public static void CloseConfirmIfAny()
        {
            if (_instance != null) _instance.CloseConfirm();
            else PlaceConfirmPanel.Hide();
        }

        /// <summary>여기 지을 수 없으면 이유, 되면 null.</summary>
        private string BlockReason()
        {
            var grid = VillagePartyPresenter.Grid;
            if (grid == null) return "지금은 지을 수 없습니다.";
            Rect footprint = VillageNavGrid.FootprintOf(_ghost, _heightScale);
            if (!grid.Area.Contains(footprint.min) || !grid.Area.Contains(footprint.max)) return "빈터 밖에는 지을 수 없습니다.";
            foreach (var blocked in grid.BlockedRects)
            {
                if (_moving && _ownArea.Contains(blocked.min) && _ownArea.Contains(blocked.max)) continue; // 옮기는 건물 자신
                if (blocked.Overlaps(footprint)) return "다른 건물이나 나무·바위와 겹칩니다.";
            }
            var clearance = Rect.MinMaxRect(footprint.xMin - ExitClearance, footprint.yMin - ExitClearance,
                footprint.xMax + ExitClearance, footprint.yMax + ExitClearance);
            if (clearance.Contains(VillagePartyPresenter.VillageExit)) return "마을 출구를 막습니다.";
            return null;
        }

        private void Place(Vector2 position)
        {
            if (BuildingCatalog.Block(_entry, true) is string block)
            {
                ToastLog.Show(block, false);
                Cancel();
                return;
            }
            if (!Wallet.TrySpend(_entry.Cost)) return;
            BuildMaterials.Consume(_entry.Materials); // Block이 방금 재료를 확인했다
            if (_entry.Replaces != null) VillageLayout.RemoveBuilding(_entry.Replaces);
            VillageLayout.Add(_slot.Key, position);

            var entry = _entry;
            string line = $"{Josa.EulReul(entry.Name)} {(entry.Decor ? "놓았습니다" : "지었습니다")}. (−{entry.Cost}G)";
            if (!entry.Decor) DailyLog.Add(line); // 장식품(울타리 조각 등)까지 적으면 일지가 너무 길어진다
            var slot = _slot;
            string replaced = entry.Replaces;
            Cancel();
            // 씬을 다시 부르지 않고 그 자리에 세운다(다시 부르면 0.7초쯤 멈췄다) — 격자·정렬·문·클릭은 Rebuilt를 받아 다시 맞춘다
            VillageLayoutApplier.ApplyLive(slot, replaced);
            ToastLog.Show(line, false);
            // 장식품은 줄줄이 놓도록 같은 것을 바로 다시 든다(오른쪽 클릭/ESC로 그만)
            if (entry.Decor && BuildingCatalog.Block(entry, true) == null) Begin(entry);
        }

        private void Move(Vector2 position)
        {
            var slot = _slot;
            Cancel(); // 흐리게 한 원래 건물 색을 OnDestroy가 되돌린다
            if ((Vector2)slot.Transform.position == position) return;
            VillageLayout.Add(slot.Key, position);
            VillageLayoutApplier.MoveLive(slot);
            ToastLog.Show($"{Josa.EulReul(slot.Entry.Name)} 옮겼습니다.", false);
        }

        private void OnDestroy()
        {
            if (_moving && _slot != null && _slot.Renderer != null) _slot.Renderer.color = _sourceColor;
            if (_instance == this)
            {
                _instance = null;
                PlaceConfirmPanel.Hide();
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession()
        {
            _instance = null;
        }
    }
}
