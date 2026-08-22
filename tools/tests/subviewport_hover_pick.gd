# Headless probe pinning the engine behavior the ground-item hover fix relies on
# (Godot 4.6/4.7; push_input replaces the old input_event_viewport).
#
# Contract under test — the map's dropped items live in the world SubViewport
# (handle_input_locally=TRUE, displayed through a plain TextureRect, no
# SubViewportContainer), so the parent (WorldViewport) must drive hover itself.
# handle_input_locally must stay true: with false, the picking queue's
# set_input_as_handled() (and push_input's flag resets) propagate up to the
# owning root Window, marking every window event handled and starving the
# root GUI of motion — window drag & drop broke exactly that way (case 5).
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
#  5. Regression guard: a motion event that is forwarded in the root's node
#     phase must still reach the root GUI phase — Control drag & drop
#     (windows, item slots) starves if the forwarded push marks the root
#     window's event as handled (what handle_input_locally=false does).
#
# Usage: godot-mono --headless --script tools/tests/subviewport_hover_pick.gd
extends SceneTree

# Mimics WorldViewport._Input: notify enter/exit around the display rect and
# push local-coord motion into the sub-viewport.
class Forwarder extends Node:
	var sv: SubViewport
	var rect: Rect2
	var in_display := false
	var forwarding := false
	func _input(e: InputEvent) -> void:
		if not (e is InputEventMouseMotion):
			return
		if sv == null or forwarding:
			return
		if rect.has_point(e.position):
			if not in_display:
				in_display = true
				sv.notify_mouse_entered()
			forwarding = true
			var m := (e as InputEventMouseMotion).duplicate()
			m.position = e.position - rect.position
			sv.push_input(m, true)
			forwarding = false
		elif in_display:
			in_display = false
			sv.notify_mouse_exited()

# Standard title-bar drag state machine driven by gui_input.
class TitleBar extends Control:
	var window: Control
	var dragging := false
	var offset := Vector2.ZERO
	var log: Array
	func _gui_input(e: InputEvent) -> void:
		if e is InputEventMouseButton and e.button_index == 1:
			if e.pressed:
				dragging = true
				offset = window.position - e.position
				log.append("drag_start")
				accept_event()
			elif dragging:
				dragging = false
				log.append("drag_end")
				accept_event()
		elif e is InputEventMouseMotion and dragging:
			window.position = e.position + offset
			log.append("drag_move")
			accept_event()

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
	sv.handle_input_locally = true
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

	# (5) root GUI drag regression while forwarding is active.
	var fwd := Forwarder.new()
	fwd.sv = sv
	fwd.rect = Rect2(0, 0, 320, 240)
	root.add_child(fwd)
	var window := Control.new()
	window.position = Vector2(350, 50)
	window.size = Vector2(200, 150)
	root.add_child(window)
	var bar := TitleBar.new()
	bar.window = window
	bar.size = Vector2(200, 20)
	bar.mouse_filter = Control.MOUSE_FILTER_STOP
	window.add_child(bar)
	var drag_log: Array[String] = []
	bar.log = drag_log
	await _settle()
	var press := InputEventMouseButton.new()
	press.position = Vector2(360, 60)   # over the bar (window at 350,50 + bar 0..200 x 0..20)
	press.button_index = 1
	press.pressed = true
	root.push_input(press, true)
	await _settle()
	# Motion INSIDE the display rect (forwards into the sub-viewport) but routed
	# to the bar by mouse focus; offset = (350,50)-(360,60) = (-10,-10).
	var move := InputEventMouseMotion.new()
	move.position = Vector2(200, 90)
	root.push_input(move, true)
	await _settle()
	var release := InputEventMouseButton.new()
	release.position = Vector2(200, 90)
	release.button_index = 1
	release.pressed = false
	root.push_input(release, true)
	await _settle()
	_check(drag_log == ["drag_start", "drag_move", "drag_end"] and window.position == Vector2(190, 80),
		"root GUI drag survives forwarding (log=%s pos=%s)" % [str(drag_log), str(window.position)])

	print("subviewport_hover_pick: done")
	quit(0)
