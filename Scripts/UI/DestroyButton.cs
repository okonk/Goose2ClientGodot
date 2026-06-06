using Godot;

namespace Goose2Client.UI
{
    /// <summary>
    /// Button that accepts dragged items, spells, or hotbar entries and destroys/clears them.
    /// </summary>
    public partial class DestroyButton : Button
    {
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

            if (kind == "item")
            {
                var src = d["slot"].As<ItemSlot>();
                if (src != null && src.HasItem && src.Window != null && src.Window.WindowFrame == WindowFrames.Inventory)
                    GameManager.Instance.NetworkClient.DestroyItem(src.SlotNumber);
            }
            else if (kind == "spell")
            {
                var src = d["slot"].As<SpellSlot>();
                if (src != null && src.HasSpell)
                    GameManager.Instance.NetworkClient.DestroySpell(src.SlotNumber);
            }
            else if (kind == "hotbar")
            {
                var src = d["slot"].As<HotbarSlot>();
                if (src != null && !src.IsEmpty)
                    src.Clear();
            }
        }
    }
}
