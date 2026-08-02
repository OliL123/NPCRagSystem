using System.Text;
using System.Text.Json;
using NPCRAGSystem.Interfaces.Core;

namespace NPCRAGSystem.Services;

public class OllamaEmbeddingService : IEmbeddingService
{
	private readonly HttpClient _http;
	private readonly string _model;
	private readonly string _url;

	public OllamaEmbeddingService(HttpClient http, string model = "nomic-embed-text", string baseURL = "http://localhost:11434")
	{
		_http = http;
		_model = model;
		_url = $"{baseURL}/v1/embeddings";
	}

	public async Task<float[]> GetEmbeddingAsync(string text, bool isDocument = false)
	{
		// nomic-embed-text task prefixes — they meaningfully improve retrieval and must
		// be applied consistently: documents indexed with one prefix, queries the other.
		var prefixed = isDocument ? $"search_document: {text}" : $"search_query: {text}";
		var body = JsonSerializer.Serialize(new { model = _model, input = prefixed });

		var response = await _http.PostAsync(
			_url,
			new StringContent(body, Encoding.UTF8, "application/json")
			);

		response.EnsureSuccessStatusCode();

		var json = await response.Content.ReadAsStringAsync();
		using var doc = JsonDocument.Parse(json);

		var embedding = doc.RootElement
			.GetProperty("data")[0]
			.GetProperty("embedding")
			.EnumerateArray()
			.Select(e => e.GetSingle())
			.ToArray();

		return embedding;
	}
}