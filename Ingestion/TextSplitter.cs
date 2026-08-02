using System.Text.RegularExpressions;

namespace NPCRAGSystem.Ingestion;

public static class TextSplitter
{
	public static List<string> SplitIntoParagraphs(string text)
	{
		return text
			.Split(new[] { "\r\n\r\n", "\n\n" }, StringSplitOptions.RemoveEmptyEntries)
			.Select(p => p.Trim())
			.Where(p => p.Length > 0)
			.ToList();
	}

	// Split a passage into individual sentences
	public static List<string> SplitIntoSentences(string text)
	{
		return Regex.Split(text, @"(?<=[.!?])\s+")
			.Select(s => s.Trim())
			// Filter fragments shorter than 3 characters
			.Where(s => s.Length > 2)
			.ToList();
	}

	// Split text into individual words
	public static List<string> SplitIntoWords(string text)
	{
		return text
			.Split(new[] { ' ', '\n', '\r', '\t', '.', ',', '!', '?', ';', ':', '"', '\'' },
				   StringSplitOptions.RemoveEmptyEntries)
			.Select(w => w.Trim())
			.Where(w => w.Length > 0)
			.ToList();
	}
}
