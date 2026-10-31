using System.Text.RegularExpressions;

namespace RabbitHole.Features;

/// <summary>Conservative format cues, not a verdict on a source's claims or accuracy.</summary>
public static class ArchiveDiscovery
{
    public const int MinimumAgeDays = 180;
    public const long RotationModulus = 2147483647;

    public static bool IsCandidate(string kind, string title, string? excerpt, string? provider = null)
    {
        if (kind is not ("article" or "paper" or "video" or "audio" or "music")) return false;
        if (IsTimeSensitive(title, excerpt)) return false;
        var text = title + " " + (excerpt is { Length: > 600 } ? excerpt[..600] : excerpt);
        // Archive searches already require a topic match and an older submission.
        if (kind is "paper" or "music" || provider == "web-archive") return true;
        return Regex.IsMatch(text, @"\b(how|why|understanding|explained|exploring|explore|learn|guide|tutorial|principles|theory|history|historical|essay|mechanics|technique|experiment|consciousness|philosophy|myth|archaeolog\w*|interview|conversation)\b", RegexOptions.IgnoreCase);
    }

    public static bool IsTimeSensitive(string title, string? excerpt)
    {
        // Inspect only supplied metadata, never personal notes or generated text.
        var text = title + " " + (excerpt is { Length: > 600 } ? excerpt[..600] : excerpt);
        return Regex.IsMatch(text, @"\b(announc(?:e[ds]?|ing|ement[s]?)|launch(?:es|ed|ing)?|introducing|now available|just released|new release|breaking news|this week|this month|today we|upcoming|register now|early bird|limited.time|call for papers)\b", RegexOptions.IgnoreCase) ||
            Regex.IsMatch(title, @"\b(release[s]?|updates?|roundup|news briefs?|newsletter|weekly|monthly|webinar|conference|sale|discount|partnership|appointments?|obituar\w*|retiring|moving to|resuscitating)\b|\b\d+\.\d+(?:\.\d+)?\b", RegexOptions.IgnoreCase);
    }

    public static long RotationKey(string value)
    {
        uint hash = 2166136261;
        foreach (var ch in value) hash = unchecked((hash ^ ch) * 16777619);
        return hash % (RotationModulus - 1) + 1;
    }
}
