using System.Text.Json;
using System.Text.RegularExpressions;
using RabbitHole.Domain;

namespace RabbitHole.Features;

public sealed record ConceptDefinition(string Id, string Label, string Family, string[] Phrases, string[] Adjacent);
public sealed record ConceptMatch(string Id, string Label, string Field, string Match);

/// <summary>A small vocabulary of subject phrases, not a credibility model or generated ontology.
/// Edges suggest directions to explore; they assert no agreement between the sources.</summary>
public static class Concepts
{
    public static readonly IReadOnlyList<ConceptDefinition> Catalog = [
        new("global-workspace", "Global Workspace Theory", "mind", ["global workspace", "global neuronal workspace", "neural broadcasting"], ["iit", "ai-consciousness", "phenomenal-consciousness", "altered-states"]),
        new("iit", "Integrated Information Theory", "mind", ["integrated information theory", "integrated information"], ["global-workspace", "phenomenal-consciousness", "panpsychism"]),
        new("ai-consciousness", "AI consciousness", "mind", ["ai consciousness", "machine consciousness", "artificial consciousness", "consciousness in artificial intelligence", "conscious language models", "language model introspection", "introspective awareness"], ["global-workspace", "phenomenal-consciousness", "world-models"]),
        new("phenomenal-consciousness", "Qualia and phenomenal consciousness", "mind", ["qualia", "phenomenal consciousness", "phenomenal experience", "hard problem of consciousness"], ["iit", "global-workspace", "panpsychism", "idealism", "near-death"]),
        new("panpsychism", "Panpsychism", "mind", ["panpsychism", "panpsychist"], ["idealism", "phenomenal-consciousness", "iit"]),
        new("idealism", "Idealism", "mind", ["analytic idealism", "analytical idealism", "metaphysical idealism", "idealism"], ["panpsychism", "phenomenal-consciousness", "near-death"]),
        new("altered-states", "Altered states of consciousness", "mind", ["altered states", "psychedelic", "psychedelics", "meditative states", "ashtanga yoga"], ["global-workspace", "phenomenal-consciousness", "near-death", "remote-viewing"]),
        new("robotics", "Robotics", "robotics", ["robotics", "robot", "robots", "robotic"], ["robot-manipulation", "embodied-learning", "world-models"]),
        new("robot-manipulation", "Robot manipulation", "robotics", ["robot manipulation", "robotic manipulation", "dexterous manipulation", "tactile guided", "robot grasping", "tactile sensing"], ["robotics", "embodied-learning", "world-models"]),
        new("embodied-learning", "Embodied learning", "robotics", ["embodied learning", "embodied intelligence", "embodied ai", "robot learning", "robotic learning", "visuomotor", "vision language action"], ["robotics", "robot-manipulation", "world-models"]),
        new("world-models", "World models", "robotics", ["world model", "world models", "world action model", "world action models"], ["robotics", "embodied-learning", "robot-manipulation", "ai-consciousness"]),
        new("younger-dryas", "Younger Dryas research", "history-earth", ["younger dryas"], ["flood-traditions", "flood-geology"]),
        new("flood-traditions", "Flood traditions", "history-earth", ["flood myth", "flood myths", "flood story", "flood traditions", "flood narrative", "deluge myth", "gilgamesh"], ["flood-geology", "younger-dryas"]),
        new("flood-geology", "Flood geology and paleoclimate", "history-earth", ["flood geology", "postglacial flood", "megaflood", "paleoflood", "palaeoflood"], ["flood-traditions", "younger-dryas"]),
        new("ufo-history", "Historical UFO investigations", "uap", ["project blue book", "project sign", "project grudge", "ufo investigation", "ufo investigations", "unidentified flying objects"], ["uap-records", "uap-reports"]),
        new("uap-records", "UAP documents and FOIA", "uap", ["uap emails", "uap documents", "ufo documents", "ufo files", "aatip", "aawsap", "uap records"], ["ufo-history", "uap-reports"]),
        new("uap-reports", "UFO and UAP reports", "uap", ["ufo", "ufos", "uap", "unidentified aerial", "unidentified anomalous"], ["ufo-history", "uap-records"]),
        new("remote-viewing", "Remote viewing", "anomalous-experience", ["remote viewing", "remote viewer", "remote viewers"], ["psi-research", "altered-states"]),
        new("psi-research", "Psi experiments", "anomalous-experience", ["parapsychology", "parapsychological", "psi research", "psi experiment", "extrasensory perception", "telepathy", "precognition", "precognitive"], ["remote-viewing", "near-death", "reincarnation"]),
        new("out-of-body", "Out-of-body experiences", "anomalous-experience", ["out of body"], ["near-death", "remote-viewing", "phenomenal-consciousness"]),
        new("near-death", "Near-death experiences", "anomalous-experience", ["near death experience", "near death experiences", "near death experiencers"], ["phenomenal-consciousness", "reincarnation", "psi-research", "altered-states"]),
        new("reincarnation", "Reincarnation", "anomalous-experience", ["reincarnation", "past life memories", "past lives"], ["near-death", "psi-research", "idealism"]),
        new("digital-filters", "Digital filters", "audio", ["digital filter", "digital filters", "filter coefficients", "frequency response", "lowpass", "low pass filter", "highpass", "bandpass"], ["biquad", "audio-sampling", "sound-design"]),
        new("biquad", "Biquad filters", "audio", ["biquad", "biquads"], ["digital-filters", "audio-sampling", "sound-design"]),
        new("audio-sampling", "Audio sampling and peaks", "audio", ["inter sample peaks", "intersample peaks", "sample rate", "sample rates", "audio sampling", "aliasing", "nyquist"], ["digital-filters", "biquad", "audio-modulation"]),
        new("audio-modulation", "Audio modulation", "audio", ["amplitude modulation", "frequency modulation", "ring modulation", "am widget", "fm synthesis"], ["digital-filters", "audio-sampling", "sound-design", "riddim"]),
        new("sound-design", "Sound design", "audio", ["sound design", "synthesizer", "synthesizers", "synthesiser", "synthesisers", "wavetable", "synth patch", "reese bass", "drone synthesis"], ["digital-filters", "biquad", "audio-modulation", "riddim"]),
        new("riddim", "Riddim production", "audio", ["riddim", "dubstep production", "140 bpm"], ["sound-design", "audio-modulation"])
    ];
    private static readonly Dictionary<string, ConceptDefinition> ById = Catalog.ToDictionary(x => x.Id);
    private static readonly Dictionary<string, string> Needles = Catalog.SelectMany(c => c.Phrases).Append("consciousness").Distinct()
        .ToDictionary(phrase => phrase, phrase => " " + Normalize(phrase) + " ");
    private static readonly Dictionary<string, Regex> BodyPatterns = Catalog.ToDictionary(c => c.Id, c => new Regex(
        @"(?<![\p{L}\p{N}])(?:" + string.Join('|', c.Phrases.Distinct().OrderByDescending(p => p.Length).Select(p => Regex.Escape(Normalize(p)))) + @")(?![\p{L}\p{N}])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)));
    public static string Normalize(string text) => Regex.Replace(text.ToLowerInvariant(), @"[^\p{L}\p{N}]+", " ").Trim();
    private static bool Contains(string text, string phrase) => text.Contains(Needles[phrase], StringComparison.Ordinal);
    public static string TagId(string text) => Catalog.FirstOrDefault(c => c.Id.Equals(text.Trim(), StringComparison.OrdinalIgnoreCase) || Normalize(c.Label) == Normalize(text))?.Id ?? Normalize(text);
    public static string Label(string id) => ById.GetValueOrDefault(id)?.Label ?? id;
    public static string? Family(string id) => ById.GetValueOrDefault(id)?.Family;
    public static bool Adjacent(string a, string b) => a != b && (ById.GetValueOrDefault(a)?.Adjacent.Contains(b) == true || ById.GetValueOrDefault(b)?.Adjacent.Contains(a) == true);
    public static int EdgeOrder(string a, string b) => ById.TryGetValue(a, out var definition) && Array.IndexOf(definition.Adjacent, b) is var index && index >= 0 ? index : 20;
    public static List<ConceptMatch> Focus(List<ConceptMatch> matches)
    {
        var prominent = matches.Where(c => c.Field is "title" or "user-tag" or "excerpt" or "metadata").ToList();
        return prominent.Count > 0 ? prominent : matches;
    }

    public static List<ConceptMatch> Match(ContentItem item)
    {
        var fields = new[] { ("title", item.Title), ("excerpt", item.Excerpt ?? ""), ("metadata", SubjectMetadata(item.MetadataJson)), ("body", (item.Body ?? "")[..Math.Min(12000, item.Body?.Length ?? 0)]) }
            .Select(x => (x.Item1, Text: " " + Normalize(x.Item2) + " ")).ToList();
        var matches = new List<ConceptMatch>();
        foreach (var definition in Catalog)
        {
            if (item.ExcludedConcepts.Contains(definition.Id)) continue;
            if (item.ConceptTags.Contains(definition.Id)) { matches.Add(new(definition.Id, definition.Label, "user-tag", definition.Label)); continue; }
            foreach (var (field, text) in fields)
            {
                var phrase = definition.Phrases.OrderByDescending(p => p.Length).FirstOrDefault(p => Contains(text, p));
                // Integrated information in a business title alone is not the theory.
                if (definition.Id == "iit" && phrase == "integrated information" && !fields.Any(f => Contains(f.Text, "consciousness") || Contains(f.Text, "integrated information theory"))) continue;
                if (phrase is null) continue;
                // A passing mention buried in a long article is not its subject.
                if (field == "body" && text.Length > 600 && BodyPatterns[definition.Id].Matches(text).Count < 2) continue;
                matches.Add(new(definition.Id, definition.Label, field, phrase));
                break;
            }
        }
        foreach (var tag in item.ConceptTags.Where(t => !ById.ContainsKey(t) && !item.ExcludedConcepts.Contains(t)))
            matches.Add(new(tag, tag, "user-tag", tag));
        return matches;
    }

    private static string SubjectMetadata(string? json)
    {
        if (json is null) return "";
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return "";
            var parts = new List<string>();
            void Add(JsonElement value, int depth = 0)
            {
                if (depth > 3 || parts.Count >= 100) return;
                if (value.ValueKind == JsonValueKind.String) parts.Add(value.GetString() ?? "");
                else if (value.ValueKind == JsonValueKind.Array) foreach (var child in value.EnumerateArray().Take(50)) Add(child, depth + 1);
                else if (value.ValueKind == JsonValueKind.Object) foreach (var property in value.EnumerateObject().Where(p => p.Name is "name" or "display_name" or "text")) Add(property.Value, depth + 1);
            }
            foreach (var property in doc.RootElement.EnumerateObject().Where(p => p.Name is "keywords" or "subjects" or "concepts" or "tags" or "abstract")) Add(property.Value);
            var text = string.Join(" ", parts);
            return text[..Math.Min(text.Length, 6000)];
        }
        catch (JsonException) { return ""; }
    }
}
