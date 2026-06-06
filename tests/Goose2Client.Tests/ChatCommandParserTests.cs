using System;
using System.Collections.Generic;
using Goose2Client.UI;
using Xunit;

namespace Goose2Client.Tests;

public class ChatCommandParserTests
{
    private static readonly Dictionary<string, string> _aliases = new()
    {
        ["/t"] = "/tell",
        ["/"] = "/who",
        ["/r"] = "/random 1000",
        ["/h"] = "Hello there!"
    };

    private static readonly string[] _handlerKeys = { "/quit" };

    [Fact]
    public void Parse_TellAlias_ExpandsToCommand()
    {
        // /t Bob hi → Kind=Command, Text="/tell Bob hi"
        var result = ChatCommandParser.Parse("/t Bob hi", _aliases, _handlerKeys);
        Assert.Equal(ChatActionKind.Command, result.Kind);
        Assert.Equal("/tell Bob hi", result.Text);
    }

    [Fact]
    public void Parse_SlashAlias_ExpandsToWhoCommand()
    {
        // / → Kind=Command, Text="/who"
        var result = ChatCommandParser.Parse("/", _aliases, _handlerKeys);
        Assert.Equal(ChatActionKind.Command, result.Kind);
        Assert.Equal("/who", result.Text);
    }

    [Fact]
    public void Parse_PlainLongMessage_TruncatesTo200()
    {
        // plain string of 250 'x' → Kind=ChatMessage, Text.Length==200
        var input = new string('x', 250);
        var result = ChatCommandParser.Parse(input, _aliases, _handlerKeys);
        Assert.Equal(ChatActionKind.ChatMessage, result.Kind);
        Assert.Equal(200, result.Text.Length);
    }

    [Fact]
    public void Parse_QuitCommand_ReturnsHandler()
    {
        // /quit → Kind=Handler, Text="/quit", Arguments=null
        var result = ChatCommandParser.Parse("/quit", _aliases, _handlerKeys);
        Assert.Equal(ChatActionKind.Handler, result.Kind);
        Assert.Equal("/quit", result.Text);
        Assert.Null(result.Arguments);
    }

    [Fact]
    public void Parse_HelloAlias_ReturnsChatMessage()
    {
        // /h → Kind=ChatMessage, Text="Hello there!"
        var result = ChatCommandParser.Parse("/h", _aliases, _handlerKeys);
        Assert.Equal(ChatActionKind.ChatMessage, result.Kind);
        Assert.Equal("Hello there!", result.Text);
    }

    [Fact]
    public void Parse_RandomAlias_ReturnsCommandWithArgs()
    {
        // /r → Kind=Command, Text="/random 1000"
        var result = ChatCommandParser.Parse("/r", _aliases, _handlerKeys);
        Assert.Equal(ChatActionKind.Command, result.Kind);
        Assert.Equal("/random 1000", result.Text);
    }

    [Fact]
    public void Parse_Whitespace_ReturnsNone()
    {
        // "   " (whitespace) → Kind=None
        var result = ChatCommandParser.Parse("   ", _aliases, _handlerKeys);
        Assert.Equal(ChatActionKind.None, result.Kind);
    }
}
