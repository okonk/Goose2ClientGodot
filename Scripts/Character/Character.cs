using System.Collections.Generic;
using Godot;
using Goose2Client.Network.Packets;

namespace Goose2Client.Character
{
    public partial class Character : Node2D
    {
        public int LoginId { get; private set; }
        public string CharacterName { get; private set; }
        public int X { get; private set; }
        public int Y { get; private set; }
        public Direction Facing { get; private set; } = Direction.Down;
        public int MoveSpeed { get; private set; } = 250;
        public bool IsMounted { get; private set; }
        public bool IsLocalPlayer { get; set; }

        // Per-slot live sprite + the graphic id it was built from (needed for the height lookup).
        private sealed class Slot { public AnimatedSprite2D Sprite; public int GraphicId; }
        private readonly Dictionary<CharacterSlot, Slot> _slots = new();
        private static AnimationHeights _heights;

        // The converter's height-prefix uses its AnimationType name, which differs from the
        // asset folder for Mount/Shield/Weapon (those reuse Body/Hand art). Map slot -> prefix.
        private static string HeightPrefix(CharacterSlot slot) => slot switch
        {
            CharacterSlot.Mount or CharacterSlot.Body => "Body",
            CharacterSlot.Eyes => "Eyes",
            CharacterSlot.Feet => "Feet",
            CharacterSlot.Legs => "Legs",
            CharacterSlot.Chest => "Chest",
            CharacterSlot.Hair => "Hair",
            CharacterSlot.Helm => "Helm",
            CharacterSlot.Shield or CharacterSlot.Weapon => "Hand",
            _ => "Body",
        };

        public override void _Ready()
        {
            _heights ??= AnimationHeights.Load(
                ProjectSettings.GlobalizePath("res://Assets/Resources/AnimationHeights.txt"));
        }

        /// <summary>(Re)build every slot from an MKC/CHP-shaped appearance.</summary>
        public void SetAppearance(MakeCharacterPacket p)
        {
            LoginId = p.LoginId;
            CharacterName = p.Name;
            MoveSpeed = p.MoveSpeed <= 0 ? 250 : p.MoveSpeed;
            X = p.MapX; Y = p.MapY; Facing = p.Facing;

            var eq = p.DisplayedEquipment;
            int chestId = Equip(eq, 0, out var ec);
            int helmId  = Equip(eq, 1, out var eh);
            int legsId  = Equip(eq, 2, out var el);
            int feetId  = Equip(eq, 3, out var ef);
            int shieldId = Equip(eq, 4, out var es);
            int weaponId = Equip(eq, 5, out var ew);
            int mountId  = Equip(eq, 6, out var em);
            IsMounted = mountId != 0;

            // Underwear defaults when slots are empty (Unity SetUnderwear).
            int uwLegs = CharacterLayout.UnderwearLegs(p.BodyId, legsId);
            if (uwLegs != 0) { legsId = uwLegs; el = Colors.White; }
            int uwChest = CharacterLayout.UnderwearChest(p.BodyId, chestId);
            if (uwChest != 0) { chestId = uwChest; ec = Colors.White; }

            ApplySlot(CharacterSlot.Body, p.BodyId, RgbaColor(p.BodyR, p.BodyG, p.BodyB, p.BodyA));
            ApplySlot(CharacterSlot.Hair, p.HairId, RgbaColor(p.HairR, p.HairG, p.HairB, p.HairA));
            ApplySlot(CharacterSlot.Eyes, p.FaceId, Colors.White);
            ApplySlot(CharacterSlot.Chest, chestId, ec);
            ApplySlot(CharacterSlot.Helm, helmId, eh);
            ApplySlot(CharacterSlot.Legs, legsId, el);
            ApplySlot(CharacterSlot.Feet, feetId, ef);
            ApplySlot(CharacterSlot.Shield, shieldId, es);
            ApplySlot(CharacterSlot.Weapon, weaponId, ew);
            ApplySlot(CharacterSlot.Mount, mountId, em);

            TeleportTo(p.MapX, p.MapY);   // sets Position + _targetPosition (Task 7), no walk anim
            ApplyDrawOrder();
            PlayState();
        }

        private static Color RgbaColor(int r, int g, int b, int a)
            => a > 0 ? new Color(r / 255f, g / 255f, b / 255f, a / 255f) : Colors.White;

        private static int Equip(int[][] eq, int i, out Color color)
        {
            color = Colors.White;
            if (eq == null || i >= eq.Length || eq[i] == null || eq[i].Length < 5) return 0;
            if (eq[i][4] > 0) color = new Color(eq[i][1] / 255f, eq[i][2] / 255f, eq[i][3] / 255f, eq[i][4] / 255f);
            return eq[i][0];
        }

        private void ApplySlot(CharacterSlot slot, int graphicId, Color tint)
        {
            if (graphicId <= 0) { RemoveSlot(slot); return; }
            var path = $"res://Assets/Sprites/{CharacterLayout.TypeFolder(slot)}/{graphicId}/animations.tres";
            if (!ResourceLoader.Exists(path)) { RemoveSlot(slot); return; }

            if (!_slots.TryGetValue(slot, out var s))
            {
                s = new Slot { Sprite = new AnimatedSprite2D
                {
                    Name = slot.ToString(),
                    TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
                } };
                AddChild(s.Sprite);
                _slots[slot] = s;
            }
            s.GraphicId = graphicId;
            s.Sprite.SpriteFrames = GD.Load<SpriteFrames>(path);
            s.Sprite.SelfModulate = tint;
        }

        private void RemoveSlot(CharacterSlot slot)
        {
            if (_slots.Remove(slot, out var s)) s.Sprite.QueueFree();
        }

        /// <summary>Order the slot sprites back-to-front by SortOrder(slot, Facing) via child order
        /// (Unity used per-direction sortingOrder; we use sibling order to stay inside the z-band).</summary>
        private void ApplyDrawOrder()
        {
            var ordered = new List<KeyValuePair<CharacterSlot, Slot>>(_slots);
            ordered.Sort((a, b) =>
                CharacterLayout.SortOrder(a.Key, Facing).CompareTo(CharacterLayout.SortOrder(b.Key, Facing)));
            for (int i = 0; i < ordered.Count; i++)
                MoveChild(ordered[i].Value.Sprite, i);   // lower SortOrder drawn first (behind)
        }

        public void SetFacing(Direction d) { Facing = d; ApplyDrawOrder(); PlayState(); }

        protected void PlayState()
        {
            if (AttackLocked) return;   // don't clobber a mid-attack animation (Task 8)
            PlayCurrent();
        }

        // Default stubs replaced in Task 7 (IsMoving) and Task 8 (AttackLocked); keep compilable.
        protected bool IsMoving => false;
        protected bool AttackLocked => false;

        // TEMPORARY: standalone placeholder for Task 6 only. Task 7 will REPLACE this with the
        // real TeleportTo (and add _targetPosition/_moving/_Process). Keep the signature identical.
        private void TeleportTo(int x, int y)
        {
            X = x; Y = y;
            Position = Goose2Client.Map.MapCoords.TileBottomCenter(x, y);
        }

        /// <summary>Fan the current state out to every slot. The Mount slot itself always plays its
        /// own non-mounted pose (Unity forces the mount to BodyState 3); rider slots use mounted-*.</summary>
        protected void PlayCurrent()
        {
            foreach (var (slot, s) in _slots)
            {
                bool slotMounted = IsMounted && slot != CharacterSlot.Mount;
                string state = CharacterMotion.State(IsMoving, AttackLocked, slotMounted);
                string clip = AnimationNames.Clip(state, Facing);
                var frames = s.Sprite.SpriteFrames;
                if (frames == null || !frames.HasAnimation(clip))
                    clip = AnimationNames.Clip(IsMoving ? "walk" : "idle", Facing);   // fallback to generic
                if (frames == null || !frames.HasAnimation(clip)) continue;

                int h = _heights.GetHeight($"{HeightPrefix(slot)}-{s.GraphicId}-{clip}");
                s.Sprite.Offset = new Vector2(0, CharacterAnchor.OffsetY(h));
                s.Sprite.Play(clip);
            }
        }
    }
}
