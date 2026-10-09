using System.Text;

namespace IosInspector.Services;

/// <summary>Removes ANSI/VT control sequences from an SSH stream for display in a plain text control.</summary>
public sealed class TerminalTextSanitizer
{
    private enum ParserState { Text, Escape, Csi, Osc, OscEscape }
    private ParserState _state;

    public string Process(ReadOnlySpan<char> input)
    {
        var output = new StringBuilder(input.Length);
        foreach (var character in input)
        {
            switch (_state)
            {
                case ParserState.Text:
                    if (character == '\x1b') _state = ParserState.Escape;
                    else if (character == '\b')
                    {
                        if (output.Length > 0) output.Length--;
                    }
                    else if (character is '\r' or '\n' or '\t' || !char.IsControl(character)) output.Append(character);
                    break;
                case ParserState.Escape:
                    _state = character switch
                    {
                        '[' => ParserState.Csi,
                        ']' => ParserState.Osc,
                        _ => ParserState.Text
                    };
                    break;
                case ParserState.Csi:
                    // CSI sequences end with a byte in the 0x40-0x7e range.
                    if (character is >= '@' and <= '~') _state = ParserState.Text;
                    break;
                case ParserState.Osc:
                    if (character == '\a') _state = ParserState.Text;
                    else if (character == '\x1b') _state = ParserState.OscEscape;
                    break;
                case ParserState.OscEscape:
                    _state = character == '\\' ? ParserState.Text : ParserState.Osc;
                    break;
            }
        }
        return output.ToString();
    }
}
