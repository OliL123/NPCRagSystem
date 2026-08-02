using System.Text.Json;

namespace NPCRAGSystem.Utils;

// Helpers for pulling JSON out of an LLM reply. Small models wrap JSON in preamble/markdown
// fences, append trailing prose, or truncate the final object mid-array — every structured
// call has to cope with that. This centralises the handling so the call sites don't each
// re-implement it. (Reasoning is suppressed at the source via OllamaLlmService's think:false,
// so no <think> stripping is needed here.)
public static class LlmJson
{
    private static readonly char[] Open  = { '{', '[' };
    private static readonly char[] Close = { '}', ']' };

    // Trim everything outside the JSON value: from the first '{'/'[' to the last '}'/']'.
    // Handles preamble, markdown fences and trailing chatter. Falls back to the trimmed
    // input if no brackets are present.
    public static string Extract(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return string.Empty;
        int start = raw.IndexOfAny(Open);
        if (start < 0) return raw.Trim();
        int end = raw.LastIndexOfAny(Close);
        return end > start ? raw[start..(end + 1)].Trim() : raw[start..].Trim();
    }

    // Repair a truncated JSON array of objects (a common small-model failure): keep through
    // the last complete '}', then wrap as an array. Returns null if there's no complete
    // object to salvage.
    public static string? RepairTruncatedArray(string extracted)
    {
        int lastClose = extracted.LastIndexOf('}');
        if (lastClose < 0) return null;
        var partial = extracted[..(lastClose + 1)];
        return partial.TrimStart().StartsWith('[') ? partial + "]" : $"[{partial}]";
    }

    // Extract + parse an LLM reply into a list via the caller's element parser, tolerating
    // preamble/suffix and a truncated trailing object. Subsumes the old "" / "[]" / "NONE"
    // special-cases: any unparseable reply simply yields an empty list. Never throws.
    public static List<T> ParseList<T>(string raw, Func<JsonElement, List<T>> parse)
    {
        var extracted = Extract(raw);
        foreach (var candidate in new[] { extracted, RepairTruncatedArray(extracted) })
        {
            if (candidate == null) continue;
            try
            {
                using var doc = JsonDocument.Parse(candidate);
                return parse(doc.RootElement);
            }
            catch { /* try the repaired form, else fall through to empty */ }
        }
        return new List<T>();
    }

    // Extract + deserialise to T, tolerating preamble/suffix. Returns default(T) on failure.
    public static T? Deserialize<T>(string raw, JsonSerializerOptions? options = null)
    {
        try { return JsonSerializer.Deserialize<T>(Extract(raw), options); }
        catch { return default; }
    }
}
