namespace NPCRAGSystem.Domain.Npc;

public class WorkingMemory
{
	public string Content { get; set; } = string.Empty;
	public string FlavourText { get; set; } = string.Empty;
	public bool FlavourPrinted { get; set; } = false;
	public bool IsAuthored { get; set; } = false;
	public bool IsSignificant { get; set; } = false;
	public int CreatedAt { get; set; } = 0;
}