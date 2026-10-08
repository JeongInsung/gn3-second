using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GN3.Mercenaries
{
    /// <summary>
    /// 용병이 앓는 부상·질병 한 건. 종류(Def)는 AilmentSO 에셋이고, Progress 0~1이 1이 되면 낫는다
    /// (병원 시간당 5% × hospitalSpeed, 마을에서 저절로 1% × naturalSpeed, 파견 중엔 그대로).
    /// DaysUntreated = 입원하지 않고 넘긴 자정 수(deathAfterDays에 쓴다). 발생·회복 규칙은 Ailments, 입원은 Hospital.
    /// </summary>
    public class Ailment
    {
        public AilmentSO Def { get; }
        public float Progress { get; set; }
        public int DaysUntreated { get; set; }

        public Ailment(AilmentSO def, float progress = 0f, int daysUntreated = 0)
        {
            Def = def;
            Progress = progress;
            DaysUntreated = daysUntreated;
        }
    }

    /// <summary>Resources/Ailments 의 AilmentSO 에셋 목록. 처음 쓸 때 한 번 읽는다(에셋은 Play 중에 바뀌지 않는다).</summary>
    public static class AilmentCatalog
    {
        public const string ResourcesFolder = "Ailments";

        private static List<AilmentSO> _all;

        // Domain Reload가 꺼져 있어도 Play마다 다시 읽는다(에디터에서 에셋을 고친 뒤 바로 반영).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession() => _all = null;

        /// <summary>전체 목록(중한 것 먼저, 그다음 에셋 이름 순 — 발생 판정 순서이기도 하다).</summary>
        public static IReadOnlyList<AilmentSO> All
        {
            get
            {
                if (_all == null)
                {
                    _all = Resources.LoadAll<AilmentSO>(ResourcesFolder)
                        .OrderByDescending(a => a.isSevere).ThenBy(a => a.name).ToList();
                    if (_all.Count == 0)
                        Debug.LogWarning($"[AilmentCatalog] Resources/{ResourcesFolder} 에 부상·질병 에셋이 없어 아무도 다치거나 병들지 않습니다.");
                }
                return _all;
            }
        }

        /// <summary>다음에 쓸 때 다시 읽는다(편집 창에서 에셋을 만들거나 지웠을 때).</summary>
        public static void Reload() => _all = null;

        public static AilmentSO Find(string id) => string.IsNullOrEmpty(id) ? null : All.FirstOrDefault(a => a.Id == id);

        /// <summary>예전 저장(종류를 숫자로 저장하던 때: 0 타박상 · 1 골절 · 2 감기 · 3 열병)을 지금 에셋 이름으로.</summary>
        public static AilmentSO FindLegacy(int kind)
        {
            string[] legacyIds = { "Bruise", "Fracture", "Cold", "Fever" };
            return kind >= 0 && kind < legacyIds.Length ? Find(legacyIds[kind]) : null;
        }
    }
}
