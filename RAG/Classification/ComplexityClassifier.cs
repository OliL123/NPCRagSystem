using NPCRAGSystem.Interfaces.Classification;
using NPCRAGSystem.Interfaces.Core;
using NPCRAGSystem.RAG.Retrieval;
using NPCRAGSystem.Domain;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NPCRAGSystem.RAG.Classification;

public enum QueryComplexity
{
	Simple,
	Medium,
	Complex
}

public class ComplexityClassifier : IComplexityClassifier
{
	private readonly IEmbeddingService _embedder;

	// Embedded once at startup via CreateAsync
	private readonly List<(float[] Embedding, QueryComplexity Label)> _embeddedExamples;

	private ComplexityClassifier(
		IEmbeddingService embedder,
		List<(float[] Embedding, QueryComplexity Label)> embeddedExamples)
	{
		_embedder = embedder;
		_embeddedExamples = embeddedExamples;
	}

	// ── Construction ────────────────────────────────────────────────────────

	public static async Task<ComplexityClassifier> CreateAsync(
		IEmbeddingService embedder,
		string examplesPath)
	{
		Console.WriteLine("Initialising complexity classifier...");

		var json = await File.ReadAllTextAsync(examplesPath);

		var options = new JsonSerializerOptions
		{
			Converters = { new JsonStringEnumConverter() }
		};

		var rawExamples = JsonSerializer.Deserialize<List<ClassifierExample>>(json, options)
			?? throw new InvalidOperationException("Failed to deserialise classifier examples.");

		var semaphore = new SemaphoreSlim(4);

		var tasks = rawExamples.Select(async example =>
		{
			await semaphore.WaitAsync();
			try
			{
				var embedding = await embedder.GetEmbeddingAsync(example.Query);
				return (embedding, example.Label);
			}
			finally
			{
				semaphore.Release();
			}
		});

		var embeddedExamples = (await Task.WhenAll(tasks)).ToList();

		Console.WriteLine($"  Classifier ready. {embeddedExamples.Count} examples embedded.\n");

		return new ComplexityClassifier(embedder, embeddedExamples);
	}

	// ── Classification ──────────────────────────────────────────────────────

	public async Task<QueryComplexity> ClassifyAsync(string query, float[]? queryEmbedding = null)
	{
		var lower = query.ToLowerInvariant();
		var wordCount = lower.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

		// Very short queries with no content are Simple
		if (wordCount <= 4) return QueryComplexity.Simple;

		// Reuse the caller's embedding when supplied — saves a duplicate embed per turn
		var embedding = queryEmbedding ?? await _embedder.GetEmbeddingAsync(lower);

		var nearest = _embeddedExamples
			.Select(e => (e.Label, Score: VectorMath.CosineSimilarity(embedding, e.Embedding)))
			.OrderByDescending(x => x.Score)
			.Take(3)
			.GroupBy(x => x.Label)
			.OrderByDescending(g => g.Count())
			.ThenByDescending(g => g.Sum(x => x.Score))
			.First()
			.Key;

		return nearest;
	}
}