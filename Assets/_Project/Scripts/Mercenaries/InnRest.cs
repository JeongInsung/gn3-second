using System.Linq;
using GN3.Quests;
using GN3.UI;
using GN3.World;
using UnityEngine;

namespace GN3.Mercenaries
{
    /// <summary>
    /// 자정(하루가 지날 때) 마을에 있는 용병은 여관에서 자며 최대 체력의 절반만큼 회복한다.
    /// 여관을 아직 안 지었으면: 길드 본부에 묵는 3명(GuildHqLodging)은 여관처럼, 그다음 용병 텐트 한 채에 3명씩(BuildingCatalog.TentBeds)
    /// 그 3/4을, 텐트에도 못 든 사람은 야영해 절반만 회복한다(VillageLayout).
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
            var hqResidents = GuildHqLodging.Residents(); // 여관이 없으면 길드 본부에 3명까지 여관처럼 묵는다
            int tentBeds = hasInn ? 0 : TentBeds();       // 그다음 텐트 자리만큼 텐트에서
            int innHealed = 0, hqHealed = 0, tentHealed = 0, campHealed = 0;
            foreach (var merc in PlayerParty.Instance.Members.Where(m => m.IsAlive && !ExpeditionLog.Instance.IsOnExpedition(m)))
            {
                bool inHq = !hasInn && hqResidents.Contains(merc);
                // 텐트 자리는 다친 사람 몫만이 아니라 묵는 사람 순서대로 찬다(본부 3명 다음 명단 순)
                bool inTent = !hasInn && !inHq && tentBeds > 0;
                if (inTent) tentBeds--;
                int max = merc.CurrentStats.MaxHealth;
                if (merc.CurrentHealth >= max) continue;
                float ratio = hasInn || inHq ? HealRatio : HealRatio * (inTent ? LodgeHealFactor : CampHealFactor);
                merc.SetHealth(merc.CurrentHealth + Mathf.CeilToInt(max * ratio));
                if (hasInn) innHealed++;
                else if (inHq) hqHealed++;
                else if (inTent) tentHealed++;
                else campHealed++;
            }
            if (innHealed > 0) DailyLog.Add($"여관에서 쉬어 {innHealed}명이 체력을 회복했다.");
            if (hqHealed > 0) DailyLog.Add($"길드 본부에서 쉬어 {hqHealed}명이 체력을 회복했다.");
            if (tentHealed > 0) DailyLog.Add($"용병 텐트에서 묵으며 {tentHealed}명이 체력을 회복했다.");
            if (campHealed > 0) DailyLog.Add($"텐트 자리가 모자라 {campHealed}명이 모닥불 곁에서 야영하며 체력을 조금 회복했다.");
        }

        /// <summary>용병 텐트 자리 수(채 수 × BuildingCatalog.TentBeds).</summary>
        public static int TentBeds()
        {
            var tent = BuildingCatalog.Get(BuildingCatalog.TentId);
            return tent == null ? 0 : BuildingCatalog.BuiltCount(tent) * BuildingCatalog.TentBeds;
        }
    }
}
