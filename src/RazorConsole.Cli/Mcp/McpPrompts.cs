// Copyright (c) RazorConsole. All rights reserved.

using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using RazorConsole.Cli.Commands;

namespace RazorConsole.Cli.Mcp;

/// <summary>
/// Prompts that tell an agent how to check a RazorConsole app, in terms of this server's tools.
/// </summary>
internal static class McpPrompts
{
    public const string KeyboardAccessibility = "check_keyboard_accessibility";
    public const string Layout = "check_layout";

    public static IReadOnlyList<Prompt> List { get; } =
    [
        new Prompt
        {
            Name = KeyboardAccessibility,
            Title = "Check keyboard accessibility",
            Description = "Walk the focus order of a running app with the keyboard and report elements that cannot be reached, show no focus, trap focus or do not react to Enter.",
            Arguments = [SessionArgument()],
        },
        new Prompt
        {
            Name = Layout,
            Title = "Check the layout",
            Description = "Read the component tree's layout and the screen at several terminal sizes, and report zero-sized, offscreen and overlapping elements and cut-off text.",
            Arguments = [SessionArgument()],
        },
    ];

    public static GetPromptResult Get(string name, IDictionary<string, JsonElement>? arguments)
    {
        string? session = null;
        if (arguments is not null
            && arguments.TryGetValue("session", out var value)
            && value.ValueKind == JsonValueKind.String
            && value.GetString() is { Length: > 0 } given)
        {
            if (!CliValueParsers.TryValidateSessionName(given, out var error))
            {
                throw new McpProtocolException($"session: {error}", McpErrorCode.InvalidParams);
            }

            session = given;
        }

        var which = session is null
            ? "the session you started with start_session (pass its name as `session` to every tool)"
            : $"session `{session}` (pass `session` \"{session}\" to every tool)";
        var text = name switch
        {
            KeyboardAccessibility => KeyboardText(which),
            Layout => LayoutText(which),
            _ => throw new McpProtocolException($"Unknown prompt '{name}'.", McpErrorCode.InvalidParams),
        };

        return new GetPromptResult
        {
            Description = List.First(prompt => prompt.Name == name).Description,
            Messages = [new PromptMessage { Role = Role.User, Content = new TextContentBlock { Text = text } }],
        };
    }

    private static PromptArgument SessionArgument() => new()
    {
        Name = "session",
        Description = "The session to check: the name start_session returned.",
        Required = false,
    };

    private static string KeyboardText(string which) => $"""
        Check the keyboard accessibility of the RazorConsole app in {which}. Use only the razorconsole tools.

        1. Call `get_focus`. Note the focus targets in order and which one has focus now.
        2. Call `find_elements` for interactive things the app shows (for example `tag:button`, `tag:input`, or the app's own `hook:` names) and compare them with the focus targets. Every interactive element should be a focus target. List the ones that are not.
        3. Press `send_keys` with ["Tab"] once per target. After each press call `wait_for` with `idle` "200ms", then `get_focus` and `screenshot` with format "json". Check that focus moved to the next target and that the focused element is visibly marked (a different style or a cursor). Note a Tab that does not move focus (a focus trap), skips a target or leaves the app's targets.
        4. Repeat with ["Shift+Tab"] and check that the order is the reverse.
        5. For each target, focus it with `send_keys` and its `selector`, press ["Enter"] (and [" "] for buttons and checkboxes), and use `screenshot` or `wait_for` to check that something happens. Where the app opens a dialog, press ["Escape"] and check that it closes and focus returns.
        6. Report each problem with the element's selector, what you expected and what you saw. Do not change the app's source unless asked.
        """;

    private static string LayoutText(string which) => $"""
        Check the layout of the RazorConsole app in {which}. Use only the razorconsole tools.

        1. Call `session_info` and note the terminal size. Call `get_tree` with includeLayout true. Each node has a rect (x, y, width, height).
        2. Look for elements with a zero width or height that have content, elements that extend past the terminal (x + width beyond the width, or y + height beyond the height) and siblings whose rects overlap.
        3. Call `screenshot` and compare it with the tree: text cut off, lines wrapped in the wrong place, content missing.
        4. Call `resize_session` to 80 columns by 24 rows, then to 40 by 12. After each, call `wait_for` with `idle` "300ms", then `get_tree` and `screenshot` again and look for the same problems.
        5. Restore the original size with `resize_session`.
        6. For anything suspicious call `inspect_at` with the cell or `inspect_element` with a selector. Report each problem with the element's selector, its rect and the terminal size where it happens. Do not change the app's source unless asked.
        """;
}
