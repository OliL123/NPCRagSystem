namespace NPCRAGSystem.Interfaces.Core;

// One prior conversation turn, passed to the model as a real chat message. Feeding history
// as structured turns (rather than as text inside the system prompt) stops weaker models
// copying earlier replies back verbatim.
public record ChatMessage(string Role, string Content);

public interface ILlmService
{
	Task<string> GenerateAsync(string systemPrompt, string userMessage, int numKeep = 80,
		IReadOnlyList<ChatMessage>? history = null);
	Task<string> GenerateJsonAsync(string systemPrompt, string userMessage, int numKeep = 80);
	IAsyncEnumerable<string> GenerateStreamAsync(
		string systemPrompt,
		string userMessage,
		int numKeep = 80,
		IReadOnlyList<ChatMessage>? history = null,
		CancellationToken cancellationToken = default);
}