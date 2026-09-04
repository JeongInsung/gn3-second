using System;

namespace GN3.Mercenaries
{
    public static class MercenaryNamePool
    {
        private static readonly string[] Names =
        {
            "카이런", "브렌", "이바", "로한", "셀린", "다르우스", "미라", "가록",
            "아엘라", "토르빈", "네사", "울프릭", "리아나", "그림", "세라핀", "발두르"
        };

        public static string GetRandom(Random rng)
        {
            return Names[rng.Next(Names.Length)];
        }
    }
}
