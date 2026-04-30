using Npgsql;
using System.Text;
using System.Text.Json;
using ygo_draw.models;

namespace ygo_draw.services;

public sealed class CardCatalogService(DatabaseConnectionSettings connectionSettings)
{
    private readonly List<CardSummary> _cards = [];
    private readonly List<CardSearchEntry> _searchIndex = [];

    public IReadOnlyList<CardSummary> Cards => _cards;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        _cards.Clear();
        _searchIndex.Clear();

        await using var connection = new NpgsqlConnection(connectionSettings.BuildConnectionString());
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(
            "SELECT id, name, payload FROM ygo_cards ORDER BY name, id",
            connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            var id = reader.GetInt64(0).ToString();
            var name = reader.GetString(1);
            var payload = ReadPayload(reader.GetString(2));

            if (!string.IsNullOrWhiteSpace(name))
            {
                var summary = new CardSummary(id, name, payload);
                _cards.Add(summary);
                _searchIndex.Add(new CardSearchEntry(
                    summary,
                    BuildSearchText(payload.Values),
                    NormalizeSearchText(name),
                    NormalizeSearchText(id),
                    NormalizeSearchText(payload.GetValueOrDefault("cardImage") ?? id),
                    NormalizeSearchText(payload.GetValueOrDefault("typeline") ?? string.Empty)));
            }
        }
    }

    public IEnumerable<CardSummary> Search(string query, int limit = 200)
    {
        var normalizedQuery = NormalizeSearchText(query);
        var tokens = BuildQueryTokens(query);
        if (tokens.Count == 0)
        {
            return _cards.Take(limit);
        }

        return _searchIndex
            .Where(entry => tokens.All(token => entry.SearchText.Contains(token, StringComparison.Ordinal)))
            .Select((entry, index) => new SearchResult(entry, index, Score(entry, normalizedQuery, tokens)))
            .OrderByDescending(result => result.Score)
            .ThenBy(result => result.Index)
            .Select(result => result.Entry)
            .Select(entry => entry.Card)
            .Take(limit);
    }

    private static IReadOnlyDictionary<string, string?> ReadPayload(string payloadJson)
    {
        var payload = new Dictionary<string, string?>(StringComparer.Ordinal);
        using var document = JsonDocument.Parse(payloadJson);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            payload[property.Name] = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString(),
                JsonValueKind.Null => null,
                _ => property.Value.GetRawText()
            };
        }

        return payload;
    }

    private static string BuildSearchText(IEnumerable<string?> values)
    {
        var builder = new StringBuilder();
        foreach (var value in values)
        {
            AppendToken(builder, value);
        }

        return NormalizeSearchText(builder.ToString());
    }

    private static void AppendToken(StringBuilder builder, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            builder.Append(' ').Append(value);
        }
    }

    private static IReadOnlyList<string> BuildQueryTokens(string query)
    {
        var normalized = NormalizeSearchText(query);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return [];
        }

        var tokens = new List<string>();
        var current = new StringBuilder();

        foreach (var ch in normalized)
        {
            if (char.IsLetterOrDigit(ch))
            {
                current.Append(ch);
                continue;
            }

            FlushCurrent();
            if (!char.IsWhiteSpace(ch))
            {
                AddToken(ch.ToString());
            }
        }

        FlushCurrent();

        return tokens;

        void FlushCurrent()
        {
            if (current.Length == 0)
            {
                return;
            }

            var segment = current.ToString();
            if (!segment.Any(IsCjk))
            {
                AddToken(segment);
                current.Clear();
                return;
            }

            var latinOrNumber = new StringBuilder();
            foreach (var ch in segment)
            {
                if (IsCjk(ch))
                {
                    FlushLatinOrNumber();
                    AddToken(ch.ToString());
                }
                else
                {
                    latinOrNumber.Append(ch);
                }
            }

            FlushLatinOrNumber();
            current.Clear();

            void FlushLatinOrNumber()
            {
                if (latinOrNumber.Length == 0)
                {
                    return;
                }

                AddToken(latinOrNumber.ToString());
                latinOrNumber.Clear();
            }
        }

        void AddToken(string token)
        {
            if (tokens.Contains(token, StringComparer.Ordinal))
            {
                return;
            }

            tokens.Add(token);
        }
    }

    private static string NormalizeSearchText(string value)
    {
        return value.Trim().Normalize(NormalizationForm.FormKC).ToLowerInvariant();
    }

    private static bool IsCjk(char ch)
    {
        return ch is >= '\u3400' and <= '\u9fff'
            or >= '\uf900' and <= '\ufaff';
    }

    private static int Score(
        CardSearchEntry entry,
        string normalizedQuery,
        IReadOnlyList<string> tokens)
    {
        var score = 0;

        if (!string.IsNullOrWhiteSpace(normalizedQuery))
        {
            if (entry.NameText == normalizedQuery)
            {
                score += 10_000;
            }
            else if (entry.IdText == normalizedQuery || entry.CardImageText == normalizedQuery)
            {
                score += 9_000;
            }

            if (entry.NameText.StartsWith(normalizedQuery, StringComparison.Ordinal))
            {
                score += 4_000;
            }

            if (entry.NameText.Contains(normalizedQuery, StringComparison.Ordinal))
            {
                score += 2_500;
            }

            if (entry.SearchText.Contains(normalizedQuery, StringComparison.Ordinal))
            {
                score += 1_500 + Math.Min(normalizedQuery.Length, 80) * 8;
            }

            if (entry.TypeLineText.Contains(normalizedQuery, StringComparison.Ordinal))
            {
                score += 700;
            }
        }

        foreach (var token in tokens)
        {
            if (entry.NameText == token)
            {
                score += 1_200;
            }
            else if (entry.NameText.Contains(token, StringComparison.Ordinal))
            {
                score += 650;
            }

            if (entry.IdText == token || entry.CardImageText == token)
            {
                score += 1_000;
            }

            if (entry.TypeLineText.Contains(token, StringComparison.Ordinal))
            {
                score += 250;
            }

            if (token.All(char.IsDigit) &&
                NormalizeNoNotation(entry.NameText).Contains($"no.{token}", StringComparison.Ordinal))
            {
                score += 2_000;
            }
        }

        return score;
    }

    private static string NormalizeNoNotation(string value)
    {
        var normalized = NormalizeSearchText(value)
            .Replace("ｎ", "n", StringComparison.Ordinal)
            .Replace("ｏ", "o", StringComparison.Ordinal)
            .Replace("．", ".", StringComparison.Ordinal);

        var builder = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (!char.IsWhiteSpace(ch))
            {
                builder.Append(ch);
            }
        }

        return builder.ToString();
    }

    private sealed record CardSearchEntry(
        CardSummary Card,
        string SearchText,
        string NameText,
        string IdText,
        string CardImageText,
        string TypeLineText);

    private sealed record SearchResult(CardSearchEntry Entry, int Index, int Score);
}
