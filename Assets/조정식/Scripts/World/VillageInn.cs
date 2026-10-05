namespace GN3.World
{
    /// <summary>
    /// 마을 여관. 용병이 드나드는 문(VillagePartyPresenter)과 여관 클릭 → 파티 구성 패널(MainMenuBootstrapper)이 같은 여관을 쓴다.
    /// 찾기·클릭 가능하게 만들기는 VillageProps가 맡는다.
    /// </summary>
    public static class VillageInn
    {
        public const string SpritePrefix = "여관";

        public static UnityEngine.SpriteRenderer FindRenderer() => VillageProps.FindRenderer(SpritePrefix);

        public static bool IsInn(SelectableBuilding building) => VillageProps.IsProp(building, SpritePrefix);

        public static SelectableBuilding EnsureClickable(UnityEngine.SpriteRenderer inn) => VillageProps.EnsureClickable(inn);
    }
}
