using NPCRAGSystem.Interfaces.Core;
using NPCRAGSystem.Interfaces.Retrieval;
using NPCRAGSystem.Interfaces.Classification;
using NPCRAGSystem.RAG.Retrieval;
using NPCRAGSystem.Interfaces.Core.NPC;
using NPCRAGSystem.Services;
using NPCRAGSystem.Configuration;

namespace NPCRAGSystem.RAG.Pipeline;

public class RagPipelineConfig
{
	public SystemConfig SystemConfig { get; init; } = new();
	public OutputConfig OutputConfig { get; init; } = new();
	public IEmbeddingService EmbeddingService { get; init; } = null!;
	public ILlmService Llm { get; init; } = null!;
	public IVectorLoreData LoreData { get; init; } = null!;
	public IComplexityClassifier ComplexityClassifier { get; init; } = null!;
	public ITopicClassifier TopicClassifier { get; init; } = null!;
	public IBM25Index Bm25 { get; init; } = null!;
	public IReranker Reranker { get; init; } = null!;
	public IChunkCompressor ChunkCompressor { get; init; } = null!;
	public IHyDEGenerator HyDE { get; init; } = null!;
	public IMMRSelector MMR { get; init; } = null!;
	public IEntityRegistry EntityRegistry { get; init; } = null!;
	public INpcRegistry NpcRegistry { get; init; } = null!;
	public INpcMemoryManager NpcMemoryManager { get; init; } = null!;
	public IConversationTracker ConversationTracker { get; init; } = null!;
	public IWorkingMemoryManager WorkingMemoryManager { get; init; } = null!;
	public IConversationMemoryCreator ConversationMemoryCreator { get; init; } = null!;
	public IEpisodicMemoryCreator EpisodicMemoryCreator { get; init; } = null!;
	public IMemoryConsolidator MemoryConsolidator { get; init; } = null!;
	public IGameState GameState { get; init; } = null!;
	public ILocationRegistry LocationRegistry { get; init; } = null!;
	public IScarTissueCompressor ScarTissueCompressor { get; init; } = null!;
	public ISelfCritiqueService SelfCritiqueService { get; init; } = null!;
	public IPlayerState? PlayerState { get; init; }
	public ClaimDetector? ClaimDetector { get; init; }
	public int TopNumChunk { get; init; } = 5;

	// Optional fine-tuning data capture. PrimaryModelName labels each logged turn with
	// the model that generated it.
	public TrainingDataLogger? TrainingLogger { get; init; }
	public string PrimaryModelName { get; init; } = "model";
}