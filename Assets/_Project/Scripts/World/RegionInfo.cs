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
        }

        // 전투 그림이 있는 몬스터로만 짠다: 고블린(근접은 검 고블린, 무리의 3명 중 1명은 고블린 궁수)과
        // 늑대(이름에 "늑대"·"울프" → 늑대 그림, BattleMonsterSprite.KindFor). 보스는 혼자 크게 나온다.
        // 이름·장소도 그림에 맞춰 통일: 고블린·고블린 늑대기수(늑대 탄 고블린) / 야생늑대·독버섯 / 보스 고블린 족장(큰 검 고블린), 장소는 모두 "은월의 숲".
        // 새 몬스터 그림을 넣으면 여기에 그 이름을 더하고 KindFor에 연결한다.
        private static readonly Dictionary<Region, Data> Table = new Dictionary<Region, Data>
        {
            [Region.West] = new Data
            {
                DisplayName = "서쪽",
                RaiderFaction = new[] { "고블린", "고블린 늑대기수" },
                RaiderLocations = new[] { "은월의 숲", "은월의 숲" },
                TerrainAnimal = new[] { "야생늑대", "독버섯" },
                TerrainAnimalLocations = new[] { "은월의 숲", "은월의 숲" },
                Boss = new[] { "고블린 족장" },
                BossLocations = new[] { "은월의 숲" },
            },
            [Region.North] = new Data
            {
                DisplayName = "북쪽",
                RaiderFaction = new[] { "고블린", "고블린 늑대기수" },
                RaiderLocations = new[] { "은월의 숲", "은월의 숲" },
                TerrainAnimal = new[] { "야생늑대", "독버섯" },
                TerrainAnimalLocations = new[] { "은월의 숲", "은월의 숲" },
                Boss = new[] { "고블린 족장" },
                BossLocations = new[] { "은월의 숲" },
            },
            [Region.South] = new Data
            {
                DisplayName = "남쪽",
                RaiderFaction = new[] { "고블린", "고블린 늑대기수" },
                RaiderLocations = new[] { "은월의 숲", "은월의 숲" },
                TerrainAnimal = new[] { "야생늑대", "독버섯" },
                TerrainAnimalLocations = new[] { "은월의 숲", "은월의 숲" },
                Boss = new[] { "고블린 족장" },
                BossLocations = new[] { "은월의 숲" },
            },
            [Region.East] = new Data
            {
                DisplayName = "동쪽",
                RaiderFaction = new[] { "고블린", "고블린 늑대기수" },
                RaiderLocations = new[] { "은월의 숲", "은월의 숲" },
                TerrainAnimal = new[] { "야생늑대", "독버섯" },
                TerrainAnimalLocations = new[] { "은월의 숲", "은월의 숲" },
                Boss = new[] { "고블린 족장" },
                BossLocations = new[] { "은월의 숲" },
            },
        };

        public static string DisplayName(Region region) => Table[region].DisplayName;

        /// <summary>tier: 0=인간형 약탈 세력, 1=지형 상징 동물, 2=지역 보스급</summary>
        public static string GetMonsterName(Region region, int tier, Random rng)
        {
            string[] pool = MonsterPool(region, tier);
            return pool[rng.Next(pool.Length)];
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
