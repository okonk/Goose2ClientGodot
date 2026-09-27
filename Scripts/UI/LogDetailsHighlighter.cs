using System;
using Godot;
using Godot.Collections;

namespace Goose2Client.UI;

public partial class LogDetailsHighlighter : SyntaxHighlighter
{
    private static readonly Color KeyColor = new(0.494118f, 0.541176f, 0.65098f);
    private static readonly Color ValueColor = new(0.921569f, 0.898039f, 0.831373f);

    public override Dictionary _GetLineSyntaxHighlighting(int line)
    {
        string text = GetTextEdit().GetLine(line);
        int split = text.IndexOf(": ", StringComparison.Ordinal);
        var colors = new Dictionary();
        if (split < 0)
        {
            colors[0] = new Dictionary { { "color", ValueColor } };
            return colors;
        }
        colors[0] = new Dictionary { { "color", KeyColor } };
        colors[split + 2] = new Dictionary { { "color", ValueColor } };
        return colors;
    }
}
