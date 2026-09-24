using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace BobsBooksShared;

public static class ToolLoggingMiddleware
{
    public static async ValueTask<object?> Log(
        AIAgent agent,
        FunctionInvocationContext context,
        Func<FunctionInvocationContext, CancellationToken, ValueTask<object?>> next,
        CancellationToken cancellationToken)
    {
        Console.WriteLine(
            $"TOOL_CALL name={context.Function.Name} arguments={Serialize(context.Arguments)}");
        var result = await next(context, cancellationToken);
        Console.WriteLine(
            $"TOOL_RESULT name={context.Function.Name} result={Serialize(result)}");
        return result;
    }

    private static string Serialize(object? value)
    {
        if (value is null)
        {
            return "null";
        }

        try
        {
            return JsonSerializer.Serialize(value, value.GetType());
        }
        catch
        {
            return value.ToString() ?? "null";
        }
    }
}
