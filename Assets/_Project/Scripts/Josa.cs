namespace GN3
{
    /// <summary>
    /// 받침 유무로 조사를 붙인다: Josa.IGa("지우") = "지우가", Josa.IGa("샬럿") = "샬럿이".
    /// 마지막 글자가 한글이 아니면(숫자·영문) 예전처럼 "이(가)"로 둘 다 적는다.
    /// </summary>
    public static class Josa
    {
        public static string IGa(string word) => Attach(word, "이", "가");
        public static string EunNeun(string word) => Attach(word, "은", "는");
        public static string EulReul(string word) => Attach(word, "을", "를");
        public static string WaGwa(string word) => Attach(word, "과", "와");

        private static string Attach(string word, string withFinal, string withoutFinal)
        {
            if (string.IsNullOrEmpty(word)) return word;
            char last = word[word.Length - 1];
            if (last < 0xAC00 || last > 0xD7A3) return $"{word}{withFinal}({withoutFinal})";
            bool hasFinal = (last - 0xAC00) % 28 != 0;
            return word + (hasFinal ? withFinal : withoutFinal);
        }
    }
}
