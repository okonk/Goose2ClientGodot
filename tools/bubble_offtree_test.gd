extends SceneTree
# Does an autowrapped Label measure correctly OUTSIDE the tree (synchronous)?
# If so: measure off-tree, then AddChild in final state -> no settling, no pop.

const MAX_W := 250.0
const FS := 12
var msgs := [
	"Hi",
	"Hello there, this is a fairly long message that should definitely wrap across multiple lines because it is way over two hundred and fifty pixels wide at twelve point font",
	"a".repeat(80),
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
		# NOT added to the tree yet
		var off_tree := label.get_minimum_size()
		# size.y was 1 -> clamp to min happened? read size too
		print("off-tree: min=%s size=%s len=%d" % [off_tree, label.size, msg.length()])
		label.size = Vector2(MAX_W, off_tree.y if off_tree.y > 1 else 14.0)
		node.add_child(label)
		await process_frame
		await process_frame
		print("settled:  min=%s size=%s" % [label.get_minimum_size(), label.size])
		label.queue_free()

	quit(0)
