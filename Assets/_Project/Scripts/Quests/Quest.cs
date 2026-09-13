using System;

namespace GN3.Quests
{
    public class Quest
    {
        public string Id { get; }
        public string Title { get; }
        public string EnemyName { get; }
        public int EnemyCount { get; }
        public int Difficulty { get; }

        /// <summary>난이도/처치 수에 비례한 예상 임무 완료 시간(초). 전투 연출 재생 시간으로 사용된다.</summary>
        public float EstimatedDurationSeconds => 3f + Difficulty * 1.5f + EnemyCount * 0.5f;

        public Quest(string title, string enemyName, int enemyCount, int difficulty)
        {
            Id = Guid.NewGuid().ToString();
            Title = title;
            EnemyName = enemyName;
            EnemyCount = enemyCount;
            Difficulty = difficulty;
        }
    }
}
