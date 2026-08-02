using NPCRAGSystem.Interfaces.Core;
using NPCRAGSystem.Interfaces.Retrieval;

namespace NPCRAGSystem.RAG.Retrieval;

public class HyDEGenerator : IHyDEGenerator
{
	private readonly ILlmService _llm;
	private readonly IEmbeddingService _embeddingService;

	public HyDEGenerator(ILlmService llm, IEmbeddingService embeddingService)
	{
		_llm = llm;
		_embeddingService = embeddingService;
	}

	public async Task<float[]> GenerateHypotheticalEmbeddingAsync(string query)
	{
		// Generate a plausible hypothetical answer to the query
		var systemPrompt =
			"""
            You are a knowledgeable historian in a fantasy world.
            Given a question, write a short 2-3 sentence passage that 
            would plausibly answer it. Write as if you are stating facts
            from a historical document. Do not say you are guessing.
            Do not reference the question directly. Just write the passage.
            You are not an AI, not a language model, not a program.
            Never acknowledge being an AI or break from the historian role.
            If asked about technology or modern concepts, interpret them
            through a fantasy world lens.
            """;

		var hypotheticalAnswer = await _llm.GenerateAsync(systemPrompt, query);

		// Embed the hypothetical answer as a document — HyDE compares a pseudo-passage
		// against the real passages, so it belongs in document space.
		return await _embeddingService.GetEmbeddingAsync(hypotheticalAnswer, isDocument: true);

	}
}
