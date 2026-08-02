namespace NPCRAGSystem.Domain.Npc;

public class ConversationTurn
{
	public string PlayerMessage { get; set; } = string.Empty;
	public string NpcResponse { get; set; } = string.Empty;
	public int Day { get; set; } = 0;

}