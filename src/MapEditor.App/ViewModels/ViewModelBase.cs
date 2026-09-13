using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using MapEditor.App.Rendering;

namespace MapEditor.App.ViewModels;

internal abstract class ViewModelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected void OnPropertyChanged(PropertyChangedEventArgs args)
    {
        PropertyChanged?.Invoke(this, args);
    }

    protected Delegate[] PropertyChangedHandlers => PropertyChanged?.GetInvocationList() ?? Array.Empty<Delegate>();

    // Each handler gets every argument; a failing handler records one failure per call
    // and cannot prevent later handlers from running.
    protected void RaisePropertyChangedSafely(PropertyChangedEventArgs[] arguments, Delegate[] handlers, PublicationNotificationErrors errors)
    {
        foreach (Delegate handler in handlers)
        {
            try
            {
                foreach (PropertyChangedEventArgs argument in arguments)
                {
                    ((PropertyChangedEventHandler)handler)(this, argument);
                }
            }
            catch (Exception ex)
            {
                errors.TryAdd(ex);
            }
        }
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
