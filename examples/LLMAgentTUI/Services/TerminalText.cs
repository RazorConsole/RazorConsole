// Copyright (c) RazorConsole. All rights reserved.

using System.Text;

namespace LLMAgentTUI.Services;

/// <summary>Keep model/tool data from emitting terminal control sequences.</summary>
public static class TerminalText
{
    public static string Safe(string text)
    {
        var output = new StringBuilder(text.Length);
        foreach (var character in text.Replace("\r\n", "\n", StringComparison.Ordinal))
        {
            if (character == '\n')
            {
                output.Append(character);
            }
            else if (character == '\t')
            {
                output.Append("    ");
            }
            else if (char.IsControl(character))
            {
                output.Append($"\\u{(int)character:x4}");
            }
            else
            {
                output.Append(character);
            }
        }
        return output.ToString();
    }
}
