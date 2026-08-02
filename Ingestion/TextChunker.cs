using System.Text;

namespace NPCRAGSystem.Ingestion;

public class TextChunker
{
	private readonly int _targetChunkSize;
	private readonly int _overlapSize;

	public TextChunker(int targetChunkSize = 1200, int overlapSize = 200)
	{
		_targetChunkSize = targetChunkSize;
		_overlapSize = overlapSize;
	}

	public List<string> Chunk(string text)
	{
		var paragraphs = TextSplitter.SplitIntoParagraphs(text);	

		var chunks = new List<string>();
		var current = new StringBuilder();

		foreach (var paragraph in paragraphs)
		{
			if (current.Length > 0 && current.Length + paragraph.Length > _targetChunkSize)
			{
				var completed = current.ToString().Trim();

				if (completed.Length > 0)
					chunks.Add(completed);

				// Carry the tail of the completed chunk forward as context overlap.
				// This ensures sentences that straddle a chunk boundary are represented
				// in both chunks, improving retrieval for queries that match that content.
				var overlap = completed.Length > _overlapSize
					? completed[^_overlapSize..]
					: completed;

				current.Clear();
				current.Append(overlap);
				current.Append(' ');
			}

			current.Append(paragraph);
			current.Append("\n\n");
		}

		var remaining = current.ToString().Trim();

		// Emit any non-empty tail. The previous `> _overlapSize` guard silently dropped
		// short documents entirely (whole doc < overlap) and discarded legitimate final
		// chunks that happened to be short. The triggering paragraph is always fresh
		// content, so this never produces a pure-overlap duplicate.
		if (remaining.Length > 0)
			chunks.Add(remaining);

		return chunks;
	}
}