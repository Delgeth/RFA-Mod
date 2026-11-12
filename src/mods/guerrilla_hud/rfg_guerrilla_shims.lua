-- What Red Faction: Guerrilla's own interface scripts use that Armageddon's interface Lua doesn't
-- have. Part of the guerrilla_hud mod: GuerrillaBuilder.exe puts this in front of each of Guerrilla's
-- scripts it brings over from your copy of Guerrilla (which is only read; nothing of it is shipped),
-- and our own script for that document after it.
--
-- Each document's script keeps the globals it makes in a table of its own (Armageddon's
-- system_lib.lua), but a global is read from whichever loaded document has it, so these are the
-- same in every document that has them. Reading a global no script defined is an error.

-- Guerrilla's name for the tween state Armageddon calls TWEEN_STATE_WAITING (both are 0).
TWEEN_STATE_IDLE = TWEEN_STATE_WAITING

-- Lua's string library, which the game's Lua doesn't have: find, for a plain part, from the mod.
string = { find = rfg_string_find }

-- Guerrilla's scripts give and read tints as 0-1, Armageddon's interface as 0-255 (in its documents
-- both are 0-1). GuerrillaBuilder.exe makes their vint_set_property and vint_get_property calls these.
-- Guerrilla's text sizes. Armageddon has fonts of the same names as Guerrilla's, drawn bigger (its font_body half as
-- big again as Guerrilla's; its font_header a little taller), so GuerrillaBuilder.exe scales the texts of some of
-- Guerrilla's documents by their font (Guerrilla's height over Armageddon's) and tells this script which, by each
-- document's text elements' names (rfg_text_scales, at the top of the document's script). A text_scale Guerrilla's
-- script sets or reads is then scaled the same way, so its script works in Guerrilla's sizes as it did.
-- Kept by every document's copy of these shims: { document = name, scales = { { element, scale }, ... }, found = its
-- handle the names were looked up in }, and element handle -> scale.
if Rfg_text_scale_documents == nil then
	Rfg_text_scale_documents = {}
end
if Rfg_text_scale_of == nil then
	Rfg_text_scale_of = {}
end

function rfg_text_scales(document, scales)
	for i, known in ipairs(Rfg_text_scale_documents) do
		if known.document == document then
			known.scales = scales
			known.found = nil
			return
		end
	end
	Rfg_text_scale_documents[#Rfg_text_scale_documents + 1] = { document = document, scales = scales }
end

local function rfg_text_scale(handle)
	-- Each document's names looked up in it, again whenever one of the documents is loaded again (its elements'
	-- handles are new, and old ones may be reused).
	local changed = false
	for i, known in ipairs(Rfg_text_scale_documents) do
		local found = vint_document_find(known.document)
		if found == nil then
			found = 0
		end
		if known.found ~= found then
			known.found = found
			changed = true
		end
	end
	if changed then
		Rfg_text_scale_of = {}
		for i, known in ipairs(Rfg_text_scale_documents) do
			if known.found ~= 0 then
				for j, pair in ipairs(known.scales) do
					local element = vint_object_find(pair[1], 0, known.found)
					if element ~= nil and element ~= 0 then
						Rfg_text_scale_of[element] = pair[2]
					end
				end
			end
		end
	end
	local scale = Rfg_text_scale_of[handle]
	if scale == nil then
		scale = 1
		Rfg_text_scale_of[handle] = scale
	end
	return scale
end

function rfg_vint_set_property(handle, property, ...)
	-- Guerrilla's game named many texts by their string tag's hash (text_tag_crc), which Armageddon's
	-- string tables don't have: our bridges hand the words instead, as a string.
	if property == "text_tag_crc" then
		local value = ...
		if type(value) == "string" then
			return vint_set_property(handle, "text_tag", value)
		end
	end
	if property == "tint" then
		local r, g, b = ...
		return vint_set_property(handle, property, r * 255, g * 255, b * 255)
	end
	if property == "text_scale" then
		local x, y = ...
		local scale = rfg_text_scale(handle)
		if x ~= nil and y ~= nil then
			return vint_set_property(handle, property, x * scale, y * scale)
		end
	end
	return vint_set_property(handle, property, ...)
end

function rfg_vint_get_property(handle, property, ...)
	if property == "tint" then
		local r, g, b = vint_get_property(handle, property, ...)
		return r / 255, g / 255, b / 255
	end
	if property == "text_scale" then
		local x, y = vint_get_property(handle, property, ...)
		local scale = rfg_text_scale(handle)
		if x ~= nil and y ~= nil then
			return x / scale, y / scale
		end
		return x, y
	end
	return vint_get_property(handle, property, ...)
end

-- What Guerrilla's game answered, as Armageddon's has it.
function rfg_is_multiplayer()		-- the Guerrilla world is single player
	return false
end

function rfg_play_audio(name)		-- Guerrilla's interface sounds aren't brought over yet
end

function rfg_get_max_charges()		-- nor its remote charges
	return 0
end

-- Its language (the handbook lays its texts out right to left for Arabic): Armageddon's get_language() gives a
-- name; the user's strings here are English.
LANGUAGE_MODE_AR = -1
function rfg_get_language()
	return 0
end

-- Guerrilla's controller buttons for its four weapon slots (its globals), and their pictures:
-- none, since its weapon select shows them only for a controller and Armageddon's PC keys pick
-- the weapons here.
Btn_slot_0 = 24
Btn_slot_1 = 25
Btn_slot_2 = 26
Btn_slot_3 = 27

function rfg_get_button_image_name(button)
	return "null"
end

-- Guerrilla's game filled the world map's data items (FS_HighlightItem, FS_Hints, the group
-- FS_POI_Group, ...), which Armageddon has none of: there our bridge calls the script's callbacks
-- itself, handing a table of the item's values in place of the item. GuerrillaBuilder.exe makes the
-- world map's vint_dataitem_get calls this, and its subscriptions the two below.
function rfg_vint_dataitem_get(item, ...)
	if type(item) == "table" then
		return unpack(item, 1, item.n or #item)	-- n: how many, for a table with empty (nil) values
	end
	return vint_dataitem_get(item, ...)
end

-- A subscription to an item or group Armageddon doesn't have isn't an error then.
function rfg_vint_dataitem_add_subscription(...)
	local values = { ... }
	return pcall(function() return vint_dataitem_add_subscription(unpack(values)) end)
end

function rfg_vint_datagroup_add_subscription(...)
	local values = { ... }
	return pcall(function() return vint_datagroup_add_subscription(unpack(values)) end)
end

-- Guerrilla's script helpers: a number from a string (0 for none), and a whole number between two
-- (the game's Lua has no math library: a plain sequence, enough for a picture's tilt).
function rfg_tonumber(value)
	return tonumber(value) or 0
end

Rfg_rand_state = 12345
function rfg_rand(low, high)
	Rfg_rand_state = (Rfg_rand_state * 16807) % 2147483647	-- stays exact in Lua's doubles
	return low + Rfg_rand_state % (high - low + 1)
end
