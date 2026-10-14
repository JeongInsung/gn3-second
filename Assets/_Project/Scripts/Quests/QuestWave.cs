namespace GN3.Quests
{
    /// <summary>퀘스트 한 라운드의 적: 이름·숫자·계층(0=약탈 세력, 1=지형 동물, 2=보스).
    /// 라운드 사이엔 체력이 그대로 이어지고, 앞 라운드를 다 잡아야 다음 라운드 적이 나온다.</summary>
    public class QuestWave
    {
        public string EnemyName { get; }
        public int Count { get; }
        public int Tier { get; }
        public bool IsBoss => Tier >= 2;

        public QuestWave(string enemyName, int count, int tier)
        {
            EnemyName = enemyName;
            Count = System.Math.Max(1, count);
            Tier = System.Math.Clamp(tier, 0, 2);
        }
    }
}
