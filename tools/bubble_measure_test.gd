extends SceneTree
# Headless probe: how does an autowrapped Label measure vs render?
# Mirrors ChatBubble.SetText variants to find why the background stayed 1 line.

const MSG = "Hello there, this is a fairly long message that should definitely wrap across multiple lines because it is way over two hundred and fifty pixels wide at twelve point font"

func _initialize() -> void:  # async via await below
	var max_text_width := 250.0
	var font_size := 12

	var node := Node2D.new()
	root.add_child(node)

	var panel := Panel.new()
	node.add_child(panel)

	var label := Label.new()
	label.text = MSG
	label.add_theme_font_size_override("font_size", font_size)
	label.add_theme_constant_override("outline_size", 4)
	panel.add_child(label)

	var font := label.get_theme_font("font")
	var natural := font.get_string_size(MSG, HORIZONTAL_ALIGNMENT_LEFT, -1, font_size)
	print("natural size: ", natural)
	var wrapped := font.get_multiline_string_size(MSG, HORIZONTAL_ALIGNMENT_LEFT, max_text_width, font_size)
	print("font wrapped size @250: ", wrapped)

	# v2 sequence: autowrap on, size set, then read min size
	label.autowrap_mode = TextServer.AUTOWRAP_WORD
	label.size = Vector2(max_text_width, 0)
	print("min size after Size=(250,0): ", label.get_minimum_size())
	print("label size now: ", label.size)

	# shrink width to longest line (as v2 final step)
	var text_width := minf(wrapped.x, max_text_width)
	var h_min := label.get_minimum_size().y
	label.size = Vector2(text_width, h_min)
	print("after final Size=(%.1f, %.1f) -> size: %s, min size: %s" % [text_width, h_min, label.size, label.get_minimum_size()])

	# fresh label, v3 plan: measure height from font, render at width 250
	var label2 := Label.new()
	label2.text = MSG
	label2.add_theme_font_size_override("font_size", font_size)
	label2.autowrap_mode = TextServer.AUTOWRAP_WORD
	panel.add_child(label2)
	label2.size = Vector2(max_text_width, wrapped.y)
	print("v3: label2 size=(250, %.1f) -> min size: %s" % [wrapped.y, label2.get_minimum_size()])

	# reference-style: label NOT in a container, direct child of Node2D
	var label3 := Label.new()
	label3.text = MSG
	label3.add_theme_font_size_override("font_size", font_size)
	label3.autowrap_mode = TextServer.AUTOWRAP_WORD
	label3.size = Vector2(max_text_width, 0)
	node.add_child(label3)
	print("ref-style (no container): min size after Size=(250,0): ", label3.get_minimum_size(), " size: ", label3.size)
	await process_frame
	await process_frame
	print("ref-style after 2 frames: min size: ", label3.get_minimum_size(), " size: ", label3.size)
	label3.size = Vector2(239.0, 13.0)   # v1/v2 final step: shrink to longest line
	await process_frame
	print("ref-style shrunk to 239: min size: ", label3.get_minimum_size(), " size: ", label3.size)

	# single-line label min height, settled
	var label4 := Label.new()
	label4.text = "Hi there"
	label4.add_theme_font_size_override("font_size", font_size)
	label4.autowrap_mode = TextServer.AUTOWRAP_WORD
	label4.size = Vector2(250.0, 0)
	node.add_child(label4)
	await process_frame
	await process_frame
	print("single-line settled min size: ", label4.get_minimum_size())
	var f := label4.get_theme_font("font")
	print("font height: %s ascent: %s descent: %s"
		% [str(f.get_height(font_size)), str(f.get_ascent(font_size)), str(f.get_descent(font_size))])
	print("font single-line size: ", f.get_string_size("Hi there", HORIZONTAL_ALIGNMENT_LEFT, -1, font_size))
	print("label4 line spacing override: ", label4.get_theme_constant("line_spacing"))

	quit(0)
