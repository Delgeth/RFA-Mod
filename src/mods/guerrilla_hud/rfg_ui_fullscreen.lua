-- Red Faction: Guerrilla's world map in Armageddon: its whole world, the player on it, and the name of
-- the district the player is in. Opened and closed with [GuerrillaHud] MapKey= (M).
--
-- Part of the guerrilla_hud mod. The document this drives, rfg_ui_fullscreen, is Guerrilla's own
-- full-screen map document, made from your copy of Guerrilla by GuerrillaHud.exe (its pictures
-- renamed rfg_...), which also packs this script with it; run that again after changing this file.
-- The mod loads it with the minimap in Guerrilla's world and puts it on screen while the map is open.
--
-- Guerrilla drew the map itself in the document's map element (its "cartographer"), which here is a
-- plain picture: this script shows Guerrilla's world map picture (terr01_map, its whole world, north
-- up) over the screen's height on a black ground, with Guerrilla's player icon where the player is,
-- turned the way the camera looks, and the district's name in the document's header. Guerrilla's
-- zooming and scrolling (and its zoom readout), its places of interest, district control and
-- morale, the salvage count, its legend and the controller hints have nothing behind them yet, so
-- they stay hidden.

local SCREEN_W, SCREEN_H = 1280, 720    -- the document's layout
local MAP_PIXELS = 1024                 -- Guerrilla's world map picture
local MAP_SIZE = SCREEN_H               -- shown the screen's height, in the middle
local MARKER_SCALE = 1.2                -- the player's icon, as the document's own map icon

local HIDDEN = { "control_main", "control_meter", "morale_meter", "crosshair_main", "hints_main", "map_label_screen_group",
	"district_filter_main", "legend_main", "fullscreen_map", "zoom_level_group" }

local Marker = 0
local Was_open = false
local Shown_district = nil
local Thread = -1

local function find(name)
	return vint_object_find(name)
end

local function picture(name, parent, image)
	local handle = vint_object_create(name, "bitmap", parent)
	vint_set_property(handle, "image", image)
	return handle
end

local function build_map()
	local root = find("scale_group")
	local ground = picture("rfg_fullmap_ground", root, "rfg_ui_hud_base_square")
	vint_set_property(ground, "scale", SCREEN_W, SCREEN_H)
	vint_set_property(ground, "tint", 0, 0, 0)
	vint_set_property(ground, "depth", 20)

	local map = vint_object_create("rfg_fullmap", "group", root)
	vint_set_property(map, "anchor", (SCREEN_W - MAP_SIZE) / 2, 0)
	vint_set_property(map, "depth", 14)	-- where the document's own map element was

	local world = picture("rfg_fullmap_world", map, "rfg_terr01_map")
	vint_set_property(world, "scale", MAP_SIZE / MAP_PIXELS, MAP_SIZE / MAP_PIXELS)
	vint_set_property(world, "depth", 10)

	Marker = picture("rfg_fullmap_player", map, "rfg_ui_map_icon_player")
	vint_set_property(Marker, "auto_offset", "c")
	vint_set_property(Marker, "scale", MARKER_SCALE, MARKER_SCALE)
	vint_set_property(Marker, "depth", 0)
end

local function follow()
	while true do
		local active, map_x, map_y, heading, speed, column, row, district, map_open = rfg_map_view()
		if map_open and not Was_open then
			local fade = find("fadein_tween")
			if fade ~= 0 then
				vint_set_property(fade, "start_time", vint_get_time_index())
				vint_set_property(fade, "state", TWEEN_STATE_RUNNING)
			end
		end
		Was_open = map_open
		if map_open and active then
			vint_set_property(Marker, "visible", true)
			vint_set_property(Marker, "anchor", map_x * MAP_SIZE / MAP_PIXELS, map_y * MAP_SIZE / MAP_PIXELS)
			vint_set_property(Marker, "rotation", heading)
			if district ~= Shown_district then
				vint_set_property(find("header_text"), "text_tag", district)
				Shown_district = district
			end
		elseif not active then
			vint_set_property(Marker, "visible", false)
		end
		thread_yield()
	end
end

function rfg_ui_fullscreen_init()
	for i, name in ipairs(HIDDEN) do
		local handle = find(name)
		if handle ~= 0 then
			vint_set_property(handle, "visible", false)
		end
	end
	vint_set_child_tween_state(find("mission_active_anim"), TWEEN_STATE_DISABLED)
	vint_set_child_tween_state(find("label_fade_out_anim"), TWEEN_STATE_DISABLED)
	if rfg_map_view == nil then
		vint_set_property(find("scale_group"), "visible", false)	-- an older loader: see rfg_ui_hud.lua
		return
	end
	build_map()
	Thread = thread_new("rfg_ui_fullscreen_follow")
end

function rfg_ui_fullscreen_follow()
	follow()
end

function rfg_ui_fullscreen_cleanup()
	if Thread ~= -1 then
		thread_kill(Thread)
		Thread = -1
	end
end
