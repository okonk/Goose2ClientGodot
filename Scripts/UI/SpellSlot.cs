using Godot;
using System;

namespace Goose2Client.UI
{
    /// <summary>
    /// Spell slot — displays a spell icon + radial cooldown overlay,
    /// handles hover tooltips, double-click actions, and built-in Godot drag/drop.
    /// </summary>
    public partial class SpellSlot : Panel
    {
        private TextureRect _icon;
        private TextureProgressBar _cooldownOverlay;

        public SpellInfo Info { get; private set; }
        public bool HasSpell => Info != null;

        public int SlotNumber { get; set; }
        public IWindow Window { get; set; }

        public Action<SpellInfo> OnDoubleClick { get; set; }
        public Action<int, int> OnMoveSpell { get; set; }

        public override void _Ready()
        {
            _icon = GetNode<TextureRect>("Icon");
            _cooldownOverlay = GetNode<TextureProgressBar>("CooldownOverlay");

            MouseEntered += OnMouseEntered;
            MouseExited += OnMouseExited;
        }

        public void SetSpell(SpellInfo info)
        {
            if (string.IsNullOrEmpty(info?.Name))
            {
                ClearSpell();
                return;
            }

            Info = info;
            Goose2Client.UI.Icon.Apply(_icon, info.GraphicFile, info.GraphicId, 0, 0, 0, 0);
        }

        public void ClearSpell()
        {
            Info = null;
            Goose2Client.UI.Icon.Clear(_icon);
            _cooldownOverlay.Value = 0;
        }

        private void OnMouseEntered()
        {
            if (HasSpell)
                TooltipManager.Instance.ShowSpellTooltip(Info, this);
        }

        private void OnMouseExited()
        {
            TooltipManager.Instance.HideSpellTooltip();
        }

        public override void _GuiInput(InputEvent @event)
        {
            if (@event is InputEventMouseButton mb &&
                mb.ButtonIndex == MouseButton.Left &&
                mb.DoubleClick &&
                HasSpell)
            {
                if (GameManager.Instance.SpellCooldownManager.GetCooldownRemaining(Info) > TimeSpan.Zero)
                    return;

                OnDoubleClick?.Invoke(Info);
            }
        }

        public override void _Process(double delta)
        {
            if (!HasSpell)
                return;

            if (Info.Cooldown == TimeSpan.Zero)
            {
                _cooldownOverlay.Value = 0;
                return;
            }

            var remaining = GameManager.Instance.SpellCooldownManager.GetCooldownRemaining(Info);
            _cooldownOverlay.Value = remaining == TimeSpan.Zero
                ? 0
                : remaining.TotalMilliseconds / Info.Cooldown.TotalMilliseconds;
        }

        public override Variant _GetDragData(Vector2 atPosition)
        {
            if (!HasSpell)
                return default;

            var preview = new TextureRect
            {
                Texture = _icon.Texture,
                CustomMinimumSize = _icon.Size,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspect
            };
            SetDragPreview(preview);

            return new Godot.Collections.Dictionary
            {
                { "kind", "spell" },
                { "slot", this }
            };
        }

        public override bool _CanDropData(Vector2 atPosition, Variant data)
        {
            if (data.VariantType != Variant.Type.Dictionary)
                return false;

            var d = data.AsGodotDictionary();
            return d.ContainsKey("kind") && d["kind"].AsString() == "spell";
        }

        public override void _DropData(Vector2 atPosition, Variant data)
        {
            var d = data.AsGodotDictionary();
            var src = d["slot"].As<SpellSlot>();
            if (src != null && src.HasSpell)
                OnMoveSpell?.Invoke(src.SlotNumber, SlotNumber);
        }
    }
}
