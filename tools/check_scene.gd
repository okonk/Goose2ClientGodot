# Headless scene loader: validates a .tscn parses and its resources resolve.
# Usage: godot --headless --script tools/check_scene.gd -- res://Scenes/UI/Foo.tscn [more...]
extends SceneTree

func _init():
	var args = OS.get_cmdline_user_args()
	var failed = false
	for path in args:
		var res = ResourceLoader.load(path)
		if res == null:
			printerr("FAIL load: ", path)
			failed = true
		else:
			print("OK load: ", path)
	quit(1 if failed else 0)
