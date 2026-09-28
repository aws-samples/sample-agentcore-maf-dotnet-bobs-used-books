using System.Runtime.CompilerServices;
using BobsBooksAgent;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

// Offline tests for the text the agent streams when the model calls a tool. They make no
// AWS calls: a stub chat client plays the model, and Microsoft Agent Framework runs the tool.
public sealed class AnswerTextTests
{
    [Fact]
    public async Task AnswerAfterToolCall_StartsOnNewLine()
    {
        var answer = await StreamAnswerAsync(textBeforeToolCall: true);

        Assert.Equal("Let me check our inventory for you!\nGreat news!", answer);
    }

    [Fact]
    public async Task ToolCallWithoutEarlierText_AddsNoLeadingLineBreak()
    {
        var answer = await StreamAnswerAsync(textBeforeToolCall: false);

        Assert.Equal("Great news!", answer);
    }

    private static async Task<string> StreamAnswerAsync(bool textBeforeToolCall)
    {
        var listBooks = AIFunctionFactory.Create(() => "[]", "ListBooks", "Lists books.");
        AIAgent agent = new ChatClientAgent(
            new ToolCallingChatClient(textBeforeToolCall),
            new ChatClientAgentOptions { ChatOptions = new ChatOptions { Tools = [listBooks] } });
        var session = await agent.CreateSessionAsync();

        var chunks = new List<string>();
        await foreach (var text in Agent.AnswerText(
            agent.RunStreamingAsync("Do you have any hardcover mysteries?", session)))
        {
            chunks.Add(text);
        }

        return string.Concat(chunks);
    }

    // First model call: optional text, then a ListBooks tool call. Second model call, after
    // the tool result: the answer.
    private sealed class ToolCallingChatClient(bool textBeforeToolCall) : IChatClient
    {
        private int _calls;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The agent handler uses streaming.");

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            if (_calls++ == 0)
            {
                if (textBeforeToolCall)
                {
                    yield return new ChatResponseUpdate(ChatRole.Assistant, "Let me check our inventory for you!");
                }

                yield return new ChatResponseUpdate(
                    ChatRole.Assistant,
                    [new FunctionCallContent("call-1", "ListBooks", new Dictionary<string, object?>())]);
            }
            else
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, "Great news!");
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
