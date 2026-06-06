using Godot;

namespace Goose2Client;

public partial class LoadingMapScene : Control
{
    private Label _statusLabel;
    private string _mapName = "";

    public override void _Ready()
    {
        _statusLabel = GetNode<Label>("StatusLabel");
        UpdateLabel();
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
