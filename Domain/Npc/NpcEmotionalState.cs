using System.Text.Json.Serialization;

namespace NPCRAGSystem.Domain.Npc;

public class NpcEmotionalState
{
	[JsonPropertyName("fear")]
	public float Fear { get; set; }

	[JsonPropertyName("grief")]
	public float Grief { get; set; }

	[JsonPropertyName("hope")]
	public float Hope { get; set; }

	[JsonPropertyName("suspicion")]
	public float Suspicion { get; set; }

	[JsonPropertyName("anger")]
	public float Anger { get; set; }

	[JsonPropertyName("anxiety")]
	public float Anxiety { get; set; }

	[JsonPropertyName("disgust")]
	public float Disgust { get; set; }

	[JsonPropertyName("guilt")]
	public float Guilt { get; set; }

	// Deep copy — used to snapshot the authored baseline at load and to restore it on reset.
	public NpcEmotionalState Clone() => new()
	{
		Fear = Fear, Grief = Grief, Hope = Hope, Suspicion = Suspicion,
		Anger = Anger, Anxiety = Anxiety, Disgust = Disgust, Guilt = Guilt,
	};

	public void CopyFrom(NpcEmotionalState o)
	{
		Fear = o.Fear; Grief = o.Grief; Hope = o.Hope; Suspicion = o.Suspicion;
		Anger = o.Anger; Anxiety = o.Anxiety; Disgust = o.Disgust; Guilt = o.Guilt;
	}
}