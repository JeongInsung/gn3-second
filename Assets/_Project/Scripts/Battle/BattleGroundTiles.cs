using System.Collections.Generic;
using GN3.Combat;
using GN3.World;
using UnityEngine;
using UnityEngine.UI;

namespace GN3.Battle
{
    /// <summary>
    /// 전투 필드(UI 판) 바닥에 잔디 타일을 깐다. 필드가 Screen Space Overlay UI라 씬 Tilemap은 가려지므로 UI 이미지 격자로 깐다.
    /// 그림은 Resources/Battle/Tiles/tile1·tile2(조정식/Tiles의 TILE 1·TILE 2 복사본, 4×4 칸 128px)를 런타임에 잘라 쓴다.
    /// 칸은 클릭을 받지 않아 필드의 땅 클릭(BattleClickTarget)은 그대로 동작한다. 같은 seed면 늘 같은 무늬.
    /// 판 바깥(캔버스 루트, Backdrop 바로 위)에도 같은 격자로 잔디를 넓혀 깔고, 판 위쪽과 양옆에 마을 숲 테두리처럼 숲을 두른다:
    /// 흔들리는 참나무(Resources/Battle/oak_sway = 조정식/Baked/Animated/참나무 바람, 148×146 8프레임 가로 한 줄)에
    /// 소나무·가을나무를 섞고, 앞줄 나무 발치에 덤불숲(Resources/Battle/Decor/thicket)을 둔다. 판 자체는 투명해 경계가 없다.
    /// 판이 BattleUIController.FieldZoom으로 줄어 그려지므로 판 좌표를 쓰는 바깥 층도 같은 배율을 받는다.
    /// 꽃·풀·돌과 나비·빛 가루·구름 그림자는 BattleDecor가 같은 rng로 얹는다.
    /// </summary>
    public static class BattleGroundTiles
    {
        private const float CellSize = 80f; // 2200×700 필드 = 28×9칸
        private const int SheetColumns = 4;
        private const float Shade = 0.85f;  // 유닛이 잘 보이게 바닥을 살짝 어둡게
        private const float EdgeCrop = 4f;   // 칸 가장자리에 구워진 어두운 테두리(128px 칸 기준 3~4px, FloorTileSetup.SeamFixPixels)를 잘라 격자선이 안 보이게

        // 판 바깥 잔디 범위(판 좌표, 화면 비율이 바뀌어도 빈 곳이 안 남게 넉넉히)
        private const int OuterColumns = 6;    // 좌우 각 480
        private const int OuterRowsBelow = 5;  // 아래 400(아래 패널이 덮는다)
        private const int OuterRowsAbove = 9;  // 위 720

        // 판 위쪽 숲
        private const string TreeSheetPath = "Battle/oak_sway";
        private const int TreeFrameCount = 8;
        private const float TreeFps = 1.5f;        // 마을 참나무와 같은 속도(TreeAnimationBuilder)
        private const float TreeWidth = 210f, TreeHeight = 207f;
        private const float TreeBandBottom = 4f;        // 밑동 높이 범위(판 위 가장자리 기준). 0보다 아래면 판이 기둥 밑을 가린다(그림 기둥이 맨 아래까지 닿음)
        private const float TreeBandTop = 300f;
        private const float TreeMinScale = 0.8f, TreeMaxScale = 1.25f;
        private const float TreeSpacingFactor = 0.38f;  // 두 나무 평균 폭 × 이 값보다 가까우면 버린다(빼곡함 조절)
        private const float TreeBackShrink = 0.15f;     // 맨 뒤 나무는 최대 15% 작게
        private const float TreeBackDarken = 0.24f;     // 맨 뒤 나무는 최대 24% 어둡게
        private const float TreeMinLight = 0.88f;       // 나무마다 밝기 0.88~1
        private const float TitleShadeHeight = 110f, TitleShadeAlpha = 0.65f; // 제목이 나무 위에서도 읽히게

        // 둥근 공터(FieldBattleSession.ArenaRadius)를 두른 숲 띠. 보스전(숲 깊은 곳)은 더 넓고 빽빽하게 + 숲 그늘
        private const float SideBandWidth = 300f, DeepSideBandWidth = 420f; // 공터 가장자리에서 바깥으로 띠 폭(판 바깥 잔디 480 안)
        // 아래 가운데·덤불숲은 밑동이 이보다 아래면 심지 않는다(아래 패널이 판 좌표 y -25 밑을 덮는다). 양옆 아래 모서리는 CornerMinBase까지.
        // 타원이라 아래 모서리 숲은 이 위로만 둥글게 차고, 아래 가운데는 경계가 판 밖이라 저절로 빈다.
        private const float MinTreeBase = -12f; // 패널 위 가장자리(-25)보다 위 — 맨 아래 줄과 패널 사이 잔디 틈을 줄인다
        private const int RimAttempts = 10000, DeepRimAttempts = 16000; // 판 둘레 전체에 던져 볼 자리 수
        private const int CornerAttempts = 4000, DeepCornerAttempts = 5500; // 아래 모서리 구석 한 곳에 더 던져 볼 자리 수
        private const float CornerMinBase = -170f; // 아래 모서리 구역 밑동 하한: 줄기는 아래 패널 뒤, 수관(≈200)이 패널 위 빈 줄을 덮는다
        private const float CornerInnerX = 700f; // 아래 모서리 구석 사각형의 안쪽 끝(왼쪽 기준 x, 오른쪽은 대칭)
        private const float RimWobbleLarge = 18f, RimWobbleSmall = 12f; // 경계 물결(px): 3번·7번 굽이
        private const float DeepForestShade = 0.85f; // 바닥을 이만큼 더 어둡게(우거진 숲 그늘)

        // 마을 숲처럼 섞는 나무: 참나무(흔들림) · 소나무 · 가을나무. 높이는 참나무(TreeHeight)와 마을 그림 비율을 맞춘 값
        private const float OakShare = 0.5f, PineShare = 0.3f; // 나머지는 가을나무
        private const float PineHeight = 245f, AutumnHeight = 214f;
        // 앞줄 나무 발치 덤불숲(thicket). 공 모양 관목(bush)·이끼 바위(rocks)는 잔디에 떨어져 어색해 쓰지 않는다
        private const int UndergrowthCount = 14;
        private const float UndergrowthHeight = 110f;
        private const float UndergrowthFrontDepth = 0.3f; // 이보다 앞(경계 가까이)인 나무 발치에만
        private const float UndergrowthSpacing = 140f;   // 덤불숲끼리 이만큼 떨어뜨린다

        private class Theme
        {
            public string Sheet;
            public int[] Cells; // 시트 칸 번호(왼쪽 위 0부터 가로로)
            public Color Tint;
        }

        // 지금은 모든 지역이 잔디. 지역별 바닥을 넣으려면 region으로 나눠 여기만 바꾸면 된다.
        private static Theme For(Region region) =>
            new Theme { Sheet = "tile1", Cells = new[] { 2, 3 }, Tint = Color.white };

        private static readonly Dictionary<string, Sprite[]> _sheets = new Dictionary<string, Sprite[]>();
        private static Sprite[] _treeFrames;

        // Domain Reload가 꺼져 있어도 Play마다 비운다(Sprite.Create로 만든 스프라이트는 Play가 끝나면 파괴된다).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession()
        {
            _sheets.Clear();
            _treeFrames = null;
        }

        /// <param name="deepForest">보스전(은월의 숲 깊은 곳): 판 왼쪽·오른쪽 바깥까지 숲으로 둘러싸고 바닥을 숲 그늘로 조금 어둡게.</param>
        /// <returns>유닛 앞에 그릴 아래쪽 숲 층(ForestFront). 부르는 쪽이 유닛 층 바로 위로 옮기고 유닛 위치를 넣어 준다. 그림이 없으면 null.</returns>
        public static BattleFrontTrees Build(RectTransform field, Region region, int seed, bool deepForest = false)
        {
            var theme = For(region);
            var cells = Sheet(theme.Sheet);
            if (cells == null) return null; // 그림이 없으면 원래 판 그대로

            var layer = new GameObject("GroundTiles", typeof(RectTransform));
            layer.transform.SetParent(field, false);
            layer.transform.SetAsFirstSibling();
            var layerRect = (RectTransform)layer.transform;
            layerRect.anchorMin = Vector2.zero;
            layerRect.anchorMax = Vector2.one;
            layerRect.offsetMin = layerRect.offsetMax = Vector2.zero;
            layerRect.pivot = Vector2.zero;

            Vector2 size = field.sizeDelta;
            int columns = Mathf.CeilToInt(size.x / CellSize);
            int rows = Mathf.CeilToInt(size.y / CellSize);
            var rng = new System.Random(seed);
            Color color = theme.Tint * (Shade * (deepForest ? DeepForestShade : 1f));
            color.a = 1f;

            for (int y = 0; y < rows; y++)
            for (int x = 0; x < columns; x++)
                AddTile(layer.transform, x, y, cells[theme.Cells[rng.Next(theme.Cells.Length)]], color);
            // 판 안에만 따로 얹는 그늘·색은 두지 않는다: 판 밖(나무 밑) 잔디와 한 장으로 이어져 보이게.
            var flowerSpots = BattleDecor.BuildField(layer.transform, size, rng); // 꽃밭·풀·바위

            var front = BuildSurroundings(field, theme, cells, color, columns, rows, rng, deepForest);
            var atmosphere = BattleFx.BuildAtmosphere(field);
            BattleDecor.BuildLife(atmosphere, field, flowerSpots); // 나비·빛 가루·구름 그림자
            return front;
        }

        /// <summary>판 바깥 잔디 + 위쪽 숲 + 제목 그늘. 캔버스 루트의 Backdrop 바로 다음에 둬서 제목·판·아래 패널보다 뒤에 그린다.</summary>
        private static BattleFrontTrees BuildSurroundings(RectTransform field, Theme theme, Sprite[] cells, Color color, int columns, int rows, System.Random rng, bool deepForest)
        {
            var root = field.parent;
            if (root == null) return null;

            var surroundings = new GameObject("Surroundings", typeof(RectTransform));
            surroundings.transform.SetParent(root, false);
            var backdrop = root.Find("Backdrop");
            surroundings.transform.SetSiblingIndex(backdrop != null ? backdrop.GetSiblingIndex() + 1 : 0);
            var surroundRect = (RectTransform)surroundings.transform;
            surroundRect.anchorMin = Vector2.zero;
            surroundRect.anchorMax = Vector2.one;
            surroundRect.offsetMin = surroundRect.offsetMax = Vector2.zero;

            // 판과 같은 자리·피벗의 층: 자식 좌표 = 판 좌표라 격자가 판 안 타일과 이어진다.
            var area = new GameObject("Outskirts", typeof(RectTransform));
            area.transform.SetParent(surroundings.transform, false);
            var areaRect = (RectTransform)area.transform;
            areaRect.anchorMin = field.anchorMin;
            areaRect.anchorMax = field.anchorMax;
            areaRect.pivot = field.pivot;
            areaRect.sizeDelta = field.sizeDelta;
            areaRect.localScale = field.localScale; // 판과 같이 줄여 그린다(줌 아웃)
            areaRect.anchoredPosition = field.anchoredPosition;

            for (int y = -OuterRowsBelow; y < rows + OuterRowsAbove; y++)
            for (int x = -OuterColumns; x < columns + OuterColumns; x++)
            {
                if (x >= 0 && x < columns && y >= 0 && y < rows) continue; // 판 자리는 판 안 타일이 덮는다
                AddTile(area.transform, x, y, cells[theme.Cells[rng.Next(theme.Cells.Length)]], color);
            }

            BattleDecor.BuildOutskirts(area.transform, field.sizeDelta, OuterColumns * CellSize, rng);
            var front = BuildForest(area.transform, field, rng, deepForest);

            var shade = new GameObject("TitleShade", typeof(RectTransform), typeof(Image));
            shade.transform.SetParent(surroundings.transform, false);
            var shadeRect = (RectTransform)shade.transform;
            shadeRect.anchorMin = new Vector2(0f, 1f);
            shadeRect.anchorMax = Vector2.one;
            shadeRect.pivot = new Vector2(0.5f, 1f);
            shadeRect.sizeDelta = new Vector2(0f, TitleShadeHeight);
            shadeRect.anchoredPosition = Vector2.zero;
            var shadeImage = shade.GetComponent<Image>();
            shadeImage.color = new Color(0f, 0f, 0f, TitleShadeAlpha);
            shadeImage.raycastTarget = false;
            return front;
        }

        private struct Tree
        {
            public Vector2 Pos;
            public float Scale, Depth;
            public bool Side;     // 판 안 층(ForestSides)에 심는다
            public string Static; // null이면 흔들리는 참나무, 아니면 Decor 그림 이름
            public float Height;  // 그림 높이(참나무는 TreeHeight)
        }

        /// <summary>판 위쪽과 양옆에 마을 숲처럼 나무를 무작위로 빼곡히(종류·크기·좌우·밝기도 나무마다 다르게). 뒤(위)부터 그려 앞 나무가 앞에 보인다.
        /// 양옆 나무는 수관이 판 안까지 뻗어서, 판 뒤(바깥 층)에 두면 판 바닥에 덮여 가장자리에서 세로로 잘려 보인다
        /// → 판 안 층(ForestSides: 바닥 타일 바로 위, 땅 표시·유닛·빛 막 아래)에 심는다. 판 좌표 = 바깥 층 좌표라 자리는 같다.
        /// 숲 안쪽 가장자리의 덤불·바위도 판 바닥에 덮이지 않게 같은 층에 둔다. deepForest(보스전)면 양옆 띠가 더 넓고 빽빽하다.</summary>
        private static BattleFrontTrees BuildForest(Transform parent, RectTransform field, System.Random rng, bool deepForest)
        {
            var frames = TreeFrames();
            if (frames == null) return null;
            Vector2 fieldSize = field.sizeDelta;

            var (shadows, sway) = CreateForestLayer(parent, "Forest", -1);
            var (sideShadows, sideSway) = CreateForestLayer(field, "ForestSides", 1); // GroundTiles(0번) 바로 위
            // 아래쪽 숲(밑동이 공터 세로 가운데보다 아래): 근처 유닛보다 늘 앞이라 유닛 위에 그린다(그림자는 땅 층 ForestSidesShadows).
            // 판 안에 만들어 두고, BattleUIController가 유닛 층 바로 위로 옮긴다.
            var frontGo = new GameObject("ForestFront", typeof(RectTransform));
            frontGo.transform.SetParent(field, false);
            var frontRect = (RectTransform)frontGo.transform;
            frontRect.anchorMin = frontRect.anchorMax = frontRect.pivot = Vector2.zero;
            var frontSway = frontGo.AddComponent<BattleTreeSway>();
            var front = frontGo.AddComponent<BattleFrontTrees>();
            float frontBelowY = FieldBattleSession.ArenaCenter.Y;

            // 마을 공터(VillageClearingBuilder)처럼: 둥근 공터(FieldBattleSession.ArenaRadius) 밖에 자리를 무작위로 던지고,
            // 공터 가장자리에 가까운 것부터 심는다 → 경계가 빼곡한 곡선이 된다. 이미 심은 나무와 너무 가까우면 버린다(줄이 맞지 않게).
            // 경계는 각도에 따라 살짝 물결친다(seed마다 다르게). 멀수록(depth 1) 작고 어둡게.
            float bandWidth = deepForest ? DeepSideBandWidth : SideBandWidth;
            int attempts = deepForest ? DeepRimAttempts : RimAttempts;
            float phase1 = (float)rng.NextDouble() * Mathf.PI * 2f, phase2 = (float)rng.NextDouble() * Mathf.PI * 2f;
            float left = -OuterColumns * CellSize, right = fieldSize.x + OuterColumns * CellSize;
            float top = fieldSize.y + TreeBandTop + 120f; // 공터 위 가장자리는 가운데서 판보다 100 위(≈800)
            // 판 바닥 타일은 80칸으로 올림해 깔려(700 → 720) 판 위 가장자리보다 조금 위까지 덮는다 → 밑동이 그 아래면 판 안 층에 심는다
            float tileTop = Mathf.CeilToInt(fieldSize.y / CellSize) * CellSize + TreeBandBottom;
            var candidates = new List<(Vector2 pos, float edge)>();
            // 판 아래 절반(양옆 아래 모서리)은 띠 폭 제한 없이 공터 밖을 아래 패널·화면 끝까지 다 채운다(반쯤 빈 구석이 어색했다).
            // 위쪽은 띠가 화면 위 끝까지 닿아 그대로.
            float halfY = fieldSize.y * 0.5f;
            // 양옆 아래 모서리 구역은 밑동을 아래 패널(화면 틀) 뒤까지 내려 수관이 패널 바로 위까지 덮게 한다(맨 아래 줄 밑이 비어 보이지 않게).
            // 아래 가운데는 싸우는 곳을 가리지 않게 MinTreeBase 위만.
            bool InCorner(float x) => x < CornerInnerX || x > fieldSize.x - CornerInnerX;
            void Consider(Vector2 pos)
            {
                if (pos.y < (InCorner(pos.x) ? CornerMinBase : MinTreeBase)) return;
                float edge = RimDistance(pos, phase1, phase2);
                if (edge >= 0f && (edge <= bandWidth || pos.y < halfY)) candidates.Add((pos, edge));
            }
            for (int attempt = 0; attempt < attempts; attempt++)
                Consider(new Vector2(Mathf.Lerp(left, right, (float)rng.NextDouble()), Mathf.Lerp(CornerMinBase, top, (float)rng.NextDouble())));
            // 아래 모서리 구석 두 곳에 후보를 더 던져 빈틈없이 빽빽하게
            int cornerAttempts = deepForest ? DeepCornerAttempts : CornerAttempts;
            for (int attempt = 0; attempt < cornerAttempts * 2; attempt++)
            {
                bool leftCorner = attempt % 2 == 0;
                float x = leftCorner ? Mathf.Lerp(left, CornerInnerX, (float)rng.NextDouble())
                                     : Mathf.Lerp(fieldSize.x - CornerInnerX, right, (float)rng.NextDouble());
                Consider(new Vector2(x, Mathf.Lerp(CornerMinBase, halfY, (float)rng.NextDouble())));
            }
            candidates.Sort((a, b) => a.edge.CompareTo(b.edge));
            var trees = new List<Tree>();
            foreach (var (pos, edge) in candidates)
            {
                float depth = Mathf.Min(1f, edge / bandWidth); // 0 = 맨 앞(공터 가장자리), 1 = 맨 뒤(띠 밖 구석도 맨 뒤처럼)
                float scale = Mathf.Lerp(TreeMinScale, TreeMaxScale, (float)rng.NextDouble()) * (1f - TreeBackShrink * depth);
                // 밑동이 판 바닥 타일 윗끝보다 아래면 판 안 층(바닥에 덮이지 않게), 그 위는 판 뒤 바깥 층
                TryPlant(trees, NewTree(rng, pos, scale, depth, pos.y < tileTop));
            }
            AddUndergrowth(trees, rng, tileTop);
            trees.Sort((a, b) => b.Pos.y.CompareTo(a.Pos.y)); // 뒤(위)부터 그려 앞 나무가 가린다

            foreach (var tree in trees)
            {
                var sprite = tree.Static != null ? BattleDecor.Load(tree.Static) : null;
                if (tree.Static != null && sprite == null) continue; // 그림이 없으면 건너뛴다
                bool inFront = tree.Side && tree.Pos.y < frontBelowY;
                var layerSway = inFront ? frontSway : tree.Side ? sideSway : sway;
                var go = new GameObject(tree.Static ?? "Tree", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(layerSway.transform, false);
                var rect = (RectTransform)go.transform;
                rect.anchorMin = rect.anchorMax = Vector2.zero;
                rect.pivot = new Vector2(0.5f, 0f);
                rect.anchoredPosition = tree.Pos;
                rect.sizeDelta = sprite != null
                    ? new Vector2(tree.Height * sprite.rect.width / sprite.rect.height, tree.Height) * tree.Scale
                    : new Vector2(TreeWidth, TreeHeight) * tree.Scale;
                if (rng.NextDouble() < 0.5) rect.localScale = new Vector3(-1f, 1f, 1f); // 좌우 뒤집어 같은 모양 반복을 줄인다

                float light = Mathf.Lerp(TreeMinLight, 1f, (float)rng.NextDouble()) * (1f - TreeBackDarken * tree.Depth);
                var image = go.GetComponent<Image>();
                image.color = new Color(light, light, light, 1f);
                image.raycastTarget = false;
                if (sprite != null) image.sprite = sprite;
                else layerSway.Add(image, rng.Next(TreeFrameCount));
                BattleShadow.Attach(image, tree.Side ? sideShadows : shadows, 0.8f); // 마을 나무와 같은 높이 비율
                if (inFront) front.Add(image, tree.Pos, rect.sizeDelta);
            }
            sway.Init(frames, TreeFps);
            sideSway.Init(frames, TreeFps);
            frontSway.Init(frames, TreeFps);
            return front;
        }

        /// <summary>나무 종류를 마을 숲 비율로 고른다.</summary>
        private static Tree NewTree(System.Random rng, Vector2 pos, float scale, float depth, bool side)
        {
            double pick = rng.NextDouble();
            string kind = pick < OakShare ? null : pick < OakShare + PineShare ? "pine" : "autumn";
            return new Tree
            {
                Pos = pos, Scale = scale, Depth = depth, Side = side, Static = kind,
                Height = kind == "pine" ? PineHeight : kind == "autumn" ? AutumnHeight : TreeHeight,
            };
        }

        /// <summary>
        /// 앞줄 나무(공터 가장자리 가까이) 발치에 덤불숲을 붙인다 — 줄기 앞을 가리는 풀숲처럼. 잔디 한가운데 혼자 떨어진 덤불은 없다.
        /// 나무 밑동보다 조금 앞(아래)이라 y 정렬로 나무 앞에 선다. tileTop 아래면 판 안 층(나무 층 기준과 같다).
        /// </summary>
        private static void AddUndergrowth(List<Tree> trees, System.Random rng, float tileTop)
        {
            var front = trees.FindAll(t => t.Static != "thicket" && t.Depth < UndergrowthFrontDepth);
            var placed = new List<Vector2>();
            for (int attempt = 0; attempt < UndergrowthCount * 20 && placed.Count < UndergrowthCount && front.Count > 0; attempt++)
            {
                var tree = front[rng.Next(front.Count)];
                float side = Mathf.Lerp(30f, 70f, (float)rng.NextDouble()) * (rng.NextDouble() < 0.5 ? -1f : 1f);
                var pos = tree.Pos + new Vector2(side, -Mathf.Lerp(10f, 30f, (float)rng.NextDouble()));
                if (pos.y < MinTreeBase) continue;
                bool far = true;
                foreach (var p in placed) if (Vector2.Distance(p, pos) < UndergrowthSpacing) { far = false; break; }
                if (!far) continue;
                placed.Add(pos);
                trees.Add(new Tree
                {
                    Pos = pos, Scale = Mathf.Lerp(0.85f, 1.15f, (float)rng.NextDouble()), Depth = 0f, Side = pos.y < tileTop,
                    Static = "thicket", Height = UndergrowthHeight,
                });
            }
        }

        /// <summary>
        /// pos에서 둥근 공터 가장자리까지 바깥쪽 거리(px, 안쪽이면 음수). superellipse 상대 거리 r을 기울기로 나눠 px로 어림하고,
        /// 각도에 따라 물결(RimWobble)을 빼 경계가 매끈한 곡선만은 아니게 한다.
        /// </summary>
        private static float RimDistance(Vector2 pos, float phase1, float phase2)
        {
            float r = FieldBattleSession.ArenaRadius(pos.x, pos.y);
            float gx = FieldBattleSession.ArenaRadius(pos.x + 1f, pos.y) - FieldBattleSession.ArenaRadius(pos.x - 1f, pos.y);
            float gy = FieldBattleSession.ArenaRadius(pos.x, pos.y + 1f) - FieldBattleSession.ArenaRadius(pos.x, pos.y - 1f);
            float grad = Mathf.Sqrt(gx * gx + gy * gy) * 0.5f;
            float distance = grad > 1e-6f ? (r - 1f) / grad : 0f;
            var center = FieldBattleSession.ArenaCenter;
            float angle = Mathf.Atan2((pos.y - center.Y) / FieldBattleSession.ArenaRadiusY, (pos.x - center.X) / FieldBattleSession.ArenaRadiusX);
            return distance - (RimWobbleLarge * Mathf.Sin(3f * angle + phase1) + RimWobbleSmall * Mathf.Sin(7f * angle + phase2));
        }

        /// <summary>나무 그림자 층 + 나무 층(흔들림 포함)을 parent 안에 만든다. siblingIndex ≥ 0이면 그 순서부터(그림자 → 나무), 아니면 맨 뒤에.</summary>
        private static (RectTransform shadows, BattleTreeSway sway) CreateForestLayer(Transform parent, string name, int siblingIndex)
        {
            var shadows = BattleDecor.CreateLayer(parent, name + "Shadows"); // 나무 해 그림자(나무보다 먼저 그린다)
            var forest = new GameObject(name, typeof(RectTransform));
            forest.transform.SetParent(parent, false);
            var forestRect = (RectTransform)forest.transform;
            forestRect.anchorMin = forestRect.anchorMax = forestRect.pivot = Vector2.zero;
            if (siblingIndex >= 0)
            {
                shadows.SetSiblingIndex(siblingIndex);
                forest.transform.SetSiblingIndex(siblingIndex + 1);
            }
            return (shadows, forest.AddComponent<BattleTreeSway>());
        }

        /// <summary>이미 심은 나무와 너무 가깝지 않으면 심는다 → 빼곡하지만 줄이 맞지 않는다.</summary>
        private static void TryPlant(List<Tree> trees, Tree tree)
        {
            foreach (var other in trees)
                if (Vector2.Distance(other.Pos, tree.Pos) < TreeWidth * (tree.Scale + other.Scale) * 0.5f * TreeSpacingFactor) return;
            trees.Add(tree);
        }

        private static void AddTile(Transform parent, int x, int y, Sprite sprite, Color color)
        {
            var go = new GameObject($"Tile {x},{y}", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
            rect.anchoredPosition = new Vector2(x * CellSize, y * CellSize);
            rect.sizeDelta = new Vector2(CellSize, CellSize);
            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
        }

        /// <summary>시트를 4×4 칸으로 자른다(시트마다 한 번). 시트가 없으면 null.</summary>
        private static Sprite[] Sheet(string name)
        {
            if (_sheets.TryGetValue(name, out var cached) && cached[0] != null) return cached; // 파괴됐으면 다시 자른다
            var texture = Resources.Load<Texture2D>("Battle/Tiles/" + name);
            if (texture == null) return null;
            int rows = SheetColumns;
            float w = texture.width / (float)SheetColumns, h = texture.height / (float)rows;
            var sprites = new Sprite[SheetColumns * rows];
            for (int i = 0; i < sprites.Length; i++)
            {
                int col = i % SheetColumns, row = i / SheetColumns;
                // 텍스처 좌표는 아래가 0이라 윗줄이 가장 큰 y.
                float crop = EdgeCrop * w / 128f;
                var rect = new Rect(col * w + crop, (rows - 1 - row) * h + crop, w - 2f * crop, h - 2f * crop);
                sprites[i] = Sprite.Create(texture, rect, new Vector2(0.5f, 0.5f));
            }
            _sheets[name] = sprites;
            return sprites;
        }

        /// <summary>참나무 시트(가로 8프레임)를 자른다(한 번). 없으면 null.</summary>
        private static Sprite[] TreeFrames()
        {
            if (_treeFrames != null && _treeFrames[0] != null) return _treeFrames; // 파괴됐으면 다시 자른다
            var texture = Resources.Load<Texture2D>(TreeSheetPath);
            if (texture == null) return null;
            float w = texture.width / (float)TreeFrameCount;
            _treeFrames = new Sprite[TreeFrameCount];
            for (int i = 0; i < TreeFrameCount; i++)
                _treeFrames[i] = Sprite.Create(texture, new Rect(i * w, 0f, w, texture.height), new Vector2(0.5f, 0f));
            return _treeFrames;
        }
    }

    /// <summary>
    /// 유닛 앞에 그리는 아래쪽 숲(ForestFront). 나무 뒤(밑동보다 위)에 선 유닛이 수관에 가려지면 그 나무를 반쯤 비치게 해
    /// 유닛이 보이게 한다. 유닛 발 위치(판 좌표)는 BattleUIController가 UnitFeet로 넣어 준다.
    /// </summary>
    public class BattleFrontTrees : MonoBehaviour
    {
        private const float CheckInterval = 0.1f;
        private const float FadedAlpha = 0.4f, FadePerSecond = 3f;
        private const float UnitHalfWidth = 30f, UnitHeight = 90f; // 유닛 몸 칸(발 기준)

        private struct Entry
        {
            public Image Image;
            public Rect Canopy; // 수관 칸(판 좌표)
            public float BaseY;
            public bool Fade;
        }

        private readonly List<Entry> _trees = new List<Entry>();
        private readonly List<Vector2> _feet = new List<Vector2>();
        private float _timer;

        /// <summary>지금 판 위에 있는 유닛(시체 포함) 발 위치.</summary>
        public System.Func<IEnumerable<Vector2>> UnitFeet;

        public void Add(Image image, Vector2 basePos, Vector2 size)
        {
            float halfWidth = size.x * 0.4f;
            _trees.Add(new Entry
            {
                Image = image, BaseY = basePos.y,
                Canopy = new Rect(basePos.x - halfWidth, basePos.y + size.y * 0.2f, halfWidth * 2f, size.y * 0.8f),
            });
        }

        private void Update()
        {
            _timer -= Time.unscaledDeltaTime;
            if (_timer <= 0f)
            {
                _timer = CheckInterval;
                Check();
            }
            float step = FadePerSecond * Time.unscaledDeltaTime;
            foreach (var tree in _trees)
            {
                if (tree.Image == null) continue;
                var color = tree.Image.color;
                float target = tree.Fade ? FadedAlpha : 1f;
                if (color.a == target) continue;
                color.a = Mathf.MoveTowards(color.a, target, step);
                tree.Image.color = color;
            }
        }

        private void Check()
        {
            _feet.Clear();
            var feet = UnitFeet?.Invoke();
            if (feet != null) _feet.AddRange(feet);
            for (int i = 0; i < _trees.Count; i++)
            {
                var tree = _trees[i];
                tree.Fade = false;
                foreach (var foot in _feet)
                {
                    if (foot.y <= tree.BaseY) continue; // 나무보다 앞(아래)에 선 유닛은 원래 나무 위에 보인다
                    var body = new Rect(foot.x - UnitHalfWidth, foot.y, UnitHalfWidth * 2f, UnitHeight);
                    if (body.Overlaps(tree.Canopy)) { tree.Fade = true; break; }
                }
                _trees[i] = tree;
            }
        }
    }

    /// <summary>전투 화면 숲의 참나무들을 한꺼번에 넘긴다. 나무마다 시작 프레임이 달라 같이 움직이지 않는다.
    /// Time.deltaTime이라 전투 일시정지·배속을 그대로 따른다.</summary>
    public class BattleTreeSway : MonoBehaviour
    {
        private readonly List<Image> _trees = new List<Image>();
        private readonly List<int> _offsets = new List<int>();
        private Sprite[] _frames;
        private float _fps;
        private float _time;
        private int _shown = -1;

        public void Add(Image tree, int offset)
        {
            _trees.Add(tree);
            _offsets.Add(offset);
        }

        public void Init(Sprite[] frames, float fps)
        {
            _frames = frames;
            _fps = fps;
            Show(0);
        }

        private void Update()
        {
            if (_frames == null) return;
            _time += Time.deltaTime;
            int step = Mathf.FloorToInt(_time * _fps);
            if (step != _shown) Show(step);
        }

        private void Show(int step)
        {
            _shown = step;
            for (int i = 0; i < _trees.Count; i++)
                if (_trees[i] != null) _trees[i].sprite = _frames[(step + _offsets[i]) % _frames.Length];
        }
    }
}
