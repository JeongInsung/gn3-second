using System;

namespace GN3.Quests
{
    public static class QuestGenerator
    {
        private static readonly string[] EnemyNames =
        {
            "고블린", "오크", "슬라임", "스켈레톤", "도적", "늑대", "코볼트", "트롤"
        };

        public static Quest Generate(int minCount, int maxCount, int minDifficulty, int maxDifficulty, Random rng)
        {
            string enemyName = EnemyNames[rng.Next(EnemyNames.Length)];
            int count = rng.Next(minCount, maxCount + 1);
            int difficulty = rng.Next(minDifficulty, maxDifficulty + 1);
            string title = $"{enemyName} {count}마리 처치";

            return new Quest(title, enemyName, count, difficulty);
        }
    }
}
