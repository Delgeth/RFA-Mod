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
function rfg_vint_set_property(handle, property, ...)
	if property == "tint" then
		local r, g, b = ...
		return vint_set_property(handle, property, r * 255, g * 255, b * 255)
	end
	return vint_set_property(handle, property, ...)
end

function rfg_vint_get_property(handle, property, ...)
	if property == "tint" then
		local r, g, b = vint_get_property(handle, property, ...)
		return r / 255, g / 255, b / 255
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
