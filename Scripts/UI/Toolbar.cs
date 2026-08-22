using Godot;
using Goose2Client;
using System.Collections.Generic;

namespace Goose2Client.UI;

public partial class Toolbar : HBoxContainer, IScalableWindow
{
    private List<UiScaleLayout.GeomRecord> _geom = null!;

    public override void _Ready()
    {
        var applier = UiScaleApplier.Instance;
        _geom = UiScaleLayout.Snapshot(this);
        applier.RegisterWindow(this);
        Relayout();
        TreeExited += () => applier.UnregisterWindow(this);
    }

    public void Relayout()
    {
        UiScaleLayout.Apply(_geom, UiScaleApplier.Instance.Factor);
    }
}
