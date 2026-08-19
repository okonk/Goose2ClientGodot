extends SceneTree
# Replicates ChatBubble.SetText ordering to find why the label renders unwrapped.
# A: add label (no autowrap) -> set autowrap + size after (current C# order)
# B: set autowrap + size BEFORE AddChild (reference order)

const MAX_W := 250.0
const FS := 12
const MSG := "Hello there, this is a fairly long message that should definitely wrap across multiple lines because it is way over two hundred and fifty pixels wide at twelve point font"

func _initialize() -> void:
	var node := Node2D.new()
	root.add_child(node)

	# A: current order
	var a := Label.new()
	a.text = MSG
	a.add_theme_font_size_override("font_size", FS)
	node.add_child(a)
	a.autowrap_mode = TextServer.AUTOWRAP_WORD
	a.size = Vector2(MAX_W, 65.0)
	await process_frame
	await process_frame
	print("A (autowrap after add): size: %s min: %s" % [a.size, a.get_minimum_size()])

	# B: reference order
	var b := Label.new()
	b.text = MSG
	b.add_theme_font_size_override("font_size", FS)
	b.autowrap_mode = TextServer.AUTOWRAP_WORD
	b.size = Vector2(MAX_W, 65.0)
	node.add_child(b)
	await process_frame
	await process_frame
	print("B (autowrap before add): size: %s min: %s" % [b.size, b.get_minimum_size()])

	# C: new structure — label SIBLING of the Panel (direct child of Node2D), C# ordering
	var panel := Panel.new()
	node.add_child(panel)
	var c := Label.new()
	c.text = MSG
	c.add_theme_font_size_override("font_size", FS)
	node.add_child(c)
	c.autowrap_mode = TextServer.AUTOWRAP_WORD
	panel.size = Vector2(253.0, 75.0)   # bg = text(239x65) + padding(7,5)*2
	c.position = Vector2(7, 5)
	c.size = Vector2(MAX_W, 65.0)
	for i in 4:
		await process_frame
	print("C (sibling of Panel, C# order): label size: %s min: %s"
		% [c.size, c.get_minimum_size()])

	# D: like A, but a Panel sibling exists and is sized (no children in it)
	var panel2 := Panel.new()
	node.add_child(panel2)
	var d := Label.new()
	d.text = MSG
	d.add_theme_font_size_override("font_size", FS)
	node.add_child(d)
	d.autowrap_mode = TextServer.AUTOWRAP_WORD
	panel2.size = Vector2(253.0, 75.0)
	d.size = Vector2(MAX_W, 65.0)
	for i in 4:
		await process_frame
	print("D (A + panel sibling): label size: %s min: %s" % [d.size, d.get_minimum_size()])

	# E: like A, no panel, but size set one frame later
	var e := Label.new()
	e.text = MSG
	e.add_theme_font_size_override("font_size", FS)
	node.add_child(e)
	e.autowrap_mode = TextServer.AUTOWRAP_WORD
	await process_frame
	e.size = Vector2(MAX_W, 65.0)
	for i in 4:
		await process_frame
	print("E (size next frame): label size: %s min: %s" % [e.size, e.get_minimum_size()])

	# F: reference order — autowrap + final size set BEFORE AddChild, panel sibling present
	var panel3 := Panel.new()
	node.add_child(panel3)
	var f := Label.new()
	f.text = MSG
	f.add_theme_font_size_override("font_size", FS)
	f.autowrap_mode = TextServer.AUTOWRAP_WORD
	f.position = Vector2(7, 5)
	f.size = Vector2(MAX_W, 65.0)
	node.add_child(f)
	panel3.size = Vector2(253.0, 75.0)
	for i in 4:
		await process_frame
	print("F (autowrap+size before add, panel sibling): label size: %s min: %s" % [f.size, f.get_minimum_size()])

	# G: F + re-assign size after a frame (like a late layout pass might)
	await process_frame
	f.size = Vector2(MAX_W, 65.0)
	for i in 4:
		await process_frame
	print("G (F + size re-assign after add): label size: %s min: %s" % [f.size, f.get_minimum_size()])

	quit(0)
