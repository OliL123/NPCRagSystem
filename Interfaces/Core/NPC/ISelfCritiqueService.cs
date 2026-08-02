namespace NPCRAGSystem.Interfaces.Core.NPC;

public interface ISelfCritiqueService
{
	Task<CritiqueResult> CritiqueAsync(
		string npcName,
		string dynamicPersona,
		string response,
		string contextBlock);
}

public class CritiqueResult
{
	public bool Passed { get; set; }
	public string Reason { get; set; } = string.Empty;
}