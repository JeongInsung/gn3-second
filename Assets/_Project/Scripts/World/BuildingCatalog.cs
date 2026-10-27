using System.Collections.Generic;
using System.Linq;
using GN3.Economy;

namespace GN3.World
{
    /// <summary>
    /// 지을 수 있는 마을 건물 목록. 건물 그림은 Village 프리팹 안에 미리 놓인 물체를 그대로 쓰고(그림자·창문 불빛 포함),
    /// 자식 이름이 Prefixes 중 하나로 시작하면 그 건물이다. 짓지 않은 건물은 숨기고 지은 건물은 고른 자리로 옮긴다(VillageLayoutApplier).
    /// 주택(House)은 여러 채가 한 줄로 묶여 짓는 순서대로 한 채씩 나온다. 짓는 데는 금화와 건설 재료(BuildMaterials)가 든다.
    /// </summary>
    public static class BuildingCatalog
    {
        public class Entry
        {
            public string Id;
            public string Name;
            public string Description;
            public string[] Prefixes;
            public int RequiredRank;   // Guild.Ranks 번호
            public int Cost;
            public (LootItem item, int count)[] Materials; // 건설 재료(BuildMaterials) — 금화와 함께 든다
            public bool StartBuilt;    // 새 게임에 처음부터 있는 건물 — 건설 목록에 안 나온다(지금은 없음: 빈 숲에서 시작)
            public bool House;         // 여러 채를 지을 수 있는 묶음
            public bool Repeatable;    // 같은 그림을 복제해 몇 채든 짓는 건물(용병 텐트) — 첫 채는 무료(CostOf·MaterialsOf)
            public string Replaces;    // 지으면 사라지는 건물 id(길드 홀 → 길드 천막)
            public bool Decor;         // 장식품(Decorations) — 원본을 복제해 몇 개든 놓고 치울 수 있다
            public string Category;    // 장식품 분류([건설] 창 [장식품]의 정렬 순서)
            public int Charm;          // 마을 매력 점수(VillageCharm — 같은 장식은 VillageCharm.MaxCountedPerKind개까지)
            public int Seats;          // 쉼터 자리 수(벤치·모닥불·분수 — 용병이 와서 쉰다, DecorActivities). 0이면 쉼터 아님
            public bool TrainingDummy; // 훈련 장식(허수아비 — 용병이 와서 친다, DecorActivities)

            /// <summary>장식품 기능 값(매력·쉼터 자리·훈련)을 정해 그대로 돌려준다(목록 줄 끝에 이어 쓴다).</summary>
            public Entry With(int charm, int seats = 0, bool trainingDummy = false)
            {
                Charm = charm;
                Seats = seats;
                TrainingDummy = trainingDummy;
                return this;
            }

            public bool Matches(string objectName) => Prefixes.Any(p => objectName.StartsWith(p, System.StringComparison.Ordinal));
        }

        public const string TentPrefix = "중세 판타지 픽셀 텐트";
        public const string CampPrefix = "갈색 원뿔형 유목민 텐트";
        public const string BarracksPrefix = "중세 군영 천막";
        public const string CottagePrefix = "중세 판타지 픽셀 오두막";
        public const string AidTentPrefix = "붉은 십자 야전 병원 천막";
        public const string StallPrefix = "중세 판타지 약초 상점 텐트";

        /// <summary>용병 텐트 한 채에 묵는 용병 수(InnRest).</summary>
        public const int TentBeds = 3;
        public const string TentId = "barracks";

        /// <summary>없앤 건물의 그림 — Village에 남아 있어도 늘 숨기고, 예전 저장의 그 자리는 버린다(VillageLayoutApplier·VillageLayout).</summary>
        public static readonly string[] RetiredPrefixes = { CampPrefix };

        /// <summary>예전 저장(건물이 처음부터 다 있던 마을)에는 없던 그림 — 그 저장을 불러와도 지은 것으로 치지 않는다.</summary>
        public static readonly string[] OldVillageMissing = { TentPrefix, CampPrefix, BarracksPrefix, CottagePrefix, AidTentPrefix, StallPrefix };

        private static (LootItem, int)[] M(params (LootItem, int)[] materials) => materials;

        public static readonly Entry[] All =
        {
            // 새 게임은 빈 숲에서 시작한다(StartBuilt 건물 없음) — 길드 천막과 첫 용병 텐트는 무료로 바로 세운다(2026-10-26).
            new Entry { Id = "tent", Name = "길드 천막", Description = "창고·시설", Prefixes = new[] { TentPrefix },
                RequiredRank = 0, Cost = 0, Materials = M() },
            // 퀘스트 게시판은 돌빛부터(2026-10-26) — 그 전엔 며칠마다 오는 의뢰 중개인(QuestBroker)에게서만 의뢰를 받는다.
            // 정착민 시스템이 생기면 "정착민이 있을 때" 조건도 붙인다.
            new Entry { Id = "board", Name = "퀘스트 게시판", Description = "의뢰 받기", Prefixes = new[] { "오크 퀘스트 게시판" },
                RequiredRank = 1, Cost = 50, Materials = M((LootCatalog.Log, 2), (LootCatalog.Nail, 3)) },
            // 용병 텐트(2026-10-26, 예전 "간이 훈련소" 자리 — id·그림이 같아 예전 저장의 간이 훈련소는 그대로 첫 텐트가 된다.
            // 예전 "용병 숙소"(갈색 원뿔형 텐트, id camp)는 없앴다): 한 채에 용병 3명이 묵는다(InnRest). 몇 채든 짓고, 첫 채는 무료 · 다음부터 재료.
            new Entry { Id = "barracks", Name = "용병 텐트", Description = $"용병 {TentBeds}명 숙소",
                Prefixes = new[] { BarracksPrefix }, RequiredRank = 0, Cost = 0, Repeatable = true,
                Materials = M((LootCatalog.Cloth, 4), (LootCatalog.Rope, 3), (LootCatalog.Stake, 4), (LootCatalog.IronRing, 2)) },
            // 초반 임시 건물(2026-10-27): 정식 건물(병원·잡화점)이 생겨도 남지만 효과는 꺼진다(Ailments.AidTentActive · StallShop).
            new Entry { Id = "aidtent", Name = "간이 의무 천막", Description = "부상·병 자연 회복 ×2 (병원 전까지)",
                Prefixes = new[] { AidTentPrefix }, RequiredRank = 0, Cost = 80,
                Materials = M((LootCatalog.Cloth, 3), (LootCatalog.Stake, 2), (LootCatalog.Rope, 1)) },
            new Entry { Id = "stall", Name = "행상 좌판", Description = "기초 치료품 (잡화점 전까지, 비쌈)",
                Prefixes = new[] { StallPrefix }, RequiredRank = 0, Cost = 60,
                Materials = M((LootCatalog.Log, 2), (LootCatalog.Cloth, 2)) },
            new Entry { Id = "inn", Name = "여관", Description = "밤 회복 · 파티 구성",
                Prefixes = new[] { "여관" }, RequiredRank = 1, Cost = 300,
                Materials = M((LootCatalog.Log, 6), (LootCatalog.Nail, 6), (LootCatalog.Cloth, 2)) },
            new Entry { Id = "smithy", Name = "대장간", Description = "무기 구입",
                Prefixes = new[] { "아늑한 판타지 마을 대장간" }, RequiredRank = 1, Cost = 400,
                Materials = M((LootCatalog.Log, 4), (LootCatalog.Nail, 6), (LootCatalog.IronRing, 4)) },
            new Entry { Id = "store", Name = "잡화점", Description = "치료 약 구입",
                Prefixes = new[] { "이끼 지붕의 중세 마을 잡화점" }, RequiredRank = 1, Cost = 300,
                Materials = M((LootCatalog.Log, 4), (LootCatalog.Nail, 4), (LootCatalog.Cloth, 3)) },
            new Entry { Id = "training", Name = "훈련소", Description = "능력치 훈련",
                Prefixes = new[] { "중세 마을 훈련소 건물" }, RequiredRank = 2, Cost = 600,
                Materials = M((LootCatalog.Log, 6), (LootCatalog.IronRing, 3), (LootCatalog.Strap, 3), (LootCatalog.Nail, 6)) },
            new Entry { Id = "hospital", Name = "병원", Description = "입원 치료",
                Prefixes = new[] { "십자가가 돋보이는 중세 픽셀 병원" }, RequiredRank = 2, Cost = 600,
                Materials = M((LootCatalog.Log, 4), (LootCatalog.Cloth, 6), (LootCatalog.Thread, 3), (LootCatalog.Needle, 2)) },
            new Entry { Id = "guildhall", Name = "길드 홀", Description = "정식 길드",
                Prefixes = new[] { "쇠락한 모험가 길드 홀" }, RequiredRank = 2, Cost = 800, Replaces = "tent",
                Materials = M((LootCatalog.Log, 10), (LootCatalog.Nail, 10), (LootCatalog.IronRing, 4), (LootCatalog.Cloth, 4)) },
            new Entry { Id = "hotspring", Name = "온천", Description = "피로 회복",
                Prefixes = new[] { "청록 지붕의 포렴 찻집" }, RequiredRank = 3, Cost = 1000,
                Materials = M((LootCatalog.Log, 8), (LootCatalog.Cloth, 4), (LootCatalog.Dye, 2), (LootCatalog.Nail, 6)) },
            new Entry { Id = "casino", Name = "도박장", Description = "사기 회복",
                Prefixes = new[] { "주사위 간판의 뒷골목 도박장" }, RequiredRank = 3, Cost = 1000,
                Materials = M((LootCatalog.Log, 8), (LootCatalog.Leather, 4), (LootCatalog.Wax, 3), (LootCatalog.Nail, 6)) },
            new Entry { Id = "magiclab", Name = "마법 연구소", Description = "마력 영구 상승",
                Prefixes = new[] { "터키석 수정의 마법사 연구소" }, RequiredRank = 4, Cost = 1500,
                Materials = M((LootCatalog.Log, 8), (LootCatalog.Wax, 6), (LootCatalog.Dye, 4), (LootCatalog.IronRing, 4)) },
            new Entry { Id = "cathedral", Name = "성당", Description = "신성력 영구 상승",
                Prefixes = new[] { "장미창과 종탑의 중세 성당" }, RequiredRank = 4, Cost = 1500,
                Materials = M((LootCatalog.Log, 8), (LootCatalog.Wax, 8), (LootCatalog.Cloth, 6), (LootCatalog.IronRing, 4)) },
            new Entry { Id = "house", Name = "주택", Description = "주민 집", House = true, Cost = 150,
                Materials = M((LootCatalog.Log, 4), (LootCatalog.Nail, 4)),
                Prefixes = new[]
                {
                    CottagePrefix,
                    "중세 마을의 소박한 2층 주택", "중세 판타지 마을의 소박한 초가집", "연회색 지붕의 중세 마을집", "회색 지붕의 판타지 마을집",
                    "청록 지붕의 판타지 마을집", "보라 지붕의 판타지 마을집", "붉은 차양의 돌계단 주점", "분홍 벽의 꽃창 2층집",
                    "맥주잔 간판의 뒷골목 선술집", "도르래 달린 낡은 창고", "빈 간판의 낡은 2층집", "판자 덧댄 회색 지붕 오두막",
                    "빨래 걸린 보라 지붕 2층집",
                } },
        };

        // ---------- 장식품 ----------

        /// <summary>장식품 원본(Village 프리팹 "장식 원본" 아래 "장식:id", DecorTemplateBuilder가 만든다)을 복제해 놓는 키의 머리.</summary>
        public const string DecorPrefix = "장식:";

        public const string PropsCategory = "소품";
        public const string TownCategory = "마을 소품";
        public const string FenceCategory = "울타리";
        public const string LivestockCategory = "가축";
        public const string WallCategory = "성벽";

        private static Entry D(string id, string name, string category, int rank, int cost, params (LootItem, int)[] materials) =>
            new Entry { Id = id, Name = name, Category = category, RequiredRank = rank, Cost = cost, Description = "", Materials = materials,
                        Decor = true, Prefixes = new string[0] };

        /// <summary>[건설] 창 [장식품]. 건물(All)과 따로 두어 건물 판정(ForObject·HasBuilding)에 섞이지 않는다.</summary>
        public static readonly Entry[] Decorations =
        {
            D("crate", "나무 상자", PropsCategory, 0, 20, (LootCatalog.Log, 1), (LootCatalog.Nail, 2)).With(charm: 1),
            D("barrel", "술통", PropsCategory, 0, 30, (LootCatalog.Log, 2), (LootCatalog.IronRing, 1)).With(charm: 1),
            D("lanternpost", "랜턴 기둥", PropsCategory, 1, 50, (LootCatalog.Log, 1), (LootCatalog.IronRing, 1), (LootCatalog.Wax, 1)).With(charm: 2),
            D("campfire", "모닥불", PropsCategory, 0, 30, (LootCatalog.Log, 2)).With(charm: 2, seats: 3),
            D("banner", "깃발", PropsCategory, 0, 40, (LootCatalog.Stake, 1), (LootCatalog.Cloth, 1), (LootCatalog.Dye, 1)).With(charm: 2),
            D("sacks", "자루", PropsCategory, 0, 20, (LootCatalog.Cloth, 1), (LootCatalog.Rope, 1)).With(charm: 1),
            D("dummy", "허수아비", PropsCategory, 0, 40, (LootCatalog.Stake, 2), (LootCatalog.Rope, 1), (LootCatalog.Cloth, 1)).With(charm: 2, trainingDummy: true),

            D("fountain", "분수", TownCategory, 3, 300, (LootCatalog.IronRing, 2), (LootCatalog.Nail, 4), (LootCatalog.Log, 2)).With(charm: 8, seats: 3),
            D("lamp", "철등", TownCategory, 2, 60, (LootCatalog.IronRing, 2), (LootCatalog.Wax, 1)).With(charm: 3),
            D("planter", "꽃 화분", TownCategory, 1, 30, (LootCatalog.Log, 1)).With(charm: 2),
            D("bench", "벤치", TownCategory, 1, 40, (LootCatalog.Log, 2), (LootCatalog.Nail, 2)).With(charm: 2, seats: 2),

            D("fence", "울타리", FenceCategory, 0, 15, (LootCatalog.Stake, 1)),
            D("fence_moss", "이끼 낀 울타리", FenceCategory, 0, 15, (LootCatalog.Stake, 1)),
            D("fence_moss2", "덩굴 울타리", FenceCategory, 0, 15, (LootCatalog.Stake, 1)),
            D("fence_tall", "높은 울타리", FenceCategory, 0, 15, (LootCatalog.Stake, 1)),
            D("fence_broken", "부러진 울타리", FenceCategory, 0, 15, (LootCatalog.Stake, 1)),
            D("fence_gate", "울타리 문", FenceCategory, 0, 20, (LootCatalog.Stake, 1), (LootCatalog.IronRing, 1)),
            D("fence_v", "세로 울타리", FenceCategory, 0, 15, (LootCatalog.Stake, 1)),
            D("fence_v_moss", "이끼 낀 세로 울타리", FenceCategory, 0, 15, (LootCatalog.Stake, 1)),
            D("fence_v_broken", "부러진 세로 울타리", FenceCategory, 0, 15, (LootCatalog.Stake, 1)),
            D("trough", "여물통", FenceCategory, 1, 25, (LootCatalog.Log, 1)).With(charm: 1),
            D("hitchpost", "고삐 기둥", FenceCategory, 0, 15, (LootCatalog.Stake, 1), (LootCatalog.Rope, 1)).With(charm: 1),

            D("cow", "소", LivestockCategory, 1, 150, (LootCatalog.Rope, 1), (LootCatalog.Strap, 1)).With(charm: 4),

            D("wall_h", "가로 성벽", WallCategory, 2, 120, (LootCatalog.Log, 2), (LootCatalog.Nail, 4), (LootCatalog.IronRing, 1)).With(charm: 1),
            D("wall_v", "세로 성벽", WallCategory, 2, 80, (LootCatalog.Log, 2), (LootCatalog.Nail, 4), (LootCatalog.IronRing, 1)).With(charm: 1),
            D("wall_corner_l", "왼쪽 성벽 모서리", WallCategory, 2, 100, (LootCatalog.Log, 2), (LootCatalog.Nail, 4), (LootCatalog.IronRing, 1)).With(charm: 1),
            D("wall_corner_r", "오른쪽 성벽 모서리", WallCategory, 2, 100, (LootCatalog.Log, 2), (LootCatalog.Nail, 4), (LootCatalog.IronRing, 1)).With(charm: 1),
            D("wall_tower", "성벽 탑", WallCategory, 3, 250, (LootCatalog.Log, 4), (LootCatalog.Nail, 6), (LootCatalog.IronRing, 2)).With(charm: 3),
            D("wall_gate", "성문", WallCategory, 3, 300, (LootCatalog.Log, 4), (LootCatalog.Nail, 6), (LootCatalog.IronRing, 2)).With(charm: 4),
        };

        public static readonly string[] DecorCategories = { PropsCategory, TownCategory, FenceCategory, LivestockCategory, WallCategory };

        public static Entry GetDecor(string id) => Decorations.FirstOrDefault(e => e.Id == id);

        /// <summary>"장식:id#n" 키 → 장식품(장식 키가 아니면 null).</summary>
        public static Entry DecorForKey(string key)
        {
            if (key == null || !key.StartsWith(DecorPrefix)) return null;
            int hash = key.LastIndexOf('#');
            return GetDecor(hash > DecorPrefix.Length ? key.Substring(DecorPrefix.Length, hash - DecorPrefix.Length) : key.Substring(DecorPrefix.Length));
        }

        public static Entry Get(string id) => All.FirstOrDefault(e => e.Id == id);

        /// <summary>Village 자식 이름 → 건물(건물이 아닌 나무·가로등 등은 null).</summary>
        public static Entry ForObject(string objectName) => All.FirstOrDefault(e => e.Matches(objectName));

        public static IEnumerable<Entry> Buildable => All.Where(e => !e.StartBuilt);

        /// <summary>그 건물을 지금 몇 채 지었나(반복 건물·주택 묶음용).</summary>
        public static int BuiltCount(Entry entry) => VillageLayout.Built.Keys.Count(entry.Matches);

        /// <summary>지금 지을 때 드는 금화 — 반복 건물(용병 텐트)은 첫 채가 무료.</summary>
        public static int CostOf(Entry entry) => entry.Repeatable && BuiltCount(entry) == 0 ? 0 : entry.Cost;

        /// <summary>지금 지을 때 드는 재료 — 반복 건물(용병 텐트)은 첫 채가 무료.</summary>
        public static (LootItem item, int count)[] MaterialsOf(Entry entry) =>
            entry.Repeatable && BuiltCount(entry) == 0 ? M() : entry.Materials;

        /// <summary>지을 수 없으면 이유, 되면 null. hasSpot: 아직 안 지은 그림이 남아 있는가(주택 묶음은 다 지으면 false). 반복 건물은 늘 자리가 있다.</summary>
        public static string Block(Entry entry, bool hasSpot)
        {
            if (!hasSpot && !entry.Decor && !entry.Repeatable) return entry.House ? "더 지을 집이 없습니다." : "이미 지었습니다.";
            // 대신 들어선 건물이 있으면 다시 못 짓는다(길드 홀이 있으면 천막)
            var replacer = All.FirstOrDefault(e => e.Replaces == entry.Id && VillageLayout.HasBuilding(e.Id));
            if (replacer != null) return $"{Josa.IGa(replacer.Name)} 이미 있습니다.";
            if (Guild.Rank < entry.RequiredRank) return $"{Guild.RankName(entry.RequiredRank)} 이상이 되어야 {(entry.Decor ? "놓을" : "지을")} 수 있습니다.";
            int cost = CostOf(entry);
            var materials = MaterialsOf(entry);
            if (Wallet.Gold < cost) return $"금화 {cost}G가 필요합니다.";
            if (!BuildMaterials.Has(materials)) return $"재료가 모자랍니다: {BuildMaterials.MissingText(materials)}";
            return null;
        }
    }
}
