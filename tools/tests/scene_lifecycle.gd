# Self-contained simulation pinning the engine contracts GameManager.ChangeMap relies on
# (I7) — it does not execute the C# code.
#
# Engine contract under test (Godot 4.7; verified headless here):
#  - Reassigning/removing a current scene NEVER frees it implicitly — the transition must
#    queue_free the previous world explicitly.
#  - When the current scene node is removed from the tree, the engine sets current_scene
#    back to null (no dangling pointer for the next ChangeMap's GetTree().CurrentScene read).
#  - A map scene lives under the WorldViewport container, not at root (set_current_scene
#    requires a direct root child, scene_tree.cpp:1665 — so it can never BE the current scene).
#  - ChangeMap's transition order (world-subviewport-stage1 flash fix): attach the new map
#    B, then wait for the completion of the NEXT RENDER PASS —
#    RenderingServer.frame_post_draw (this engine version's successor to the old Viewport
#    'rendered' signal, which no longer exists in the 4.7.1 API — the Viewport class exposes
#    only size_changed/gui_focus_changed) — the moment B's first clean texture is presented
#    by WorldViewport, and ONLY THEN queue_free the old worlds A/A2. A fresh SubViewport's
#    buffer is undefined before its first render, and the old map is what the display texture
#    still shows — so freeing it earlier would flash garbage or black.
#  - ChangeMap implements that wait as a ToSignal race between frame_post_draw and
#    SceneTree.process_frame. PROBED on this 4.7.1 headless build: frame_post_draw is NOT
#    emitted at all in headless (connect succeeds, but never fires), so headless exercises
#    the process_frame fallback leg of the race, exactly like the C# code would; headed runs
#    take the frame_post_draw leg (rendering completes later in the same frame, before the
#    next process_frame).
#  - (C# load-bearing invariant) Attach only lands in the mid-frame flush AFTER its frame's
#    process_frame emission because of the loading-overlay await in ChangeMap — do not
#    remove/hoist that await without re-deriving this ordering (see the race comment there).
#
# Simulates BOTH entry shapes: first entry (previous scene A = Login, a root child and the
# current scene) and a later entry (previous map A2 attached under the container).
#
# Usage: godot-mono --headless --script tools/tests/scene_lifecycle.gd
extends SceneTree

var _failed := false

# One-shot winner of the frame_post_draw / process_frame race (emitted in _initialize).
signal race_win

func _check(cond: bool, label: String) -> void:
	if cond:
		print("PASS: ", label)
	else:
		printerr("FAIL: ", label)
		_failed = true

func _initialize() -> void:
	# Autoloads (GameManager) are already root children in a real run — capture the baseline
	# so the "no leftover top-level scenes" check only looks at what the transition adds.
	var baseline: Array[Node] = []
	for child in root.get_children():
		baseline.append(child)

	# Previous scene A: added to root AND set as current_scene (Login, first entry).
	var a := Node2D.new()
	a.name = "SceneA"
	root.add_child(a)
	current_scene = a

	# WorldViewport stand-in: a plain container node owning the sub-viewport.
	var container := Node.new()
	container.name = "WorldViewport"
	root.add_child(container)

	# Previous map A2: attached under the container (a later entry's old map).
	var a2 := SubViewport.new()
	a2.name = "OldMap"
	container.add_child(a2)

	# New scene B: a SubViewport (the re-rooted Map.tscn), attached under the container —
	# mimicking WorldViewport.Attach (force first render + AddChild + sizing; the texture
	# swap itself is deferred to the next frame_post_draw, which ChangeMap awaits before
	# freeing the old worlds).
	var b := SubViewport.new()
	b.name = "Map"
	container.add_child(b)
	_check(b.get_texture() != null, "B exposes a texture (WorldTexture can be swapped)")

	# ChangeMap ordering: attach B first, then await completion of the next render pass with
	# a process_frame fallback — the same ToSignal-style race as the C# code, resolved here
	# by taking whichever leg fires first. Headless (this run) never emits frame_post_draw,
	# so the fallback leg is taken (verified empirically, see header).
	var race_slot := [null]
	var race := func() -> void:
		if race_slot[0] == null:
			return
		var cb = race_slot[0]
		race_slot[0] = null
		RenderingServer.frame_post_draw.disconnect(cb)
		process_frame.disconnect(cb)
		race_win.emit()
	race_slot[0] = race
	RenderingServer.frame_post_draw.connect(race)
	process_frame.connect(race)
	await race_win

	# Only now free the previous worlds (the display already shows B's first clean frame,
	# or — headless — the new map is at least attached and sized before the frees run).
	a.queue_free()
	a2.queue_free()
	await process_frame

	_check(not is_instance_valid(a), "A (previous root scene) freed by explicit queue_free")
	_check(not is_instance_valid(a2), "A2 (previous attached map) freed by explicit queue_free")
	_check(is_instance_valid(b), "B alive")
	_check(b.get_parent() == container, "B inside WorldViewport container")

	# The engine must have nulled the current scene when A left the tree — the next
	# ChangeMap reads GetTree().CurrentScene and must not touch a freed node.
	_check(current_scene == null, "current_scene nulled by engine after A left the tree (no dangling read)")

	# No leftover top-level scene nodes: root gained exactly the container.
	var strays: Array[Node] = []
	for child in root.get_children():
		if child != container and not baseline.has(child):
			strays.append(child)
	if strays.is_empty():
		_check(true, "root has no leftover top-level scene nodes")
	else:
		_check(false, "root has no leftover top-level scene nodes (got: %s)" % strays)

	quit(1 if _failed else 0)
