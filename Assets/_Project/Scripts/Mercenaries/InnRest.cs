using System.Linq;
using GN3.Quests;
using GN3.UI;
using GN3.World;
using UnityEngine;

namespace GN3.Mercenaries
{
    /// <summary>
    /// 자정(하루가 지날 때) 마을에 있는 용병은 여관에서 자며 최대 체력의 절반만큼 회복한다.
    /// 여관을 아직 안 지었으면 야영해 그 절반만, 용병 숙소(가죽 천막)가 있으면 3/4을 회복한다(VillageLayout).
    /// 파견 중인 용병은 길 위라 회복하지 않는다(파견 중 회복은 Expedition.Rest).
    /// </summary>
    public static class InnRest
    {
        private const float HealRatio = ConditionTuning.InnHealRatio;
        private const float CampHealFactor = 0.5f;
        private const float LodgeHealFactor = 0.75f;

        // GameClock.ResetForPlaySession(SubsystemRegistration)이 이벤트를 비운 뒤에 구독한다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Subscribe() => GameClock.OnDayAdvanced += RestOvernight;

        private static void RestOvernight()
        {
            bool hasInn = VillageLayout.HasBuilding("inn");
            bool hasLodge = !hasInn && VillageLayout.HasBuilding("camp");
            float ratio = hasInn ? HealRatio : HealRatio * (hasLodge ? LodgeHealFactor : CampHealFactor);
            int healed = 0;
            foreach (var merc in PlayerParty.Instance.Members.Where(m => m.IsAlive && !ExpeditionLog.Instance.IsOnExpedition(m)))
            {
                int max = merc.CurrentStats.MaxHealth;
                if (merc.CurrentHealth >= max) continue;
                merc.SetHealth(merc.CurrentHealth + Mathf.CeilToInt(max * ratio));
                healed++;
            }
            if (healed == 0) return;
            DailyLog.Add(hasInn ? $"여관에서 쉬어 {healed}명이 체력을 회복했다."
                : hasLodge ? $"용병 숙소에서 묵으며 {healed}명이 체력을 회복했다."
                : $"모닥불 곁에서 야영하며 {healed}명이 체력을 조금 회복했다.");
        }
    }
}
