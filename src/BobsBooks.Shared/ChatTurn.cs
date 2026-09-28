using Microsoft.Extensions.AI;

namespace BobsBooksShared;

// One earlier turn of a chat, as the console sends it back with the next prompt:
// {"role":"user"|"assistant","text":"..."}.
public sealed record ChatTurn(string? Role, string? Text)
{
    // Builds the model input for one run: the earlier turns in order, then the new prompt.
    // Only user and assistant text is accepted. Any other role, such as system or tool, is
    // rejected, so a client cannot add instructions or tool results; the agent's instructions
    // come only from its own configuration.
    public static IReadOnlyList<ChatMessage> ToMessages(IEnumerable<ChatTurn>? history, string prompt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        var messages = new List<ChatMessage>();
        foreach (var turn in history ?? [])
        {
            ChatRole? role = turn?.Role?.ToLowerInvariant() switch
            {
                "user" => ChatRole.User,
                "assistant" => ChatRole.Assistant,
                _ => null
            };
            var text = turn?.Text;
            if (role is null || string.IsNullOrWhiteSpace(text))
            {
                throw new ArgumentException(
                    $"History entry {messages.Count} must be a user or assistant turn with non-empty text.");
            }

            messages.Add(new ChatMessage(role.Value, text));
        }

        messages.Add(new ChatMessage(ChatRole.User, prompt));
        return messages;
    }
}
