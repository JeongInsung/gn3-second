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
