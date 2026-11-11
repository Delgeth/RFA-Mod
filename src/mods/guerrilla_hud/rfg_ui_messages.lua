-- Red Faction: Guerrilla's prompts under the reticle, in Armageddon's Guerrilla world: so far its use prompt,
-- at the safehouses' stations (the upgrade shop: Guerrilla's upgrade icon) and at its ammo boxes (its hand
-- icon), each with the use key's picture.
--
-- Part of the guerrilla_hud mod. The document this drives, rfg_ui_messages, is Guerrilla's own, and so is its
-- script, both from your copy of Guerrilla: GuerrillaBuilder.exe packs our shims, Guerrilla's script (its init
-- and cleanup renamed rfg_ui_messages_guerrilla_init and _cleanup) and this script, in that order; run it again
-- after changing this file. Guerrilla's game told the script what to show through data items (UseData,
-- SwapData, AmmoData), which Armageddon hasn't; this script calls Guerrilla's callbacks itself with the values
-- in a table (the shims' rfg_vint_dataitem_get reads either): use_info for the use prompt, as Guerrilla's
-- game filled UseData (visible, use_type, use_button). Its weapon swap and pickup messages come later.

local USE_DEFAULT = 1                   -- Guerrilla's use_type for a plain use, its ammo boxes' (Use_image_names: ui_hud_hand_icon)
local USE_UPGRADES = 10                 -- Guerrilla's use_type for its upgrades (Use_image_names: ui_hud_icon_upgrade)
local USE_KEY = "rfg_key_24"            -- Guerrilla's PC key picture for E, the use key's default

-- The document's other parts, which Guerrilla's game drove from data Armageddon hasn't (its saving icon,
-- its HUD messages, salvage picked up, weapon swaps, ammo picked up): hidden until they're ported, else
-- their placeholder texts and pictures show ("NEW MISSION AVAILABLE", "Pressing text").
local HIDDEN = { "small_saving", "usage_text_major_group1", "usage_text_minor_group1", "usage_text_perm_group1",
	"saving_effects", "salvage_add", "message_swap_main", "message_ammo" }

local Shown = 0			-- the use_type shown now; 0 = no prompt
local Thread = -1

local function log(text)
	if rfa_log ~= nil then
		rfa_log("[rfg_ui_messages] " .. text)
	end
end

-- The use_type of the prompt to show now; 0 = none.
local function wanted()
	if rfg_station_prompt() then
		return USE_UPGRADES
	end
	if rfg_ammo_box_prompt ~= nil and rfg_ammo_box_prompt() then
		return USE_DEFAULT
	end
	return 0
end

local function follow()
	while true do
		local use_type = wanted()
		if use_type ~= Shown then
			-- Hiding keeps the picture it had, as it fades out.
			local picture = use_type
			if use_type == 0 then
				picture = Shown
			end
			local ok, problem = pcall(use_info, { use_type ~= 0, picture, USE_KEY }, "update")
			if not ok then
				log("the use prompt stopped: " .. tostring(problem))
				return
			end
			Shown = use_type
		end
		thread_yield()
	end
end

function rfg_ui_messages_init()
	local ok, problem = pcall(rfg_ui_messages_guerrilla_init)
	if not ok then
		log("Guerrilla's init: " .. tostring(problem))
	end
	for i, name in ipairs(HIDDEN) do
		local handle = vint_object_find(name)
		if handle ~= 0 then
			vint_set_property(handle, "visible", false)
		end
	end
	if rfg_station_prompt == nil then
		return		-- an older mod: no stations
	end
	pcall(use_info, { false, USE_UPGRADES, USE_KEY }, "update")
	Thread = thread_new("rfg_ui_messages_follow")
end

function rfg_ui_messages_follow()
	follow()
end

function rfg_ui_messages_cleanup()
	if Thread ~= -1 then
		thread_kill(Thread)
		Thread = -1
	end
	pcall(rfg_ui_messages_guerrilla_cleanup)
end
