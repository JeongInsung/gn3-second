using System.Collections.Generic;
using System.Linq;
using GN3.Quests;
using GN3.World;

namespace GN3.Mercenaries
{
    /// <summary>
    /// 길드 본부(길드 천막, 지으면 길드 홀)에 묵는 용병: 여관이 없을 때 마을에 있는 파티원 중 파티 순서대로 Capacity명.
    /// 이들은 여관처럼 밤에 푹 쉬고(InnRest) 마을에서 본부 문으로 드나든다(VillagePartyPresenter). 여관이 생기면 모두 여관에 묵는다.
    /// </summary>
    public static class GuildHqLodging
    {
        public const int Capacity = 3;

        public static bool HasInn => VillageLayout.HasBuilding("inn");
        public static bool HasHq => VillageLayout.HasBuilding("tent") || VillageLayout.HasBuilding("guildhall");

        /// <summary>지금 본부에 묵는 사람(여관이 있거나 본부가 없으면 없음).</summary>
        public static List<Mercenary> Residents()
        {
            if (HasInn || !HasHq) return new List<Mercenary>();
            return PlayerParty.Instance.Members
                .Where(m => m.IsAlive && !ExpeditionLog.Instance.IsOnExpedition(m))
                .Take(Capacity).ToList();
        }

        public static bool IsResident(Mercenary merc) => merc != null && Residents().Contains(merc);

        public static bool IsResident(string mercId) => mercId != null && Residents().Any(m => m.Id == mercId);
    }
}
