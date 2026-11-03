using System;
using System.Collections.Generic;
using GN3.World;

namespace GN3.Quests
{
    public static class QuestGenerator
    {
        /// <summary>은월의 숲 Lv(1~5) 퀘스트. 레벨이 곧 난이도라 적 능력치·권장 전투력·보상이 레벨을 따르고,
        /// 레벨마다 라운드 수·적 구성·일수가 다르다(RollWaves). 보스 앤트킹은 Lv5 마지막 라운드에만 나온다.</summary>
        public static Quest Generate(Region region, int level, int minCount, int maxCount, Random rng)
        {
            level = Math.Clamp(level, 1, ForestProgress.MaxLevel);
            int mainTier = MainTier(level, rng);
            RegionInfo.GetEncounter(region, mainTier, rng, out string enemyName, out string locationName);
            var waves = RollWaves(region, level, mainTier, enemyName, minCount, maxCount, rng);
            int durationDays = RollDurationDays(level, rng);

            // 마을 사람의 부탁: 같은 적이라도 5가지 글 중 하나(QuestRequests). 장소는 정보창 "장소:" 줄에 나온다.
            var request = QuestRequests.Pick(enemyName, rng);
            int count = waves[waves.Count - 1].Count;
            string title = $"{request.Requester}의 부탁 — {enemyName} 토벌";

            var quest = new Quest(title, region, locationName, enemyName, count, level, durationDays, waves, request.Requester, request.Text);
            AddMaterialReward(quest, rng);
            return quest;
        }

        private const double MaterialRewardChance = 0.35;

        /// <summary>
        /// 일부 의뢰(MaterialRewardChance)는 부탁한 마을 사람이 금화와 함께 건설 재료를 준다: 그 의뢰 적이 떨어뜨리는 재료 중
        /// 한두 가지를 레벨만큼(Lv1 2개 ~ Lv5 6개) 나눠서.
        /// </summary>
        private static void AddMaterialReward(Quest quest, Random rng)
        {
            if (rng.NextDouble() >= MaterialRewardChance) return;
            var pool = new List<GN3.Economy.LootItem>(quest.DroppedMaterials);
            if (pool.Count == 0) return;
            int total = quest.Difficulty + 1;
            int kinds = Math.Min(pool.Count, total >= 4 ? 2 : 1);
            for (int k = 0; k < kinds; k++)
            {
                var item = pool[rng.Next(pool.Count)];
                pool.Remove(item);
                int count = k == kinds - 1 ? total : rng.Next(1, total);
                total -= count;
                quest.MaterialReward.Add((item, count));
            }
        }

        /// <summary>대표 적(마지막 라운드)의 계층: Lv1 약탈 세력, Lv2 약탈 세력 또는 지형 동물, Lv3·4 지형 동물, Lv5 보스.</summary>
        private static int MainTier(int level, Random rng) => level switch
        {
            1 => 0,
            2 => rng.Next(0, 2),
            5 => 2,
            _ => 1,
        };

        private const double MixedWaveChance = 0.4;

        /// <summary>
        /// 라운드 구성. 마지막 라운드가 퀘스트의 대표 적이고, 앞 라운드는 그보다 낮거나 같은 계층의 다른 무리.
        /// Lv1·2 1라운드(초반 소규모: Lv1 1~2마리 · Lv2 2~3마리) · Lv3 2~3라운드 · Lv4 3라운드(약탈 → 지형 동물 → 지형 동물) · Lv5 3라운드(약탈 → 지형 동물 → 앤트킹 단독).
        /// 한 라운드짜리는 minCount~maxCount마리, 여러 라운드면 라운드마다 2~3마리(Lv4 마지막·Lv5 앞 라운드는 3~4).
        /// </summary>
        private static List<QuestWave> RollWaves(Region region, int level, int mainTier, string mainEnemy, int minCount, int maxCount, Random rng)
        {
            // 초반 소규모 의뢰(Quest.EarlyMaxDispatch): Lv1 혼자 — 적 1마리(70%)·2마리, Lv2 둘이 — 적 2마리(60%)·3마리. 한 라운드.
            if (level <= 2)
            {
                int count = level <= 1 ? (rng.NextDouble() < 0.7 ? 1 : 2) : (rng.NextDouble() < 0.6 ? 2 : 3);
                return new List<QuestWave> { new QuestWave(mainEnemy, count, mainTier) };
            }

            int waveCount = level switch
            {
                1 => 1,
                2 => rng.Next(1, 3),
                3 => rng.Next(2, 4),
                _ => 3,
            };

            var waves = new List<QuestWave>(waveCount);
            for (int i = 0; i < waveCount; i++)
            {
                bool last = i == waveCount - 1;
                int waveTier = last ? mainTier : level >= 4 ? Math.Min(i, 1) : rng.Next(0, mainTier + 1);
                string name = last ? mainEnemy : RegionInfo.GetMonsterName(region, waveTier, rng);
                int count = waveTier == 2 ? 1 // 보스는 단독 등장
                    : waveCount == 1 ? rng.Next(minCount, (level == 1 ? Math.Min(maxCount, 4) : maxCount) + 1)
                    : (last && level == 4) || level >= 5 ? rng.Next(3, 5) // Lv5는 6명(두 분대)이 가니 앞 라운드도 3~4마리
                    : rng.Next(2, Math.Max(2, Math.Min(maxCount, 3)) + 1);
                // 혼합 라운드: 보스가 아닌 3마리 이상 라운드의 MixedWaveChance는 같은 계층의 다른 무리가 1~2마리 섞인다(총 수는 그대로).
                string support = waveTier < 2 && count >= 3 && rng.NextDouble() < MixedWaveChance
                    ? RegionInfo.GetSupportName(region, waveTier, name, rng) : null;
                int supportCount = support == null ? 0 : count >= 4 ? rng.Next(1, 3) : 1;
                waves.Add(new QuestWave(name, count - supportCount, waveTier, support, supportCount));
            }
            return waves;
        }

        private static int RollDurationDays(int level, Random rng)
        {
            (int min, int max) = level switch
            {
                1 => (1, 2),
                2 => (1, 3),
                3 => (2, 3),
                4 => (3, 4),
                _ => (3, 5),
            };
            return rng.Next(min, max + 1);
        }
    }
}
