using Goose2Client;

namespace Goose2Client.UI;

public partial class InfoWindowCreator : BaseMultipleWindowManager<InfoWindow>
{
    public override string PrefabPath => "res://Scenes/UI/InfoWindow.tscn";
    public override WindowFrames WindowFrame => WindowFrames.GenericInfo;
}
