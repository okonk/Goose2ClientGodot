# Headless probe pinning the Camera2D scroll-update contract MapManager relies on.
# It does NOT execute the C# code — it verifies the engine behavior behind
# MapManager's ForceUpdateScroll() call (see Scripts/MapManager.cs _Process).
#
# Contract under test (Godot 4.x; verified headless here):
#  1. Camera2D pushes its transform to the viewport from its own internal process at
#     process_priority 0. MapManager writes the camera position at priority 50 (it must run
#     after Character, which moves at 0), so the write lands AFTER the camera has already
#     published. The transform-changed notification is deferred to the end of the frame, so
#     rendering is correct but Viewport.get_canvas_transform() is STALE for the rest of the
#     processing stage — by exactly one frame.
#  2. WorldTextBridge (priority 100) projects world text through get_canvas_transform().
#     Reading the stale transform pairs this frame's character position with last frame's
#     camera, so overhead text jitters against the character by the per-frame step (which
#     alternates 2px/3px at the default MoveSpeed). Camera2D.force_update_scroll() publishes
#     immediately and makes the same-frame read correct.
#
# Usage: godot-mono --headless --script tools/tests/camera_scroll_order.gd
extends SceneTree

var _failed := false
var stale_log: Array = []
var forced_log: Array = []

# Priority-50 stand-in for MapManager's camera follow.
class Follow extends Node:
	var cam: Camera2D
	var force: bool
	var frame := 0
	func _ready() -> void: process_priority = 50
	func _process(_d: float) -> void:
		frame += 1
		cam.global_position = Vector2(100 * frame, 0)
		if force:
			cam.force_update_scroll()

# Priority-100 stand-in for WorldTextBridge: projects through the canvas transform.
class Projector extends Node:
	var sub: SubViewport
	var cam: Camera2D
	var log: Array
	func _ready() -> void: process_priority = 100
	func _process(_d: float) -> void:
		log.append([cam.global_position.x, sub.get_canvas_transform().origin.x])

func _check(cond: bool, label: String) -> void:
	if cond:
		print("PASS: ", label)
	else:
		printerr("FAIL: ", label)
		_failed = true

func _build(force: bool, log: Array) -> void:
	var sub := SubViewport.new()
	sub.size = Vector2i(640, 360)   # even axes: camera offset parity is 0, origin stays integral
	root.add_child(sub)
	var cam := Camera2D.new()
	sub.add_child(cam)
	var f := Follow.new(); f.cam = cam; f.force = force; root.add_child(f)
	var p := Projector.new(); p.sub = sub; p.cam = cam; p.log = log; root.add_child(p)

func _initialize() -> void:
	_build(false, stale_log)
	_build(true, forced_log)

	await process_frame
	await process_frame
	await process_frame

	# Expected canvas origin for a camera anchored at the viewport centre: 320 - cam.x.
	var stale = stale_log[stale_log.size() - 1]
	var forced = forced_log[forced_log.size() - 1]
	print("  without force: cam.x=%s canvas.origin.x=%s (want %s)" % [stale[0], stale[1], 320.0 - stale[0]])
	print("  with force:    cam.x=%s canvas.origin.x=%s (want %s)" % [forced[0], forced[1], 320.0 - forced[0]])

	_check(not is_equal_approx(stale[1], 320.0 - stale[0]),
		"without force_update_scroll the canvas transform trails the camera write by a frame")
	_check(is_equal_approx(stale[1], 320.0 - (stale[0] - 100.0)),
		"the stale value is exactly the PREVIOUS frame's camera position")
	_check(is_equal_approx(forced[1], 320.0 - forced[0]),
		"force_update_scroll publishes the write in the SAME frame")
	quit(1 if _failed else 0)
