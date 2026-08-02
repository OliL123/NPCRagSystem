namespace NPCRAGSystem.Interfaces.Core;

public interface IPlayerState
{
    string Name { get; }
    string Appearance { get; }
    bool HasCompletedIntro { get; }
    bool IsNpcKnown(string npcId);
    Task SetNameAsync(string name, bool persist = true);
    Task CompleteIntroAsync(bool persist = true);
    Task RevealNpcAsync(string npcId, bool persist = true);
    Task SaveAsync();
}
