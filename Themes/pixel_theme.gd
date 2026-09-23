@tool
extends ProgrammaticTheme

const UPDATE_ON_SAVE = true
const INK = Color("253044")
const CREAM = Color("f6f1e4")

func setup() -> void:
	set_save_path("res://Themes/pixel_theme.tres")
	define_default_font_size(24)


func define_theme() -> void:
	var tan = _surface("res://Assets/PixelUI/Ancient/tan.png")
	var white = _surface("res://Assets/PixelUI/Ancient/white.png")
	var pressed = _surface("res://Assets/PixelUI/Ancient/tan_pressed.png")
	var focus = stylebox_texture({
		"texture": load("res://Assets/PixelUI/Outline/yellow.png"),
		"texture_margin_left": 4, "texture_margin_top": 4,
		"texture_margin_right": 4, "texture_margin_bottom": 4,
		"content_margin_left": 4, "content_margin_top": 4,
		"content_margin_right": 4, "content_margin_bottom": 4
	})
	define_style("Button", {
		"normal": tan, "hover": white, "pressed": pressed,
		"disabled": tan, "focus": focus,
		"font_color": INK, "font_hover_color": INK,
		"font_pressed_color": INK, "font_focus_color": INK,
		"font_disabled_color": Color("6d7480")
	})
	define_variant_style("PrimaryButton", "Button", {
		"normal": white, "hover": tan, "pressed": pressed,
		"focus": focus, "font_color": INK,
		"font_hover_color": INK, "font_pressed_color": INK
	})
	define_style("PanelContainer", {
		"panel": stylebox_texture({
			"texture": load("res://Assets/PixelUI/Ancient/white.png"),
			"texture_margin_left": 14, "texture_margin_top": 14,
			"texture_margin_right": 14, "texture_margin_bottom": 14,
			"content_margin_left": 22, "content_margin_top": 22,
			"content_margin_right": 22, "content_margin_bottom": 22
		})
	})
	define_style("Label", {"font_color": CREAM})


func _surface(path: String) -> Dictionary:
	return stylebox_texture({
		"texture": load(path),
		"texture_margin_left": 14, "texture_margin_top": 14,
		"texture_margin_right": 14, "texture_margin_bottom": 14,
		"content_margin_left": 12, "content_margin_top": 12,
		"content_margin_right": 12, "content_margin_bottom": 12
	})
