using NPCRAGSystem.RAG.Classification;

namespace NPCRAGSystem.Domain;

public class DocumentChunk
{
	public string ID { get; set; } = string.Empty;
	public string SourceTxtFile { get; set; } = string.Empty;
	public string ChunkContent { get; set; } = string.Empty;
	public float[] Embedding { get; set; } = Array.Empty<float>();
	public List<Topic> Tags { get; set; } = new();
	public int ChunkIndex { get; set; }
}