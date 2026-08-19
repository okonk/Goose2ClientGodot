extends SceneTree
# Long unbroken string (no spaces): does font.get_multiline_string_size agree with a
# WordSmart label's settled layout?

const MAX_W := 250.0
const FS := 12

var msgs := [
	"a".repeat(80),                        # ~400px, no spaces
	"aaaaaa aaaa bbbbbbbbbbbbbbbbbbbbbb".repeat(4),  # words, one very long
	"supercalifragilisticexpialidociousantidisestablishmentarianism".repeat(2), # 2 giant words
]

func _initialize() -> void:
	var node := Node2D.new()
	root.add_child(node)

	for msg in msgs:
		var label := Label.new()
		label.text = msg
		label.add_theme_font_size_override("font_size", FS)
		label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		label.size = Vector2(MAX_W, 1.0)
		node.add_child(label)

		var font := label.get_theme_font("font")
		var line_h := font.get_height(FS)
		var ls := label.get_theme_constant("line_spacing")
		var natural := font.get_string_size(msg, HORIZONTAL_ALIGNMENT_LEFT, -1, FS)
		var wrapped := font.get_multiline_string_size(msg, HORIZONTAL_ALIGNMENT_LEFT, MAX_W, FS)
		var n_font := maxi(1, round(wrapped.y / line_h))
		var formula := n_font * (line_h + ls) - ls
		label.size = Vector2(MAX_W, formula)

		await process_frame
		await process_frame
		var settled := label.get_minimum_size().y
		var ok := "OK " if absf(settled - formula) < 0.5 else "MISMATCH"
		print("%s natural=%.0f font_wrapped=(%.0f,%.0f) font_lines=%d formula=%.1f settled=%.1f len=%d"
			% [ok, natural.x, wrapped.x, wrapped.y, n_font, formula, settled, msg.length()])
		label.queue_free()

	quit(0)
