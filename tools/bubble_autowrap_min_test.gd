extends SceneTree
# Decisive probe: same-frame GetMinimumSize() after AddChild, size set pre-add
# (the reference flow). On Godot 4.6.2 the wrapped height is available
# synchronously ONLY with WordSmart; with plain Word it is still the
# pre-layout placeholder (one line) until a frame later.
# This is why ChatBubble measures with WordSmart + the label's own min size.

const MAX_W := 250.0
const MSG := "Hello there, this is a fairly long message that should definitely wrap across multiple lines because it is way over two hundred and fifty pixels wide at twelve point font"

func _initialize() -> void:
	var node := Node2D.new()
	root.add_child(node)

	for mode in [TextServer.AUTOWRAP_WORD, TextServer.AUTOWRAP_WORD_SMART]:
		var label := Label.new()
		label.text = MSG
		label.add_theme_font_size_override("font_size", 12)
		label.autowrap_mode = mode
		label.size = Vector2(MAX_W, 0)
		node.add_child(label)
		var same_frame := label.get_minimum_size().y
		await process_frame
		await process_frame
		var settled := label.get_minimum_size().y
		print("mode=%d same_frame=%.1f settled=%.1f" % [mode, same_frame, settled])
		label.queue_free()

	quit(0)
