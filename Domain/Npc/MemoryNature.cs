namespace NPCRAGSystem.Domain.Npc;

public static class MemoryNature
{
    public const string Fact       = "fact";       // stated and believed
    public const string Claim      = "claim";      // stated, credibility uncertain
    public const string Joke       = "joke";       // implausible bravado, not taken seriously
    public const string Deduction  = "deduction";  // NPC inferred this themselves
    public const string Rumour     = "rumour";     // heard from someone else
    public const string Accusation = "accusation"; // player accused the NPC of something
}
