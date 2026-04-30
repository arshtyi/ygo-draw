namespace ygo_draw.models;

public sealed record CardSummary(
    string Id,
    string Name,
    IReadOnlyDictionary<string, string?> Payload)
{
    public string DisplayText => $"{Name}  [{Id}]";
}
