namespace GN3.Mercenaries
{
    public enum MercenaryClassKind
    {
        Warrior,
        Archer,
        Healer,
        Assassin,
        /// <summary>전투 패시브는 없지만, 파견 이동 중 습격 이벤트를 확률적으로 피하게 해준다(ExpeditionLog 참고).</summary>
        Guide,
    }
}
