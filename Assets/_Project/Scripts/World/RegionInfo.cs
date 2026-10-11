using System;
using System.Collections.Generic;
using System.Linq;
using GN3.Mercenaries;

namespace GN3.World
{
    /// <summary>지역별 표시 이름 / 여는 길드 단계 / 몬스터 3계층(약탈 세력·지형 동물·보스)과 장소명 데이터.</summary>
    public static class RegionInfo
    {
        private class Data
        {
            public string DisplayName;
            public int UnlockRank; // 이 길드 단계(Guild.Rank, 0=견습)부터 퀘스트가 나온다
            public string[] RaiderFaction;
            public string[] RaiderLocations;
            public string[] TerrainAnimal;
            public string[] TerrainAnimalLocations;
            public string[] Boss;
            public string[] BossLocations;
        }

        private static readonly Dictionary<Region, Data> Table = new Dictionary<Region, Data>
        {
            [Region.West] = new Data
            {
                DisplayName = "서쪽",
                UnlockRank = 0,
                RaiderFaction = new[] { "숲의 도적단", "엘프 은둔자" },
                RaiderLocations = new[] { "초승달 숲", "은빛 나무 마을" },
                TerrainAnimal = new[] { "다이어울프" },
                TerrainAnimalLocations = new[] { "안개 협곡" },
                Boss = new[] { "트렌트" },
                BossLocations = new[] { "천년 거목" },
            },
            [Region.North] = new Data
            {
                DisplayName = "북쪽",
                UnlockRank = 2, // 은빛 길드
                RaiderFaction = new[] { "눈보라 도적단", "빙하 부족 전사" },
                TerrainAnimal = new[] { "얼음늑대" },
                Boss = new[] { "설인" },
            },
            [Region.South] = new Data
            {
                DisplayName = "남쪽",
                UnlockRank = 2, // 은빛 길드
                RaiderFaction = new[] { "해적단" },
                TerrainAnimal = new[] { "대형 갑각류", "전기가오리" },
                Boss = new[] { "크라켄" },
            },
            [Region.East] = new Data
            {
                DisplayName = "동쪽",
                UnlockRank = 2, // 은빛 길드
                RaiderFaction = new[] { "사막 도적단", "자칼전사" },
                TerrainAnimal = new[] { "사막전갈" },
                Boss = new[] { "모래벌레" },
            },
        };

        public static string DisplayName(Region region) => Table[region].DisplayName;

        public static int UnlockRank(Region region) => Table[region].UnlockRank;

        /// <summary>길드 단계 해금 문구용: 이 단계에서 새로 열리는 지역(" · 새 지역: 북쪽, 남쪽, 동쪽"), 없으면 빈 문자열.</summary>
        public static string OpenedAtText(int rank)
        {
            var opened = Table.Where(p => p.Value.UnlockRank == rank && rank > 0).Select(p => p.Value.DisplayName).ToList();
            return opened.Count > 0 ? $" · 새 지역: {string.Join(", ", opened)}" : "";
        }

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

        public static bool IsUnlocked(Region region)
        {
            return GN3.Economy.Guild.Rank >= UnlockRank(region);
        }

        public static IReadOnlyList<Region> GetUnlockedRegions()
        {
            return Enum.GetValues(typeof(Region)).Cast<Region>().Where(IsUnlocked).ToList();
        }

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
