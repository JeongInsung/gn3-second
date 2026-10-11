using UnityEngine;

namespace GN3.Mercenaries
{
    [CreateAssetMenu(menuName = "GN3/Mercenary Class", fileName = "NewMercenaryClass")]
    public class MercenaryClassSO : ScriptableObject
    {
        public string ClassName;
        public MercenaryClassKind Kind;

        [Header("Base Stats (성장 전)")]
        public int BaseAttack;
        public int BaseDefense;
        public int BaseHealth;

        [Header("성장 1회에 오르는 양 (어느 능력치가 오를지도 이 값 × 전투력 가중치로 정한다)")]
        public int AttackGrowth;
        public int DefenseGrowth;
        public int HealthGrowth;

        [Header("Move Speed (회피 확률에 영향)")]
        public int BaseMoveSpeed;
        public int MoveSpeedGrowth;
    }
}
