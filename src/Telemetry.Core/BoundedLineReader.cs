using System.Text;

namespace Telemetry.Core;

/// <summary>Enforces the IPC line limit during reading, before allocating an unbounded string.</summary>
public sealed class BoundedLineReader(TextReader reader, int maximumCharacters)
{
    private readonly char[] _buffer = new char[maximumCharacters > 0 ? Math.Min(maximumCharacters, 4095) + 1 : throw new ArgumentOutOfRangeException(nameof(maximumCharacters))];
    private int _position, _count;

    public async ValueTask<string?> ReadLineAsync(CancellationToken cancellation = default)
    {
        var text = new StringBuilder();
        while (true)
        {
            cancellation.ThrowIfCancellationRequested();
            if (_position == _count)
            {
                _count = await reader.ReadAsync(_buffer.AsMemory(), cancellation).ConfigureAwait(false);
                _position = 0;
                if (_count == 0) return text.Length == 0 ? null : text.ToString().TrimEnd('\r');
            }
            int newline = Array.IndexOf(_buffer, '\n', _position, _count - _position);
            int end = newline < 0 ? _count : newline;
            int length = end - _position;
            if (text.Length + length > maximumCharacters) throw new InvalidDataException("IPC line exceeds the permitted length.");
            text.Append(_buffer, _position, length);
            _position = end;
            if (newline >= 0) { _position++; return text.ToString().TrimEnd('\r'); }
        }
    }
}
