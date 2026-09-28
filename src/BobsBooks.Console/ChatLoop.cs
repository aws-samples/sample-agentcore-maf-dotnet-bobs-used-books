using BobsBooksShared;

namespace BobsBooksConsole;

// Interactive chat for --chat. The user and assistant turns live only in this method's list:
// each new prompt is sent with the earlier turns, and the list is discarded when the loop ends
// on /exit or end of input.
public static class ChatLoop
{
    public static async Task RunAsync(
        TextReader input,
        TextWriter output,
        Func<string, IReadOnlyList<ChatTurn>, Task<string>> ask)
    {
        var history = new List<ChatTurn>();
        output.WriteLine("Chat mode: ask a question, or type /exit (or press Ctrl+D) to quit.");
        while (true)
        {
            output.Write("> ");
            var prompt = await input.ReadLineAsync();
            if (prompt is null || prompt.Trim() == "/exit")
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(prompt))
            {
                continue;
            }

            var answer = await ask(prompt, history.ToArray());
            output.WriteLine(answer);
            output.WriteLine();

            // The agent rejects history turns without text, so a blank answer is not kept.
            if (!string.IsNullOrWhiteSpace(answer))
            {
                history.Add(new ChatTurn("user", prompt));
                history.Add(new ChatTurn("assistant", answer));
            }
        }
    }
}
