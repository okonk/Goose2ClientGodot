using Goose2Client;

namespace Goose2Client.UI;

public interface IWindow
{
    WindowFrames WindowFrame { get; }
    int WindowId { get; }
}
