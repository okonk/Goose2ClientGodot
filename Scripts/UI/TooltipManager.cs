using Godot;
using Goose2Client;
using Goose2Client.Character;
using System;
using System.Collections.Generic;

namespace Goose2Client.UI
{
    /// <summary>
    /// Central tooltip manager. Holds five tooltip controls and shows/hides them.
    ///
    /// TextTooltipEventHandler replacement: any Control wanting a text tooltip
    /// connects its MouseEntered / MouseExited signals to:
    ///   TooltipManager.Instance.ShowTextTooltip("text", self)
    ///   TooltipManager.Instance.HideTextTooltip()
    /// </summary>
    public partial class TooltipManager : Control, IScalableWindow
    {
        public static TooltipManager Instance { get; private set; } = null!;

        private ItemTooltipControl _itemTooltip = null!;
        private SpellTooltipControl _spellTooltip = null!;
        private TextTooltipControl _textTooltip = null!;
        private MapItemTooltipControl _mapItemTooltip = null!;
        private NameTooltipControl _nameTooltip = null!;

        public MapItemTooltipControl MapItemTooltip => _mapItemTooltip;

        private List<UiScaleLayout.GeomRecord> _geom = null!;

        public override void _Ready()
        {
            Instance = this;

            _itemTooltip = GetNode<ItemTooltipControl>("ItemTooltip");
            _spellTooltip = GetNode<SpellTooltipControl>("SpellTooltip");
            _textTooltip = GetNode<TextTooltipControl>("TextTooltip");
            _mapItemTooltip = GetNode<MapItemTooltipControl>("MapItemTooltip");
            _nameTooltip = GetNode<NameTooltipControl>("NameTooltip");

            // The five dynamic tooltip nodes carry ui_scale_skip meta (Tooltips.tscn); the snapshot excludes them.
            var applier = UiScaleApplier.Instance!;
            _geom = UiScaleLayout.Snapshot(this);
            applier.RegisterWindow(this);
            Relayout();
            TreeExited += () => applier.UnregisterWindow(this);
        }

        public void Relayout()
        {
            UiScaleLayout.Apply(_geom, UiScaleApplier.Instance!.Factor);
        }

        public void ShowItemTooltip(ItemStats stats, Control parent)
        {
            _itemTooltip.SetItem(stats, parent);
            _itemTooltip.Visible = true;
        }

        public void HideItemTooltip() => _itemTooltip.Visible = false;

        public void ShowSpellTooltip(SpellInfo spell, Control parent)
        {
            _spellTooltip.SetSpell(spell, parent);
            _spellTooltip.Visible = true;
        }

        public void HideSpellTooltip() => _spellTooltip.Visible = false;

        public void ShowMapItemTooltip(ItemStats stats, Node2D owner)
        {
            _mapItemTooltip.SetItem(stats, owner);
            _mapItemTooltip.Visible = true;
        }

        public void HideMapItemTooltip() => _mapItemTooltip.HideTooltip();

        public void HideMapItemTooltipIfMatching(ItemStats stats)
        {
            if (_mapItemTooltip.Item == stats)
                HideMapItemTooltip();
        }

        public void ShowTextTooltip(string text, Control parent)
        {
            _textTooltip.SetText(text, parent);
            _textTooltip.Visible = true;
        }

        public void HideTextTooltip() => _textTooltip.Visible = false;

        public void ShowNameTooltip(Goose2Client.Character.Character c) { _nameTooltip.SetCharacter(c); _nameTooltip.Visible = c != null; }

        public void HideNameTooltip() => _nameTooltip.Visible = false;

        public void HideAll()
        {
            _itemTooltip.Visible = false;
            _spellTooltip.Visible = false;
            _textTooltip.Visible = false;
            _mapItemTooltip.HideTooltip();
            _nameTooltip.Visible = false;
        }
    }
}
