-- Red Faction: Guerrilla's minimap in Armageddon: the map turning with the camera around the player,
-- Guerrilla's frame with its player arrow, and the district's name above it.
--
-- Part of the guerrilla_hud mod. The document this drives, rfg_ui_hud, is Guerrilla's own HUD
-- document, made from your copy of Guerrilla by GuerrillaBuilder.exe (its pictures renamed rfg_...),
-- which also packs this script with it; run that again after changing this file. The mod loads it in
-- the worlds named by [GuerrillaHud] Worlds= (with [GuerrillaHud] Map=1) and unloads it everywhere
-- else.
--
-- Guerrilla drew the map inside its minimap in its game code (the "cartographer"): the octagon its
-- mask picture cuts out, each zone's own picture (a metre a pixel) around the player, turned so the
-- camera looks up, then the frame picture over it (ui_map_overlay: the frame, its shading and the
-- player's arrow in the middle). Armageddon kept that code only for its compass, so this script
-- builds the same from the document's own elements:
--   - a clip box and a square turned 45 degrees, used as a mask: together they cut Guerrilla's
--     octagon (its mask picture's, measured: the box 7 to 163 of the frame's 170, the corners cut
--     where x + y = 59);
--   - in them, a group turned against the camera, holding Guerrilla's world map picture (a zone
--     every 85 1/3 pixels, under everything, for the zones whose own pictures aren't loaded) and
--     up to nine zones' own pictures, and over them Guerrilla's map icons near the player (its
--     safehouses and its missions' and activities' starts, from map_icons.txt), each kept upright;
--   - the frame on top: the document's own map element, which GuerrillaBuilder.exe made a picture.
-- How far the minimap shows is Guerrilla's: 40 metres from the player to the frame's edge, and half
-- the vehicle's speed more while driving. Where the player is, which way the camera looks and which
-- zone pictures are loaded, the mod tells this script through rfg_map_view() and rfg_map_tiles().
--
-- The district's name is shown as Guerrilla's HUD shows it when the player enters another district
-- (its own animation). Guerrilla's alert level, EDF control and morale meters, new action banner
-- and radio have nothing behind them in Armageddon yet, so they stay hidden.

local FRAME = 170                       -- the frame picture's size (ui_map_overlay)
local OCTAGON_FROM = 7                  -- the octagon's box, from the frame's edge
local OCTAGON_SIZE = 156
local OCTAGON_DIAMOND = 156.8           -- side of the turned square that cuts its corners (its half-diagonal 110.9)
local MAP_PIXELS = 1024                 -- Guerrilla's world map picture
local ZONE_PIXELS = MAP_PIXELS / 12     -- a zone on it (columns and rows 2 to 13)
local ZONE_METRES = 511
local TILE_PIXELS = 512                 -- a zone's own picture
local RADIUS = 40                       -- Guerrilla's minimap: metres from the player to the frame's edge
local SPEED_RADIUS = 0.5                -- and this much more per m/s of the vehicle's speed
local TILES = 9
local ICONS = 24                        -- icons shown at once (the nearest first come first)
local ICON_SCALE = 0.55                 -- Guerrilla's map icons are 40 x 40 pictures
local ICON_REACH = 1.5                  -- icons drawn this many radii out (the octagon's corners are 1.41)
-- The game's Lua has no math or string library (its globals are checked: reading a missing one is
-- an error), so these are written out.
local QUARTER_TURN = 1.5707963267949

local function two_digits(number)
	if number < 10 then
		return "0" .. number
	end
	return "" .. number
end

-- Parts of this document with nothing behind them in Armageddon yet.
local HIDDEN = { "control_meter", "morale_meter", "action_group", "radio_main", "radio_subtitle_group", "alert_level_group" }

local Map_handles = {}                  -- the elements this script made
local Tile_handles = {}
local Icons = {}                        -- Guerrilla's map icons: { picture, x, y } on its world map picture
local Icon_handles = {}
local Icon_shown = {}                   -- the picture each icon element shows, or nil while hidden
local Tile_shown = {}                   -- "column_row" each tile element shows, or nil
local In_vehicle = false
local Shown_district = nil
local District_open = false
local Hud_hidden = false
local Map_open = false
local Thread = -1

-- For finding out what goes wrong ([GuerrillaHud] Log=1, through the loader's rfa_log): what the
-- map is built of and where its parts land on screen, at load and on the first frames. Errors in
-- this script are always written.
local LOG_LINES = 120
local Log_on = false
local Logged = 0
local Frames_logged = 0

local function log(text, always)
	if rfa_log ~= nil and (always or (Log_on and Logged < LOG_LINES)) then
		Logged = Logged + 1
		rfa_log("[rfg_ui_hud] " .. text)
	end
end

local function find(name)
	return vint_object_find(name)
end

local function make(name, kind, parent)
	local handle = vint_object_create(name, kind, parent)
	Map_handles[#Map_handles + 1] = handle
	log("made " .. name .. " (" .. kind .. ") = " .. tostring(handle) .. " in " .. tostring(parent))
	return handle
end

local function values(...)
	local text = ""
	for i, value in ipairs({ ... }) do
		text = text .. (i > 1 and " " or "") .. tostring(value)
	end
	return text
end

-- An element as the game holds it: shown, alpha, depth, anchor, scale, rotation, its picture's
-- size and where it lands on screen.
local function describe(name, handle)
	local text = name .. "=" .. tostring(handle)
	for i, property in ipairs({ "visible", "alpha", "depth", "anchor", "scale", "rotation", "unscaled_size" }) do
		local ok, a, b = pcall(vint_get_property, handle, property)
		text = text .. ", " .. property .. " " .. (ok and values(a, b) or "?")
	end
	local ok, x1, y1, x2, y2 = pcall(vint_get_bbox, handle)
	return text .. ", bbox " .. (ok and values(x1, y1, x2, y2) or "?")
end

local function log_map(when)
	log(when .. ": " .. describe("frame", find("map_overlay")))
	for i, name in ipairs({ "rfg_minimap_clip", "rfg_minimap_mask", "rfg_minimap_turn", "rfg_minimap_world", "rfg_minimap_tile1" }) do
		log("  " .. describe(name, find(name)))
	end
end

local function picture(name, parent, image)
	local handle = make(name, "bitmap", parent)
	vint_set_property(handle, "image", image)
	return handle
end

-- Builds the map inside the frame (see the top of this file).
local function build_map()
	local frame = find("map_overlay")
	local main = find("mini_map_main")
	local x, y = vint_get_property(frame, "anchor")

	local clip = make("rfg_minimap_clip", "clip", main)
	vint_set_property(clip, "anchor", x + OCTAGON_FROM, y + OCTAGON_FROM)
	vint_set_property(clip, "clip_size", OCTAGON_SIZE, OCTAGON_SIZE)
	vint_set_property(clip, "clip_enabled", true)
	vint_set_property(clip, "depth", 0)		-- under the frame (depth -1), over its background (4)

	local mask = picture("rfg_minimap_mask", clip, "rfg_ui_hud_base_square")
	vint_set_property(mask, "mask", true)
	vint_set_property(mask, "render_mode", "multiply")	-- as Armageddon's own masks: drawing it leaves the picture as it is
	vint_set_property(mask, "auto_offset", "c")
	vint_set_property(mask, "anchor", OCTAGON_SIZE / 2, OCTAGON_SIZE / 2)
	vint_set_property(mask, "scale", OCTAGON_DIAMOND, OCTAGON_DIAMOND)
	vint_set_property(mask, "rotation", QUARTER_TURN / 2)
	vint_set_property(mask, "depth", 0)		-- before the map: a mask cuts the elements after it

	local turn = make("rfg_minimap_turn", "group", clip)
	vint_set_property(turn, "anchor", FRAME / 2 - OCTAGON_FROM, FRAME / 2 - OCTAGON_FROM)
	vint_set_property(turn, "depth", -10)
	Map_handles.turn = turn

	local world = picture("rfg_minimap_world", turn, "rfg_terr01_map")
	vint_set_property(world, "depth", 10)		-- under the zones' own pictures
	Map_handles.world = world

	for i = 1, TILES do
		Tile_handles[i] = make("rfg_minimap_tile" .. i, "bitmap", turn)
		vint_set_property(Tile_handles[i], "visible", false)
		vint_set_property(Tile_handles[i], "depth", 0)
	end

	-- Guerrilla's map icons (its safehouses, missions and activities: map_icons.txt, through the mod),
	-- over the map, each kept upright as the map turns.
	if rfg_map_icon_count ~= nil then
		for i = 1, rfg_map_icon_count() do
			local kind, image, x, y = rfg_map_icon(i)
			if kind == "icon" or kind == "bomb" then	-- a M.O.A.B. only once its radio tags are found
				Icons[#Icons + 1] = { picture = image, x = x, y = y, index = i, bomb = kind == "bomb" }
			end
		end
	end
	for i = 1, ICONS do
		Icon_handles[i] = make("rfg_minimap_icon" .. i, "bitmap", turn)
		vint_set_property(Icon_handles[i], "visible", false)
		vint_set_property(Icon_handles[i], "auto_offset", "c")
		vint_set_property(Icon_handles[i], "scale", ICON_SCALE, ICON_SCALE)
		vint_set_property(Icon_handles[i], "depth", -5)
	end
	log(#Icons .. " map icons")
end

-- Guerrilla's icons within reach of the player, upright (the map group turns by -heading).
local function place_icons(map_x, map_y, heading, radius, world_scale)
	local reach = radius * ICON_REACH * ZONE_PIXELS / ZONE_METRES	-- in world map pixels
	local used = 0
	for i, icon in ipairs(Icons) do
		local dx, dy = icon.x - map_x, icon.y - map_y
		if used < ICONS and dx > -reach and dx < reach and dy > -reach and dy < reach and (not icon.bomb or rfg_map_icon_shown(icon.index)) then
			used = used + 1
			local handle = Icon_handles[used]
			if Icon_shown[used] ~= icon.picture then
				vint_set_property(handle, "image", icon.picture)
				if Icon_shown[used] == nil then
					vint_set_property(handle, "visible", true)
				end
				Icon_shown[used] = icon.picture
			end
			vint_set_property(handle, "anchor", dx * world_scale, dy * world_scale)
			vint_set_property(handle, "rotation", heading)
		end
	end
	for i = used + 1, ICONS do
		if Icon_shown[i] ~= nil then
			vint_set_property(Icon_handles[i], "visible", false)
			Icon_shown[i] = nil
		end
	end
end

-- The map around the player: turned against the camera, scaled to the minimap's reach.
local function place_map(map_x, map_y, heading, speed)
	local radius = RADIUS
	if In_vehicle then
		radius = radius + SPEED_RADIUS * speed
	end
	local scale = (FRAME / 2) / radius			-- frame units a metre
	local world_scale = scale * ZONE_METRES / ZONE_PIXELS	-- a world map pixel
	vint_set_property(Map_handles.turn, "rotation", -heading)
	vint_set_property(Map_handles.world, "scale", world_scale, world_scale)
	vint_set_property(Map_handles.world, "anchor", -map_x * world_scale, -map_y * world_scale)

	-- The zones' own pictures that are loaded.
	local tiles = { rfg_map_tiles() }
	local count = tiles[1]
	local tile_scale = scale * ZONE_METRES / TILE_PIXELS
	for i = 1, TILES do
		local handle = Tile_handles[i]
		if i <= count then
			local column, row = tiles[2 * i], tiles[2 * i + 1]
			local name = two_digits(column) .. "_" .. two_digits(row)
			if Tile_shown[i] ~= name then
				vint_set_property(handle, "image", "rfg_terr01_" .. name .. "_map")
				Tile_shown[i] = name
			end
			vint_set_property(handle, "scale", tile_scale, tile_scale)
			vint_set_property(handle, "anchor", ((column - 2) * ZONE_PIXELS - map_x) * world_scale,
				((row - 2) * ZONE_PIXELS - map_y) * world_scale)
			vint_set_property(handle, "visible", true)
		elseif Tile_shown[i] ~= nil then
			vint_set_property(handle, "visible", false)
			Tile_shown[i] = nil
		end
	end

	place_icons(map_x, map_y, heading, radius, world_scale)
end

-- Guerrilla's set_district_info: shows the name with its animation, the text fitted to its box.
local function show_district(name)
	local text = find("mini_map_district")
	local anim = find("district_name_anim")
	vint_set_property(text, "visible", name ~= "")
	if not District_open then
		vint_set_property(anim, "start_time", vint_get_time_index())
		vint_set_child_tween_state(anim, TWEEN_STATE_WAITING)	-- Guerrilla's TWEEN_STATE_IDLE: both are 0
		vint_set_property(find("district_group_anchor_twn_2"), "end_event", "rfg_ui_hud_district_tween_end")
		District_open = true
	else
		vint_set_property(anim, "start_time", vint_get_time_index() - 0.17)
	end
	vint_set_property(text, "text_tag", name)

	vint_set_property(text, "text_scale", 0.75, 0.75)
	local text_scale_x, text_scale_y = vint_get_property(text, "text_scale")
	local parent_scale_x = vint_get_property(find("mini_map_main"), "scale")
	local text_width = vint_get_property(text, "screen_size")
	local max_width = 164.0 * parent_scale_x
	if text_width > max_width then
		vint_set_property(text, "text_scale", max_width / text_width * text_scale_x, text_scale_y)
	end
end

function rfg_ui_hud_district_tween_end()
	District_open = false
end

local function show(visible)
	vint_set_property(find("scale_group"), "visible", visible)
end

-- One frame: follows the player and the camera.
local function frame()
	local active, map_x, map_y, heading, speed, column, row, district, map_open = rfg_map_view()
	Map_open = map_open
	show(active and not Hud_hidden and not map_open)
	if active then
		place_map(map_x, map_y, heading, speed)
		if district ~= Shown_district then
			show_district(district)
			Shown_district = district
		end
	end
	if Log_on and active and (Frames_logged < 3 or Frames_logged == 60) then
		log("frame " .. Frames_logged .. ": map " .. values(map_x, map_y) .. ", heading " .. tostring(heading) .. ", speed " ..
			tostring(speed) .. ", zone " .. values(column, row) .. ", district " .. tostring(district) .. ", tiles " ..
			values(rfg_map_tiles()))
		log_map("frame " .. Frames_logged)
	end
	Frames_logged = Frames_logged + 1
end

-- Every frame, until the document goes. An error is written to the log once, and the map stops.
local function follow()
	while true do
		local ok, problem = pcall(frame)
		if not ok then
			log("the minimap stopped: " .. tostring(problem), true)
			return
		end
		thread_yield()
	end
end

-- Hides what has nothing behind it, and stops its animations.
local function hide_unused()
	for i, name in ipairs(HIDDEN) do
		vint_set_property(find(name), "visible", false)
	end
	for i, name in ipairs({ "control_meter_anim", "morale_meter_anim", "action_anim", "mini_map_alert_anim", "district_name_anim" }) do
		vint_set_child_tween_state(find(name), TWEEN_STATE_DISABLED)
	end
end

function rfg_ui_hud_init()
	Log_on = rfa_get_setting ~= nil and rfa_get_setting("GuerrillaHud", "Log", 0) ~= 0
	Logged = 0
	Frames_logged = 0
	log("init: this document " .. tostring(vint_document_find("rfg_ui_hud")) .. ", rfg_map_view " .. tostring(rfg_map_view))
	local hidden, problem = pcall(hide_unused)
	if not hidden then
		log("hiding the unused parts: " .. tostring(problem), true)
	end

	vint_dataitem_add_subscription("health_hud", "update", "rfg_ui_hud_health_target")
	vint_dataitem_add_subscription("hud_hide", "update", "rfg_ui_hud_hide")
	if rfg_map_view == nil then
		-- An older loader, which doesn't add the mods' Lua functions: the map can't know where the player is.
		show(false)
		if rfa_log ~= nil then
			rfa_log("[rfg_ui_hud] rfg_map_view() is missing (an older rfa_loader.dll?): Guerrilla's minimap stays hidden")
		end
		return
	end
	local ok, problem = pcall(build_map)
	if not ok then
		log("the minimap couldn't be built: " .. tostring(problem), true)
		return
	end
	log_map("built")
	Thread = thread_new("rfg_ui_hud_follow")
	log("init done, thread " .. tostring(Thread))
end

function rfg_ui_hud_follow()
	follow()
end

function rfg_ui_hud_cleanup()
	if Thread ~= -1 then
		thread_kill(Thread)
		Thread = -1
	end
end

-- health_hud: visible, target ("player", or the vehicle or exo-suit being driven), ...
function rfg_ui_hud_health_target(di_h, event_type)
	local visible, target_name = vint_dataitem_get(di_h)
	In_vehicle = target_name ~= nil and target_name ~= "player"
end

-- Cutscenes and the like hide the HUD (all but the subtitles): hide this with it.
function rfg_ui_hud_hide(di_h, event_type)
	Hud_hidden = vint_dataitem_get(di_h)
end
