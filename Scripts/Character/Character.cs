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
        public float HPPercent { get; private set; } = 1f;
        public float MPPercent { get; private set; } = 1f;
        public CharacterType CharacterType { get; private set; }
        // Server body state: 3 = unarmed (no-equip), 4=1hand, 5=staff, 6=2hand, 7=bow. Drives whether
        // slots play their -equip vs -no-equip idle/walk and which attack-<type> clip they swing.
        public int BodyState { get; private set; } = 3;

        // Per-slot live sprite + the graphic id it was built from (needed for the height lookup).
        private sealed class Slot { public AnimatedSprite2D Sprite; public int GraphicId; }
        private readonly Dictionary<CharacterSlot, Slot> _slots = new();
        private static AnimationHeights _heights;
        private AppearanceData _appearance;

        private Label _nameLabel;
        private ColorRect _hpBar;

        private void EnsureBars()
        {
            if (_hpBar != null) return;
            _hpBar = new ColorRect
            {
                Position = new Vector2(-16, -56),   // centered 32px-wide bar, just below the name label
                Size = new Vector2(32, 3),
                Color = Colors.Green,
                ZIndex = 20,
            };
            AddChild(_hpBar);
        }

        /// <summary>Update the HP bar (and accept MP for future use). hpPercent/mpPercent are 0..1.</summary>
        public void SetVitals(float hpPercent, float mpPercent)
        {
            HPPercent = hpPercent;
            MPPercent = mpPercent;
            EnsureBars();
            _hpBar.Size = new Vector2(32 * Mathf.Clamp(hpPercent, 0f, 1f), 3);
            _hpBar.Color = hpPercent > 0.66f ? Colors.Green : hpPercent > 0.33f ? Colors.Orange : Colors.Red;
        }

        private void EnsureNameLabel()
        {
            if (_nameLabel != null) return;
            _nameLabel = new Label
            {
                Text = CharacterName,
                HorizontalAlignment = HorizontalAlignment.Center,
                ZIndex = 20,
                Position = new Vector2(-50, -74),   // name sits on top; HP bar (-56) below it, no overlap
                Size = new Vector2(100, 16),
            };
            _nameLabel.AddThemeFontSizeOverride("font_size", 12);
            _nameLabel.AddThemeConstantOverride("outline_size", 4);
            _nameLabel.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.9f));
            AddChild(_nameLabel);
        }

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

        /// <summary>(Re)build every slot from an MKC spawn packet (position + appearance).</summary>
        public void SetAppearance(MakeCharacterPacket p)
        {
            LoginId = p.LoginId;
            CharacterName = p.Name;
            BodyState = p.BodyState;
            MoveSpeed = p.MoveSpeed <= 0 ? 250 : p.MoveSpeed;
            X = p.MapX; Y = p.MapY; Facing = p.Facing;

            CharacterType = p.CharacterType;

            ApplyAppearance(p.BodyId, p.BodyR, p.BodyG, p.BodyB, p.BodyA,
                            p.HairId, p.HairR, p.HairG, p.HairB, p.HairA,
                            p.FaceId, p.DisplayedEquipment);

            TeleportTo(p.MapX, p.MapY);   // no walk anim
            ApplyDrawOrder();
            PlayState();

            EnsureNameLabel();
            _nameLabel.Text = CharacterName;
            SetVitals(p.HPPercent, 1f);
        }

        /// <summary>Appearance-only rebuild from a CHP packet. Keeps current position/facing/name;
        /// does NOT teleport (CHP carries no coordinates).</summary>
        public void SetAppearance(UpdateCharacterPacket p)
        {
            if (p.MoveSpeed > 0) MoveSpeed = p.MoveSpeed;   // keep existing speed if CHP omits it
            BodyState = p.BodyState;

            ApplyAppearance(p.BodyId, p.BodyR, p.BodyG, p.BodyB, p.BodyA,
                            p.HairId, p.HairR, p.HairG, p.HairB, p.HairA,
                            p.FaceId, p.DisplayedEquipment);

            ApplyDrawOrder();
            PlayState();
        }

        private void ApplyAppearance(int bodyId, int bodyR, int bodyG, int bodyB, int bodyA,
                                     int hairId, int hairR, int hairG, int hairB, int hairA,
                                     int faceId, int[][] eq)
        {
            int chestId = Equip(eq, 0, out var ec);
            int helmId  = Equip(eq, 1, out var eh);
            int legsId  = Equip(eq, 2, out var el);
            int feetId  = Equip(eq, 3, out var ef);
            int shieldId = Equip(eq, 4, out var es);
            int weaponId = Equip(eq, 5, out var ew);
            int mountId  = Equip(eq, 6, out var em);
            IsMounted = mountId != 0;

            // Underwear defaults when slots are empty (Unity SetUnderwear).
            int uwLegs = CharacterLayout.UnderwearLegs(bodyId, legsId);
            if (uwLegs != 0) { legsId = uwLegs; el = NoTint; }
            int uwChest = CharacterLayout.UnderwearChest(bodyId, chestId);
            if (uwChest != 0) { chestId = uwChest; ec = NoTint; }

            ApplySlot(CharacterSlot.Body, bodyId, RgbaColor(bodyR, bodyG, bodyB, bodyA));
            ApplySlot(CharacterSlot.Hair, hairId, RgbaColor(hairR, hairG, hairB, hairA));
            ApplySlot(CharacterSlot.Eyes, faceId, NoTint);
            ApplySlot(CharacterSlot.Chest, chestId, ec);
            ApplySlot(CharacterSlot.Helm, helmId, eh);
            ApplySlot(CharacterSlot.Legs, legsId, el);
            ApplySlot(CharacterSlot.Feet, feetId, ef);
            ApplySlot(CharacterSlot.Shield, shieldId, es);
            ApplySlot(CharacterSlot.Weapon, weaponId, ew);
            ApplySlot(CharacterSlot.Mount, mountId, em);

            _appearance = new AppearanceData(bodyId, RgbaColor(bodyR, bodyG, bodyB, bodyA),
                hairId, RgbaColor(hairR, hairG, hairB, hairA), faceId,
                chestId, ec, helmId, eh);
        }

        // The slot tint shader reads the alpha as a BLEND FACTOR (lerp texture->tint), NOT opacity
        // (faithful to Unity's CharacterAnimation shader). So "no tint" is alpha 0, never white.
        private static Color NoTint => new Color(0f, 0f, 0f, 0f);

        private static Color RgbaColor(int r, int g, int b, int a)
            => a > 0 ? new Color(r / 255f, g / 255f, b / 255f, a / 255f) : NoTint;

        private static int Equip(int[][] eq, int i, out Color color)
        {
            color = NoTint;
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
            // Only dyed slots get the tint shader; untinted slots use the default canvas path so they
            // render byte-identically to pre-shader behaviour (no global color-management shift).
            if (tint.A > 0f)
            {
                if (s.Sprite.Material is not ShaderMaterial mat)
                    s.Sprite.Material = mat = new ShaderMaterial { Shader = TintShader };
                mat.SetShaderParameter("tint", tint);
            }
            else
            {
                s.Sprite.Material = null;
            }
        }

        // Faithful port of Unity Custom/CharacterAnimation: tint.a lerps the texture rgb toward the
        // tint rgb; final opacity is always the texture's own alpha, so a tint never fades the sprite.
        private static Shader _tintShader;
        private static Shader TintShader => _tintShader ??= new Shader
        {
            Code = @"shader_type canvas_item;
uniform vec4 tint : source_color = vec4(0.0);
void fragment() {
    vec4 tex = texture(TEXTURE, UV);
    COLOR = vec4(mix(tex.rgb, tint.rgb, tint.a), tex.a) * COLOR;
}"
        };

        private void RemoveSlot(CharacterSlot slot)
        {
            if (_slots.Remove(slot, out var s)) s.Sprite.QueueFree();
        }

        public AppearanceData GetAppearance() => _appearance;

        public int Height =>
            _slots.TryGetValue(CharacterSlot.Body, out var b)
                ? _heights.GetHeight($"Body-{b.GraphicId}-{ResolveClip(b, "idle", BodyState) ?? "idle-down"}")
                : 0;

        /// <summary>Play the caster's spell-cast pose. Locked like an attack so walk/idle don't clobber it.</summary>
        public void Cast()
        {
            _attackLocked = true;
            _attackTimer = AttackDuration();
            PlayCurrent();
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

        private Vector2 _targetPosition;
        private bool _moving;
        protected bool IsMoving => _moving;   // replaces the Task 6 stub
        private bool _attackLocked;
        private double _attackTimer;
        protected bool AttackLocked => _attackLocked;   // replaces the Task 6 stub
        private readonly AttackGate _attackGate = new();

        public void TriggerAttack()
        {
            _attackLocked = true;
            _attackTimer = AttackDuration();
            PlayCurrent();   // CharacterMotion.State returns "attack" while locked -> all slots swing
        }

        private double AttackDuration()
        {
            // Time the lock to the Body's actual attack clip (weapon-type aware); fallback 0.5s.
            if (_slots.TryGetValue(CharacterSlot.Body, out var body) &&
                ResolveClip(body, "attack", BodyState) is { } clip &&
                body.Sprite.SpriteFrames is { } frames)
            {
                int n = frames.GetFrameCount(clip);
                float fps = (float)frames.GetAnimationSpeed(clip);
                if (fps > 0) return n / fps;
            }
            return 0.5;
        }

        /// <summary>Server (or local prediction) says this character stepped to (x,y).</summary>
        public void MoveTo(int x, int y)
        {
            if (x != X) Facing = x > X ? Direction.Right : Direction.Left;
            else if (y != Y) Facing = y > Y ? Direction.Down : Direction.Up;
            X = x; Y = y;
            _targetPosition = Goose2Client.Map.MapCoords.TileBottomCenter(x, y);
            _moving = true;
            ApplyDrawOrder();   // facing may have changed -> reorder shield/weapon
            PlayState();
        }

        /// <summary>Instant placement (spawn / SUP teleport) — no walk animation.</summary>
        public void TeleportTo(int x, int y)
        {
            X = x; Y = y;
            Position = Goose2Client.Map.MapCoords.TileBottomCenter(x, y);
            _targetPosition = Position;
            _moving = false;
        }

        public override void _Process(double delta)
        {
            ProcessLocalInput(delta);

            if (_moving)
            {
                float speed = CharacterMotion.PixelsPerSecond(MoveSpeed);
                Position = Position.MoveToward(_targetPosition, speed * (float)delta);
                if (Position.IsEqualApprox(_targetPosition))
                {
                    _moving = false;
                    PlayState();   // back to idle/mounted-idle
                }
            }
            TickAttackLock(delta);   // defined in Task 8
        }

        private const double MoveRepeatDelay = 0.12;   // Unity used ~0.1s debounce
        private double _moveCooldown;

        private void ProcessLocalInput(double delta)
        {
            if (!IsLocalPlayer) return;
            if (GetViewport().GuiGetFocusOwner() is LineEdit) return;   // ignore movement/attack while typing in chat
            _moveCooldown -= delta;

            if (Input.IsActionJustPressed("Attack"))
            {
                int ws = GameManager.Instance.CurrentMapManager?.WeaponSpeed ?? 0;
                if (_attackGate.TryAttack(Time.GetTicksMsec() / 1000.0, ws))
                {
                    TriggerAttack();
                    GameManager.Instance.NetworkClient.Attack();
                }
            }

            if (_moving || _moveCooldown > 0) return;

            Direction? dir = null;
            if (Input.IsActionPressed("MoveUp")) dir = Direction.Up;
            else if (Input.IsActionPressed("MoveDown")) dir = Direction.Down;
            else if (Input.IsActionPressed("MoveLeft")) dir = Direction.Left;
            else if (Input.IsActionPressed("MoveRight")) dir = Direction.Right;
            if (dir == null) return;

            var (dx, dy) = Delta(dir.Value);
            int nx = X + dx, ny = Y + dy;
            var map = GetParent()?.GetParent() as Goose2Client.MapManager;   // Characters -> Map(MapManager)
            if (map != null && map.IsValidMove(nx, ny))
            {
                MoveTo(nx, ny);
                GameManager.Instance.NetworkClient.Move(dir.Value);
            }
            else if (Facing != dir.Value)
            {
                SetFacing(dir.Value);
                GameManager.Instance.NetworkClient.Face(dir.Value);
            }
            _moveCooldown = MoveRepeatDelay;
        }

        private static (int dx, int dy) Delta(Direction d) => d switch
        {
            Direction.Up => (0, -1),
            Direction.Down => (0, 1),
            Direction.Left => (-1, 0),
            Direction.Right => (1, 0),
            _ => (0, 0),
        };

        protected void TickAttackLock(double delta)
        {
            if (!_attackLocked) return;
            _attackTimer -= delta;
            if (_attackTimer <= 0)
            {
                _attackLocked = false;
                PlayCurrent();   // resume walk/idle/mounted-*
            }
        }

        /// <summary>Fan the current state out to every slot. The Mount slot itself always plays its
        /// own non-mounted pose (Unity forces the mount to BodyState 3); rider slots use mounted-*.</summary>
        protected void PlayCurrent()
        {
            foreach (var (slot, s) in _slots)
            {
                bool slotMounted = IsMounted && slot != CharacterSlot.Mount;
                string motion = CharacterMotion.State(IsMoving, AttackLocked, slotMounted);
                // The mount itself always animates as an unmounted walking body (Unity forces state 3).
                int state = slot == CharacterSlot.Mount ? 3 : BodyState;
                if (ResolveClip(s, motion, state) is not { } clip) continue;

                int h = _heights.GetHeight($"{HeightPrefix(slot)}-{s.GraphicId}-{clip}");
                s.Sprite.Offset = new Vector2(0, CharacterAnchor.OffsetY(h));
                s.Sprite.Play(clip);
            }
        }

        /// <summary>First candidate clip (per BodyState/equip/weapon-type) that this slot's
        /// SpriteFrames actually contains, or null if none match.</summary>
        private string ResolveClip(Slot s, string motion, int state)
        {
            var frames = s.Sprite.SpriteFrames;
            if (frames == null) return null;
            foreach (var cand in AnimationNames.Candidates(motion, state, Facing))
                if (frames.HasAnimation(cand)) return cand;
            return null;
        }
    }
}
