using System.Globalization;
using System.Text.Json;
using ygo_draw.models;

namespace ygo_draw.services;

public static class TypstInputMapper
{
    public static IReadOnlyDictionary<string, string> BuildInputs(CardSummary card)
    {
        var cardKind = Field(card, "cardType") ?? "monster";
        var frameType = Field(card, "frameType") ?? "effect";
        var frameVariant = frameType.EndsWith("-pendulum", StringComparison.OrdinalIgnoreCase)
            ? frameType[..^"-pendulum".Length]
            : frameType;
        var frameFamily = frameType.EndsWith("-pendulum", StringComparison.OrdinalIgnoreCase)
            ? "pendulum"
            : string.Equals(frameType, "link", StringComparison.OrdinalIgnoreCase)
                ? "link"
                : "normal";

        var inputs = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["atk"] = IntField(card, "atk", -1).ToString(CultureInfo.InvariantCulture),
            ["attribute"] = BuildAttribute(card, cardKind),
            ["card_image"] = CardImage(card),
            ["def"] = IntField(card, "def", -1).ToString(CultureInfo.InvariantCulture),
            ["description"] = Field(card, "description") ?? string.Empty,
            ["card_kind"] = cardKind,
            ["frame_family"] = frameFamily,
            ["frame_variant"] = TypstIdentifier(frameVariant),
            ["id"] = IntField(card, "id", 0).ToString(CultureInfo.InvariantCulture),
            ["level"] = IntField(card, "level", 0).ToString(CultureInfo.InvariantCulture),
            ["link_value"] = OptionalIntInput(card, "linkVal"),
            ["name"] = card.Name,
            ["pendulum_description"] = Field(card, "pendulumDescription") ?? string.Empty,
            ["race"] = BuildRaceInput(card, cardKind),
            ["scale"] = OptionalIntInput(card, "scale"),
            ["type_line"] = Field(card, "typeline") ?? string.Empty,
        };

        foreach (var marker in ParseLinkMarkers(card))
        {
            inputs[$"link_marker_{marker}"] = "true";
        }

        return inputs;
    }

    public static string CardImage(CardSummary card)
    {
        return Field(card, "cardImage") ?? card.Id;
    }

    private static IReadOnlyList<string> ParseLinkMarkers(CardSummary card)
    {
        var raw = Field(card, "linkMarkers");
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        var markers = new List<string>();
        using var document = JsonDocument.Parse(raw);
        if (document.RootElement.ValueKind == JsonValueKind.Array)
        {
            markers.AddRange(
                document.RootElement.EnumerateArray()
                    .Select(item => item.GetString())
                    .Where(item => !string.IsNullOrWhiteSpace(item))!);
        }

        return markers
            .Select(marker => marker!.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static string BuildAttribute(CardSummary card, string cardKind)
    {
        if (string.Equals(cardKind, "spell", StringComparison.OrdinalIgnoreCase))
        {
            return "spell";
        }

        if (string.Equals(cardKind, "trap", StringComparison.OrdinalIgnoreCase))
        {
            return "trap";
        }

        return Field(card, "attribute") ?? "earth";
    }

    private static string BuildRaceInput(CardSummary card, string cardKind)
    {
        var race = Field(card, "race");
        if (string.IsNullOrWhiteSpace(race))
        {
            return string.Empty;
        }

        return string.Equals(cardKind, "spell", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(cardKind, "trap", StringComparison.OrdinalIgnoreCase)
            ? race
            : string.Empty;
    }

    private static string? Field(CardSummary card, string key)
    {
        return card.Payload.TryGetValue(key, out var value) ? value : null;
    }

    private static int IntField(CardSummary card, string key, int fallback)
    {
        return int.TryParse(Field(card, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;
    }

    private static string OptionalIntInput(CardSummary card, string key)
    {
        return int.TryParse(Field(card, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value.ToString(CultureInfo.InvariantCulture)
            : string.Empty;
    }

    private static string TypstIdentifier(string value)
    {
        var normalized = value.Replace("-", "_", StringComparison.Ordinal);
        return normalized.All(ch => char.IsLetterOrDigit(ch) || ch == '_') ? normalized : "effect";
    }
}
