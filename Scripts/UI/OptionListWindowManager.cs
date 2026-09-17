using Goose2Client;

namespace Goose2Client.UI;

public partial class OptionListWindowManager : BaseMultipleWindowManager<OptionListWindow>
{
    public override string PrefabPath => "res://Scenes/UI/OptionListWindow.tscn";
    public override bool MatchesFrame(WindowFrames frame) => frame == WindowFrames.OptionList;
}
