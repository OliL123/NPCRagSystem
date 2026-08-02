namespace NPCRAGSystem.Configuration;

public class OutputConfig
{
	// Whether to stream responses token by token
	public bool UseStreaming { get; init; } = true;

	// Print day/hour after each exchange
	public bool ShowTimeStamp { get; init; } = true;

	// Base delay between tokens in milliseconds
	public int StreamingTokenDelayMs { get; init; } = 45;

	// Pause durations at punctuation — simulates natural speech rhythm
	public int StreamingPauseShortMs { get; init; } = 200;  // comma, semicolon, colon
	public int StreamingPauseLongMs { get; init; } = 450;  // period, question mark, exclamation
	public int StreamingPauseEllipsisMs { get; init; } = 1000;  // ellipsis
}