using Itinero.Navigation.Language;

namespace MusterHubCommand.Api.Routing;

// Itinero's DefaultLanguageReference renders relative-direction tokens as
// unspaced compounds ("slightlyleft", "straighton") -- fine as internal
// keys, not as text a crew reads off a screen while driving. Overrides
// just those known tokens; everything else (the "Go {0} on {1}." style
// templates, roundabout phrasing) falls through to Itinero's own English
// defaults unchanged.
public class HumanizedLanguageReference : ILanguageReference
{
    private static readonly Dictionary<string, string> DirectionWords = new()
    {
        ["sharpleft"] = "sharp left",
        ["sharpright"] = "sharp right",
        ["slightlyleft"] = "slightly left",
        ["slightlyright"] = "slightly right",
        ["straighton"] = "straight on",
        ["turnback"] = "a U-turn",
    };

    private readonly DefaultLanguageReference fallback = new();

    public string this[string key] => DirectionWords.TryGetValue(key, out var humanized) ? humanized : fallback[key];
}
