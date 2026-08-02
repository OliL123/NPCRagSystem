using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using NPCRAGSystem.Interfaces.Core;

namespace NPCRAGSystem.Services;

// Sampling knobs for dialogue generation. Null = leave Ollama's defaults in place.
public record LlmSamplingOptions(float Temperature, float TopP, float RepeatPenalty, int RepeatLastN);

public class OllamaLlmService : ILlmService
{
	private readonly HttpClient _http;
	private readonly string _model;
	private readonly string _url;
	private readonly LlmSamplingOptions? _sampling;

	public OllamaLlmService(
		HttpClient http,
		string model = "llama3.1:8b",
		string baseUrl = "http://localhost:11434",
		LlmSamplingOptions? sampling = null)
	{
		_http = http;
		// Native Ollama chat endpoint. The OpenAI-compatible /v1/chat/completions
		// endpoint silently ignores `options` (num_keep) and `format` (json mode),
		// both of which are core to this pipeline — so we use the native API.
		_url = $"{baseUrl}/api/chat";
		_model = model;
		_sampling = sampling;
	}

	// ── Standard generation ───────────────────────────────────────────────────

	public async Task<string> GenerateAsync(
		string systemPrompt,
		string userPrompt,
		int numKeep = 80,
		IReadOnlyList<ChatMessage>? history = null)
	{
		var body = BuildRequestBody(systemPrompt, userPrompt, stream: false, numKeep: numKeep, history: history);
		return await PostAndExtractAsync(body);
	}

	// ── JSON-mode generation ──────────────────────────────────────────────────
	// Forces valid JSON output at the token level — no markdown fences,
	// no preamble, no malformed output. Use for all structured LLM calls.

	public async Task<string> GenerateJsonAsync(
		string systemPrompt,
		string userPrompt,
		int numKeep = 80)
	{
		var body = BuildRequestBody(systemPrompt, userPrompt, stream: false, jsonMode: true, numKeep: numKeep);
		return await PostAndExtractAsync(body);
	}

	// ── Streaming generation ──────────────────────────────────────────────────

	public async IAsyncEnumerable<string> GenerateStreamAsync(
		string systemPrompt,
		string userPrompt,
		int numKeep = 80,
		IReadOnlyList<ChatMessage>? history = null,
		[EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var body = BuildRequestBody(systemPrompt, userPrompt, stream: true, numKeep: numKeep, history: history);
		var request = new HttpRequestMessage(HttpMethod.Post, _url)
		{
			Content = new StringContent(body, Encoding.UTF8, "application/json")
		};

		var response = await _http.SendAsync(
			request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

		if (!response.IsSuccessStatusCode)
		{
			var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
			throw new HttpRequestException(
				$"Ollama {(int)response.StatusCode} {response.ReasonPhrase}: {errorBody}");
		}

		using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
		using var reader = new StreamReader(stream);

		// Native /api/chat streams newline-delimited JSON objects (JSONL), each of the
		// form {"message":{"content":"..."},"done":false}. No "data:" prefix, no [DONE].
		while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
		{
			var line = await reader.ReadLineAsync(cancellationToken);
			if (string.IsNullOrWhiteSpace(line)) continue;

			string? token = null;
			bool done = false;
			try
			{
				using var doc = JsonDocument.Parse(line);
				var root = doc.RootElement;

				if (root.TryGetProperty("message", out var message) &&
					message.TryGetProperty("content", out var content))
					token = content.GetString();

				done = root.TryGetProperty("done", out var doneEl) &&
					   doneEl.ValueKind == JsonValueKind.True;
			}
			catch
			{
				continue;
			}

			if (!string.IsNullOrEmpty(token))
				yield return token;

			if (done) break;
		}
	}

	// ── Helpers ───────────────────────────────────────────────────────────────

	private string BuildRequestBody(
		string systemPrompt,
		string userPrompt,
		bool stream,
		bool jsonMode = false,
		int numKeep = 80,
		IReadOnlyList<ChatMessage>? history = null)
	{
		// system, then prior turns as real chat messages, then the new user message. Passing
		// history as structured turns (not text in the system prompt) stops weaker models
		// echoing earlier replies back verbatim.
		var messages = new List<object>
		{
			new { role = "system", content = systemPrompt }
		};
		if (history != null)
			foreach (var h in history)
				messages.Add(new { role = h.Role, content = h.Content });
		messages.Add(new { role = "user", content = userPrompt });

		// num_keep protects stable prefix tokens from context window truncation. Sampling
		// params are applied to dialogue only (not JSON mode), so utility/structured calls
		// stay on Ollama defaults and aren't destabilised by a higher temperature.
		var options = new Dictionary<string, object?> { ["num_keep"] = numKeep };
		if (!jsonMode && _sampling != null)
		{
			options["temperature"] = _sampling.Temperature;
			options["top_p"] = _sampling.TopP;
			options["repeat_penalty"] = _sampling.RepeatPenalty;
			options["repeat_last_n"] = _sampling.RepeatLastN;
		}

		var body = new Dictionary<string, object?>
		{
			["model"] = _model,
			["messages"] = messages,
			["stream"] = jsonMode ? false : stream,
			["options"] = options
		};

		if (jsonMode) body["format"] = "json";

		// Disable "thinking" on reasoning models (Qwen3/3.5, DeepSeek-R1, QwQ, Magistral …).
		// Their reasoning streams in a separate `thinking` field we don't display, so leaving it
		// on delays the visible reply behind a long hidden pass (looks like a hang) and can leak
		// chain-of-thought into NPC dialogue. Only sent for those families, so non-reasoning
		// models (llama/gemma/qwen2.5) are unaffected — Ollama rejects `think` on models without it.
		if (IsThinkingModel(_model)) body["think"] = false;

		return JsonSerializer.Serialize(body);
	}

	private static bool IsThinkingModel(string model)
	{
		var m = model.ToLowerInvariant();
		return m.Contains("qwen3") || m.Contains("deepseek-r1") || m.Contains("qwq") || m.Contains("magistral");
	}

	private async Task<string> PostAndExtractAsync(string body)
	{
		var response = await _http.PostAsync(
			_url,
			new StringContent(body, Encoding.UTF8, "application/json"));

		if (!response.IsSuccessStatusCode)
		{
			var errorBody = await response.Content.ReadAsStringAsync();
			throw new HttpRequestException(
				$"Ollama {(int)response.StatusCode} {response.ReasonPhrase}: {errorBody}");
		}

		var json = await response.Content.ReadAsStringAsync();
		using var doc = JsonDocument.Parse(json);

		// Native /api/chat non-streaming response: { "message": { "content": "..." }, ... }
		return doc.RootElement
			.GetProperty("message")
			.GetProperty("content")
			.GetString() ?? string.Empty;
	}
}
