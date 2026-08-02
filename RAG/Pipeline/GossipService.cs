using NPCRAGSystem.Ingestion;
using NPCRAGSystem.State.Managers;
using NPCRAGSystem.State.Repositories;
using NPCRAGSystem.Interfaces.Core;
using NPCRAGSystem.Interfaces.Core.NPC;
using NPCRAGSystem.Domain.Npc;
using NPCRAGSystem.Utils;

namespace NPCRAGSystem.RAG.Pipeline;

public class GossipService
{
    private const float TrustThreshold      = 0.35f; // min trust to pass on mundane news
    // Sensitive/vulnerable things are shared probabilistically, scaled by confide:
    // below the floor they're never shared; at/above "certain" they always are; in between
    // the NPC is increasingly hesitant the less close the relationship.
    private const float SensitiveFloor      = 0.20f;
    private const float SensitiveCertain    = 0.90f;
    private const float FidelityFloor       = 0.25f; // don't propagate memories too faint to be worth repeating
    private const float HearsayPenalty      = 0.60f; // fidelity multiplier for secondhand knowledge
    private const float CredibilityMult     = 0.85f; // credibility multiplier (source vouches but it's still hearsay)
    private const float FidelityCap         = 0.55f; // hearsay can't be more reliable than this
    private const int   CrossSettlementDays = 1;     // travel time before far-off gossip can land

    private readonly ILlmService _llm;
    private readonly INpcRegistry _npcRegistry;
    private readonly INpcMemoryManager _npcMemoryManager;
    private readonly ILocationRegistry _locationRegistry;
    private readonly PendingGossipStore _pendingGossip;

    public GossipService(
        ILlmService llm,
        INpcRegistry npcRegistry,
        INpcMemoryManager npcMemoryManager,
        ILocationRegistry locationRegistry,
        PendingGossipStore pendingGossip)
    {
        _llm = llm;
        _npcRegistry = npcRegistry;
        _npcMemoryManager = npcMemoryManager;
        _locationRegistry = locationRegistry;
        _pendingGossip = pendingGossip;
    }

    public async Task PropagateAsync(
        NpcState source,
        List<NpcMemory> sessionMemories,
        string sourceLocationId,
        int currentHour,
        int currentDay,
        bool log = false)
    {
        if (source.Relationships.Count == 0 || sessionMemories.Count == 0) return;

        var shareable = sessionMemories
            .Where(m => m.Fidelity >= FidelityFloor)
            .ToList();

        if (shareable.Count == 0) return;

        var sourceRegion = _locationRegistry.GetLocation(sourceLocationId)?.Region;

        // Heightened states loosen the tongue: anger/anxiety/fear/grief or drink make the
        // source more likely to let slip things they'd normally keep close. Only the part
        // above 0.5 counts, so a composed NPC behaves exactly as their confide dictates.
        var se = source.EmotionalState;
        var looseLips = MathF.Max(
            MathF.Max(MathF.Max(se.Anger, se.Anxiety), MathF.Max(se.Fear, se.Grief)),
            source.PhysicalState.Intoxication);
        var indiscretion = MathF.Max(0f, looseLips - 0.5f);

        foreach (var rel in source.Relationships)
        {
            // Skip only if this relationship can carry neither mundane nor sensitive gossip
            if (rel.Trust < TrustThreshold && rel.EffectiveConfide < SensitiveFloor)
                continue;

            var target = _npcRegistry.GetNpc(rel.NpcId);
            if (target == null) continue;

            // Higher trust = more memories considered for sharing
            var shareCount = Math.Max(1, (int)Math.Ceiling(rel.Trust * shareable.Count));
            var candidates = shareable
                .OrderByDescending(m => m.Fidelity)
                .Take(shareCount)
                .ToList();

            // Audience gate. Mundane news passes on trust. Sensitive/vulnerable things are
            // shared probabilistically, scaled by how much this NPC confides in the target:
            // a partner hears almost everything, a noble peer most things, a child almost
            // nothing. The per-item roll means they sometimes hold a thing back even from a
            // confidant — closer relationships just hold back less.
            var sensitiveChance = Math.Clamp(
                (rel.EffectiveConfide - SensitiveFloor) / (SensitiveCertain - SensitiveFloor)
                    + indiscretion * 0.6f, 0f, 1f);

            var toShare = candidates
                .Where(m => IsSensitive(m)
                    ? Random.Shared.NextSingle() < sensitiveChance
                    : rel.Trust >= TrustThreshold)
                .ToList();

            if (log)
            {
                var sensTotal = candidates.Count(IsSensitive);
                ConsoleEx.Dim($"[gossip] {source.Name} → {target.Name}: confide {rel.EffectiveConfide:F2} " +
                                  $"(sensitive share chance {sensitiveChance:P0}); sharing {toShare.Count}/{candidates.Count}, " +
                                  $"{toShare.Count(IsSensitive)}/{sensTotal} sensitive");
            }

            if (toShare.Count == 0) continue;

            var rephrased = await RephraseThroughSourceAsync(source, target, rel, toShare);
            if (rephrased.Count == 0) continue;

            // Distance gate — only NPCs in the same settlement hear it now; word travels
            // slowly across settlements, so the rest is queued for the Phase 4 events system.
            var targetLocId = _locationRegistry.GetCurrentLocationId(target, currentHour, currentDay);
            var targetRegion = _locationRegistry.GetLocation(targetLocId)?.Region;
            // When a target is off-schedule they resolve to an off-map private room, which has
            // no region — fall back to where they live so a townsfolk still counts as in-town
            // and isn't wrongly punted to the cross-settlement queue.
            if (string.IsNullOrEmpty(targetRegion) && !string.IsNullOrEmpty(target.HomeDoor))
                targetRegion = _locationRegistry.GetLocation(target.HomeDoor)?.Region;
            var sameSettlement = !string.IsNullOrEmpty(sourceRegion) && sourceRegion == targetRegion;

            var deferred = new List<PendingGossip>();

            for (int i = 0; i < rephrased.Count && i < toShare.Count; i++)
            {
                var original = toShare[i];
                var fidelity = MathF.Min(original.Fidelity * HearsayPenalty * rel.Trust, FidelityCap);
                var credibility = MathF.Min(original.Credibility * CredibilityMult * rel.Trust, 0.85f);

                if (sameSettlement)
                {
                    var gossip = new NpcMemory
                    {
                        Id              = Guid.NewGuid().ToString("N")[..8],
                        Content         = rephrased[i],
                        Fidelity        = fidelity,
                        InitialFidelity = fidelity,
                        Credibility     = credibility,
                        DecayWeight     = original.DecayWeight,
                        Nature          = MemoryNature.Rumour,
                        Timestamp       = $"day-{currentDay}",
                    };

                    // Route through AddMemory so hearsay gets dedup, the memory cap, and
                    // suspect-routing for faint rumours — instead of an unbounded raw append.
                    _npcMemoryManager.AddMemory(target.Id, gossip, isPlayerMemory: true);

                    if (log)
                    {
                        ConsoleEx.Dim($"[gossip] {source.Name} → {target.Name}: \"{gossip.Content}\" (fidelity: {gossip.Fidelity:F2})");
                    }
                }
                else
                {
                    deferred.Add(new PendingGossip
                    {
                        TargetNpcId         = target.Id,
                        Content             = rephrased[i],
                        Fidelity            = fidelity,
                        Credibility         = credibility,
                        DecayWeight         = original.DecayWeight,
                        Nature              = MemoryNature.Rumour,
                        SourceName          = source.Name,
                        CreatedDay          = currentDay,
                        DeliverableAfterDay = currentDay + CrossSettlementDays,
                    });
                }
            }

            if (deferred.Count > 0)
            {
                await _pendingGossip.EnqueueRangeAsync(deferred);
                if (log)
                {
                    ConsoleEx.Dim($"[gossip] {deferred.Count} item(s) queued for {target.Name} (different settlement)");
                }
            }

            rel.LastContact = $"day-{currentDay}";
        }
    }

    // Sensitive = the kind of thing shared in confidence, not idle chatter.
    private static bool IsSensitive(NpcMemory m)
        => m.Nature is MemoryNature.Accusation or MemoryNature.Claim or MemoryNature.Rumour;

    private async Task<List<string>> RephraseThroughSourceAsync(
        NpcState source,
        NpcState target,
        NpcRelationship rel,
        List<NpcMemory> memories)
    {
        var emotionSummary = BuildEmotionSummary(source);
        var memoryList = string.Join("\n", memories.Select((m, i) => $"{i + 1}. {m.Content}"));

        var trustDesc = rel.Trust switch
        {
            >= 0.8f => "very close — they trust each other deeply",
            >= 0.6f => "close friends",
            >= 0.4f => "on good terms",
            _       => "acquaintances",
        };

        var system = $"""
            You are writing how {source.Name} tells {target.Name} about a traveller they just met.
            Rephrase each memory as {source.Name} would naturally say it — brief, first-person retelling.
            Their relationship: {trustDesc}.
            {source.Name}'s current mood: {emotionSummary}.
            Rules: one sentence per memory, no invented facts, output ONLY a valid JSON array of strings.
            """;

        var user = $"""
            Memories about the traveller:
            {memoryList}

            Output a JSON array with exactly {memories.Count} strings.
            """;

        try
        {
            var json = await _llm.GenerateJsonAsync(system, user);
            return LlmJson.Deserialize<List<string>>(json) ?? FallbackRephrase(source, memories);
        }
        catch
        {
            // Network/LLM failure — fall back so provenance is at least preserved.
            return FallbackRephrase(source, memories);
        }
    }

    // Used when LLM fails — simple prefix so at least provenance is clear
    private static List<string> FallbackRephrase(NpcState source, List<NpcMemory> memories)
        => memories.Select(m => $"{source.Name} mentioned: {m.Content}").ToList();

    private static string BuildEmotionSummary(NpcState npc)
    {
        var e = npc.EmotionalState;
        var dominant = new[]
        {
            ("suspicious",  e.Suspicion),
            ("fearful",     e.Fear),
            ("grieving",    e.Grief),
            ("hopeful",     e.Hope),
            ("angry",       e.Anger),
            ("anxious",     e.Anxiety),
            ("disgusted",   e.Disgust),
            ("guilty",      e.Guilt),
        }
        .Where(x => x.Item2 > 0.3f)
        .OrderByDescending(x => x.Item2)
        .Select(x => x.Item1)
        .FirstOrDefault();

        return dominant ?? "relatively neutral";
    }
}
