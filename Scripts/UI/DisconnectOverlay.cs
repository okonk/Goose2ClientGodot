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

            // Same card style as the login panel (Scenes/Login.tscn login_card).
            var card = new PanelContainer();
            card.AddThemeStyleboxOverride("panel", new StyleBoxFlat
            {
                BgColor = new Color(0.019608f, 0.031373f, 0.07451f, 0.93f),
                BorderWidthLeft = 2,
                BorderWidthTop = 2,
                BorderWidthRight = 2,
                BorderWidthBottom = 2,
                BorderColor = new Color(0.721569f, 0.52549f, 0.223529f, 0.95f),
                CornerRadiusTopLeft = 4,
                CornerRadiusTopRight = 4,
                CornerRadiusBottomLeft = 4,
                CornerRadiusBottomRight = 4,
                ShadowColor = new Color(0, 0, 0, 0.72f),
                ShadowSize = 12,
            });
            center.AddChild(card);

            var margins = new MarginContainer();
            foreach (var side in new[] { "margin_left", "margin_top", "margin_right", "margin_bottom" })
                margins.AddThemeConstantOverride(side, 20);
            card.AddChild(margins);

            var vbox = new VBoxContainer
            {
                Alignment = BoxContainer.AlignmentMode.Center
            };
            vbox.AddThemeConstantOverride("separation", 16);
            margins.AddChild(vbox);

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
