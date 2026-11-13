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
--     under the icons, Guerrilla's blips for the people and vehicles near the player (a dot a person,
--     a marker turned as the vehicle is; who shows and in which colour is Guerrilla's, in the mod:
--     rfg_map_blips), each fading behind Guerrilla's radar sweep, a ring growing on each person it passes;
--   - over them, not turned, the sweep's line moving down the minimap (see SWEEP_PIXELS_PER_SECOND);
--   - the frame on top: the document's own map element, which GuerrillaBuilder.exe made a picture.
-- How far the minimap shows is Guerrilla's: 40 to 80 metres from the player to the frame's edge, by
-- its zoom keys (the mod's rfg_minimap_radius), and half the vehicle's speed more while driving. Where the player is, which way the camera looks and which
-- zone pictures are loaded, the mod tells this script through rfg_map_view() and rfg_map_tiles().
--
-- The district's name is shown as Guerrilla's HUD shows it when the player enters another district
-- (its own animation). Guerrilla's alert level shows above the map while the guerrilla_campaign mod
-- runs it (rfg_alert()): its light green, yellow, orange or red, pulsing faster and wider the higher
-- it is, as Guerrilla's script set it. Guerrilla's EDF control and morale meters, new action banner
-- and radio have nothing behind them in Armageddon yet, so they stay hidden.

local FRAME = 170                       -- the frame picture's size (ui_map_overlay)
local OCTAGON_FROM = 7                  -- the octagon's box, from the frame's edge
local OCTAGON_SIZE = 156
local OCTAGON_DIAMOND = 156.8           -- side of the turned square that cuts its corners (its half-diagonal 110.9)
local MAP_PIXELS = 1024                 -- Guerrilla's world map picture
local ZONE_PIXELS = MAP_PIXELS / 12     -- a zone on it (columns and rows 2 to 13)
local ZONE_METRES = 511
local TILE_PIXELS = 512                 -- a zone's own picture
local RADIUS = 40                       -- Guerrilla's minimap: metres from the player to the frame's edge (zoomed in)
local SPEED_RADIUS = 0.5                -- and this much more per m/s of the vehicle's speed
local TILES = 9
local ICONS = 24                        -- icons shown at once (the nearest first come first)
local ICON_SCALE = 0.55                 -- Guerrilla's map icons are 40 x 40 pictures
-- Guerrilla's map icons on its minimap (cartographer::render_sticky_icons RM 0x878180, the activities' start icons;
-- the mod's GuerrillaMapIcons gives each its rule): a "sticky" one (a safehouse, a mission's start, a M.O.A.B.) further
-- than 0.9 of the radius sits on the rim, pointing the way; the others (the activities' starts) show only inside the
-- radius. Only the nearest safehouse shows, at any distance; a mission's start within 250 m; a M.O.A.B. within 1000 m
-- (any sticky icon but a safehouse). A demolitions master's start only at a green alert; an area defense's, riding
-- shotgun's and house arrest's at green or yellow, a house arrest's only 80 m or more away; a raid's and Guerrilla
-- Express's at any. Not yet: their hiding while an activity or mission runs, and grey once done (the campaign's).
local STICKY_RIM = 0.9
local STICKY_MOST_METRES = 1000
local MISSION_START_METRES = 250
local HOUSE_ARREST_LEAST_METRES = 80
local ALERT_GREEN_MOST, ALERT_YELLOW_MOST = 25, 50   -- rfg_alert's value: 0-25 green, to 50 yellow
-- The north marker on the minimap's rim, where north is (render_north 0x862020: ui_map_icon_north along the overlay).
local NORTH_RIM = 80                    -- frame units from the middle: the octagon's edge
-- Guerrilla's radio tags not found yet within the radius (collect_bftp_nodes 0x86D450, render_blast_from_the_past_tags
-- 0x860310): a green ring (ui_map_icon_blip, 64 255 64) of +-3.5 at the minimap's scale, growing and fading over 750 ms
-- of every 3 s.
local TAGS = 8
local TAG_RING_UNITS = 2 * 3.5 * 1.5    -- across, at its biggest
local TAG_RING_SECONDS, TAG_RING_EVERY = 0.75, 3.0
local TAG_RING_COLOUR = { 64, 255, 64 }
-- What other mods give the minimap (the mod's rfg_map_* calls; nothing of one whose mod is older):
--   the campaign: an activity running hides the activities' starts (and doesn't pin the important buildings), a mission
--   hides the safehouse and missions' starts; a start done is grey (Completed_icon_color 130 130 130);
--   the world: the important EDF buildings around (render_bldg_icons 0x86F4D0: ui_map_icon_phigh / _pmedium), pinned to
--   the rim while no activity or mission runs, else only inside the radius;
--   the gear: placed explosives inside the radius (render_projectiles 0x85FCC0: ui_map_icon_blip 2.5 across and a ring
--   growing to 7.5 every 0.5 s; a remote charge white and ringed when stuck, else faint grey; a mine orange; a
--   singularity bomb teal);
--   the people: their markers (a squad's icon, the player's allies' blue stars, morale's converts' gold heads): plain,
--   sticky, closest sticky (only the 3 nearest), or a convert's 0.5 s pop.
local DONE_COLOUR = { 130, 130, 130 }
local BUILDINGS = 12
local EXPLOSIVES = 8
local EXPLOSIVE_UNITS, EXPLOSIVE_RING_UNITS, EXPLOSIVE_RING_SECONDS = 2.5, 7.5, 0.5
local EXPLOSIVE_COLOURS = { [0] = { 255, 255, 255 }, { 255, 128, 0 }, { 0, 128, 128 } }
local LOOSE_CHARGE_ALPHA = 0.35
local MARKERS = 16
local CLOSEST_STICKY_MOST = 3
local CONVERT_POP_MS = 500
-- Guerrilla's blips (its render_humans and render_vehicles): a person is its dot picture drawn 12 units
-- across, smaller as the minimap zooms out (down to 0.75 of that at its widest, 80 metres); a vehicle is
-- its marker picture drawn as a square 9.75 metres wide in the world, turned as the vehicle is.
local BLIPS = 40                        -- people and vehicles shown at once (the mod sends the nearest)
local BLIP_PIXELS = 32                  -- both pictures' size
local HUMAN_BLIP = 12
local HUMAN_BLIP_LEAST = 0.75
local WIDEST_RADIUS = 80
local VEHICLE_BLIP_METRES = 9.75
-- Guerrilla's blip colours (0 to 255), by the number the mod gives: grey (nobody's, or no one in it), the
-- Red Faction's yellow, the EDF's red, the Marauders' purple.
local BLIP_COLOURS = { [0] = { 200, 200, 200 }, { 240, 200, 52 }, { 148, 13, 0 }, { 120, 80, 130 } }
-- Guerrilla's radar sweep (its cartographer's render_scan_line, render_humans and render_vehicles: RM 0x863A60,
-- 0x86D710, 0x86DED0). A line moves down the minimap at 150 screen pixels a second (Minimap_radar_update_rate;
-- Guerrilla's minimap window is the frame's size on screen), from the frame's top until its picture, as tall as
-- the frame with the line at its bottom, has left the frame's bottom: twice the frame's height, then from the top
-- again. It stands still while the game is paused (the mod's rfg_map_sweep_clock). Behind it each blip fades,
-- from full where the line has just passed to nothing when it comes round again (Minimap_radar_min_alpha is 0);
-- and on each person a ring (ui_map_icon_ring) grows: from half its size (Minimap_blip_start_scale) by 6 sizes
-- (Minimap_radar_blip_scale_rate) a sweep, twice the dot across at a size of 1 (Minimap_radar_blip_scale), so as
-- big as the dot as the line passes; in the dot's colour lightened by 128; fading from 128 x 2
-- (Minimap_blip_opacity) to nothing a third of a sweep behind the line. People's rings are drawn under every
-- dot, the vehicles under the people, the line over them all (Guerrilla's order).
local SWEEP_PIXELS_PER_SECOND = 150
local SWEEP_PICTURE = "rfg_ui_minimap_scan_line"
local SWEEP_PICTURE_PIXELS = 128
local RING_PICTURE = "rfg_ui_map_icon_ring"
local RING_PIXELS = 24
local RING_START = 0.5
local RING_GROWTH = 6
local RING_ACROSS = 2
local RING_OPACITY = 128
local RING_LIGHTER = 128
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

-- Guerrilla's alert light by level (its script's alert_level): the light's and its shine's colour (0 to 1,
-- as Guerrilla's script gives them; Armageddon's scripts set a tint in 0 to 255), and the shine's pulse (how
-- long, how strong, how wide); green has none.
local ALERT = {
	{ 0.01, 0.64, 0.01 },
	{ 0.98, 0.90, 0.02, 1.0, 0.33, 1.0 },
	{ 1.0, 0.40, 0.0, 0.75, 0.50, 1.2 },
	{ 0.70, 0.08, 0.08, 0.50, 0.75, 1.5 },
}
local Alert_shown = false               -- the alert light is up
local Alert_band = 0                    -- which of ALERT it shows (0: none yet)

local Map_handles = {}                  -- the elements this script made
local Tile_handles = {}
local Icons = {}                        -- Guerrilla's map icons: { picture, x, y } on its world map picture
local Icon_handles = {}
local Icon_shown = {}                   -- the picture each icon element shows, or nil while hidden
local Blip_handles = {}
local Blip_shown = {}                   -- what each blip element shows (its kind and colour), or nil while hidden
local Blips_on = true
local Has_zoom = false					-- the mod has Guerrilla's minimap zoom (rfg_minimap_radius)
local Has_tags = false					-- the mod gives the radio tags (rfg_map_radio_tags)
local Has_others = false				-- the mod asks the other mods (rfg_map_buildings, ...)
local Icon_tint = {}					-- the tint each icon element shows ("done" or "own")
local Building_handles, Building_shown = {}, {}
local Explosive_handles, Explosive_rings, Explosive_shown = {}, {}, {}
local Marker_handles, Marker_shown = {}, {}
local Tag_handles = {}
local Tag_shown = {}
local Ring_handles = {}
local Ring_shown = {}                   -- the colour each ring element shows, or nil while hidden
local Sweep = { on = false, position = 0, clock = nil, handle = nil, pixels = FRAME, checked = 0, check_at = 5, least_alpha = 0 }
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
	Map_handles.frame = frame

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
			local kind, image, x, y, caption, description, on_world_map, rule = rfg_map_icon(i)
			if kind == "icon" or kind == "bomb" then	-- a M.O.A.B. only once its radio tags are found
				Icons[#Icons + 1] = { picture = image, x = x, y = y, index = i, bomb = kind == "bomb", rule = rule or "" }
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

	-- Guerrilla's blips: under its icons, the people over the vehicles (the order Guerrilla draws them in), the
	-- sweep's rings between them.
	for i = 1, BLIPS do
		Blip_handles[i] = make("rfg_minimap_blip" .. i, "bitmap", turn)
		vint_set_property(Blip_handles[i], "visible", false)
		vint_set_property(Blip_handles[i], "auto_offset", "c")
		vint_set_property(Blip_handles[i], "depth", -1)
	end

	-- Guerrilla's radio tags' rings, under the blips.
	Has_tags = _GetAnyGlobalSilent("rfg_map_radio_tags") ~= nil
	if Has_tags then
		for i = 1, TAGS do
			Tag_handles[i] = picture("rfg_minimap_tag" .. i, turn, "rfg_ui_map_icon_blip")
			vint_set_property(Tag_handles[i], "visible", false)
			vint_set_property(Tag_handles[i], "auto_offset", "c")
			vint_set_property(Tag_handles[i], "tint", TAG_RING_COLOUR[1], TAG_RING_COLOUR[2], TAG_RING_COLOUR[3])
			vint_set_property(Tag_handles[i], "depth", 0)
		end
	end
	-- What other mods give: the important buildings, the explosives (with their rings) and the people's markers.
	Has_others = _GetAnyGlobalSilent("rfg_map_buildings") ~= nil
	if Has_others then
		for i = 1, BUILDINGS do
			Building_handles[i] = make("rfg_minimap_building" .. i, "bitmap", turn)
			vint_set_property(Building_handles[i], "visible", false)
			vint_set_property(Building_handles[i], "auto_offset", "c")
			vint_set_property(Building_handles[i], "scale", ICON_SCALE, ICON_SCALE)
		end
		for i = 1, EXPLOSIVES do
			Explosive_rings[i] = picture("rfg_minimap_explosive_ring" .. i, turn, "rfg_ui_map_icon_ring")
			vint_set_property(Explosive_rings[i], "visible", false)
			vint_set_property(Explosive_rings[i], "auto_offset", "c")
			vint_set_property(Explosive_rings[i], "depth", -4)
			Explosive_handles[i] = make("rfg_minimap_explosive" .. i, "bitmap", turn)
			vint_set_property(Explosive_handles[i], "visible", false)
			vint_set_property(Explosive_handles[i], "auto_offset", "c")
			vint_set_property(Explosive_handles[i], "depth", -4)
		end
		for i = 1, MARKERS do
			Marker_handles[i] = make("rfg_minimap_marker" .. i, "bitmap", turn)
			vint_set_property(Marker_handles[i], "visible", false)
			vint_set_property(Marker_handles[i], "auto_offset", "c")
			vint_set_property(Marker_handles[i], "depth", -7)
		end
	end
	-- Guerrilla's north marker, on the frame's rim (not turned, not cut by the octagon).
	if _GetAnyGlobalSilent("rfg_sin_cos") ~= nil then
		local north = picture("rfg_minimap_north", main, "rfg_ui_map_icon_north")
		vint_set_property(north, "auto_offset", "c")
		vint_set_property(north, "scale", ICON_SCALE, ICON_SCALE)
		vint_set_property(north, "depth", -2)		-- over the frame (-1)
		Map_handles.north = north
		Map_handles.centre_x = x + FRAME / 2
		Map_handles.centre_y = y + FRAME / 2
	end

	-- Guerrilla's radar sweep: its line over the map, not turned with it; a ring for each person.
	-- An older mod has neither (reading a global that isn't there is an error: Armageddon's own silent look).
	if _GetAnyGlobalSilent("rfg_map_sweep_clock") ~= nil and _GetAnyGlobalSilent("rfg_sin_cos") ~= nil then
		for i = 1, BLIPS do
			Ring_handles[i] = picture("rfg_minimap_ring" .. i, turn, RING_PICTURE)
			vint_set_property(Ring_handles[i], "visible", false)
			vint_set_property(Ring_handles[i], "auto_offset", "c")
			vint_set_property(Ring_handles[i], "depth", -2)
		end
		local line = picture("rfg_minimap_sweep", clip, SWEEP_PICTURE)
		vint_set_property(line, "auto_offset", "nw")
		vint_set_property(line, "scale", FRAME / SWEEP_PICTURE_PIXELS, FRAME / SWEEP_PICTURE_PIXELS)
		vint_set_property(line, "depth", -12)		-- over the turned map (-10)
		Sweep.handle = line
		Sweep.on = true
	else
		log("no rfg_map_sweep_clock or rfg_sin_cos (an older guerrilla_hud.dll?): no radar sweep", true)
	end
end

-- The sweep's line, moved by the time since the last frame: 150 screen pixels a second, in the frame's units.
local function sweep_step()
	local now = rfg_map_sweep_clock()
	local seconds = 0
	if Sweep.clock ~= nil then
		seconds = now - Sweep.clock
	end
	Sweep.clock = now
	local ok, x1, y1, x2, y2 = pcall(vint_get_bbox, Map_handles.frame)
	if ok and y1 ~= nil and y2 ~= nil and y2 - y1 > 1 then
		Sweep.pixels = y2 - y1
	end
	Sweep.position = Sweep.position + seconds * SWEEP_PIXELS_PER_SECOND * FRAME / Sweep.pixels
	if Sweep.position > 2 * FRAME or Sweep.position < 0 then
		Sweep.position = 0
	end
	vint_set_property(Sweep.handle, "anchor", -OCTAGON_FROM, Sweep.position - FRAME - OCTAGON_FROM)
end

-- How far behind the line a point on the minimap is, as a part of a sweep (0 just passed, to 1 about to be passed
-- again); from (y) its height in the frame, from the top.
local function behind_sweep(y)
	local ahead = y - Sweep.position
	if ahead > 0 then
		ahead = ahead - 2 * FRAME
	end
	local part = -ahead / (2 * FRAME)
	if part > 1 then
		part = 1
	end
	return part
end

local function lighter(value)
	value = value + RING_LIGHTER
	if value > 255 then
		value = 255
	end
	return value
end

-- Guerrilla's blips for the people and vehicles within reach (the mod picks them and their colours).
local function place_blips(map_x, map_y, heading, radius, scale, world_scale)
	local count = 0
	if rfg_map_blips ~= nil then
		-- Guerrilla collects them within its zoom radius only (its zoom_radius_squared), not out to the corners.
		count = rfg_map_blips(radius)
		if count > BLIPS then
			count = BLIPS
		end
	end
	local zoomed_out = (radius - RADIUS) / (WIDEST_RADIUS - RADIUS)
	if zoomed_out < 0 then
		zoomed_out = 0
	elseif zoomed_out > 1 then
		zoomed_out = 1
	end
	local human_units = HUMAN_BLIP * (1 - (1 - HUMAN_BLIP_LEAST) * zoomed_out)
	local human_size = human_units / BLIP_PIXELS
	local vehicle_size = VEHICLE_BLIP_METRES * scale / BLIP_PIXELS
	-- The turned map's sine and cosine: where a blip is in the frame (the map group turns by -heading about its
	-- middle), for the sweep.
	local sine, cosine = 0, 1
	if Sweep.on then
		sine, cosine = rfg_sin_cos(heading)
	end
	for i = 1, count do
		local vehicle, x, y, colour, facing = rfg_map_blip(i)
		local handle = Blip_handles[i]
		local look = colour
		if vehicle then
			look = look + 10
		end
		local tint = BLIP_COLOURS[colour] or BLIP_COLOURS[0]
		if Blip_shown[i] ~= look then
			vint_set_property(handle, "image", vehicle and "rfg_ui_map_icon_vehicle" or "rfg_ui_map_icon_blip_human")
			vint_set_property(handle, "tint", tint[1], tint[2], tint[3])
			vint_set_property(handle, "depth", vehicle and -1 or -3)
			if Blip_shown[i] == nil then
				vint_set_property(handle, "visible", true)
			end
			Blip_shown[i] = look
		end
		local anchor_x, anchor_y = (x - map_x) * world_scale, (y - map_y) * world_scale
		vint_set_property(handle, "anchor", anchor_x, anchor_y)
		if vehicle then
			vint_set_property(handle, "scale", vehicle_size, vehicle_size)
			vint_set_property(handle, "rotation", facing)
		else
			vint_set_property(handle, "scale", human_size, human_size)
			vint_set_property(handle, "rotation", heading)
		end

		-- The sweep: the blip fades behind the line; a person's ring grows from where the line passed.
		local ring_alpha = 0
		local ring_part = 0
		if Sweep.on then
			local frame_y = FRAME / 2 - anchor_x * sine + anchor_y * cosine
			local part = behind_sweep(frame_y)
			-- Guerrilla's alpha: 255 x (1 - part) + Minimap_radar_min_alpha, at most 255.
			local alpha = (255 * (1 - part) + Sweep.least_alpha) / 255
			if alpha > 1 then
				alpha = 1
			end
			vint_set_property(handle, "alpha", alpha)
			if not vehicle then
				ring_part = part * RING_GROWTH
				if ring_part < 2 then
					ring_alpha = RING_OPACITY * (2 - ring_part) / 255
					if ring_alpha > 1 then
						ring_alpha = 1
					end
				end
			end
			if i == 1 and Sweep.checked < 3 and Sweep.clock ~= nil and Sweep.clock >= Sweep.check_at then
				-- For finding out what goes wrong (always logged, three times 5 s apart): the height worked out against
				-- where the game drew it (the two should match), and the frame's height on screen.
				Sweep.checked = Sweep.checked + 1
				Sweep.check_at = Sweep.clock + 5
				local ok_blip, bx1, by1, bx2, by2 = pcall(vint_get_bbox, handle)
				local ok_frame, fx1, fy1, fx2, fy2 = pcall(vint_get_bbox, Map_handles.frame)
				if ok_blip and ok_frame and by1 ~= nil and by2 ~= nil and fy1 ~= nil and fy2 ~= nil and fy2 > fy1 then
					log("sweep check: a blip at " .. tostring(frame_y) .. " of the frame's " .. FRAME .. ", drawn at " ..
						tostring(((by1 + by2) / 2 - fy1) * FRAME / (fy2 - fy1)) .. "; the line at " .. tostring(Sweep.position) ..
						"; the frame " .. tostring(fy2 - fy1) .. " pixels high; the blip's alpha " .. tostring(alpha), true)
				end
			end
		end
		local ring = Ring_handles[i]
		if ring ~= nil then
			if ring_alpha > 0 then
				if Ring_shown[i] ~= colour then
					vint_set_property(ring, "tint", lighter(tint[1]), lighter(tint[2]), lighter(tint[3]))
					if Ring_shown[i] == nil then
						vint_set_property(ring, "visible", true)
					end
					Ring_shown[i] = colour
				end
				local ring_size = human_units * RING_ACROSS * (RING_START + ring_part) / RING_PIXELS
				vint_set_property(ring, "anchor", anchor_x, anchor_y)
				vint_set_property(ring, "scale", ring_size, ring_size)
				vint_set_property(ring, "alpha", ring_alpha)
			elseif Ring_shown[i] ~= nil then
				vint_set_property(ring, "visible", false)
				Ring_shown[i] = nil
			end
		end
	end
	for i = count + 1, BLIPS do
		if Blip_shown[i] ~= nil then
			vint_set_property(Blip_handles[i], "visible", false)
			Blip_shown[i] = nil
		end
		if Ring_shown[i] ~= nil then
			vint_set_property(Ring_handles[i], "visible", false)
			Ring_shown[i] = nil
		end
	end
end

-- Guerrilla's icons within reach of the player, upright (the map group turns by -heading).
-- A square root (the game's Lua has no math library): Newton's way, plenty for screen units.
local function square_root(value)
	if value <= 0 then
		return 0
	end
	local root = value
	if root > 1 then
		root = value / 2
	end
	for i = 1, 20 do
		root = (root + value / root) / 2
	end
	return root
end

local function alert_bands()
	local green, yellow = true, true
	if rfg_alert ~= nil then
		local running, value = rfg_alert()
		if running and value ~= nil then
			green = value <= ALERT_GREEN_MOST
			yellow = value <= ALERT_YELLOW_MOST
		end
	end
	return green, yellow
end

local function place_icons(map_x, map_y, heading, radius, world_scale)
	local metres = ZONE_METRES / ZONE_PIXELS		-- a world map pixel
	local edge = FRAME / 2							-- the radius, in frame units
	local rim = STICKY_RIM * edge
	local green, yellow = alert_bands()
	local activity, mission = false, false
	if Has_others then
		activity, mission = rfg_activity_running()
	end
	-- The nearest safehouse.
	local nearest, nearest_distance = nil, nil
	for i, icon in ipairs(Icons) do
		if icon.rule == "safehouse" then
			local dx, dy = icon.x - map_x, icon.y - map_y
			local distance = dx * dx + dy * dy
			if nearest_distance == nil or distance < nearest_distance then
				nearest, nearest_distance = icon, distance
			end
		end
	end
	local used = 0
	for i, icon in ipairs(Icons) do
		local dx, dy = icon.x - map_x, icon.y - map_y
		local metres_squared = (dx * dx + dy * dy) * metres * metres
		local sticky, show = false, true
		if icon.rule == "safehouse" then
			sticky, show = true, icon == nearest and not mission
		elseif icon.rule == "mission" then
			sticky, show = true, metres_squared <= MISSION_START_METRES * MISSION_START_METRES and not mission
		elseif icon.rule ~= "" and (activity or mission) then
			show = false		-- an activity's start, while an activity or mission runs
		elseif icon.bomb then
			sticky, show = true, rfg_map_icon_shown(icon.index) and metres_squared <= STICKY_MOST_METRES * STICKY_MOST_METRES
		elseif icon.rule == "demo" then
			show = green
		elseif icon.rule == "areadefense" or icon.rule == "shotgun" then
			show = yellow
		elseif icon.rule == "housearrest" then
			show = yellow and metres_squared >= HOUSE_ARREST_LEAST_METRES * HOUSE_ARREST_LEAST_METRES
		end
		local x, y = dx * world_scale, dy * world_scale	-- frame units from the middle
		if show then
			local squared = x * x + y * y
			if sticky then
				if squared > rim * rim then
					local distance = square_root(squared)
					x, y = x * rim / distance, y * rim / distance
				end
			elseif squared > edge * edge then
				show = false
			end
		end
		if show and used < ICONS then
			used = used + 1
			local handle = Icon_handles[used]
			if Icon_shown[used] ~= icon.picture then
				vint_set_property(handle, "image", icon.picture)
				if Icon_shown[used] == nil then
					vint_set_property(handle, "visible", true)
				end
				Icon_shown[used] = icon.picture
			end
			vint_set_property(handle, "anchor", x, y)
			vint_set_property(handle, "rotation", heading)
			local tint = "own"
			if Has_others and icon.rule ~= "" and icon.rule ~= "safehouse" and icon.rule ~= "mission" and rfg_map_icon_done(icon.index) then
				tint = "done"
			end
			if Icon_tint[used] ~= tint then
				if tint == "done" then
					vint_set_property(handle, "tint", DONE_COLOUR[1], DONE_COLOUR[2], DONE_COLOUR[3])
				else
					vint_set_property(handle, "tint", 255, 255, 255)
				end
				Icon_tint[used] = tint
			end
		end
	end
	for i = used + 1, ICONS do
		if Icon_shown[i] ~= nil then
			vint_set_property(Icon_handles[i], "visible", false)
			Icon_shown[i] = nil
		end
	end
end

-- A point on the minimap (frame units from the middle): kept on the rim if sticky and further than its rim, hidden if
-- plain and outside the radius. Gives x, y, shown.
local function on_minimap(x, y, sticky)
	local edge = FRAME / 2
	local rim = STICKY_RIM * edge
	local squared = x * x + y * y
	if sticky then
		if squared > rim * rim then
			local distance = square_root(squared)
			return x * rim / distance, y * rim / distance, true
		end
		return x, y, true
	end
	return x, y, squared <= edge * edge
end

local function show_element(handles, shown, i, picture)
	local handle = handles[i]
	if shown[i] ~= picture then
		vint_set_property(handle, "image", picture)
		if shown[i] == nil then
			vint_set_property(handle, "visible", true)
		end
		shown[i] = picture
	end
	return handle
end

local function hide_from(handles, shown, first, most)
	for i = first, most do
		if shown[i] ~= nil then
			vint_set_property(handles[i], "visible", false)
			shown[i] = nil
		end
	end
end

-- The important EDF buildings (the world's), the placed explosives (the gear's) and the people's markers.
local function place_others(map_x, map_y, heading, radius, world_scale)
	if not Has_others then
		return
	end
	local activity, mission = rfg_activity_running()
	-- Buildings: pinned while no activity or mission runs; drawn under the plain icons then, over them otherwise.
	local pinned = not activity and not mission
	local used = 0
	for i = 1, rfg_map_buildings() do
		local bx, by, importance = rfg_map_building(i)
		local x, y, shown = on_minimap((bx - map_x) * world_scale, (by - map_y) * world_scale, pinned)
		if shown and used < BUILDINGS then
			used = used + 1
			local picture = "rfg_ui_map_icon_pmedium"
			if importance >= 3 then
				picture = "rfg_ui_map_icon_phigh"
			end
			local handle = show_element(Building_handles, Building_shown, used, picture)
			vint_set_property(handle, "anchor", x, y)
			vint_set_property(handle, "rotation", heading)
			if pinned then
				vint_set_property(handle, "depth", -6)
			else
				vint_set_property(handle, "depth", -4)
			end
		end
	end
	hide_from(Building_handles, Building_shown, used + 1, BUILDINGS)
	-- Explosives inside the radius, and their rings every 0.5 s.
	local phase = 0
	if Sweep.clock ~= nil then
		phase = (Sweep.clock % EXPLOSIVE_RING_SECONDS) / EXPLOSIVE_RING_SECONDS
	end
	used = 0
	for i = 1, rfg_map_explosives(radius) do
		local ex, ey, kind, stuck = rfg_map_explosive(i)
		local x, y, shown = on_minimap((ex - map_x) * world_scale, (ey - map_y) * world_scale, false)
		if shown and used < EXPLOSIVES then
			used = used + 1
			local colour = EXPLOSIVE_COLOURS[kind] or EXPLOSIVE_COLOURS[0]
			local handle = show_element(Explosive_handles, Explosive_shown, used, "rfg_ui_map_icon_blip")
			vint_set_property(handle, "anchor", x, y)
			vint_set_property(handle, "scale", EXPLOSIVE_UNITS / BLIP_PIXELS, EXPLOSIVE_UNITS / BLIP_PIXELS)
			vint_set_property(handle, "tint", colour[1], colour[2], colour[3])
			local loose = kind == 0 and not stuck
			if loose then
				vint_set_property(handle, "alpha", LOOSE_CHARGE_ALPHA)
			else
				vint_set_property(handle, "alpha", 1)
			end
			local ring = Explosive_rings[used]
			vint_set_property(ring, "visible", not loose)
			if not loose then
				local size = (EXPLOSIVE_UNITS + (EXPLOSIVE_RING_UNITS - EXPLOSIVE_UNITS) * phase) / BLIP_PIXELS
				vint_set_property(ring, "anchor", x, y)
				vint_set_property(ring, "scale", size, size)
				vint_set_property(ring, "tint", colour[1], colour[2], colour[3])
				vint_set_property(ring, "alpha", 1 - phase)
			end
		end
	end
	for i = used + 1, EXPLOSIVES do
		if Explosive_shown[i] ~= nil then
			vint_set_property(Explosive_rings[i], "visible", false)
		end
	end
	hide_from(Explosive_handles, Explosive_shown, used + 1, EXPLOSIVES)
	-- The people's markers: the closest-sticky ones only the 3 nearest.
	local count = rfg_map_markers()
	local closest = {}
	for i = 1, count do
		local mx, my, picture, placement = rfg_map_marker(i)
		if placement == 2 then
			local dx, dy = mx - map_x, my - map_y
			closest[#closest + 1] = { i = i, distance = dx * dx + dy * dy }
		end
	end
	local allowed = {}
	for n = 1, CLOSEST_STICKY_MOST do
		local best = nil
		for j, c in ipairs(closest) do
			if not allowed[c.i] and (best == nil or c.distance < best.distance) then
				best = c
			end
		end
		if best ~= nil then
			allowed[best.i] = true
		end
	end
	used = 0
	for i = 1, count do
		local mx, my, picture, placement, r, g, b, age = rfg_map_marker(i)
		if placement ~= 2 or allowed[i] then
			local x, y, shown = on_minimap((mx - map_x) * world_scale, (my - map_y) * world_scale, placement == 1 or placement == 2)
			if shown and used < MARKERS then
				used = used + 1
				local handle = show_element(Marker_handles, Marker_shown, used, picture)
				vint_set_property(handle, "anchor", x, y)
				vint_set_property(handle, "rotation", heading)
				if r ~= nil and r >= 0 then
					vint_set_property(handle, "tint", r, g, b)
				else
					vint_set_property(handle, "tint", 255, 255, 255)
				end
				if placement == 3 and age ~= nil and age >= 0 then
					local part = age / CONVERT_POP_MS
					if part > 1 then
						part = 1
					end
					vint_set_property(handle, "scale", ICON_SCALE * (1 + 0.75 * part), ICON_SCALE * (1 + 0.75 * part))
					vint_set_property(handle, "alpha", 1 - part)
				else
					vint_set_property(handle, "scale", ICON_SCALE, ICON_SCALE)
					vint_set_property(handle, "alpha", 1)
				end
			end
		end
	end
	hide_from(Marker_handles, Marker_shown, used + 1, MARKERS)
end

-- The radio tags not found yet within the radius: a green ring each, over 750 ms of every 3 s (by the sweep's clock,
-- which stands still while the game is paused).
local function place_tags(map_x, map_y, radius, world_scale)
	local count = 0
	if Has_tags then
		count = rfg_map_radio_tags(radius)
		if count > TAGS then
			count = TAGS
		end
	end
	local phase = 0
	if Sweep.clock ~= nil then
		phase = Sweep.clock % TAG_RING_EVERY
	end
	local part = phase / TAG_RING_SECONDS
	for i = 1, TAGS do
		local handle = Tag_handles[i]
		if handle ~= nil and i <= count and part < 1 then
			local x, y = rfg_map_radio_tag(i)
			if Tag_shown[i] == nil then
				vint_set_property(handle, "visible", true)
				Tag_shown[i] = true
			end
			local size = TAG_RING_UNITS * part / BLIP_PIXELS
			vint_set_property(handle, "anchor", (x - map_x) * world_scale, (y - map_y) * world_scale)
			vint_set_property(handle, "scale", size, size)
			vint_set_property(handle, "alpha", 1 - part)
		elseif handle ~= nil and Tag_shown[i] ~= nil then
			vint_set_property(handle, "visible", false)
			Tag_shown[i] = nil
		end
	end
end

-- The north marker on the rim, where north is on the turned map.
local function place_north(heading)
	local north = Map_handles.north
	if north == nil then
		return
	end
	local sine, cosine = rfg_sin_cos(heading)
	vint_set_property(north, "anchor", Map_handles.centre_x - NORTH_RIM * sine, Map_handles.centre_y - NORTH_RIM * cosine)
end

-- The map around the player: turned against the camera, scaled to the minimap's reach.
local function place_map(map_x, map_y, heading, speed, shown)
	-- Guerrilla's zoom: 40 to 80 metres by its keys (the mod's), then half the vehicle's speed more.
	local radius = RADIUS
	if Has_zoom then
		radius = rfg_minimap_radius()
	end
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
	place_tags(map_x, map_y, radius, world_scale)
	place_north(heading)
	-- What other mods give, on their own: should it fail, the map goes on without it.
	if Has_others then
		local ok, problem = pcall(place_others, map_x, map_y, heading, radius, world_scale)
		if not ok then
			Has_others = false
			log("the minimap's buildings, explosives and markers stopped: " .. tostring(problem), true)
		end
	end
	-- The sweep's line, only while the minimap shows (Guerrilla's moved as it drew the minimap); should it fail,
	-- the blips go on without it.
	if Sweep.on and shown then
		local ok, problem = pcall(sweep_step)
		if not ok then
			Sweep.on = false
			vint_set_property(Sweep.handle, "visible", false)
			for i = 1, BLIPS do
				vint_set_property(Blip_handles[i], "alpha", 1)
				vint_set_property(Ring_handles[i], "visible", false)
				Ring_shown[i] = nil
			end
			log("the minimap's radar sweep stopped: " .. tostring(problem), true)
		end
	elseif Sweep.on then
		Sweep.clock = nil		-- hidden: the line waits where it is
	end
	-- The blips on their own: should they fail, the map goes on without them.
	if Blips_on then
		local ok, problem = pcall(place_blips, map_x, map_y, heading, radius, scale, world_scale)
		if not ok then
			Blips_on = false
			log("the minimap's blips stopped: " .. tostring(problem), true)
		end
	end
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

-- Guerrilla's alert_level: the light's colour and pulse for the value its HUD is given (0 to 25 green,
-- to 50 yellow, to 75 orange, above red); hidden while nothing runs the alert level.
local function show_alert()
	local known, value = false, 0
	if rfg_alert ~= nil then
		known, value = rfg_alert()
	end
	if known ~= Alert_shown then
		vint_set_property(find("alert_level_group"), "visible", known)
		Alert_shown = known
		Alert_band = 0
	end
	if not known then
		return
	end
	local band = 4
	if value <= 25 then
		band = 1
	elseif value <= 50 then
		band = 2
	elseif value <= 75 then
		band = 3
	end
	if band == Alert_band then
		return
	end
	Alert_band = band
	local look = ALERT[band]
	local light, shine, anim = find("mini_map_alert_level"), find("mini_map_alert_shine"), find("mini_map_alert_anim")
	vint_set_property(light, "tint", look[1] * 255, look[2] * 255, look[3] * 255)
	vint_set_property(shine, "tint", look[1] * 255, look[2] * 255, look[3] * 255)
	if band == 1 then
		vint_set_child_tween_state(anim, TWEEN_STATE_DISABLED)
		vint_set_property(shine, "alpha", 0)
		vint_set_property(shine, "scale", 0.8, 0.8)
	else
		local alpha_tween, scale_tween = find("mini_map_alert_alpha_tween"), find("mini_map_alert_scale_tween")
		vint_set_property(alpha_tween, "duration", look[4])
		vint_set_property(scale_tween, "duration", look[4])
		vint_set_property(alpha_tween, "end_value", look[5])
		vint_set_property(scale_tween, "end_value", look[6], look[6])
		vint_set_property(anim, "start_time", vint_get_time_index())
		vint_set_child_tween_state(anim, TWEEN_STATE_RUNNING)
	end
	log("alert light " .. band .. " (value " .. tostring(value) .. ")")
end

-- One frame: follows the player and the camera.
local function frame()
	local active, map_x, map_y, heading, speed, column, row, district, map_open = rfg_map_view()
	Map_open = map_open
	local shown = active and not Hud_hidden and not map_open
	show(shown)
	if active then
		place_map(map_x, map_y, heading, speed, shown)
		if district ~= Shown_district then
			show_district(district)
			Shown_district = district
		end
		show_alert()
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
	Has_zoom = _GetAnyGlobalSilent("rfg_minimap_radius") ~= nil
	-- Guerrilla's Minimap_radar_min_alpha (0 to 255): how faint a blip gets before the sweep comes round again. Guerrilla
	-- left it 0 (nothing sets it), so a blip fades out entirely; RadarMinAlpha= under [GuerrillaHud] keeps them showing.
	if rfa_get_setting ~= nil then
		Sweep.least_alpha = rfa_get_setting("GuerrillaHud", "RadarMinAlpha", 0)
		if Sweep.least_alpha < 0 then
			Sweep.least_alpha = 0
		elseif Sweep.least_alpha > 255 then
			Sweep.least_alpha = 255
		end
	end
	Logged = 0
	Frames_logged = 0
	log("init: this document " .. tostring(vint_document_find("rfg_ui_hud")) .. ", rfg_map_view " .. tostring(rfg_map_view))
	local hidden, problem = pcall(hide_unused)
	if not hidden then
		log("hiding the unused parts: " .. tostring(problem), true)
	end
	Alert_shown = false
	Alert_band = 0

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
