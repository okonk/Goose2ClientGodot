using Godot;

namespace Goose2Client.UI
{
    /// <summary>
    /// HUD overlay showing current FPS and game version. Port of Unity DebugWindow.
    /// </summary>
    public partial class DebugWindow : Control
    {
        private Label _fpsText;
        private Label _versionText;

        public static int FramesPerSecond { get; private set; }

        private double _accum;
        private const double Frequency = 0.5;

        public override void _Ready()
        {
            _fpsText = GetNode<Label>("FpsText");
            _versionText = GetNode<Label>("VersionText");

            _versionText.Text = (string)ProjectSettings.GetSetting("application/config/version", "");
        }

        public override void _Process(double delta)
        {
            _accum += delta;
            if (_accum < Frequency) return;

            _accum = 0;
            FramesPerSecond = (int)Engine.GetFramesPerSecond();
            _fpsText.Text = $"{FramesPerSecond} fps";
        }
    }
}
