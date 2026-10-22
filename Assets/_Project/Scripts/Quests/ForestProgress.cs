using System;
using UnityEngine;

namespace GN3.Quests
{
    /// <summary>
    /// 은월의 숲 레벨(Lv1~5) 진행. Lv1부터 열려 있고, 지금 열린 최고 레벨 퀘스트를 깨면 다음 레벨이 열린다.
    /// 레벨이 곧 퀘스트 난이도(Quest.Difficulty)이고, 보스 앤트킹은 Lv5에서만 나온다(QuestGenerator.Generate).
    /// QuestClearHistory와 같은 패턴(static + 저장/불러오기).
    /// </summary>
    public static class ForestProgress
    {
        public const int MaxLevel = 5;

        public static int UnlockedLevel { get; private set; } = 1;

        public static event Action OnChanged;

        /// <summary>퀘스트 승리 시 호출(ExpeditionBattle.Conclude). 열린 최고 레벨을 깼으면 다음 레벨을 열고 true.</summary>
        public static bool TryUnlockNext(int clearedLevel)
        {
            if (clearedLevel < UnlockedLevel || UnlockedLevel >= MaxLevel) return false;
            UnlockedLevel++;
            OnChanged?.Invoke();
            return true;
        }

        /// <summary>저장 파일에서 불러올 때. 예전 저장엔 값이 없어(0) Lv1부터.</summary>
        public static void Restore(int saved)
        {
            UnlockedLevel = Math.Clamp(saved, 1, MaxLevel);
            OnChanged?.Invoke();
        }

        // Domain Reload가 꺼져 있어 Play마다 비운다(QuestClearHistory와 같은 사정).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession()
        {
            UnlockedLevel = 1;
            OnChanged = null;
        }
    }
}
