-- Red Faction: Guerrilla's reticles and ammo in Armageddon: Guerrilla's own document and script, fed
-- from Armageddon's weapon data.
--
-- Part of the guerrilla_hud mod. GuerrillaHud.exe makes the document rfg_ui_hud_reticles from your
-- copy of Guerrilla (its pictures renamed rfg_...), and its script from three parts: our
-- rfg_guerrilla_shims.lua, then Guerrilla's own script for it (its init and cleanup renamed
-- rfg_ui_hud_reticles_guerrilla_init and _cleanup, its pictures' names renamed like the
-- document's), then this file. Run it again after changing this file.
--
-- Guerrilla's script draws everything from one data item Guerrilla's game filled every frame,
-- WeaponFire. Armageddon has no such item; it fills its own (reticule_hud, primary_weapon_hud) for
-- its own HUD. So this script makes WeaponFire (through the mod's rfg_dataitem_make) and fills it
-- from those (rfg_dataitem_set), and Guerrilla's script does the rest as it did in Guerrilla:
--   - the reticle: the weapon's own picture (Guerrilla's weapons each name one; a weapon without one,
--     as every Armageddon weapon, gets Guerrilla's assault reticle, as its script says), red over an
--     enemy, green over a friend, Guerrilla's hit markers;
--   - the ammo: rounds in the magazine and the rest in the box beside the reticle (the infinity sign
--     where Armageddon's is infinite), blinking red when a reload is due;
--   - the overheat meter: for a weapon without a magazine, filling as the energy Armageddon's
--     weapon meter shows runs down.
-- Armageddon's own reticle, weapon meter and ammo ring are faded out while this is loaded, and back
-- in when it goes; its Nano Forge meter, its magnet gun's marks and its reload and wait prompts stay.

local WEAPON_FIRE = "WeaponFire"
-- WeaponFire's fields, as Guerrilla's script reads them (b: boolean, s: string, f: float, i: integer):
-- bogus, reload_warning, can_fire, is_reloading, weapon_name, weapon_icon_name, weapon_reticule_name,
-- accuracy_percent, ammo_rounds, ammo_reserve, ammo_magazine_size, targeting,
-- local_player_hit_someone_this_frame, is_overheated, overheat_percent, is_turret,
-- secondary_can_fire, lock_on, show_ammo_counter, multi_rockets, charges, status
local WEAPON_FIRE_FIELDS = "bbbbsssfiiiibbfbbibiii"

-- Guerrilla's reticle for a weapon that has none of its own.
local DEFAULT_RETICLE = "rfg_ui_hud_reti_assault"

-- Armageddon's values (ui_hud.lua).
local RETICULE_STATE_HIDE = -1
local AMMO_AREA_STATUS_HIDDEN = 0
local AMMO_AREA_STATUS_AMMO_COUNT = 2
local AMMO_COUNT_INFINITE = -1
local RELOAD_MESSAGE_RELOAD = 1

-- Armageddon's HUD (ui_hud) parts this replaces. Faded once: its script only shows and hides them.
-- Faded every frame: its script sets their alpha back. Hidden every frame: its hit animation fades
-- them in.
local ARMAGEDDON_FADED = { "normal", "reticule_friendly", "meter_weapon", "ammo_area_vehicle", "ammo_area_bg",
	"ammo_bg_ring", "ammo_bg_ring_2", "ammo_bg_tech" }
local ARMAGEDDON_FADED_EACH_FRAME = { "ammo_area_on_foot" }
local ARMAGEDDON_HIDDEN_EACH_FRAME = { "reticule_hit", "reticule_hit_2" }

local Reticle = {}		-- reticule_hud: weapon_name, state, scale, hit, magnet_target, fine_aimed
local Weapon = {}		-- primary_weapon_hud: weapon_name, icon, target_name, bar_visible, bar_pct,
					-- bar_is_red, ammo_status, total_ammo, clip_size, clip_ammo, reload_message, flash_type
local Hud_hidden = false
local Fade_thread = -1

local function armageddon_part(name)
	return vint_object_find(name, 0, vint_document_find("ui_hud"))
end

local function fade_armageddon(alpha)
	for i, name in ipairs(ARMAGEDDON_FADED) do
		local part = armageddon_part(name)
		if part ~= 0 then
			vint_set_property(part, "alpha", alpha)
		end
	end
end

-- Shows or hides Guerrilla's reticle and ammo as Armageddon would show its own.
local function show_parts()
	local reticle_shown = not Hud_hidden and Reticle[1] ~= nil and Reticle[1] ~= "" and Reticle[2] ~= RETICULE_STATE_HIDE
	local ammo_shown = not Hud_hidden and Weapon[7] ~= nil and Weapon[7] ~= AMMO_AREA_STATUS_HIDDEN
	vint_set_property(vint_object_find("reticle_main"), "visible", reticle_shown)
	vint_set_property(vint_object_find("ammo_main"), "visible", ammo_shown)
	vint_set_property(vint_object_find("charge_main"), "visible", ammo_shown)
end

-- WeaponFire from Armageddon's two items.
local function send()
	local weapon_name = Reticle[1] or Weapon[1] or ""
	local state = Reticle[2] or RETICULE_STATE_HIDE
	local scale = Reticle[3] or 1
	local hit = Reticle[4] == true
	local fine_aimed = Reticle[6] == true
	local bar_visible = Weapon[4] == true
	local bar_pct = Weapon[5] or 0
	local bar_is_red = Weapon[6] == true
	local ammo_status = Weapon[7] or AMMO_AREA_STATUS_HIDDEN
	local total = Weapon[8] or 0
	local clip_size = Weapon[9] or 0
	local clip = Weapon[10] or 0
	local reload_message = Weapon[11] or 0
	local target_name = Weapon[3] or "player"

	-- Guerrilla counts the magazine and the rest apart; Armageddon's total includes the magazine.
	-- Guerrilla's -1 is "no limit": in the magazine's place (no magazine) or the rest's.
	local rounds, reserve, magazine
	if total == AMMO_COUNT_INFINITE and clip_size > 0 then
		rounds, reserve, magazine = clip, -1, clip_size
	elseif total == AMMO_COUNT_INFINITE then
		rounds, reserve, magazine = -1, 0, 0
	elseif clip_size > 0 then
		rounds, reserve, magazine = clip, total - clip, clip_size
	else
		rounds, reserve, magazine = total, 0, 0
	end
	local can_fire = rounds ~= 0
	local targeting = state
	if targeting < 0 then
		targeting = 0
	end
	-- Guerrilla's accuracy is a percentage, below zero while zoomed.
	local accuracy = scale * 100
	if fine_aimed then
		accuracy = -accuracy
	end
	-- Armageddon's weapon meter is what is left: of the magazine, or for a weapon without one, of
	-- its energy. Guerrilla's overheat meter is how near the weapon is to having to stop, shown
	-- only once it starts to fill; so a weapon without a magazine gets it, filling as its energy
	-- runs down, red where Armageddon's turns red. (Magazines are counted in numbers, as in
	-- Guerrilla; its magnet gun has no meter.)
	local overheat = 0
	local overheated = false
	if bar_visible and clip_size <= 0 and weapon_name ~= "magnet_gun" then
		overheat = (1 - bar_pct) * 100
		overheated = bar_is_red
	end

	rfg_dataitem_set(WEAPON_FIRE, true, reload_message == RELOAD_MESSAGE_RELOAD, can_fire, false, weapon_name,
		Weapon[2] or "", DEFAULT_RETICLE, accuracy, rounds, reserve, magazine, targeting, hit,
		overheated, overheat, target_name ~= "player", true, 0,
		ammo_status == AMMO_AREA_STATUS_AMMO_COUNT, -1, 0, 0)
	show_parts()
end

function rfg_ui_hud_reticles_reticle_update(di_h, event_type)
	Reticle = { vint_dataitem_get(di_h) }
	send()
end

function rfg_ui_hud_reticles_weapon_update(di_h, event_type)
	Weapon = { vint_dataitem_get(di_h) }
	send()
end

-- Cutscenes and the like hide the HUD (all but the subtitles): hide this with it.
function rfg_ui_hud_reticles_hide(di_h, event_type)
	Hud_hidden = vint_dataitem_get(di_h)
	show_parts()
end

-- Every frame, the parts of Armageddon's HUD its own script would bring back.
function rfg_ui_hud_reticles_keep_faded()
	while true do
		for i, name in ipairs(ARMAGEDDON_FADED_EACH_FRAME) do
			local part = armageddon_part(name)
			if part ~= 0 then
				vint_set_property(part, "alpha", 0)
			end
		end
		for i, name in ipairs(ARMAGEDDON_HIDDEN_EACH_FRAME) do
			local part = armageddon_part(name)
			if part ~= 0 then
				vint_set_property(part, "visible", false)
			end
		end
		thread_yield()
	end
end

function rfg_ui_hud_reticles_init()
	rfg_dataitem_make(WEAPON_FIRE, WEAPON_FIRE_FIELDS)	-- before Guerrilla's script subscribes to it
	rfg_ui_hud_reticles_guerrilla_init()
	-- Guerrilla's safehouse weapon panel (a placeholder "SHOTGUN 999/999" until its game said
	-- otherwise, through SafehouseInfo, which Armageddon hasn't got).
	vint_set_property(vint_object_find("weapon_data_main"), "visible", false)
	vint_set_property(vint_object_find("weapon_swap_main"), "visible", false)
	fade_armageddon(0)
	vint_dataitem_add_subscription("reticule_hud", "update", "rfg_ui_hud_reticles_reticle_update")
	vint_dataitem_add_subscription("primary_weapon_hud", "update", "rfg_ui_hud_reticles_weapon_update")
	vint_dataitem_add_subscription("hud_hide", "update", "rfg_ui_hud_reticles_hide")
	Fade_thread = thread_new("rfg_ui_hud_reticles_keep_faded")
end

function rfg_ui_hud_reticles_cleanup()
	if Fade_thread ~= -1 then
		thread_kill(Fade_thread)
		Fade_thread = -1
	end
	fade_armageddon(1)
	for i, name in ipairs(ARMAGEDDON_FADED_EACH_FRAME) do
		local part = armageddon_part(name)
		if part ~= 0 then
			vint_set_property(part, "alpha", 1)
		end
	end
	for i, name in ipairs(ARMAGEDDON_HIDDEN_EACH_FRAME) do
		local part = armageddon_part(name)
		if part ~= 0 then
			vint_set_property(part, "visible", true)
		end
	end
	rfg_ui_hud_reticles_guerrilla_cleanup()
end
