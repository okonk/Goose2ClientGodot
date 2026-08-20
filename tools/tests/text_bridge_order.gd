# Headless probe pinning the per-frame processing-order contract WorldTextBridge relies on.
# It does NOT execute the C# bridge — it verifies the engine behavior the bridge's design
# decision depends on (see docs/plans/2026-08-19-world-text-bridge.md, Task 2).
#
# Contract under test (Godot 4.x; verified headless here):
#  1. SceneTree.process_frame is emitted BEFORE any node _process callback in a frame.
#     A bridge that projected on process_frame would therefore read the PREVIOUS frame's
#     character positions/camera and trail the sprite by one frame (T2 failure). This is
#     why WorldTextBridge drives projection from its own _Process, NOT from process_frame.
#  2. Within a frame's processing stage, LOWER process_priority runs first. A priority-0
#     node (Character / MapManager camera / WorldOverlay all use the default 0) mutates
#     state, and a priority-100 node (WorldTextBridge) observes that mutation in the SAME
#     frame, before rendering. The bridge must set ProcessPriority above every world node.
#
# Usage: godot-mono --headless --script tools/tests/text_bridge_order.gd
extends SceneTree

var _failed := false
var log: Array[String] = []
var state := {"moved": false}
var sig_count := 0

# Priority-0 stand-in for Character / MapManager: moves itself every frame.
class Mover extends Node:
	var log: Array
	var state: Dictionary
	func _process(_delta: float) -> void:
		log.append("mover:p0:sees=%s" % str(state.moved))
		state.moved = true   # the "character moved this frame" mutation

# Priority-100 stand-in for WorldTextBridge: reads the owner's post-move state.
class Late extends Node:
	var log: Array
	var state: Dictionary
	func _ready() -> void:
		process_priority = 100
	func _process(_delta: float) -> void:
		log.append("bridge:p100:sees=%s" % str(state.moved))

func _check(cond: bool, label: String) -> void:
	if cond:
		print("PASS: ", label)
	else:
		printerr("FAIL: ", label)
		_failed = true

func _initialize() -> void:
	var mover := Mover.new()
	mover.log = log
	mover.state = state
	root.add_child(mover)
	var late := Late.new()
	late.log = log
	late.state = state
	root.add_child(late)
	process_frame.connect(_on_process_frame)

	# Run two frames so the first frame's ordering is fully flushed before asserting.
	await process_frame
	await process_frame

	# --- Contract 1: process_frame is emitted BEFORE node _process. ---
	# Frame 1's first log line must be the process_frame signal seeing moved=false
	# (the mover has not run yet this frame). If process_frame ran AFTER node processing,
	# it would see moved=true and this ordering guarantee — and the whole priority-based
	# design — would be unsound.
	_check(log.size() >= 4, "at least 4 events across two frames (got %d)" % log.size())
	if log.size() >= 1:
		_check(log[0].begins_with("sig:p1:sees=false"),
			"process_frame emits BEFORE node _process (a process_frame projector would lag a frame)")
	# --- Contract 2: within one frame, priority-0 runs before priority-100, and the
	# priority-100 node sees the priority-0 mutation from the SAME frame. ---
	# Find the first frame's mover + bridge pair: mover logs sees=false then moves;
	# the bridge must log sees=true immediately after (same frame, post-mutation).
	var i := 0
	while i < log.size() and not log[i].begins_with("mover:p0:"):
		i += 1
	if i + 1 < log.size():
		_check(log[i + 1].begins_with("bridge:p100:sees=true"),
			"priority-100 bridge sees the SAME frame's priority-0 mutation (T2: no 1-frame lag)")
	else:
		_check(false, "found a mover event with a following bridge event")

	quit(1 if _failed else 0)

func _on_process_frame() -> void:
	sig_count += 1
	log.append("sig:p%d:sees=%s" % [sig_count, str(state.moved)])
