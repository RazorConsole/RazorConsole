// Copyright (c) RazorConsole. All rights reserved.

using System.Diagnostics;
using System.Text;

namespace RazorConsole.Core.Input;

public interface ITerminalActions
{
    void Copy(string text);
    void OpenLink(string link);
}

internal sealed class TerminalActions : ITerminalActions
{
    public void Copy(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        Console.Write($"\u001b]52;c;{Convert.ToBase64String(Encoding.UTF8.GetBytes(text))}\u0007");
    }

    public void OpenLink(string link)
    {
        if (Uri.TryCreate(link, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http")
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
    }
}
