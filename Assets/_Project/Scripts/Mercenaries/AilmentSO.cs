using System;
using System.Collections.Generic;
using UnityEngine;

namespace GN3.Mercenaries
{
    public enum AilmentCategory { Injury, Illness }

    /// <summary>부상·질병이 생길 수 있는 때(여러 개 고를 수 있다).</summary>
    [Flags]
    public enum AilmentTrigger
    {
        None = 0,
        AfterBattle = 1,       // 파견 전투에서 살아남았을 때
        AfterAmbush = 2,       // 이동 중 습격에서 살아남았을 때
        ExpeditionDaily = 4,   // 파견 중 매일 자정
        VillageDaily = 8,      // 마을에 있을 때 매일 자정(입원 중 제외)
    }

    /// <summary>
    /// 부상·질병 한 종류. Project 창 우클릭 → Create → GN3 → 부상·질병 으로 만들어 Assets/_Project/Resources/Ailments 에 두면
    /// 다음 Play부터 게임에 나온다(AilmentCatalog가 읽는다). 저장 파일은 에셋 이름으로 기억하므로, 이미 쓰던 에셋 이름을 바꾸면
    /// 예전 저장의 그 상태이상은 사라진다. 발생·회복 규칙은 Ailments, 입원은 Hospital.
    /// </summary>
    [CreateAssetMenu(menuName = "GN3/부상·질병", fileName = "NewAilment")]
    public class AilmentSO : ScriptableObject
    {
        [Header("기본")]
        public string displayName = "새 상태이상";
        public AilmentCategory category = AilmentCategory.Injury;
        [Tooltip("중하면 파견할 수 없고, 병원에 먼저 입원한다.")]
        public bool isSevere;
        [Tooltip("건강 창의 진행도 막대 색.")]
        public Color color = new Color(0.9f, 0.55f, 0.2f);

        [Header("능력치 감소 (0.25 = -25%)")]
        [Range(0f, 0.9f)] public float attackPenalty;
        [Range(0f, 0.9f)] public float defensePenalty;
        [Range(0f, 0.9f)] public float moveSpeedPenalty;

        [Header("발생")]
        [Tooltip("언제 걸릴 수 있는지. 여러 개를 고를 수 있다.")]
        public AilmentTrigger triggers = AilmentTrigger.AfterBattle | AilmentTrigger.AfterAmbush;
        [Tooltip("한 번 판정할 때 걸릴 확률(전투·습격은 한 번마다, 매일은 하루마다).")]
        [Range(0f, 1f)] public float chance = 0.3f;
        [Tooltip("전투·습격: 남은 체력 비율이 이 값 밑일 때만 걸린다. 1이면 조건 없음.")]
        [Range(0f, 1f)] public float healthBelow = 1f;
        [Tooltip("전투·습격: 체력이 낮게 남을수록 확률이 커진다. chance × (healthBelow − 남은 비율) / healthBelow")]
        public bool scaleWithMissingHealth;
        [Tooltip("매일 판정: 비 오는 날 더하는 확률.")]
        [Range(0f, 1f)] public float rainBonus;
        [Tooltip("매일 판정: 눈 오는 날 더하는 확률.")]
        [Range(0f, 1f)] public float snowBonus;
        [Tooltip("매일 판정: 지친(피로 80↑) 용병에게 더하는 확률.")]
        [Range(0f, 1f)] public float exhaustedBonus;

        [Header("전염")]
        [Tooltip("마을에서 입원하지 않은 환자가, 입원하지 않은 동료 한 명에게 하루에 옮길 확률. 0이면 안 옮는다.")]
        [Range(0f, 1f)] public float contagionPerDay;

        [Header("회복")]
        [Tooltip("병원 회복 속도 배율(1 = 시간당 5%, 0.5 = 2.5%).")]
        [Min(0.01f)] public float hospitalSpeed = 1f;
        [Tooltip("마을에서 저절로 낫는 속도 배율(1 = 시간당 1%).")]
        [Min(0f)] public float naturalSpeed = 1f;
        [Tooltip("켜면 병원에서만 낫는다.")]
        public bool noNaturalRecovery;

        [Header("추가 효과 (매일 자정)")]
        [Min(0)] public int moraleLossPerDay;
        [Tooltip("최대 체력 대비 비율. 이 효과로는 체력이 1 밑으로 내려가지 않는다.")]
        [Range(0f, 1f)] public float healthLossPerDay;
        [Tooltip("입원하지 않은 날이 이만큼 쌓이면 죽는다. 0이면 죽지 않는다.")]
        [Min(0)] public int deathAfterDays;

        public bool IsInjury => category == AilmentCategory.Injury;
        public string Id => name;

        public bool Has(AilmentTrigger trigger) => (triggers & trigger) != 0;

        /// <summary>UI용: "공격 -25% · 방어 -25% · 이동 -10% · 파견 불가 · 전염 · 매일 사기 -3 · 7일 방치하면 사망".</summary>
        public string EffectText()
        {
            var parts = new List<string>();
            if (attackPenalty > 0f) parts.Add($"공격 -{Mathf.RoundToInt(attackPenalty * 100f)}%");
            if (defensePenalty > 0f) parts.Add($"방어 -{Mathf.RoundToInt(defensePenalty * 100f)}%");
            if (moveSpeedPenalty > 0f) parts.Add($"이동 -{Mathf.RoundToInt(moveSpeedPenalty * 100f)}%");
            if (isSevere) parts.Add("파견 불가");
            if (contagionPerDay > 0f) parts.Add("전염");
            if (moraleLossPerDay > 0) parts.Add($"매일 사기 -{moraleLossPerDay}");
            if (healthLossPerDay > 0f) parts.Add($"매일 체력 -{Mathf.RoundToInt(healthLossPerDay * 100f)}%");
            if (noNaturalRecovery) parts.Add("병원에서만 나음");
            if (deathAfterDays > 0) parts.Add($"{deathAfterDays}일 방치하면 사망");
            return parts.Count > 0 ? string.Join(" · ", parts) : "효과 없음";
        }
    }
}
