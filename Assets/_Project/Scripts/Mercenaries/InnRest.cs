using System.Linq;
using GN3.Quests;
using GN3.UI;
using GN3.World;
using UnityEngine;

namespace GN3.Mercenaries
{
    /// <summary>
    /// 자정(하루가 지날 때) 마을에 있는 용병은 여관에서 자며 최대 체력의 절반만큼 회복한다.
    /// 파견 중인 용병은 길 위라 회복하지 않는다(파견 중 회복은 Expedition.Rest).
    /// </summary>
    public static class InnRest
    {
        private const float HealRatio = 0.5f;

        // GameClock.ResetForPlaySession(SubsystemRegistration)이 이벤트를 비운 뒤에 구독한다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Subscribe() => GameClock.OnDayAdvanced += RestOvernight;

        private static void RestOvernight()
        {
            int healed = 0;
            foreach (var merc in PlayerParty.Instance.Members.Where(m => m.IsAlive && !ExpeditionLog.Instance.IsOnExpedition(m)))
            {
                int max = merc.CurrentStats.MaxHealth;
                if (merc.CurrentHealth >= max) continue;
                merc.SetHealth(merc.CurrentHealth + Mathf.CeilToInt(max * HealRatio));
                healed++;
            }
            if (healed > 0) ToastLog.Show($"여관에서 하룻밤 쉬어 체력을 회복했다 ({healed}명)");
        }
    }
}
