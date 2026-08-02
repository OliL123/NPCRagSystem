using NPCRAGSystem.RAG.Classification;
using System.Text.Json.Serialization;

namespace NPCRAGSystem.Domain;

public class ClassifierExample
{
	[JsonPropertyName("query")]

	public string Query { get; set; } = string.Empty;
	[JsonPropertyName("label")]

	public QueryComplexity Label { get; set; }
}