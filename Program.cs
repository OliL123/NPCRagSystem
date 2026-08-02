using NPCRAGSystem.Ingestion;
using NPCRAGSystem.Domain;
using NPCRAGSystem.Services;
using NPCRAGSystem.RAG.Retrieval;
using NPCRAGSystem.RAG.Classification;
using NPCRAGSystem.RAG.Pipeline;
using NPCRAGSystem.State.Managers;
using NPCRAGSystem.State.Repositories;
using NPCRAGSystem;
using NPCRAGSystem.Configuration;
using NPCRAGSystem.Game;
using NPCRAGSystem.Interfaces.Core;

// ── Config ────────────────────────────────────────────────────────────────────
const string OLLAMA_URL = "http://localhost:11434";
const string EMBEDDING_MODEL = "nomic-embed-text";
const string CHAT_MODEL = "llama3.1:8b";

var lorePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "Lore");
// Data/ splits by purpose: World = live-loaded authored world (npcs, locations, accents,
// entities); Classifier = the complexity-classifier example set; SaveTemplate = the pristine
// starting state copied into a fresh save slot.
var worldPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "World");
var classifierPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "Classifier");
var saveTemplatePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "SaveTemplate");

// Regional speech registers — referenced by NpcState.Accent, injected by PersonaBuilder.
NPCRAGSystem.Utils.AccentRegistry.Load(Path.Combine(worldPath, "accents.json"));

var systemConfig = new SystemConfig();
var outputConfig = new OutputConfig();

// Developer mode — unlocks the wm/debug/advance/compare commands. Off for normal players;
// flip it with the "--dev" launch arg or NPCRAG_DEV=1 for testing / shared builds.
if (args.Contains("--dev", StringComparer.OrdinalIgnoreCase)
    || Environment.GetEnvironmentVariable("NPCRAG_DEV") == "1")
{
    systemConfig.DevMode = true;    // unlock the dev command set
    systemConfig.SkipIntro = true;  // skip the intro for fast iteration
}

// ── Startup ───────────────────────────────────────────────────────────────────
// Render em-dashes / smart quotes (in authored prose and model output) correctly,
// instead of the console best-fitting them to '-' or '?'.
Console.OutputEncoding = System.Text.Encoding.UTF8;

Console.WriteLine("=== NPC RAG System ===\n");

var http = new HttpClient { Timeout = TimeSpan.FromSeconds(600) };
var (selectedModel, critiqueModel, compareModels) = await ModelPicker.PickAsync(http, OLLAMA_URL, CHAT_MODEL, systemConfig);

// ── Save slot ───────────────────────────────────────────────────────────────────
// Data/SaveTemplate + Data/World hold the pristine authored starting state; the live game
// lives in Data/Saves/auto, seeded from them. Continuing keeps an in-progress game across rebuilds.
var saveDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "Saves", "auto");
var continueGame = false;
if (SaveSlot.Exists(saveDir))
{
    Console.Write("Continue saved game? [Y/n] ");
    var answer = Console.ReadLine()?.Trim().ToLowerInvariant();
    continueGame = string.IsNullOrEmpty(answer) || answer is "y" or "yes";
}
if (!continueGame)
{
    SaveSlot.Reset(worldPath, saveTemplatePath, saveDir);
    Console.WriteLine("New game started.\n");
}
else
{
    Console.WriteLine("Continuing saved game.\n");
}

var npcsDir = Path.Combine(saveDir, "npcs");

RagPipeline? pipeline = null;
NpcRegistry? npcRegistry = null;
NpcMemoryManager? npcMemoryManager = null;
ConversationTracker? conversationTracker = null;
WorkingMemoryManager? workingMemoryManager = null;
GameStateManager? gameStateManager = null;
LocationRegistry? locationRegistry = null;
EpisodicMemoryCreator? episodicCreator = null;
MemoryConsolidator? memoryConsolidator = null;
PlayerStateManager? playerStateManager = null;
GossipService? gossipService = null;
List<(string name, ILlmService llm)> compareLlms = new();

try
{
    // ── Services & Data Loading ───────────────────────────────────────────────
    var embeddingService = new OllamaEmbeddingService(http, EMBEDDING_MODEL, OLLAMA_URL);

    var entityRegistry = await EntityRegistry.LoadAsync(
        Path.Combine(worldPath, "entities.json"));

    // NPC state, game state, player state and queued gossip live in the save slot.
    // Authored world content (locations, accents, entities) lives in Data/World; the
    // classifier example set in Data/Classifier.
    npcRegistry = await NpcRegistry.LoadAsync(npcsDir);

    npcMemoryManager = new NpcMemoryManager(npcRegistry);
    conversationTracker = new ConversationTracker(npcRegistry, systemConfig.ConversationHistoryWindow);
    workingMemoryManager = new WorkingMemoryManager(npcRegistry, systemConfig.LogMemory);

    var gameStatePath = Path.Combine(saveDir, "game_state.json");
    gameStateManager = await GameStateManager.LoadAsync(gameStatePath, npcMemoryManager);

    var locationsPath = Path.Combine(worldPath, "locations.json");
    locationRegistry = await LocationRegistry.LoadAsync(locationsPath, npcRegistry);

    var playerStatePath = Path.Combine(saveDir, "player_state.json");
    playerStateManager = await PlayerStateManager.LoadAsync(playerStatePath);

    var debugPath = Path.Combine(saveDir, "debug_npc.json");
    if (File.Exists(debugPath))
        await npcRegistry.MergeAsync(debugPath);

    var pendingGossipStore = await PendingGossipStore.LoadAsync(
        Path.Combine(saveDir, "pending_gossip.json"));

    var complexityClassifier = await ComplexityClassifier.CreateAsync(
        embeddingService,
        Path.Combine(classifierPath, "examples.json"));

    // ── Pipeline Services ─────────────────────────────────────────────────────
    var sampling = new LlmSamplingOptions(
        systemConfig.Temperature, systemConfig.TopP, systemConfig.RepeatPenalty, systemConfig.RepeatLastN);

    // Captures dialogue turns to JSONL for fine-tuning (tag them in-game with 'tag …').
    // Deliberately OUTSIDE the save slot: starting a New Game runs SaveSlot.Reset, which
    // Directory.Delete's the whole save dir — that would wipe collected training data. Keeping
    // it under Data/training (persistent across games and rebuilds) decouples the two. Archive
    // to ml/finetune/logs when a batch is done.
    var trainingLogger = systemConfig.LogTrainingData
        ? new TrainingDataLogger(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "training", "training_log.jsonl"))
        : null;

    var llm = new OllamaLlmService(http, selectedModel, OLLAMA_URL, sampling);
    compareLlms = compareModels
        .Select(m => (name: m, llm: (ILlmService)new OllamaLlmService(http, m, OLLAMA_URL, sampling)))
        .ToList();
    var critiqueLlm = critiqueModel != null
        ? new OllamaLlmService(http, critiqueModel, OLLAMA_URL, sampling)
        : llm;
    var loreData = new InMemoryLoreData();
    var topicClassifier = new TopicClassifier(entityRegistry);
    var bm25 = new BM25Index();
    // LLM-judge reranker — uses the smaller critique model when one was selected.
    // Disabled by default via SystemConfig.UseReranker.
    var crossEncoder = new LlmRerankerService(critiqueLlm);
    var reranker = new Reranker(crossEncoder, bm25);
    var chunkCompressor = new ChunkCompressor(bm25);
    var hyde = new HyDEGenerator(llm, embeddingService);
    var mmr = new MMRSelector();
    var conversationMemory = new ConversationMemoryCreator(llm);
    episodicCreator = new EpisodicMemoryCreator(llm);
    memoryConsolidator = new MemoryConsolidator(llm);
    var scarTissueCompressor = new ScarTissueCompressor(llm);
    var selfCritiqueService = new SelfCritiqueService(critiqueLlm);
    gossipService = new GossipService(llm, npcRegistry, npcMemoryManager, locationRegistry, pendingGossipStore);
    var claimDetector = new ClaimDetector(llm);

    // ── Ingestion ─────────────────────────────────────────────────────────────
    Console.WriteLine("Loading documents...");

    var loader = new DocumentLoader();
    var chunker = new TextChunker();
    var allChunks = new List<DocumentChunk>();

    var cachePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "Cache", "embedding_cache.json");
    Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
    // Cache key includes a task-prefix version so the switch to search_document/search_query
    // prefixes invalidates any pre-existing cache and forces a one-time re-embed.
    var embeddingCacheKey = $"{EMBEDDING_MODEL}+taskprefix-v1";
    var cached = await ChunkEmbeddingCache.TryLoadAsync(cachePath, lorePath, embeddingCacheKey);

    if (cached != null)
    {
        allChunks = cached;

        foreach (var chunk in allChunks)
        {
            loreData.Add(chunk);
            bm25.IndexChunk(chunk);
        }

        bm25.Build();
        Console.WriteLine($"Ready. {loreData.Count} chunks loaded from cache.\n");
    }
    else
    {
        var documents = loader.LoadFromDirectory(lorePath, systemConfig.StripMarkdown);

        foreach (var (fileName, content) in documents)
        {
            var textChunks = chunker.Chunk(content);
            for (int i = 0; i < textChunks.Count; i++)
            {
                allChunks.Add(new DocumentChunk
                {
                    ID = $"{fileName}_{i}",
                    SourceTxtFile = fileName,
                    ChunkContent = textChunks[i],
                    ChunkIndex = i
                });
            }
        }

        Console.WriteLine($"\nEmbedding {allChunks.Count} chunks...");

        for (int i = 0; i < allChunks.Count; i++)
        {
            allChunks[i].Tags = entityRegistry.GetTopicsForText(allChunks[i].ChunkContent);
            var contextualised = $"Document: {allChunks[i].SourceTxtFile}\n\n{allChunks[i].ChunkContent}";
            allChunks[i].Embedding = await embeddingService.GetEmbeddingAsync(contextualised, isDocument: true);
            loreData.Add(allChunks[i]);
            bm25.IndexChunk(allChunks[i]);
            Console.Write(i % 10 == 9 ? $" {i + 1}\n" : ".");
        }

        bm25.Build();
        Console.WriteLine($"\n\nReady. {loreData.Count} chunks indexed.\n");

        await ChunkEmbeddingCache.SaveAsync(allChunks, cachePath, lorePath, embeddingCacheKey);
    }

    // ── Pipeline Construction ─────────────────────────────────────────────────
    pipeline = new RagPipeline(new RagPipelineConfig
    {
        EmbeddingService = embeddingService,
        Llm = llm,
        LoreData = loreData,
        ComplexityClassifier = complexityClassifier,
        TopicClassifier = topicClassifier,
        Bm25 = bm25,
        Reranker = reranker,
        ChunkCompressor = chunkCompressor,
        HyDE = hyde,
        MMR = mmr,
        EntityRegistry = entityRegistry,
        NpcRegistry = npcRegistry,
        NpcMemoryManager = npcMemoryManager,
        ConversationTracker = conversationTracker,
        WorkingMemoryManager = workingMemoryManager,
        ConversationMemoryCreator = conversationMemory,
        EpisodicMemoryCreator = episodicCreator,
        MemoryConsolidator = memoryConsolidator,
        GameState = gameStateManager,
        LocationRegistry = locationRegistry,
        ScarTissueCompressor = scarTissueCompressor,
        SelfCritiqueService = selfCritiqueService,
        PlayerState = playerStateManager,
        ClaimDetector = claimDetector,
        SystemConfig = systemConfig,
        OutputConfig = outputConfig,
        TrainingLogger = trainingLogger,
        PrimaryModelName = selectedModel
    });
}
catch (HttpRequestException)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine("\nCould not reach Ollama.");
    Console.WriteLine("Make sure Ollama is running: ollama serve");
    Console.ResetColor();
    Console.WriteLine("\nPress any key to exit.");
    Console.ReadKey();
    return;
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"\nStartup failed: {ex.Message}");
    Console.ResetColor();
    Console.WriteLine("\nPress any key to exit.");
    Console.ReadKey();
    return;
}

// ── Main loop ─────────────────────────────────────────────────────────────────
await new GameLoop(
    pipeline!,
    npcRegistry!,
    npcMemoryManager!,
    conversationTracker!,
    workingMemoryManager!,
    gameStateManager!,
    locationRegistry!,
    episodicCreator!,
    memoryConsolidator!,
    playerStateManager!,
    systemConfig,
    gossipService!,
    primaryModelName: selectedModel,
    compareLlms: compareLlms
).RunAsync();
