using System.Collections.Generic;
using Godot;
using Goose2Client.Network.Packets;

namespace Goose2Client.Character
{
    public partial class Character : Node2D
    {
        public int LoginId { get; private set; }
        public string CharacterName { get; private set; }
        public string Title { get; private set; } = "";
        public string Surname { get; private set; } = "";
        public string FullName => NameFormatting.FullName(Title, CharacterName, Surname);
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
        private ColorRect _mpBar;

        // MP bar fill color, matching the Unity CharacterHealthBars prefab (RGB 32,60,128).
        private static readonly Color MpColor = new(0.1254902f, 0.23529412f, 0.5019608f);
        private const float BarWidth = 32f;
        private Goose2Client.Overlays.BattleText _battleText;
        private Overlays.ChatBubble _chatBubble;
        private Overlays.EmoteAnimation _emote;
        private readonly HealthBarAutoHide _healthBarAutoHide = new();

        private void EnsureBars()
        {
            if (_hpBar != null) return;
            // HP bar: 3px tall, centered 32px-wide bar, just below the name label.
            _hpBar = new ColorRect
            {
                Position = new Vector2(-16, -56),
                Size = new Vector2(BarWidth, 3),
                Color = GameColors.HpGreen,
                ZIndex = 20,
            };
            AddChild(_hpBar);
            // MP bar: 2px tall, stacked directly under the HP bar (Unity stacks MP below HP).
            _mpBar = new ColorRect
            {
                Position = new Vector2(-16, -53),
                Size = new Vector2(BarWidth, 2),
                Color = MpColor,
                ZIndex = 20,
            };
            AddChild(_mpBar);
        }

        /// <summary>Update the HP and MP bars. hpPercent/mpPercent are 0..1. Bars resize left-aligned
        /// (shrink from the right), matching the Unity CharacterHealthBar.</summary>
        public void SetVitals(float hpPercent, float mpPercent)
        {
            HPPercent = hpPercent;
            MPPercent = mpPercent;
            EnsureBars();
            _hpBar.Size = new Vector2(BarWidth * Mathf.Clamp(hpPercent, 0f, 1f), 3);
            _hpBar.Color = hpPercent > 0.66f ? GameColors.HpGreen : hpPercent > 0.33f ? GameColors.HpOrange : GameColors.HpRed;
            _mpBar.Size = new Vector2(BarWidth * Mathf.Clamp(mpPercent, 0f, 1f), 2);

            _healthBarAutoHide.OnVitalsChanged(hpPercent, mpPercent, Time.GetTicksMsec() / 1000.0);
            ApplyBarVisibility();
        }

        private void ApplyBarVisibility()
        {
            double now = Time.GetTicksMsec() / 1000.0;
            _healthBarAutoHide.Tick(now);
            bool visible = _healthBarAutoHide.Visible;
            if (_hpBar != null) _hpBar.Visible = visible;
            if (_mpBar != null) _mpBar.Visible = visible;
        }

        private void EnsureNameLabel()
        {
            if (_nameLabel != null) return;
            _nameLabel = new Label
            {
                Text = FullName,
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
            Title = p.Title ?? "";
            Surname = p.Surname ?? "";
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
            _nameLabel.Text = FullName;
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

        /// <summary>Hit-test: does a world-space point lie inside the Body slot's sprite rect?
        /// Body sprites are Centered, so the rect is center ± size/2.</summary>
        public bool ContainsPoint(Vector2 worldPoint)
        {
            if (!_slots.TryGetValue(CharacterSlot.Body, out var b) || b.Sprite.SpriteFrames == null) return false;
            var tex = b.Sprite.SpriteFrames.GetFrameTexture(b.Sprite.Animation, b.Sprite.Frame);
            if (tex == null) return false;
            var size = tex.GetSize();
            var center = GlobalPosition + b.Sprite.Offset;   // Centered sprite: Offset is the sprite center relative to origin
            var rect = new Rect2(center - size / 2f, size);
            return rect.HasPoint(worldPoint);
        }

        /// <summary>Play the caster's spell-cast pose. Locked like an attack so walk/idle don't clobber it.</summary>
        public void Cast()
        {
            BeginLock("cast");
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
        private string _lockedMotion;
        private double _attackTimer;
        protected bool AttackLocked => _lockedMotion != null;   // true iff a motion is locked
        private readonly AttackGate _attackGate = new();

        public void TriggerAttack()
        {
            BeginLock("attack");
        }

        private void BeginLock(string motion)
        {
            _lockedMotion = motion;
            _attackTimer = LockDuration(motion);
            PlayCurrent();
        }

        private double LockDuration(string motion)
        {
            // Time the lock to the Body's actual clip for the requested motion; fallback 0.5s.
            if (_slots.TryGetValue(CharacterSlot.Body, out var body) &&
                ResolveClip(body, motion, BodyState) is { } clip &&
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
            // Snap to previous target if a chained packet arrives mid-move
            if (_moving) Position = _targetPosition;

            // Facing priority: Down > Right > Up > Left; zero delta preserves facing
            int dx = x - X, dy = y - Y;
            if (dy > 0) Facing = Direction.Down;
            else if (dx > 0) Facing = Direction.Right;
            else if (dy < 0) Facing = Direction.Up;
            else if (dx < 0) Facing = Direction.Left;

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
            PlayState();   // walk -> idle, matching Unity SetMoving(false)
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
            ApplyBarVisibility();
        }

        private const double MoveStartDelay = 0.1;   // Unity hold threshold: short release → turn in place
        private double _movePressedTime;
        private bool _wasMovingVertical;
        private Direction? _heldDir;

        private void ProcessLocalInput(double delta)
        {
            if (!IsLocalPlayer) return;
            if (GameManager.Instance.IsTargeting) return;   // spell targeting consumes movement/attack input
            if (GetViewport().GuiGetFocusOwner() is LineEdit) return;   // ignore movement/attack while typing in chat

            // Unity suppresses attacks entirely while mounted; gate input before AttackGate.
            if (!IsMounted && Input.IsActionPressed("Attack"))
            {
                int ws = GameManager.Instance.CurrentMapManager?.WeaponSpeed ?? 0;
                if (_attackGate.TryAttack(Time.GetTicksMsec() / 1000.0, ws))
                {
                    TriggerAttack();
                    GameManager.Instance.NetworkClient.Attack();
                }
            }

            // Resolve currently held movement actions through MovementInput.
            // Use a local copy so diagonal direction stays stable while waiting for the hold
            // delay and while moving; the alternation state is committed only when a new
            // movement attempt is actually initiated.
            bool nextWasMovingVertical = _wasMovingVertical;
            Direction? dir = MovementInput.Resolve(
                Input.IsActionPressed("MoveUp"),
                Input.IsActionPressed("MoveDown"),
                Input.IsActionPressed("MoveLeft"),
                Input.IsActionPressed("MoveRight"),
                ref nextWasMovingVertical);

            // No direction — tap-to-turn: if we were holding a key but released within delay, turn in place
            if (dir == null)
            {
                if (_heldDir.HasValue && _movePressedTime < MoveStartDelay)
                {
                    SetFacing(_heldDir.Value);
                    GameManager.Instance.NetworkClient.Face(_heldDir.Value);
                }
                _heldDir = null;
                _movePressedTime = 0;
                return;
            }

            _heldDir = dir;

            // Already moving — preserve accumulated duration for seamless chained movement
            if (_moving) return;

            // Accumulate delta only while standing
            _movePressedTime += delta;
            if (_movePressedTime < MoveStartDelay) return;

            // Commit the alternation state now that the hold delay has elapsed.
            // This ensures diagonal direction alternates once per movement attempt,
            // not every frame while keys are held.
            _wasMovingVertical = nextWasMovingVertical;

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
            if (_lockedMotion == null) return;
            _attackTimer -= delta;
            if (_attackTimer <= 0)
            {
                _lockedMotion = null;
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
                string motion = CharacterMotion.State(IsMoving, _lockedMotion, slotMounted);
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

        public void AddBattleText(BattleTextType type, string text)
        {
            _battleText ??= new Goose2Client.Overlays.BattleText
            {
                Name = "BattleText",
                Position = new Vector2(0, -40),   // above the character, adjusted by Part 2
                ZIndex = 20,
            };
            if (_battleText.GetParent() != this) AddChild(_battleText);
            _battleText.AddText(type, text, Height);
        }

        /// <summary>Show a speech chat bubble above this character's head. Replaces any existing bubble.</summary>
        public void ShowChatBubble(string message)
        {
            // Destroy previous bubble if still alive (one bubble per character)
            if (GodotObject.IsInstanceValid(_chatBubble))
                _chatBubble.QueueFree();

            _chatBubble = new Overlays.ChatBubble
            {
                Name = "ChatBubble",
                ZIndex = 20,
            };
            AddChild(_chatBubble);
            _chatBubble.SetText(message);

            // Position above head — faithful pixel port of Unity formula.
            // Unity: localPosition = (0, character.Height/32 + (bubbleHeight - 0.4355469f)/2f)
            // Godot pixels: Height is already in px, 0.4355469 * 32 = 13.9375px
            // Godot Y is down, so negate for "above". The Unity formula was anchored at its tile-center
            // character origin; this port's Character node origin is the feet (tile bottom edge), so lift
            // by half a tile to match — same feet->center correction ShowEmote/ShowSpell apply.
            _chatBubble.Position = new Vector2(0, -(Height + (_chatBubble.BackgroundHeight - 0.4355469f * 32f) / 2f) - Goose2Client.Map.MapCoords.TileSize / 2f - Overlays.ChatBubbleLayout.VerticalGap);
        }

        /// <summary>Show a spell impact animation at this character's origin. Spells stack (no replacement).</summary>
        public void ShowSpell(int animationId)
        {
            var s = new Overlays.SpellAnimation { Name = "Spell", ZIndex = 20 };
            AddChild(s);
            if (!s.Setup(animationId)) { s.QueueFree(); return; }   // discard orphan on missing asset
            // Node origin is the feet (tile bottom edge); lift to the tile-cell center so the
            // effect is centered on the character's tile.
            s.Position = new Vector2(0, -Goose2Client.Map.MapCoords.TileSize / 2f);
        }

        /// <summary>Show an emote animation above this character. Replaces any existing emote.</summary>
        public void ShowEmote(int animationId)
        {
            if (GodotObject.IsInstanceValid(_emote))
                _emote.QueueFree();

            var e = new Overlays.EmoteAnimation
            {
                Name = "Emote",
                ZIndex = 20,
            };
            AddChild(e);

            if (!e.Setup(animationId))
            {
                e.QueueFree();
                _emote = null;
                return;
            }

            // Position above the body. Unity's (0.5, Height/32 - 0.75) offset was anchored at its
            // tile-center character origin, but this port's Character node origin is the feet (tile
            // bottom edge), so lift by half a tile to reach the tile cell — the same feet->center
            // correction ShowSpell already applies. Without it the emote lands a half-tile too low.
            e.Position = new Vector2(0.5f * 32f, -(Height - 0.75f * 32f) - Goose2Client.Map.MapCoords.TileSize / 2f);
            _emote = e;
        }
    }
}
