# Headless probe pinning the engine behavior the ground-item hover fix relies on
# (Godot 4.6/4.7; push_input replaces the old input_event_viewport).
#
# Contract under test — the map's dropped items live in the world SubViewport
# (handle_input_locally=false, displayed through a plain TextureRect, no
# SubViewportContainer), so the parent (WorldViewport) must drive hover itself:
#
#  0. physics_object_picking defaults to FALSE in 4.6/4.7 (older engines
#     defaulted it to true, which is why the pre-4.6 hover "just worked");
#     the map viewport must enable it explicitly or no picking ever runs.
#  1. push_input(motion, in_local_coords=true) updates the sub-viewport's mouse
#     position (get_mouse_position) but fires nothing until the viewport is
#     notified the mouse is in it: _process_picking bails when
#     gui.mouse_in_viewport is false, and only a SubViewportContainer would
#     emit NOTIFICATION_VP_MOUSE_ENTER on its own. Hence notify_mouse_entered().
#  2. After notify_mouse_entered(), pushed local motion over an Area2D fires
#     mouse_entered, and motion away fires mouse_exited (picking runs every
#     physics tick in SceneTree.physics_process on group _picking_viewports).
#  3. notify_mouse_exited() (NOTIFICATION_VP_MOUSE_EXIT) drops the physics
#     mouseover SYNCHRONOUSLY — the hovered Area2D's mouse_exited fires inside
#     the call. So leaving the display rect needs no extra "far position" push.
#  4. Passive hover: with the mouse "in" the viewport, a world object moving
#     under a static mouse fires enter/exit with no new input event
#     (camera-follow case) — WorldViewport therefore only forwards motion
#     events and never re-picks itself.
#
# Usage: godot-mono --headless --script tools/tests/subviewport_hover_pick.gd
extends SceneTree

var events: Array[String] = []
var sv: SubViewport
var area: Area2D
var item: Node2D

func _check(cond: bool, label: String) -> void:
	if cond:
		print("PASS: ", label)
	else:
		printerr("FAIL: ", label)
		quit(1)

func _make_motion(pos: Vector2) -> InputEventMouseMotion:
	var e := InputEventMouseMotion.new()
	e.position = pos
	return e

func _forward(pos: Vector2) -> void:
	sv.push_input(_make_motion(pos), true)

func _settle(n: int = 3) -> void:
	for _i in n:
		await process_frame

func _initialize() -> void:
	sv = SubViewport.new()
	sv.handle_input_locally = false
	sv.size = Vector2i(320, 240)
	sv.render_target_update_mode = 1
	root.add_child(sv)

	# Stand-in for MapItem: sprite at a world pos with a hover area over its rect.
	item = Node2D.new()
	item.position = Vector2(100, 100)
	sv.add_child(item)
	area = Area2D.new()
	area.input_pickable = true
	item.add_child(area)
	var shape := CollisionShape2D.new()
	var rect_shape := RectangleShape2D.new()
	rect_shape.size = Vector2(32, 32)
	shape.shape = rect_shape
	shape.position = Vector2(16, 16)   # RectangleShape2D is centered on the shape node (MapItem parity)
	area.add_child(shape)
	area.mouse_entered.connect(func(): events.append("entered"))
	area.mouse_exited.connect(func(): events.append("exited"))

	await _settle()

	# (0) with physics_object_picking at its 4.6/4.7 default (false), nothing fires.
	_forward(Vector2(110, 110))
	await _settle()
	_check(events.is_empty(), "default physics_object_picking=false fires nothing")
	sv.physics_object_picking = true   # (0) the map viewport must enable it

	# (1) push_input alone (picking on) never fires until the viewport is notified in.
	_forward(Vector2(110, 110))
	await _settle()
	_check(sv.get_mouse_position() == Vector2(110, 110),
		"push_input(local) updates the sub-viewport mouse position")
	_check(events.is_empty(), "push_input without notify_mouse_entered fires nothing")
	sv.notify_mouse_entered()
	_forward(Vector2(110, 110))
	await _settle()
	_check(events == ["entered"],
		"after notify_mouse_entered, the pushed motion fires mouse_entered (got %s)" % str(events))

	# (2) motion away → mouse_exited.
	_forward(Vector2(5, 5))
	await _settle()
	_check(events == ["entered", "exited"], "motion away fires mouse_exited (got %s)" % str(events))

	# (3) leaving the display rect: notify_mouse_exited drops the hover synchronously.
	_forward(Vector2(110, 110))
	await _settle()
	sv.notify_mouse_exited()
	await _settle()
	_check(events == ["entered", "exited", "entered", "exited"],
		"notify_mouse_exited fires the pending mouse_exited (got %s)" % str(events))

	# (4) passive hover: world objects move under a static (in-viewport) mouse.
	sv.notify_mouse_entered()
	_forward(Vector2(110, 110))
	await _settle()
	_check(events == ["entered", "exited", "entered", "exited", "entered"],
		"re-entering after a clean notify_mouse_exited fires mouse_entered (got %s)" % str(events))
	item.position = Vector2(-100, -100)
	await _settle()
	_check(events == ["entered", "exited", "entered", "exited", "entered", "exited"],
		"object moving away from a static mouse fires mouse_exited (got %s)" % str(events))
	item.position = Vector2(90, 90)   # stale mouse pos (110,110) is now inside the 32x32 rect (90..122)
	await _settle()
	_check(events == ["entered", "exited", "entered", "exited", "entered", "exited", "entered"],
		"object moving under a static mouse fires mouse_entered (got %s)" % str(events))

	print("subviewport_hover_pick: done")
	quit(0)
