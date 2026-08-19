extends SceneTree
# Verifies ChatBubble's synchronous height formula against the label's settled layout.
# formula: n = round(font_wrapped.y / font_height); h = n*(font_height + line_spacing) - line_spacing

const MAX_W := 250.0
const FS := 12

var msgs := [
	"Hi",
	"Hello there, friend",
	"Hello there, this is a message of medium length that may or may not wrap to exactly two lines",
	"Hello there, this is a fairly long message that should definitely wrap across multiple lines because it is way over two hundred and fifty pixels wide at twelve point font",
	"An extremely long message: " + "word ".repeat(60),
]

func _initialize() -> void:
	var node := Node2D.new()
	root.add_child(node)

	for msg in msgs:
		var label := Label.new()
		label.text = msg
		label.add_theme_font_size_override("font_size", FS)
		label.add_theme_constant_override("outline_size", 4)
		node.add_child(label)

		var font := label.get_theme_font("font")
		var line_h := font.get_height(FS)
		var ls := label.get_theme_constant("line_spacing")
		var natural := font.get_string_size(msg, HORIZONTAL_ALIGNMENT_LEFT, -1, FS)
		var formula_h := 0.0
		var wraps := natural.x > MAX_W
		if wraps:
			var wrapped := font.get_multiline_string_size(msg, HORIZONTAL_ALIGNMENT_LEFT, MAX_W, FS)
			var n := maxi(1, round(wrapped.y / line_h))
			formula_h = n * (line_h + ls) - ls
			label.autowrap_mode = TextServer.AUTOWRAP_WORD
			label.size = Vector2(MAX_W, formula_h)
		else:
			formula_h = line_h
			label.size = Vector2(minf(natural.x, MAX_W), formula_h)

		for i in 5:
			await process_frame
		var settled := label.get_minimum_size().y
		var ok := "OK " if absf(settled - formula_h) < 0.5 else "MISMATCH"
		print("%s wraps=%s formula=%.1f settled=%.1f len=%d" % [ok, wraps, formula_h, settled, msg.length()])
		label.queue_free()

	quit(0)
