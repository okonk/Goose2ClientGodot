using Godot;
using System;
using Goose2Client;

namespace Goose2Client.UI;

public enum ToolbarItemType
{
    Destroy,
    CombineBag,
    Options,
    Exit
}

/// <summary>
/// Toolbar button — handles CombineBag, Options, Exit actions.
/// Destroy is handled by the separate DestroyButton class.
/// </summary>
public partial class ToolbarItem : Button
{
    [Export] public ToolbarItemType ItemType { get; set; }

    public Action OnOptions { get; set; }

    public override void _Ready()
    {
        base._Ready();
        Pressed += OnPressed;
    }

    private void OnPressed()
    {
        switch (ItemType)
        {
            case ToolbarItemType.CombineBag:
                GameManager.Instance.NetworkClient.OpenCombineBag();
                break;
            case ToolbarItemType.Options:
                OnOptions?.Invoke();
                break;
            case ToolbarItemType.Exit:
                GameManager.Instance.Quit();
                break;
            case ToolbarItemType.Destroy:
                // No-op — actual destroy is the DestroyButton drop target
                break;
        }
    }
}
