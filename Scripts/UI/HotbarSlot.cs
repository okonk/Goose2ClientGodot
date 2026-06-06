using Godot;
using System;

namespace Goose2Client.UI
{
    /// <summary>
    /// Hotbar slot — can hold an item OR a spell.
    /// Handles hover tooltips, double-click to use, and four-way drag/drop
    /// (item, spell, hotbar-to-hotbar swap).
    /// </summary>
    public partial class HotbarSlot : Panel
    {
        private TextureRect _icon;
        private Label _count;
        private TextureProgressBar _cooldownOverlay;

        public Action<int> OnUseSlot { get; set; }
        public Action OnSaveSlots { get; set; }

        public int SlotNumber { get; set; }
        public IWindow Window { get; set; }

        public int ItemSlotIndex = -1;
        public int SpellSlotIndex = -1;

        public ItemStats ItemStats { get; private set; }
        public SpellInfo SpellInfo { get; private set; }

        public bool IsEmpty => ItemStats == null && SpellInfo == null;

        public bool CanUse()
        {
            if (IsEmpty)
                return false;

            if (SpellInfo != null &&
                GameManager.Instance.SpellCooldownManager.GetCooldownRemaining(SpellInfo) > TimeSpan.Zero)
                return false;

            return true;
        }

        public override void _Ready()
        {
            _icon = GetNode<TextureRect>("Icon");
            _count = GetNode<Label>("Count");
            _cooldownOverlay = GetNode<TextureProgressBar>("CooldownOverlay");

            MouseEntered += OnMouseEntered;
            MouseExited += OnMouseExited;
        }

        public override void _Process(double delta)
        {
            if (SpellInfo == null)
                return;

            if (SpellInfo.Cooldown == TimeSpan.Zero)
                return;

            _cooldownOverlay.Value = GetCooldownRemainingPercent();
        }

        public void SetItem(ItemStats stats)
        {
            Clear();

            ItemStats = stats;
            ItemSlotIndex = stats.SlotNumber;
            Icon.Apply(_icon, stats.GraphicFile, stats.GraphicId,
                stats.GraphicR, stats.GraphicG, stats.GraphicB, stats.GraphicA);
            _count.Text = stats.StackSize.ToString();
            _count.Visible = stats.StackSize > 1;
        }

        public void SetSpell(SpellInfo spell)
        {
            Clear(keepSpellSlot: SpellSlotIndex);

            if (string.IsNullOrEmpty(spell?.Name))
                return;

            SpellInfo = spell;
            SpellSlotIndex = spell.SlotNumber;
            Icon.Apply(_icon, spell.GraphicFile, spell.GraphicId, 0, 0, 0, 0);
            _cooldownOverlay.Value = GetCooldownRemainingPercent();
            _cooldownOverlay.Visible = true;
        }

        public void Clear(int keepItemSlot = -1, int keepSpellSlot = -1)
        {
            Icon.Clear(_icon);
            _cooldownOverlay.Visible = false;
            _count.Visible = false;
            ItemStats = null;
            SpellInfo = null;
            ItemSlotIndex = keepItemSlot;
            SpellSlotIndex = keepSpellSlot;
        }

        private void SaveSlots()
        {
            OnSaveSlots?.Invoke();
        }

        public void OnInventorySlot(ItemStats stats)
        {
            if (ItemSlotIndex == stats.SlotNumber)
                SetItem(stats);
        }

        public void OnClearInventorySlot(int slotNumber)
        {
            if (ItemSlotIndex == slotNumber)
                Clear(keepItemSlot: ItemSlotIndex);
        }

        public void OnSpellbookSlot(SpellInfo spell)
        {
            if (SpellSlotIndex == spell.SlotNumber)
                SetSpell(spell);
        }

        private float GetCooldownRemainingPercent()
        {
            var remaining = GameManager.Instance.SpellCooldownManager.GetCooldownRemaining(SpellInfo);
            if (remaining == TimeSpan.Zero)
                return 0;
            return (float)(remaining.TotalMilliseconds / SpellInfo.Cooldown.TotalMilliseconds);
        }

        private void OnMouseEntered()
        {
            if (SpellInfo != null)
            {
                TooltipManager.Instance.ShowSpellTooltip(SpellInfo, this);
            }
            else if (ItemStats != null)
            {
                TooltipManager.Instance.ShowTextTooltip(ItemStats.Name, this);
            }
        }

        private void OnMouseExited()
        {
            TooltipManager.Instance.HideTextTooltip();
            TooltipManager.Instance.HideSpellTooltip();
        }

        public override void _GuiInput(InputEvent @event)
        {
            if (@event is InputEventMouseButton mb &&
                mb.ButtonIndex == MouseButton.Left &&
                mb.DoubleClick)
            {
                OnUseSlot?.Invoke(SlotNumber);
            }
        }

        // ── Drag / Drop ──────────────────────────────────────────────

        public override Variant _GetDragData(Vector2 atPosition)
        {
            if (IsEmpty)
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
                { "kind", "hotbar" },
                { "slot", this }
            };
        }

        public override bool _CanDropData(Vector2 atPosition, Variant data)
        {
            if (data.VariantType != Variant.Type.Dictionary)
                return false;

            var d = data.AsGodotDictionary();
            if (!d.ContainsKey("kind"))
                return false;

            var kind = d["kind"].AsString();
            return kind == "item" || kind == "spell" || kind == "hotbar";
        }

        public override void _DropData(Vector2 atPosition, Variant data)
        {
            var d = data.AsGodotDictionary();
            var kind = d["kind"].AsString();

            if (kind == "spell")
            {
                var src = d["slot"].As<SpellSlot>();
                if (src != null && src.HasSpell)
                {
                    SetSpell(src.Info);
                    SaveSlots();
                }
            }
            else if (kind == "item")
            {
                var src = d["slot"].As<ItemSlot>();
                if (src != null && src.HasItem &&
                    (src.Window.WindowFrame == WindowFrames.Inventory ||
                     src.Window.WindowFrame == WindowFrames.Equipped))
                {
                    SetItem(src.Stats);
                    SaveSlots();
                }
            }
            else if (kind == "hotbar")
            {
                var src = d["slot"].As<HotbarSlot>();
                if (src != null && !src.IsEmpty)
                {
                    var (t, s) = HotbarSwap.Resolve(ContentOf(this), ContentOf(src));
                    Apply(this, t);
                    Apply(src, s);
                    SaveSlots();
                }
            }
        }

        private static HotbarContent ContentOf(HotbarSlot slot)
        {
            if (slot.ItemStats != null)
                return HotbarContent.FromItem(slot.ItemStats);
            if (slot.SpellInfo != null)
                return HotbarContent.FromSpell(slot.SpellInfo);
            return HotbarContent.Empty;
        }

        private static void Apply(HotbarSlot slot, HotbarContent content)
        {
            switch (content.Kind)
            {
                case HotbarContentKind.Item:
                    slot.SetItem(content.Item);
                    break;
                case HotbarContentKind.Spell:
                    slot.SetSpell(content.Spell);
                    break;
                case HotbarContentKind.Empty:
                    slot.Clear();
                    break;
            }
        }
    }
}
