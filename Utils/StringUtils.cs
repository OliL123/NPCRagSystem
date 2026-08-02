using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace NPCRAGSystem.Utils;

public static class StringUtils
{
	// One compiled whole-word regex per term. The term set is small and reused across many
	// calls (topic keywords, known entities), so caching + compiling avoids rebuilding a
	// Regex on every call — TopicClassifier alone calls this ~250×/query.
	private static readonly ConcurrentDictionary<string, Regex> WholeWordCache = new();

	// Matches a term as a whole word
	public static bool IsWholeWordMatch(string text, string term)
	{
		var regex = WholeWordCache.GetOrAdd(term,
			t => new Regex($@"\b{Regex.Escape(t)}\b", RegexOptions.Compiled));
		return regex.IsMatch(text);
	}

	// Normalises text for comparison — strips punctuation, collapses whitespace, lowercases
	public static string NormaliseForComparison(string text)
	{
		return new string(text
			.Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c))
			.ToArray())
			.ToLowerInvariant()
			.Trim();
	}

	// Jaccard similarity on meaningful tokens — filters stop words so "the traveller
	// told me they are a merchant" and "the traveller is a merchant" still match.
	public static float JaccardSimilarity(string a, string b)
	{
		var setA = Tokenise(a);
		var setB = Tokenise(b);
		if (setA.Count == 0 && setB.Count == 0) return 1f;
		var intersection = setA.Intersect(setB).Count();
		var union = setA.Union(setB).Count();
		return union == 0 ? 0f : (float)intersection / union;
	}

	private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
	{
		"the","a","an","is","are","was","were","be","been","being",
		"to","of","in","and","or","but","me","my","i","you","your",
		"they","their","this","that","it","for","with","about","from",
		"told","said","claims","seemed","appears","apparently","traveller",
		"have","has","had","not","no","so","at","by","on","as","into",
		"something","someone","anything","everything","nothing","very",
		"just","still","also","even","however","though"
	};

	private static HashSet<string> Tokenise(string text)
		=> NormaliseForComparison(text)
			.Split(' ', StringSplitOptions.RemoveEmptyEntries)
			.Where(w => w.Length > 2 && !StopWords.Contains(w))
			.ToHashSet(StringComparer.OrdinalIgnoreCase);
}