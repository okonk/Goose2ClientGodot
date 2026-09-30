using System.Collections.Generic;

namespace Goose2Client.InputBindings;

public interface IInputMapSurface
{
    bool HasAction(string action);

    float GetActionDeadzone(string action);

    IReadOnlyList<InputMapEventDescriptor> GetActionEvents(string action);

    void EraseActionEvents(string action);

    void AddActionEvent(string action, InputMapEventDescriptor descriptor);
}
