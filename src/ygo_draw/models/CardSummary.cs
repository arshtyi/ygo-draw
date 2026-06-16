namespace ygo_draw.models;

public sealed record CardSummary(
    string Series,
    string Id,
    string Name,
    string ImageId,
    IReadOnlyDictionary<string, string?> Payload)
{
    public string SeriesLabel => Series == "rd" ? "Rush Duel" : "OCG/TCG";
    public string DisplayText => $"{Name}  [{SeriesLabel} {Id}]";
}
