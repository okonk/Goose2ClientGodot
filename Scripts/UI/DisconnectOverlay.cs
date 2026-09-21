using Godot;

namespace Goose2Client.UI
{
    /// <summary>
    /// Full-screen overlay shown when the server drops the connection while in-game.
    /// Offers a return-to-login path. Owned by the GameManager autoload so it survives
    /// scene swaps; hidden until <see cref="ShowDisconnect"/> is called.
    /// </summary>
    public partial class DisconnectOverlay : CanvasLayer
    {
        public DisconnectOverlay()
        {
            Name = "DisconnectOverlay";
            Layer = 120;
            Visible = false;

            var dim = new ColorRect
            {
                Color = new Color(0, 0, 0, 0.6f),
                MouseFilter = Control.MouseFilterEnum.Stop
            };
            dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            AddChild(dim);

            var center = new CenterContainer();
            center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            AddChild(center);

            var vbox = new VBoxContainer
            {
                Alignment = BoxContainer.AlignmentMode.Center
            };
            vbox.AddThemeConstantOverride("separation", 16);
            center.AddChild(vbox);

            var label = new Label
            {
                Text = "Disconnected from server",
                HorizontalAlignment = HorizontalAlignment.Center
            };
            vbox.AddChild(label);

            var button = new Button { Text = "Return to Login" };
            button.Pressed += OnReturnToLogin;
            vbox.AddChild(button);
        }

        public void ShowDisconnect() => Visible = true;

        private void OnReturnToLogin()
        {
            Visible = false;
            GameManager.Instance.ReturnToLogin();
        }
    }
}
