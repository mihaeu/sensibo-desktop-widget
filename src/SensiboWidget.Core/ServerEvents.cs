using System.Runtime.CompilerServices;
using System.Text;

namespace SensiboWidget.Core;

public sealed record ServerEvent(string Name, string Data);

public static class ServerEvents
{
    public static async IAsyncEnumerable<ServerEvent> ReadAsync(Stream stream,
        Func<TimeSpan> idleTimeout, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        var name = "message";
        var data = new StringBuilder();
        while (true)
        {
            using var idle = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            idle.CancelAfter(idleTimeout());
            var line = await reader.ReadLineAsync(idle.Token).ConfigureAwait(false);
            if (line is null) yield break;
            if (line.Length > 65536 || data.Length + line.Length > 262144)
                throw new InvalidDataException("Event exceeds the supported size.");
            if (line.Length == 0)
            {
                if (data.Length > 0) yield return new(name, data.ToString().TrimEnd('\n'));
                name = "message";
                data.Clear();
                continue;
            }
            if (line.StartsWith(':'))
            {
                yield return new("heartbeat", "");
                continue;
            }
            var separator = line.IndexOf(':');
            var field = separator < 0 ? line : line[..separator];
            var value = separator < 0 ? "" : line[(separator + 1)..];
            if (value.StartsWith(' ')) value = value[1..];
            if (field == "event") name = value;
            if (field == "data") data.Append(value).Append('\n');
        }
    }
}
