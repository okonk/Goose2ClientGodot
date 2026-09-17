using Goose2Client;

namespace Goose2Client.UI;

public partial class QuestWindowManager : BaseMultipleWindowManager<QuestWindow>
{
    public override string PrefabPath => "res://Scenes/UI/QuestWindow.tscn";
    public override bool MatchesFrame(WindowFrames frame) => frame == WindowFrames.Quest;
}
