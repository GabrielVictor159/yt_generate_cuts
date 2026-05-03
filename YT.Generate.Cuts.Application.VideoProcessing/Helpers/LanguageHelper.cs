using System.Globalization;

namespace YT.Generate.Cuts.Application.VideoProcessing.Helpers;

public static class LanguageHelper
{
    public static string GetEnglishName(string languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
            return string.Empty;

        try
        {
            var neutralCode = languageCode.Split('-')[0];
            var cultureInfo = CultureInfo.GetCultureInfo(neutralCode);

            return cultureInfo.EnglishName;
        }
        catch (CultureNotFoundException)
        {
            return languageCode;
        }
    }
}