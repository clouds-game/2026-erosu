extends SceneTree

func _initialize() -> void:
	call_deferred("_generate")


func _generate() -> void:
	var generator = load("res://Themes/pixel_theme.gd").new()
	generator._run()
	quit()
