using System.Collections.Generic;
using System.Linq;

namespace Goose2Client.UI;

public enum ChatActionKind { None, ChatMessage, Command, Handler }

public readonly struct ChatParseResult
{
    public ChatActionKind Kind { get; }
    public string Text { get; }       // ChatMessage: the (possibly truncated) message; Command: the full command; Handler: the command key
    public string Arguments { get; }  // Handler only (may be null)
    public ChatParseResult(ChatActionKind kind, string text, string arguments = null) { Kind = kind; Text = text; Arguments = arguments; }
}

public static class ChatCommandParser
{
    public const int MaxChatLength = 200;

    // aliases: lowercase command -> replacement (e.g. "/t" -> "/tell"). handlerKeys: lowercase handler commands (e.g. "/quit").
    public static ChatParseResult Parse(string input, IReadOnlyDictionary<string, string> aliases, IReadOnlyCollection<string> handlerKeys)
    {
        if (string.IsNullOrWhiteSpace(input)) return new ChatParseResult(ChatActionKind.None, null);

        // Plain message (no leading '/') -> truncate to 200. (Unity truncates ONLY this direct path.)
        if (input[0] != '/')
            return new ChatParseResult(ChatActionKind.ChatMessage, input.Substring(0, System.Math.Min(input.Length, MaxChatLength)));

        // Command path (HandleCommand)
        int space = input.IndexOf(' ');
        string command = space == -1 ? input : input.Substring(0, space);
        string arguments = space == -1 ? null : input.Substring(space + 1);

        if (aliases != null && aliases.TryGetValue(command.ToLowerInvariant(), out var replaced))
            command = replaced;

        if (command.Length > 0 && command[0] == '/' && handlerKeys != null && handlerKeys.Contains(command.ToLowerInvariant()))
            return new ChatParseResult(ChatActionKind.Handler, command, arguments);

        string fullCommand = $"{command} {arguments}".TrimEnd();
        if (fullCommand.Length > 0 && fullCommand[0] == '/')
            return new ChatParseResult(ChatActionKind.Command, fullCommand);
        return new ChatParseResult(ChatActionKind.ChatMessage, fullCommand); // e.g. "/h" -> "Hello there!" (NOT truncated, matching Unity)
    }
}
