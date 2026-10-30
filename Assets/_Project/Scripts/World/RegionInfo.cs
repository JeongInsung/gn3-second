using System;
using System.Collections.Generic;
using System.Linq;
using GN3.Mercenaries;

namespace GN3.World
{
    /// <summary>
    /// 퀘스트 지역 데이터: 몬스터 3계층(약탈 세력·지형 동물·보스)과 장소명.
    /// 동서남북 구분은 없앴다: 퀘스트는 한 지역(Region.West)에서만 나오고 화면에 지역을 보이지 않는다.
    /// 나머지 지역 항목은 예전 저장(북·남·동 퀘스트·파견)을 불러올 때를 위해 같은 내용으로 남겨 둔다.
    /// </summary>
    public static class RegionInfo
    {
        private class Data
        {
            public string DisplayName;
            public string[] RaiderFaction;
            public string[] RaiderLocations;
            public string[] TerrainAnimal;
            public string[] TerrainAnimalLocations;
            public string[] Boss;
            public string[] BossLocations;
            public string[] BossTitles; // 보스 칭호(Boss와 같은 인덱스) — 등장 배너·상단 보스 체력바
        }

        // 전투 그림이 있는 몬스터로만 짠다: 고블린(근접은 검 고블린, 무리의 3명 중 1명은 고블린 궁수)과
        // 늑대(이름에 "늑대"·"울프" → 늑대 그림, BattleMonsterSprite.KindFor). 보스는 혼자 크게 나온다.
        // 이름·장소도 그림에 맞춰 통일: 고블린·고블린 늑대기수(늑대 탄 고블린) / 야생늑대·독버섯·그루터기 엔트(2026-10-26) / 보스 앤트킹(2026-10-19 고블린 족장을 교체한 거목 엔트를 이름만 바꿈, 은월의 숲 Lv5에서만), 장소는 모두 "은월의 숲".
        // 예전 저장의 "고블린 족장" 퀘스트는 이름 그대로 남아 큰 검 고블린 그림으로 나온다.
        // 보스는 "은월의 숲의 주인"이라 보스 퀘스트만 장소가 "은월의 숲 깊은 곳"(제목도 그 장소로)이고, 전투 배경도 숲 한가운데다.
        // 새 몬스터 그림을 넣으면 여기에 그 이름을 더하고 KindFor에 연결한다.
        private static readonly Dictionary<Region, Data> Table = new Dictionary<Region, Data>
        {
            [Region.West] = new Data
            {
                DisplayName = "서쪽",
                RaiderFaction = new[] { "고블린", "고블린 늑대기수" },
                RaiderLocations = new[] { "은월의 숲", "은월의 숲" },
                TerrainAnimal = new[] { "야생늑대", "독버섯", "그루터기 엔트" },
                TerrainAnimalLocations = new[] { "은월의 숲", "은월의 숲", "은월의 숲" },
                Boss = new[] { "앤트킹" },
                BossLocations = new[] { "은월의 숲 깊은 곳" },
                BossTitles = new[] { "은월의 숲의 주인" },
            },
            [Region.North] = new Data
            {
                DisplayName = "북쪽",
                RaiderFaction = new[] { "고블린", "고블린 늑대기수" },
                RaiderLocations = new[] { "은월의 숲", "은월의 숲" },
                TerrainAnimal = new[] { "야생늑대", "독버섯", "그루터기 엔트" },
                TerrainAnimalLocations = new[] { "은월의 숲", "은월의 숲", "은월의 숲" },
                Boss = new[] { "앤트킹" },
                BossLocations = new[] { "은월의 숲 깊은 곳" },
                BossTitles = new[] { "은월의 숲의 주인" },
            },
            [Region.South] = new Data
            {
                DisplayName = "남쪽",
                RaiderFaction = new[] { "고블린", "고블린 늑대기수" },
                RaiderLocations = new[] { "은월의 숲", "은월의 숲" },
                TerrainAnimal = new[] { "야생늑대", "독버섯", "그루터기 엔트" },
                TerrainAnimalLocations = new[] { "은월의 숲", "은월의 숲", "은월의 숲" },
                Boss = new[] { "앤트킹" },
                BossLocations = new[] { "은월의 숲 깊은 곳" },
                BossTitles = new[] { "은월의 숲의 주인" },
            },
            [Region.East] = new Data
            {
                DisplayName = "동쪽",
                RaiderFaction = new[] { "고블린", "고블린 늑대기수" },
                RaiderLocations = new[] { "은월의 숲", "은월의 숲" },
                TerrainAnimal = new[] { "야생늑대", "독버섯", "그루터기 엔트" },
                TerrainAnimalLocations = new[] { "은월의 숲", "은월의 숲", "은월의 숲" },
                Boss = new[] { "앤트킹" },
                BossLocations = new[] { "은월의 숲 깊은 곳" },
                BossTitles = new[] { "은월의 숲의 주인" },
            },
        };

        public static string DisplayName(Region region) => Table[region].DisplayName;

        /// <summary>tier: 0=인간형 약탈 세력, 1=지형 상징 동물, 2=지역 보스급</summary>
        public static string GetMonsterName(Region region, int tier, Random rng)
        {
            string[] pool = MonsterPool(region, tier);
            return pool[rng.Next(pool.Length)];
        }

        /// <summary>혼합 라운드의 보조 적: 같은 지역·계층에서 주력(mainName)이 아닌 이름 하나. 고를 게 없으면 null.</summary>
        public static string GetSupportName(Region region, int tier, string mainName, Random rng)
        {
            var pool = Array.FindAll(MonsterPool(region, tier), n => n != mainName);
            return pool.Length == 0 ? null : pool[rng.Next(pool.Length)];
        }

        /// <summary>몬스터 이름과, 그 지역·계층에 장소명이 준비돼 있으면 같은 인덱스의 장소명을 함께 뽑는다.
        /// 장소명이 아직 없는 지역(북/남/동, 2026-09-16 기준 서쪽만 초안)은 locationName이 null로 나온다.</summary>
        public static void GetEncounter(Region region, int tier, Random rng, out string monsterName, out string locationName)
        {
            string[] monsterPool = MonsterPool(region, tier);
            string[] locationPool = LocationPool(region, tier);

            int index = rng.Next(monsterPool.Length);
            monsterName = monsterPool[index];
            locationName = (locationPool != null && index < locationPool.Length) ? locationPool[index] : null;
        }

        /// <summary>보스 이름 → 칭호("앤트킹" → "은월의 숲의 주인"). 칭호가 없는 이름(예전 저장의 고블린 족장 등)은 null.
        /// 예전 저장의 "거목 엔트"도 같은 보스라 앤트킹 칭호를 쓴다.</summary>
        public static string BossTitle(string bossName)
        {
            if (IsEnt(bossName)) bossName = Table[QuestRegion].Boss[0];
            foreach (var data in Table.Values)
            {
                int i = Array.IndexOf(data.Boss, bossName);
                if (i >= 0 && data.BossTitles != null && i < data.BossTitles.Length) return data.BossTitles[i];
            }
            return null;
        }

        /// <summary>엔트 보스(앤트킹, 예전 저장의 거목 엔트)인지. 그림·뿌리 내려찍기·포효·뿌리박기가 이것으로 붙는다.
        /// 그루터기 엔트(숲 지형 몬스터)는 이름에 "엔트"가 있어도 보스가 아니다(IsStump).</summary>
        public static bool IsEnt(string name) => name != null && (name.Contains("엔트") || name.Contains("앤트")) && !IsStump(name);

        /// <summary>그루터기 엔트(느리고 단단한 숲 근접 몬스터)인지.</summary>
        public static bool IsStump(string name) => name != null && name.Contains("그루터기");

        /// <summary>퀘스트가 나오는 지역. 지역 구분이 없어 하나뿐이다.</summary>
        public static readonly Region QuestRegion = Region.West;

        public static IReadOnlyList<Region> GetUnlockedRegions() => new[] { QuestRegion };

        private static string[] MonsterPool(Region region, int tier) => tier switch
        {
            0 => Table[region].RaiderFaction,
            1 => Table[region].TerrainAnimal,
            _ => Table[region].Boss,
        };

        private static string[] LocationPool(Region region, int tier) => tier switch
        {
            0 => Table[region].RaiderLocations,
            1 => Table[region].TerrainAnimalLocations,
            _ => Table[region].BossLocations,
        };

    }
}
