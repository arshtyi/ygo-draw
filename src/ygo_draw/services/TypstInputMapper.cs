using ygo_draw.models;

namespace ygo_draw.services;

public static class TypstInputMapper
{
    public static IReadOnlyDictionary<string, string> BuildInputs(CardSummary card)
    {
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["series"] = card.Series,
            ["id"] = card.Id
        };
    }

    public static string CardImage(CardSummary card)
    {
        return card.ImageId;
    }
}
