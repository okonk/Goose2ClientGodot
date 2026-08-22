using Godot;
using Goose2Client;
using System;
using System.Collections.Generic;

namespace Goose2Client.UI
{
    /// <summary>
    /// Central tooltip manager. Holds four tooltip controls and shows/hides them.
    ///
    /// TextTooltipEventHandler replacement: any Control wanting a text tooltip
    /// connects its MouseEntered / MouseExited signals to:
    ///   TooltipManager.Instance.ShowTextTooltip("text", self)
    ///   TooltipManager.Instance.HideTextTooltip()
    /// </summary>
    public partial class TooltipManager : Control, IScalableWindow
    {
        public static TooltipManager Instance { get; private set; }

        private ItemTooltipControl _itemTooltip;
        private SpellTooltipControl _spellTooltip;
        private TextTooltipControl _textTooltip;
        private MapItemTooltipControl _mapItemTooltip;

        private List<UiScaleLayout.GeomRecord> _geom = null!;

        public override void _Ready()
        {
            Instance = this;

            _itemTooltip = GetNode<ItemTooltipControl>("ItemTooltip");
            _spellTooltip = GetNode<SpellTooltipControl>("SpellTooltip");
            _textTooltip = GetNode<TextTooltipControl>("TextTooltip");
            _mapItemTooltip = GetNode<MapItemTooltipControl>("MapItemTooltip");

            // The four dynamic tooltip nodes carry ui_scale_skip meta (Tooltips.tscn); the snapshot excludes them.
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

        public void HideMapItemTooltip() => _mapItemTooltip.Visible = false;

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

        public void HideAll()
        {
            _itemTooltip.Visible = false;
            _spellTooltip.Visible = false;
            _textTooltip.Visible = false;
            _mapItemTooltip.Visible = false;
        }
    }
}
