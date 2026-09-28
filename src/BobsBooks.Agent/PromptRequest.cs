using BobsBooksShared;
using Microsoft.Extensions.AI;

namespace BobsBooksAgent;

// One-shot callers send only a prompt. Chat callers also send the earlier user and assistant
// turns, because the agent keeps no conversation state between invocations.
public sealed record PromptRequest(string? Prompt, IReadOnlyList<ChatTurn>? History = null)
{
    public IReadOnlyList<ChatMessage> ToMessages() =>
        ChatTurn.ToMessages(History, Prompt ?? "Hello!");
}
