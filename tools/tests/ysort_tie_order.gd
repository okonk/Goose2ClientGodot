# Headless-capable probe pinning the Y-sort draw-order contract MapManager's Objects1
# placement relies on (see Scripts/MapManager.cs _Ready). It verifies engine behavior, not
# the C# code: a nested Y-sort container's sprites merge into the parent's sort by global Y,
# and same-Y ties resolve by tree order. MapManager therefore places the layer-2 ObjectLayer
# AFTER the Characters node so an object sharing a character's tile paints over it, matching
# Aspereta's renderer (Map.cs Render draws each tile's layer-2 graphic after the character
# standing on that tile), while a character on a lower row still paints in front.
#
# Pixel assertions need a real rasterizer; with --headless the dummy renderer produces no
# image and the probe reports SKIP. Verified under GL: godot-mono --path . -s tools/tests/ysort_tie_order.gd
extends SceneTree

var _failed := 0

func _check(cond: bool, label: String) -> void:
	if cond:
		print("PASS: ", label)
	else:
		print("FAIL: ", label)
		_failed += 1

class PaintBox extends ColorRect:
	func _init(color_: Color, pos_: Vector2) -> void:
		color = color_
		position = pos_
		size = Vector2(32, 32)

func _build(update_target: SubViewport) -> Array:
	var world := Node2D.new()
	world.y_sort_enabled = true
	update_target.add_child(world)
	var objects := Node2D.new()
	objects.z_index = 15
	objects.y_sort_enabled = true
	var characters := Node2D.new()
	characters.z_index = 15
	characters.y_sort_enabled = true
	world.add_child(objects)
	world.add_child(characters)
	objects.add_child(PaintBox.new(Color.BLUE, Vector2(0, 0)))
	characters.add_child(PaintBox.new(Color.RED, Vector2(0, 16)))
	return [objects, characters]

func _sample(v: SubViewport, at: Vector2i) -> Color:
	var img := v.get_texture().get_image()
	return Color(0, 0, 0, 0) if img == null else img.get_pixelv(at)

func _initialize() -> void:
	root.hide()
	var v := SubViewport.new()
	v.size = Vector2i(64, 64)
	v.render_target_update_mode = SubViewport.UPDATE_ALWAYS
	root.add_child(v)

	var nodes := _build(v)
	for _i in 4:
		await process_frame
	var probe := _sample(v, Vector2i(16, 24))
	if probe.a <= 0.0:
		print("SKIP: no rendered image (dummy rasterizer) — rerun with a GL context")
		quit(0)
		return

	# objects before characters: the object at y=0 must still lose to the character at y=16
	# (global-Y sort, not container-block order).
	_check(probe.r > probe.b, "character on lower row paints over object on higher row")

	# Same Y: the later node in tree order (characters) wins.
	nodes[0].get_child(0).position = Vector2(0, 16)
	for _i in 4:
		await process_frame
	probe = _sample(v, Vector2i(16, 24))
	_check(probe.r > probe.b, "same-Y tie goes to the later tree node (characters)")

	# Move the object container after characters (MapManager's placement): the tie flips.
	v.get_child(0).move_child(nodes[0], 1)
	for _i in 4:
		await process_frame
	probe = _sample(v, Vector2i(16, 24))
	_check(probe.b > probe.r, "object container after characters wins the same-Y tie")

	# ...without disturbing the non-tie case: object back at y=0 stays under the y=16 character.
	nodes[0].get_child(0).position = Vector2(0, 0)
	for _i in 4:
		await process_frame
	probe = _sample(v, Vector2i(16, 24))
	_check(probe.r > probe.b, "reordered container still Y-sorts by global Y")

	quit(1 if _failed else 0)
