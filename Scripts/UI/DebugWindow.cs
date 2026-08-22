using Godot;
using Goose2Client;
using System.Collections.Generic;

namespace Goose2Client.UI
{
    /// <summary>
    /// HUD overlay showing current FPS and game version. Port of Unity DebugWindow.
    /// </summary>
    public partial class DebugWindow : Control, IScalableWindow
    {
        private Label _fpsText;
        private Label _versionText;

        public static int FramesPerSecond { get; private set; }

        private double _accum;
        private const double Frequency = 0.5;

        private List<UiScaleLayout.GeomRecord> _geom = null!;

        public override void _Ready()
        {
            _fpsText = GetNode<Label>("FpsText");
            _versionText = GetNode<Label>("VersionText");

            _versionText.Text = (string)ProjectSettings.GetSetting("application/config/version", "");

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
