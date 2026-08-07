using System.Globalization;

namespace CodexAccountSwitcher.Windows
{
    internal static class L
    {
        public static bool IsKorean
        {
            get
            {
                return string.Equals(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName,
                    "ko", System.StringComparison.OrdinalIgnoreCase);
            }
        }

        public static string T(string korean, string english)
        {
            return IsKorean ? korean : english;
        }

        public static string F(string korean, string english, params object[] args)
        {
            return string.Format(T(korean, english), args);
        }
    }
}
