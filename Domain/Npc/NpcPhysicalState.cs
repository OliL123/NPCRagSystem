using System.Text.Json.Serialization;

namespace NPCRAGSystem.Domain.Npc;

public class NpcPhysicalState
{
	[JsonPropertyName("exhaustion")]
	public float Exhaustion { get; set; }

	[JsonPropertyName("pain")]
	public float Pain { get; set; }

	[JsonPropertyName("intoxication")]
	public float Intoxication { get; set; }

	[JsonPropertyName("hunger")]
	public float Hunger { get; set; }

	[JsonPropertyName("illness")]
	public float Illness { get; set; }

	// Deep copy — used to snapshot the authored baseline at load and restore it on reset.
	public NpcPhysicalState Clone() => new()
	{
		Exhaustion = Exhaustion, Pain = Pain, Intoxication = Intoxication,
		Hunger = Hunger, Illness = Illness,
	};

	public void CopyFrom(NpcPhysicalState o)
	{
		Exhaustion = o.Exhaustion; Pain = o.Pain; Intoxication = o.Intoxication;
		Hunger = o.Hunger; Illness = o.Illness;
	}
}