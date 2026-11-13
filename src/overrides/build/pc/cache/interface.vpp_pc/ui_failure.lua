-- Override of ui_failure.lua from <game>\build\pc\cache\interface.vpp_pc (the game-over screen,
-- GAME OVER / YOU DIED). rfa_loader.dll gives the game this file instead of its own copy.
--
-- In the Guerrilla world it offers Guerrilla's death menu: CONTINUE (Guerrilla's respawn: Mason wakes at
-- a safehouse with the world as he left it; guerrilla_save's rfg_respawn_available and rfg_respawn) in
-- place of LOAD CHECKPOINT, then LOAD GAME and QUIT GAME. Everywhere else it is the game's own. Every
-- line we added or changed is marked [RFA]; everything else is the game's original script. Only the
-- [RFA] lines are this project's (GNU GPL version 3 or later, see LICENSE); the rest belongs to the
-- game's owners. To compare with the original, extract it with the editor.

local CHECKPOINT_ID		= 1
local LOAD_GAME_ID		= 2
local QUIT_GAME_ID		= 3
local RESPAWN_ID		= 4 -- [RFA] CONTINUE in the Guerrilla world (guerrilla_save)

local OPTIONS_INFOS = {
	CHECKPOINT_ADDON = { 
		{label = "MENU_LOAD_CHECKPOINT",	id = CHECKPOINT_ID},
	},
	
	LOAD_GAME_ADDON = {
		{label = "MENU_LOAD_GAME",			id = LOAD_GAME_ID},
	},
	
	RESPAWN_ADDON = { -- [RFA] Guerrilla's CONTINUE: wake at a safehouse
		{label = "MENU_CONTINUE_UC",		id = RESPAWN_ID}, -- [RFA]
	}, -- [RFA]

	QUIT_GAME_ADDON = {
		{label = "MENU_QUIT_GAME",			id = QUIT_GAME_ID},
	},
}

local Tween_done = false
local Options = {}
local Option_ids = {}

local List
local End_animation

local Input_tracker
local Mouse_input_tracker

function ui_failure_init()

	local text1_string = {
		[1] = "23908475290348   324578345234785   2374589239485729 459287034895207934851";
		[2] = "259384572384           2349857398547tam87694            62837465";
		[3] = "457368954746           845gnuechtel4576498";
	}

	local texticle1 = Vdo_scrolling_text:new("texticle1")
	texticle1:run_text(text1_string, 0.001, true, 4)

	Input_tracker = Vdo_input_tracker:new()
	Input_tracker:add_input("menu_select", "ui_failure_button_a", 60)
	Input_tracker:add_input("nav_up", "ui_failure_nav_up", 60)
	Input_tracker:add_input("nav_down", "ui_failure_nav_down", 60)
	Input_tracker:subscribe(true)
	
	if is_pc() then
		Mouse_input_tracker = Vdo_input_tracker:new()
	end

	-- Clear the title and hints
	local hint_bar = Vdo_hint_bar:new("hint_bar", 0, vint_document_find("ui_background_ingame"))
	hint_bar:set_header("")
	hint_bar:set_hints()
	hint_bar:set_ring_visibility(false)
	
	-- Set up the animation
	End_animation = Vdo_anim_object:new("animate_out")
	End_animation:stop()

	local end_tween = Vdo_base_object:new("aspect_placer_alpha_twn_1")
	end_tween:set_property("end_event","ui_failure_finish")

	-- Retrieve the text we need
	vint_dataresponder_request("fail_header_responder", "ui_failure_populate_header")
	
	-- Set up the list
	Options = {}
	Option_ids = {}
	local num_options = 0
	
	-- Helper function that loops through a list of infos and tacks them onto the list
	local function tack_on_infos(infos)
		for i, info in ipairs(infos) do
			num_options = num_options + 1
			Options[num_options] = info.label
			Option_ids[num_options] = info.id
		end
	end
	
	-- [RFA] In the Guerrilla world rfg_respawn_available() says whether CONTINUE can wake Mason at a
	-- [RFA] safehouse (true) or not (false); elsewhere, or without guerrilla_save, it gives nil
	local rfg_respawn_ok = nil -- [RFA]
	if rfg_respawn_available ~= nil then -- [RFA]
		rfg_respawn_ok = rfg_respawn_available() -- [RFA]
	end -- [RFA]
	if rfg_respawn_ok == true then -- [RFA]
		tack_on_infos(OPTIONS_INFOS.RESPAWN_ADDON) -- [RFA]
	elseif have_state_data_in_memory() then -- [RFA] (elseif)
		tack_on_infos(OPTIONS_INFOS.CHECKPOINT_ADDON)
	end
	
	if is_signed_into_profile() then
		tack_on_infos(OPTIONS_INFOS.LOAD_GAME_ADDON)
	end
	
	tack_on_infos(OPTIONS_INFOS.QUIT_GAME_ADDON)
	
	List = Vdo_list:new("fail_list")
	List:draw_items(Options, 1)
	
	if Mouse_input_tracker ~= nil then
		List:add_mouse_input("ui_failure", Mouse_input_tracker)
		Mouse_input_tracker:subscribe(true)
	end
	
	local list_tween = Vdo_base_object:new("fail_list_alpha_twn_1")
	list_tween:set_property("end_event", "ui_failure_list_anim_done")
	
	local screen_in_animate = Vdo_anim_object:new("animate_in")
	screen_in_animate:play()
end

function ui_failure_cleanup()
	-- Blow away input events
	Input_tracker:subscribe(false)
	
	if Mouse_input_tracker ~= nil then
		Mouse_input_tracker:subscribe(false)
	end
end

function ui_failure_populate_header(title, reason)
	-- Set up the title text
	local heading = Vdo_base_object:new("fail_heading")
	heading:set_text(title)

	-- Set up the body text
	local body = Vdo_base_object:new("fail_text")
	body:set_text(reason)
end

function ui_failure_nav_up(event, acceleration)
	if Tween_done then
		-- Move highlight up
		if #Options > 1 then
			List:move_cursor(-1)
		end
	end
end

function ui_failure_nav_down(event, acceleration)
	if Tween_done then
		-- Move highlight down
		if #Options > 1 then
			List:move_cursor(1)
		end
	end
end

function ui_failure_initiate_quit_game()
	-- Kill inputs
	Input_tracker:subscribe(false)
	
	if Mouse_input_tracker ~= nil then
		Mouse_input_tracker:subscribe(false)
	end

	-- Start the sequence by asking the player if they really want to quit
	rf4_popup_create("MENU_SALVAGE_WARNING_QUIT", "ui_failure_quit_game_return", {"MENU_ACCEPT", "MENU_CANCEL"}, 2)
end

function ui_failure_quit_game_return(return_value)
	if return_value == 1 then
		-- Set up to leave screen
		ui_failure_leave_screen()
	else
		-- Turn inputs back on
		Input_tracker:subscribe(true)
		
		if Mouse_input_tracker ~= nil then
			Mouse_input_tracker:subscribe(true)
		end
	end
end

function ui_failure_button_a(event, acceleration)
	if Tween_done then
		local id = Option_ids[ List:get_selection() ]
		play_ui_sound( "INT_SELECT" )
		if id == QUIT_GAME_ID then
			-- Need a confirmation popup
			ui_failure_initiate_quit_game()
		else
			-- Set up to leave screen
			ui_failure_leave_screen()
		end
	end
end

function ui_failure_mouse_click(event, target_handle, mouse_x, mouse_y)
	-- Do nothing if the target_handle is invalid
	if target_handle == INVALID_VDO_HANDLE then
		return
	end
	
	-- Select the button with the associated target_handle
	local new_index = List:get_button_index(target_handle)
	if new_index ~= 0 then
		List:set_selection(new_index)
		List:move_cursor(0, true)
		ui_failure_button_a(event, 0)
	end
end

function ui_failure_mouse_move(event, target_handle, mouse_x, mouse_y)
	-- Do nothing if the target_handle is invalid
	if target_handle == INVALID_VDO_HANDLE then
		return
	end
	
	-- Move the highlight to the button with the associated target_handle
	local new_index = List:get_button_index(target_handle)
	if new_index ~= 0 then
		List:set_selection(new_index)
		List:move_cursor(0, true)
	end
end

function ui_failure_leave_screen()
	-- Kill inputs
	Input_tracker:subscribe(false)
	if Mouse_input_tracker ~= nil then
			Mouse_input_tracker:subscribe(false)
	end
	
	-- Animate out
	End_animation:play()
end

function ui_failure_finish(tween_h, event_name)
	local id = Option_ids[ List:get_selection() ]

	if id == CHECKPOINT_ID then
		rf4_failure_load_checkpoint()

	elseif id == RESPAWN_ID then -- [RFA] the world saved with Mason at the safehouse, then opened
		if not rfg_respawn() then -- [RFA] again from that save; with nowhere to put him, the last save
			rf4_failure_load_checkpoint() -- [RFA]
		end -- [RFA]
	
	elseif id == LOAD_GAME_ID then
		rf4_saveload_start(true)
		
	elseif id == QUIT_GAME_ID then
		-- Same as pause menu's exit
		rf4_failure_quit_game()
	end
end

function ui_failure_list_anim_done(tween_h, event_name)
	Tween_done = true
end