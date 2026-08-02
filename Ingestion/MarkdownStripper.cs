using System.Text.RegularExpressions;

namespace NPCRAGSystem.Ingestion;

public static class MarkdownStripper
{
	public static string Strip(string text)
	{
		// Remove image links entirely — no useful text content
		text = Regex.Replace(text, @"!\[\[.*?\]\]", "");

		// Remove HTML tags entirely
		text = Regex.Replace(text, @"<[^>]+>", "");

		// Remove wiki links but keep the display text
		text = Regex.Replace(text, @"\[\[([^\]]+)\]\]", "$1");

		// Remove heading markers but keep the text
		text = Regex.Replace(text, @"^#{1,6}\s+", "", RegexOptions.Multiline);

		// Remove blockquote markers
		text = Regex.Replace(text, @"^>\s+", "", RegexOptions.Multiline);

		// Remove bold and italic markers
		text = Regex.Replace(text, @"\*{1,3}([^*]+)\*{1,3}", "$1");

		// Remove list markers
		text = Regex.Replace(text, @"^\s*[-*+]\s+", "", RegexOptions.Multiline);

		// Remove end of file tags
		text = Regex.Replace(text, @"(?m)^\s*#\w+\s*$", "");

		// Collapse multiple blank lines into one
		text = Regex.Replace(text, @"\n{3,}", "\n\n");

		return text.Trim();
	}
}
