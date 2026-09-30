using System;
using System.Collections.Generic;

namespace Goose2Client.InputBindings;

public interface IInputMapAdapter
{
    InputBindingSet CaptureFactory(IReadOnlyList<InputActionDefinition> catalog);

    void Replace(InputBindingSet previous, InputBindingSet next);
}

public sealed class InputMapConfigurationException : Exception
{
    public InputMapConfigurationException(string message) : base(message)
    {
    }
}

public sealed class InputMapAdapterFailure : Exception
{
    public bool RuntimeRestored { get; }

    public Exception OriginalFailure { get; }

    public Exception? RestorationFailure { get; }

    public InputMapAdapterFailure(bool runtimeRestored, Exception originalFailure, Exception? restorationFailure)
        : base(
            runtimeRestored
                ? $"InputMap replacement failed and the previous map was restored: {originalFailure.Message}"
                : $"InputMap replacement failed and the previous map could not be restored; manual recovery is required. Original: {originalFailure.Message}; Restoration: {restorationFailure?.Message}",
            originalFailure)
    {
        RuntimeRestored = runtimeRestored;
        OriginalFailure = originalFailure;
        RestorationFailure = restorationFailure;
    }
}
