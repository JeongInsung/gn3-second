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
        /// <summary>공격이 높고 몸이 약하다. 마법 연구소(RestVenues.MagicLab)에서 연구해 공격을 영구히 올릴 수 있다.</summary>
        Mage,
    }
}
