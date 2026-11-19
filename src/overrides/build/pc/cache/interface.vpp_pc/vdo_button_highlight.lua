-- Override of vdo_button_highlight.lua from <game>\build\pc\cache\interface.vpp_pc (the highlight bar of every
-- menu list in the game). rfa_loader.dll gives the game this file instead of its own copy.
--
-- It also holds RFA-Mod's Modern style for the whole interface (Options > Main Menu > Style and Accent), since
-- every menu loads it: rfa_theme_apply sets the game's shared menu colours (COLOR_HIGHLIGHT_BAR and the rest, in
-- rfg_ui_globals.lua, 0 to 255; every list, toggle, slider and title bar reads them as it draws) to the accent,
-- and rfa_theme_screen restyles a menu screen (the main menu's, the pause menu's: they share their layout).
-- Every line we added or changed is marked [RFA]; everything else is the game's original script. Only the [RFA]
-- lines are this project's (GNU GPL version 3 or later, see LICENSE); the rest belongs to the game's owners. To
-- compare with the original, extract it with the editor.

-- [RFA] ---- The Modern style --------------------------------------------------------------------------------
-- [RFA] The accents, in Options > Main Menu > Accent's order (0 to 255, as the game's colours are).
RFA_THEME_ACCENTS = { -- [RFA]
	{R = 77, G = 184, B = 255}, -- [RFA] blue
	{R = 255, G = 140, B = 38}, -- [RFA] orange
	{R = 235, G = 56, B = 56}, -- [RFA] red
	{R = 89, G = 230, B = 140}, -- [RFA] green
	{R = 204, G = 115, B = 255}, -- [RFA] violet
	{R = 245, G = 245, B = 245}, -- [RFA] white
} -- [RFA]
RFA_THEME_PACK = "rfa_ui_modern" -- [RFA] the UI pack's document (its pictures; the editor makes it)

local function rfa_theme_set(color, r, g, b) -- [RFA] a shared colour, the game's kept the first time
	if color == nil then return end -- [RFA]
	if color.rfa_game == nil then color.rfa_game = {R = color.R, G = color.G, B = color.B} end -- [RFA]
	color.R, color.G, color.B = r, g, b -- [RFA]
end -- [RFA]

-- [RFA] Is the Modern style on? (Set by rfa_theme_apply, on the game's own colour table so every menu sees it.)
function rfa_theme_modern() -- [RFA]
	return COLOR_HIGHLIGHT_BAR ~= nil and COLOR_HIGHLIGHT_BAR.rfa_modern == true -- [RFA]
end -- [RFA]

-- [RFA] The accent now (a {R, G, B} of 0 to 255).
function rfa_theme_accent() -- [RFA]
	local get = _GetAnyGlobalSilent("rfa_get_setting") -- [RFA] (the loader's)
	local index = (get ~= nil) and get("MainMenu", "Accent", 0) or 0 -- [RFA]
	return RFA_THEME_ACCENTS[index + 1] or RFA_THEME_ACCENTS[1] -- [RFA]
end -- [RFA]

-- [RFA] Sets the shared colours from the settings (Modern: the accent; Classic: the game's own). Called as the
-- [RFA] main menu opens and when the style or accent is changed; what is drawn after takes them.
function rfa_theme_apply() -- [RFA]
	local get = _GetAnyGlobalSilent("rfa_get_setting") -- [RFA]
	local modern = get ~= nil and get("MainMenu", "Style", 0) == 1 -- [RFA]
	local accent = rfa_theme_accent() -- [RFA]
	if modern then -- [RFA]
		rfa_theme_set(COLOR_HIGHLIGHT_BAR, accent.R, accent.G, accent.B) -- [RFA]
		rfa_theme_set(COLOR_HIGHLIGHT_BG, accent.R * 0.45, accent.G * 0.45, accent.B * 0.45) -- [RFA]
		rfa_theme_set(COLOR_HIGHLIGHT_TEXT, 245, 245, 245) -- [RFA]
		rfa_theme_set(COLOR_UNHIGHLIGHT_TEXT, 185, 188, 194) -- [RFA]
	else -- [RFA]
		for i, color in ipairs({COLOR_HIGHLIGHT_BAR, COLOR_HIGHLIGHT_BG, COLOR_HIGHLIGHT_TEXT, COLOR_UNHIGHLIGHT_TEXT}) do -- [RFA]
			if color ~= nil and color.rfa_game ~= nil then -- [RFA]
				color.R, color.G, color.B = color.rfa_game.R, color.rfa_game.G, color.rfa_game.B -- [RFA]
			end -- [RFA]
		end -- [RFA]
	end -- [RFA]
	if COLOR_HIGHLIGHT_BAR ~= nil then COLOR_HIGHLIGHT_BAR.rfa_modern = modern end -- [RFA]
	return modern -- [RFA]
end -- [RFA]

-- [RFA] A highlight bar in the Modern style: the UI pack's pictures (a bar down its left, a light band, the right end
-- [RFA] fading), white and coloured by the accent. Without the pack loaded (not made yet) it keeps the game's.
function rfa_theme_highlight(highlight) -- [RFA]
	if highlight == nil or highlight.left == nil or not rfa_theme_modern() then return end -- [RFA]
	if vint_document_find(RFA_THEME_PACK) ~= 0 then -- [RFA]
		highlight.left:set_property("image", "rfa_modern_hl_left") -- [RFA]
		highlight.center:set_property("image", "rfa_modern_hl_center") -- [RFA]
		highlight.right:set_property("image", "rfa_modern_hl_right") -- [RFA]
	end -- [RFA]
	for i, part in ipairs({highlight.left, highlight.center, highlight.right}) do -- [RFA]
		part:set_color(COLOR_HIGHLIGHT_BAR.R, COLOR_HIGHLIGHT_BAR.G, COLOR_HIGHLIGHT_BAR.B) -- [RFA]
	end -- [RFA]
end -- [RFA]

-- [RFA] A menu list (Vdo_menu_list) in the Modern style, its buttons too (each draw makes them again): sharp (no
-- [RFA] blur), the highlighted entry in the accent without its glow, the buttons without their boxes and the
-- [RFA] little text beside them.
function rfa_theme_list(list) -- [RFA]
	if list == nil or list.handle == nil or not rfa_theme_modern() then return end -- [RFA]
	local group = Vdo_base_object:new("list_group", list.handle) -- [RFA]
	group:set_property("blur", 0) -- [RFA]
	Vdo_base_object:new("menu_list_button_text_2", list.handle):set_property("alpha", 0) -- [RFA] the glow
	Vdo_base_object:new("menu_list_button_text", list.handle):set_color(COLOR_HIGHLIGHT_BAR.R, COLOR_HIGHLIGHT_BAR.G, COLOR_HIGHLIGHT_BAR.B) -- [RFA]
	Vdo_base_object:new("button_highlight", list.handle):set_color(COLOR_HIGHLIGHT_BAR.R, COLOR_HIGHLIGHT_BAR.G, COLOR_HIGHLIGHT_BAR.B) -- [RFA]
	if list.list_data ~= nil and list.list_data.buttons ~= nil then -- [RFA]
		for i, button in pairs(list.list_data.buttons) do -- [RFA]
			Vdo_base_object:new("background", button.handle):set_property("alpha", 0) -- [RFA]
			Vdo_base_object:new("techno_text", button.handle):set_property("alpha", 0) -- [RFA]
		end -- [RFA]
	end -- [RFA]
	rfa_theme_highlight(list.highlight) -- [RFA]
end -- [RFA]

-- [RFA] A menu screen in the Modern style (the main menu's and the pause menu's layout: their names are the same),
-- [RFA] in the document that calls it: the scrolling numbers, tech graphics and dotted bars go, the panel's corner
-- [RFA] is rounded (the pack's), and each list restyles itself after every draw. Nothing in Classic.
function rfa_theme_screen(lists) -- [RFA]
	if not rfa_theme_modern() then return end -- [RFA]
	for i, name in ipairs({"techno_bits", "right_dags", "right_dags_1", "right_dags_1_1", "right_dags_1_2", "tech_corner_1",
		"tech_meter", "tech_module", "scrolling_text1", "scrolling_text2", "scrolling_text3"}) do -- [RFA]
		local object = vint_object_find(name) -- [RFA]
		if object ~= 0 then vint_set_property(object, "visible", false) end -- [RFA]
	end -- [RFA]
	if vint_document_find(RFA_THEME_PACK) ~= 0 then -- [RFA]
		local corner = vint_object_find("mask_corner") -- [RFA]
		if corner ~= 0 then vint_set_property(corner, "image", "rfa_modern_corner") end -- [RFA]
	end -- [RFA]
	for i, list in ipairs(lists) do -- [RFA]
		if list ~= nil and list.rfa_themed == nil then -- [RFA] (wrapped once)
			list.rfa_themed = true -- [RFA]
			local draw_items = list.draw_items -- [RFA]
			list.draw_items = function(self, ...) -- [RFA]
				draw_items(self, ...) -- [RFA]
				rfa_theme_list(self) -- [RFA]
			end -- [RFA]
		end -- [RFA]
		rfa_theme_list(list) -- [RFA]
	end -- [RFA]
end -- [RFA]
-- [RFA] ---------------------------------------------------------------------------------------------------------

function vdo_button_highlight_init()
	vint_set_property(vint_object_find("highlight_blur_anim"),"state",TWEEN_STATE_DISABLED)
end

function vdo_button_highlight_cleanup()
end

-- Inherited from Vdo_base_object
Vdo_button_highlight = Vdo_base_object:new_base()

function Vdo_button_highlight:init()
	self.button = Vdo_hint_button:new("icon", self.handle)
	self.center = Vdo_base_object:new("center", self.handle)
	self.left = Vdo_base_object:new("left", self.handle)
	self.right = Vdo_base_object:new("right", self.handle)
	self.center:set_color(COLOR_HIGHLIGHT_BAR.R, COLOR_HIGHLIGHT_BAR.G, COLOR_HIGHLIGHT_BAR.B)
	self.left:set_color(COLOR_HIGHLIGHT_BAR.R, COLOR_HIGHLIGHT_BAR.G, COLOR_HIGHLIGHT_BAR.B)
	self.right:set_color(COLOR_HIGHLIGHT_BAR.R, COLOR_HIGHLIGHT_BAR.G, COLOR_HIGHLIGHT_BAR.B)
	rfa_theme_highlight(self) -- [RFA] the Modern style's pictures
end

function Vdo_button_highlight:show_button(button_icon)
	self.button:set_button( button_icon )
	self.button:set_property("visible", true)
end

function Vdo_button_highlight:hide_button()
	self.button:set_property("visible", false)
end

function Vdo_button_highlight:set_width(width)
	
	local blur_group = Vdo_base_object:new("blur_group", self.handle)
	blur_group:set_scale(1.0,1.0)

	--set the size of the highlight
	local center = Vdo_base_object:new("center", self.handle)
	local right = Vdo_base_object:new("right", self.handle)

	--get the size of the center
	local center_width, center_height = center:get_property("screen_size")

	--set the width of the center
	local highlight_fudge = 15
	center:set_property("screen_size", width + highlight_fudge, center_height)

	--get the x and y of the right
	local right_x, right_y = right:get_property("anchor")
	local center_x, center_y = center:get_property("anchor")
	local adjusted_x = center_x + width + highlight_fudge

	--set the x of the right
	right:set_property("anchor", adjusted_x, right_y)

	center = Vdo_base_object:new("blur_center", self.handle)
	right = Vdo_base_object:new("blur_right", self.handle)

	center_width, center_height = center:get_property("screen_size")

	--set the width of the center
	--center:set_property("screen_size", width, center_height)

	right_x, right_y = right:get_property("anchor")
	center_x, center_y = center:get_property("anchor")
	adjusted_x = center_x + (width)

	--set the x of the right
	--right:set_property( "anchor", adjusted_x, right_y )
end

function Vdo_button_highlight:set_highlight()
	local blur_anim = Vdo_anim_object:new( "highlight_blur_anim", self.handle )
	local blur_group = Vdo_base_object:new( "blur_group", self.handle )
	blur_anim:set_child_tween_target_handle( blur_group.handle )
	--blur_anim:play()
end
