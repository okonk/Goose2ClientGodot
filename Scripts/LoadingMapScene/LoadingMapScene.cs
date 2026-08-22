using System.Collections.Generic;
using Godot;

namespace Goose2Client;

public partial class LoadingMapScene : Control, IScalableWindow
{
    private List<UiScaleLayout.GeomRecord> _geom = null!;
    private Label _statusLabel;
    private string _mapName = "";

    public override void _Ready()
    {
        _statusLabel = GetNode<Label>("StatusLabel");
        UpdateLabel();

        var applier = UiScaleApplier.Instance;
        _geom = UiScaleLayout.Snapshot(this);
        applier.RegisterWindow(this);
        Relayout();
        TreeExited += () => applier.UnregisterWindow(this);
    }

    public void Relayout()
    {
        UiScaleLayout.Apply(_geom, UiScaleApplier.Instance.Factor);
        _statusLabel.UpdateMinimumSize(); // Label min goes stale on theme default font change; see LoginScene.Relayout
    }

    /// <summary>
    /// Called by GameManager.ChangeMap to display which map is loading.
    /// Safe to call before _Ready (e.g. from scene tree setup).
    /// </summary>
    public void SetMapName(string mapName)
    {
        _mapName = mapName;
        UpdateLabel();
    }

    private void UpdateLabel()
    {
        if (_statusLabel != null)
            _statusLabel.Text = $"Loading {_mapName}...";
    }

    // Step 5: build the TileMapLayer world here (ImportMap, MapManager.OnMapLoaded). Out of scope this step.
}
