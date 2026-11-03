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

        /// <summary>혼합 라운드의 보조 적(주력과 같은 계층의 다른 무리, 예: 고블린 + 늑대기수). 없으면 null.</summary>
        public string SupportName { get; }
        public int SupportCount { get; }
        public bool HasSupport => SupportName != null && SupportCount > 0;
        /// <summary>주력 + 보조 마릿수.</summary>
        public int TotalCount => Count + (HasSupport ? SupportCount : 0);
        /// <summary>"고블린 ×2" 또는 혼합이면 "고블린 ×2 + 고블린 늑대기수 ×1".</summary>
        public string Label => HasSupport ? $"{EnemyName} ×{Count} + {SupportName} ×{SupportCount}" : $"{EnemyName} ×{Count}";

        public QuestWave(string enemyName, int count, int tier, string supportName = null, int supportCount = 0)
        {
            EnemyName = enemyName;
            Count = System.Math.Max(1, count);
            Tier = System.Math.Clamp(tier, 0, 2);
            bool support = !string.IsNullOrEmpty(supportName) && supportCount > 0;
            SupportName = support ? supportName : null;
            SupportCount = support ? supportCount : 0;
        }
    }
}
