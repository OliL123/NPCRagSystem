namespace NPCRAGSystem.Utils;

// Small console helpers — collapse the repeated "set colour → WriteLine → ResetColor" trio
// (dev/status messages) into one call. Dim is the dark-grey used for [tag]/[debug]/[wm]/etc.
public static class ConsoleEx
{
	public static void Dim(string text)  => Write(ConsoleColor.DarkGray, text);
	public static void Note(string text) => Write(ConsoleColor.Cyan, text);
	public static void Warn(string text) => Write(ConsoleColor.Yellow, text);

	public static void Write(ConsoleColor color, string text)
	{
		Console.ForegroundColor = color;
		Console.WriteLine(text);
		Console.ResetColor();
	}
}
