using Godot;

namespace Goose2Client.UI
{
    /// <summary>
    /// Full-screen drop region that sits behind the windows.
    /// Accepts item and hotbar drops to drop items on the ground or clear hotbar slots.
    /// </summary>
    public partial class WorldDropTarget : Control
    {
        public override void _Ready()
        {
            MouseFilter = MouseFilterEnum.Pass;
        }

        public override bool _CanDropData(Vector2 atPosition, Variant data)
        {
            if (data.VariantType != Variant.Type.Dictionary)
                return false;

            var d = data.AsGodotDictionary();
            if (!d.ContainsKey("kind"))
                return false;

            var kind = d["kind"].AsString();
            return kind == "item" || kind == "hotbar";
        }

        public override void _DropData(Vector2 atPosition, Variant data)
        {
            var d = data.AsGodotDictionary();
            var kind = d["kind"].AsString();

            if (kind == "item")
            {
                var src = d["slot"].As<ItemSlot>();
                if (src != null && src.HasItem && src.Window != null && src.Window.WindowFrame == WindowFrames.Inventory)
                    GameManager.Instance.NetworkClient.Drop(src.SlotNumber, Helpers.GetStackSplitAmount(src.StackSize));
            }
            else if (kind == "hotbar")
            {
                var src = d["slot"].As<HotbarSlot>();
                if (src != null && !src.IsEmpty)
                {
                    src.Clear();
                    // Persist the cleared slot, same as every other hotbar mutation path —
                    // otherwise it reappears from the settings file on next login.
                    src.OnSaveSlots?.Invoke();
                }
            }
        }
    }
}
