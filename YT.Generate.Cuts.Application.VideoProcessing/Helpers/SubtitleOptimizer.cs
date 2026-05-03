using System.Text;
using System.Text.RegularExpressions;

namespace YT.Generate.Cuts.Application.VideoProcessing.Helpers;

public static class SubtitleOptimizer
{
    public static string CleanSrt(string rawSrt)
    {
        if (string.IsNullOrWhiteSpace(rawSrt)) return string.Empty;

        var lines = rawSrt.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        var sb = new StringBuilder();

        string currentTimestamp = "";
        var currentText = new StringBuilder();

        foreach (var line in lines)
        {
            if (line.Contains("-->"))
            {
                currentTimestamp = line.Trim();
            }
            else if (!int.TryParse(line, out _))
            {
                currentText.Append(line.Trim() + " ");
            }

            if (!string.IsNullOrWhiteSpace(currentText.ToString()) && !string.IsNullOrEmpty(currentTimestamp))
            {
                var cleanText = Regex.Replace(currentText.ToString().Trim(), @"\s+", " ");

                if (!string.IsNullOrEmpty(cleanText))
                {
                    sb.AppendLine($"{currentTimestamp} {cleanText}");
                }

                currentText.Clear();
                currentTimestamp = "";
            }
        }

        return sb.ToString();
    }
}
