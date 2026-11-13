-- Override of ui_main_menu_pc.lua from <game>\build\pc\cache\interface.vpp_pc (the main menu on
-- PC). rfa_loader.dll gives the game this file instead of its own copy.
--
-- Adds Options > Experimentals: the Gameplay screen opened with Rfa_options_page set to
-- "experimentals", which makes it show RFA-Mod's own settings (see ui_options_gameplay.lua). Every
-- line we added or changed is marked [RFA]; everything else is the game's original script. Only the
-- [RFA] lines are this project's (GNU GPL version 3 or later, see LICENSE); the rest belongs to the
-- game's owners. To compare with the original, extract it with the editor.

--------------
-- MENU IDS --
--------------

-- Top menu general
local NEW_GAME_ID				= 1
local CONTINUE_ID				= 2
local LOAD_GAME_ID				= 3
local MULTIPLAYER_ID			= 4
local OPTIONS_ID				= 5
local EXTRAS_ID					= 6
local WRECKING_CREW_ID			= 7
local QUIT_GAME_ID				= 8
local NEW_GAME_PLUS_ID			= 9
local DLC_ID					= 10

-- MP sub-menu
local MULTI_ONLINE_ID			= 101
local MULTI_SYSLINK_ID			= 102
local MULTI_OFFLINE_ID			= 103

-- Options sub-menu
local OPTIONS_CONTROLS_ID		= 201
local OPTIONS_GAMEPLAY_ID		= 202
local OPTIONS_AUDIO_ID			= 203
local OPTIONS_VIDEO_ID			= 204
local OPTIONS_EXPERIMENTALS_ID	= 205 -- [RFA]

-- DLC sub-menu
local DLC_OUTFITS_ID			= 301
local DLC_DLC04_MISSIONS_ID		= 302
local DLC_SHOW_STORE_ID			= 303


-- Extras sub-menu
local EXTRAS_PLAYER_STATS_ID	= 401
local EXTRAS_CUTSCENES_ID		= 402
local EXTRAS_AUDIO_LOGS_ID		= 403
local EXTRAS_CREDITS_ID			= 404

local MAIN_MENU_TIER			= 1
local MULTIPLAYER_TIER			= 2
local OPTIONS_TIER				= 3
local DLC_TIER					= 4
local EXTRAS_TIER				= 5

-- Demo Mode
local DEMO_MODE = false

-- Special next-screen strings
local NEXT_SCREEN_LOAD_GAME	= "next_screen_load_game"
local NEXT_SCREEN_CREDITS		= "next_screen_credits"

----------
-- DATA --
----------

local MENU_DATA = {
	MAIN_CONTINUE = {
		{label = "MENU_CONTINUE_UC",	id = CONTINUE_ID},
	},

	MAIN_NEW_GAME = {
		{label = "MENU_NEW_GAME",		id = NEW_GAME_ID},
	},

	MAIN_NEW_GAME_PLUS = {
		{label = "MENU_NEW_GAME_PLUS", id = NEW_GAME_PLUS_ID},
	},

	MAIN_DEMO = {		-- Blast away all the stuff we don't want for demo mode
		{label = "MENU_NEW_GAME",		id = NEW_GAME_ID},
		{label = "MENU_OPTIONS_UC",	id = OPTIONS_ID},
	},

	MAIN_HAS_PROFILE = {
		{label = "MENU_LOAD_GAME",		id = LOAD_GAME_ID},
	},

	MAIN_DLC = {		-- DLC option
		{label = "MENU_DOWNLOADABLE_CONTENT",	id = DLC_ID},
	},

	MAIN_GENERAL = {		-- Other stuff at top level
		{label = "MENU_INFESTATION",	id = MULTIPLAYER_ID},
		{label = "MENU_RUIN_MODE",		id = WRECKING_CREW_ID},
		{label = "MENU_OPTIONS_UC",	id = OPTIONS_ID},
		{label = "MENU_EXTRAS_UC",		id = EXTRAS_ID},
		{label = "MENU_QUIT_DESKTOP",	id = QUIT_GAME_ID},
	},

	DLC_SUB_OUTFITS = {
		{label = "MENU_OUTFITS",		id = DLC_OUTFITS_ID},
	},

	DLC_SUB_MISSIONS = {
		{label = "MENU_DLC04_MISSIONS_TITLE",	id = DLC_DLC04_MISSIONS_ID},
	},
	
	DLC_SUB_STORE = {
		{label = "MENU_SHOW_DLC_STORE_STEAM", id = DLC_SHOW_STORE_ID},
	},


	MULTIPLAYER_SUB_MENU = {			-- MP sub-menu
		{label = "NETWORK_PLATFORM_NAME",	id = MULTI_ONLINE_ID},
		{label = "NETWORK_LOCAL_NAME",		id = MULTI_SYSLINK_ID},		
		{label = "NETWORK_OFFLINE_NAME",	id = MULTI_OFFLINE_ID},
	},

	OPTIONS_SUB_MENU = {				-- Options sub-menu
		{label = "MENU_CONTROLS_UC",		id = OPTIONS_CONTROLS_ID},
		{label = "MENU_GAMEPLAY_UC",		id = OPTIONS_GAMEPLAY_ID},
		{label = "MENU_AUDIO_UC",			id = OPTIONS_AUDIO_ID},
		{label = "MENU_VIDEO_UC",			id = OPTIONS_VIDEO_ID},
		{label = "EXPERIMENTALS",			id = OPTIONS_EXPERIMENTALS_ID}, -- [RFA]
	},
	
	EXTRAS_SUB_MENU = {
		{label = "MENU_PLAYER_STATS",	id = EXTRAS_PLAYER_STATS_ID},
		{label = "MENU_CUTSCENES",		id = EXTRAS_CUTSCENES_ID},
		{label = "MENU_AUDIO_LOGS",	id = EXTRAS_AUDIO_LOGS_ID},
		{label = "MENU_CREDITS",		id = EXTRAS_CREDITS_ID},
	},
	
	CREDITS_SUB_MENU = {
		{label = "MENU_CREDITS",		id = EXTRAS_CREDITS_ID},
	}	
}
-------------
-- GLOBALS --
-------------

local Texticles = {}

local Top_menu_data = {}

local Input_tracker
local Mouse_input_tracker

local Anims = {}
local Hint_bar, List, Sub_list

local Tween_done = true

local Next_screen = nil

function ui_main_menu_init()

	DEMO_MODE = is_demo()
	
	Global_show_kb_controls = (pc_is_active_input_gamepad() == false)

	-- Subscribe to the button presses we need
	Input_tracker = Vdo_input_tracker:new()
	Input_tracker:add_input("menu_select", "ui_main_menu_button_a", 50)
	Input_tracker:add_input("menu_cancel", "ui_main_menu_button_b", 50)
	Input_tracker:add_input("nav_up", "ui_main_menu_nav_up", 50)
	Input_tracker:add_input("nav_down", "ui_main_menu_nav_down", 50)
	Input_tracker:subscribe(true)

	Mouse_input_tracker = Vdo_input_tracker:new()

	-- Set button text
	Hint_bar = Vdo_hint_bar:new("hint_bar", 0, vint_document_find("ui_background_video"))
	Hint_bar:set_hints( nil )
	Hint_bar:set_header("MENU_TITLE_UC", COLOR_HIGHLIGHT_BAR)
	Hint_bar:set_value("")	-- Updated later

	-- Initialize the list object
	List = Vdo_menu_list:new("list")
	Sub_list = Vdo_menu_list:new("list2")

	--Animation Stuff
	local alpha_tween = Vdo_tween_object:new("list_alpha_twn")
	alpha_tween:set_end_event("ui_main_menu_animate")

	alpha_tween = Vdo_tween_object:new("list_alpha_twn_1")
	alpha_tween:set_end_event("ui_main_menu_animate")

	alpha_tween = Vdo_tween_object:new("list2_alpha_twn_1")
	alpha_tween:set_end_event("ui_main_menu_animate")

	alpha_tween = Vdo_tween_object:new("list2_alpha_twn_2")

	alpha_tween = Vdo_tween_object:new("list_alpha_twn2")

	Anims.list_out = Vdo_anim_object:new("list_anim_out")
	Anims.list_in = Vdo_anim_object:new("list_anim_in")
	Anims.list_in2 = Vdo_anim_object:new("list_anim_in2")
	Anims.sub_list_out = Vdo_anim_object:new("list2_anim_out")
	Anims.sub_list_in = Vdo_anim_object:new("list2_anim_in")
	Anims.menu_in = Vdo_anim_object:new("menu_animate_in")
	Anims.menu_out = Vdo_anim_object:new("menu_animate_out")
	Anims.menu_tier_1_out = Vdo_anim_object:new("list_anim_out_screen")

	local menu_out_tween = Vdo_base_object:new("list2_alpha_twn_3")
	menu_out_tween:set_property("end_event", "ui_main_menu_load_screen")

	local menu_tier_1_out_tween = Vdo_base_object:new("list_alpha_twn2_1")
	menu_tier_1_out_tween:set_property("end_event", "ui_main_menu_load_screen")

	local menu_tier_1_in_tween = Vdo_tween_object:new("list2_scale_twn_1")
	menu_tier_1_in_tween:set_property("end_event", "ui_main_menu_animate")

	local tech_module = Vdo_tech_module:new("tech_module")
	tech_module:module_animate_in()

	local tech_meter = Vdo_tech_meter:new("tech_meter")
	tech_meter:meter_animate_in()

	local tech_corner = Vdo_tech_corner:new("tech_corner_1")
	tech_corner:play()

	-- Little scrolling texticles
	Texticles.scrolling_text1 = Vdo_scrolling_text:new("scrolling_text1")
	Texticles.scrolling_text2 = Vdo_scrolling_text:new("scrolling_text2")
	Texticles.scrolling_text3 = Vdo_scrolling_text:new("scrolling_text3")
	local text1_string = {
		[1] = "23horn75290348   324578345234785   2374589239485729";
		[2] = "259384572384           2349857398547tam87694";
		[3] = "457368954746           845gnuechtel4576498";
		[4] = "43297873                                                958sdrose67584938";
		[5] = "54898062                                                084533498023tam2311111";
		[6] = "3423425645345987345345                          54689kim93";
		[7] = "\n ";
		[8] = "879549738495765                                      0721334432";
		[9] = "4508montgomery570234                                                         534schnieder3049345";
		[10] = "453986tam48567498567                             56987496834756987547233245";
		[11] = "5schnieder8405   54                                081234874";
		[12] = "4325435345                                            50897596470394tam603485";
	}
	local text2_string = {
		[1] = "214523215234563        78   7899925349582734535                                                    984759schnieder423453745";
		[2] = "987653498679345        54  3290875montgomery544235324                      234098501montgomery8945689";
		[3] = "234589893284         096802356234789273485938533horn85";
		[4] = "20394850236753460983495gnuechtel72394568723945890685";
		[5] = "\n ";
		[6] = "04895234527349527134098511kim2309487520635         42039845620893457690348gnuechtel39246534890756983456";
		[7] = "45827394587234985678023094867923856               0895234609834kim683745638495654689734";
		[8] = "gnuechtel montgomery schnieder tam kim horn";
	}

	Texticles.scrolling_text1:run_text(text1_string, 0.001, true, 5)
	Texticles.scrolling_text2:run_text(text2_string, 0.001, true, 3)
	Texticles.scrolling_text3:run_text(text1_string, 0.01, true, 2)

	vint_dataitem_add_subscription("main_menu_update", "update", "ui_main_menu_update")

	ui_main_menu_update_top_menu_and_draw()
end

function ui_main_menu_cleanup()
	-- Nuke the scrolling texticles
	for idx, val in pairs(Texticles) do
		val:object_destroy()
	end

	-- Nuke all button subscriptions
	Input_tracker:subscribe(false)
	Mouse_input_tracker:subscribe(false)
	
	Hint_bar:set_header("")
	Hint_bar:set_value("")

	rf4_popup_close()
end

function ui_main_menu_initiate_quit_game()
	-- Kill inputs
	Input_tracker:subscribe(false)
	Mouse_input_tracker:subscribe(false)

	local quit_string = "MENU_DESKTOP_QUIT_WARNING"

	-- Start the sequence by asking the player if they really want to quit
	rf4_popup_create(quit_string, "ui_main_menu_quit_game_return", {"MENU_ACCEPT", "MENU_CANCEL"}, 2)
end

function ui_main_menu_quit_game_return(return_value)

	if return_value == 1 then
		-- Fix the globals
		main_menu_reset_selections()

		rf4_main_menu_pc_quit_game()
	else
		-- Turn inputs back on
		Input_tracker:subscribe(true)
		Mouse_input_tracker:subscribe(true)
	end
end

-- Constructs the top level menu menu based on init flags
function ui_main_menu_construct_top_menu_from_flags()

	Top_menu_data = {}
	local num_data = 0

	-- Helper function that loops through a list of data and tacks them onto the list
	local function tack_on_data(data)
		for i, info in ipairs(data) do
			num_data = num_data + 1
			Top_menu_data[num_data] = info
		end
	end

	if DEMO_MODE == true then
		-- Use the ultra simple style menu
		tack_on_data(MENU_DATA.MAIN_DEMO)
	else
		-- Check for save games
		if rf4_main_menu_has_save_games() then
			tack_on_data(MENU_DATA.MAIN_CONTINUE)
		end

		tack_on_data(MENU_DATA.MAIN_NEW_GAME)

		-- Check for New Game+
		if rf4_main_menu_show_new_game_plus() then
			tack_on_data(MENU_DATA.MAIN_NEW_GAME_PLUS)
		end

		if is_signed_into_profile() then
			tack_on_data(MENU_DATA.MAIN_HAS_PROFILE)
		end

		-- Check for downloadable outfits or missions
		--if rf4_dlc_is_character_unlock_available() or rf4_dlc_are_dlc04_missions_available() then
			tack_on_data(MENU_DATA.MAIN_DLC)
		--end

		-- Everything else
		tack_on_data(MENU_DATA.MAIN_GENERAL)
	end
end

function ui_main_menu_update_top_menu_and_draw(saved_id)
	-- Now construct the top level menu from the flags
	ui_main_menu_construct_top_menu_from_flags()

	if Global_last_main_menu_tier == MAIN_MENU_TIER then
		if saved_id ~= nil then
			Global_last_main_menu_option = 1

			-- We have an old ID selection, use this as the option if we can find it
			-- Special case: Continue (first option) always takes priority over New Game
			if saved_id ~= NEW_GAME_ID then
				for i, option in ipairs(Top_menu_data) do
					if option.id == saved_id then
						Global_last_main_menu_option = i
						break
					end
				end
			end
		end
	elseif Global_last_main_menu_tier == DLC_TIER then
		-- We need to make sure the DLC tier is still valid
		local found_dlc = false
		for i, option in ipairs(Top_menu_data) do
			if option.id == DLC_ID then
				found_dlc = true
				break
			end
		end
		
		if not found_dlc then
			-- Reset the main menu to the top option
			Global_last_main_menu_tier = MAIN_MENU_TIER
			Global_last_main_menu_option = 1
		end
	end

	-- Draw the list using the saved tier and option
	if Global_last_main_menu_tier == DLC_TIER and dlc_sub_menu_is_valid() == false then
		Global_last_main_menu_tier = MAIN_MENU_TIER
		Global_last_main_menu_option = ui_main_menu_get_option_from_data(Top_menu_data, DLC_ID)
	end
	ui_main_menu_change_tier(Global_last_main_menu_tier, Global_last_main_menu_option, true)
end

function dlc_sub_menu_is_valid()
	if rf4_dlc_is_character_unlock_available() then
		return true
	end
	
	if rf4_dlc_are_dlc04_missions_available() then
		return true
	end
	
	return false
end

function ui_main_menu_construct_dlc_sub_menu()
	local sub_data = {}
	
	-- Helper function that loops through a list of data and tacks them onto the list
	local num_data = 0
	local function tack_on_data(data)
		for i, info in ipairs(data) do
			num_data = num_data + 1
			sub_data[num_data] = info
		end
	end
	
	if rf4_dlc_is_character_unlock_available() then
		tack_on_data(MENU_DATA.DLC_SUB_OUTFITS)
	end
	
	if rf4_dlc_are_dlc04_missions_available() then
		tack_on_data(MENU_DATA.DLC_SUB_MISSIONS)
	end
	
	if dlc_sub_menu_is_valid() then
		tack_on_data(MENU_DATA.DLC_SUB_STORE)
	end
	
	return sub_data
end

function ui_main_menu_nav_up(event, acceleration)
	if not Tween_done then
		return
	end

	--check to see what list to send the input to
	local menu_list = (Global_last_main_menu_tier == MAIN_MENU_TIER) and List or Sub_list

	if menu_list:get_selection() == 0 then
		-- If nothing on the menu is selected, set the top-most option as selected
		menu_list:set_selection(1)
		menu_list:set_highlight(true)
		Global_last_main_menu_option = menu_list:move_cursor(0, true)
	else
		-- Move highlight up
		Global_last_main_menu_option = menu_list:move_cursor(-1)
		
		if Global_last_main_menu_tier == MAIN_MENU_TIER then
			ui_main_menu_set_screen_hint()
		end
	end
end

function ui_main_menu_nav_down(event, acceleration)
	if not Tween_done then
		return
	end

	--check to see what list to sendacceleration the input to
	local menu_list = (Global_last_main_menu_tier == MAIN_MENU_TIER) and List or Sub_list

	if menu_list:get_selection() == 0 then
		-- If nothing on the menu is selected, set the top-most option as selected
		menu_list:set_selection(1)
		menu_list:set_highlight(true)
		Global_last_main_menu_option = menu_list:move_cursor(0, true)
	else
		-- Move highlight down
		Global_last_main_menu_option = menu_list:move_cursor(1)
		
		if Global_last_main_menu_tier == MAIN_MENU_TIER then
			ui_main_menu_set_screen_hint()
		end
	end
end

function ui_main_menu_set_screen_hint(new_tier)
	local current_id = List:get_id()
	if new_tier == MULTIPLAYER_TIER then
		current_id = MULTIPLAYER_ID
	elseif new_tier == OPTIONS_TIER then
		current_id = OPTIONS_ID
	elseif new_tier == DLC_TIER then	
		current_id = DLC_ID
	elseif new_tier == EXTRAS_TIER then
		current_id = EXTRAS_ID
	end
	
	-- Now for the huge-ass if/thens for each ID...
	if current_id == MULTIPLAYER_ID then
		Hint_bar:set_value("MENU_DESC_INFESTATION")

	elseif current_id == WRECKING_CREW_ID then
		Hint_bar:set_value("MENU_DESC_RUIN")

	elseif current_id == OPTIONS_ID then
		Hint_bar:set_value("MENU_DESC_OPTIONS")
		
	elseif current_id == EXTRAS_ID then
		Hint_bar:set_value("MENU_DESC_EXTRAS")

	elseif current_id == NEW_GAME_ID then
		Hint_bar:set_value("MENU_DESC_NEW")

	elseif current_id == CONTINUE_ID then
		Hint_bar:set_value("MENU_DESC_CONTINUE")

	elseif current_id == NEW_GAME_PLUS_ID then
		Hint_bar:set_value("MENU_DESC_PLUS")

	elseif current_id == LOAD_GAME_ID then
		Hint_bar:set_value("MENU_DESC_LOAD")

	elseif current_id == DLC_ID then
		Hint_bar:set_value("MENU_DESC_DLC")
	
	elseif current_id == QUIT_GAME_ID then
		Hint_bar:set_value("MENU_DESC_QUIT")
	end
end

function ui_main_menu_button_a(event, acceleration)
	if not Tween_done then
		return
	end

	-- Make sure there's a valid selection index before processing the A button (or click)
	local current_selection = (Global_last_main_menu_tier == MAIN_MENU_TIER) and List:get_selection() or Sub_list:get_selection()
	if current_selection == 0 then
		return
	end

	play_ui_sound( "INT_SELECT" )

	-- Check whether we're in the sub-list or not to get the right ID
	local current_id = (Global_last_main_menu_tier == MAIN_MENU_TIER) and List:get_id() or Sub_list:get_id()

	-- Helper function to set the next screen
	local function set_next_screen( screen, tier )
		Next_screen = screen
		if tier ~= MAIN_MENU_TIER then
			-- These are sub-menu tiers
			Anims.menu_out:play()
		else
			Anims.menu_tier_1_out:play()
		end
		Tween_done = false

		-- Stop the input now, so no input can be read during the animation
		--Input_tracker:subscribe(false)
	end

	-- Now for the huge-ass if/thens for each ID...
	if current_id == MULTIPLAYER_ID then
		ui_main_menu_change_tier(MULTIPLAYER_TIER, 1)

	elseif current_id == WRECKING_CREW_ID then
		rf4_main_menu_start_wrecking_crew()

	elseif current_id == OPTIONS_ID then
		ui_main_menu_change_tier(OPTIONS_TIER, 1)

	elseif current_id == EXTRAS_ID then
		ui_main_menu_change_tier(EXTRAS_TIER, 1)

	elseif current_id == NEW_GAME_ID then
		-- Don't animate out, just start a new game
		rf4_main_menu_new_game()

	elseif current_id == CONTINUE_ID then
		rf4_main_menu_continue()

	elseif current_id == NEW_GAME_PLUS_ID then
		rf4_main_menu_new_game_plus()

	elseif current_id == LOAD_GAME_ID then
		set_next_screen(NEXT_SCREEN_LOAD_GAME, MAIN_MENU_TIER)

	elseif current_id == DLC_ID then
	
		if dlc_sub_menu_is_valid() == false then
			rf4_main_menu_show_dlc_store()
		else
			ui_main_menu_change_tier(DLC_TIER, 1)
		end

	elseif current_id == DLC_DLC04_MISSIONS_ID then
		rf4_main_menu_dlc04_start()

	elseif current_id == DLC_SHOW_STORE_ID then
		rf4_main_menu_show_dlc_store()
		
	elseif current_id == DLC_OUTFITS_ID then
		set_next_screen("ui_dlc_outfits", DLC_TIER)

	elseif current_id == QUIT_GAME_ID then
		ui_main_menu_initiate_quit_game()

	elseif current_id == MULTI_ONLINE_ID then
		-- Don't animate out, just push to live
		rf4_main_menu_start_live()

	elseif current_id == MULTI_SYSLINK_ID then
		-- Don't animate out, just push to syslink
		rf4_main_menu_start_syslink()

	elseif current_id == MULTI_OFFLINE_ID then
		-- Don't animate out, just push to offline MP
		rf4_main_menu_start_offline()

	elseif current_id == OPTIONS_CONTROLS_ID then
		set_next_screen("ui_options_controller", OPTIONS_TIER)

	elseif current_id == OPTIONS_GAMEPLAY_ID then
		rawset(_UGGlobals, "Rfa_options_page", "gameplay") -- [RFA] (rawset: the menus refuse new globals)
		set_next_screen("ui_options_gameplay", OPTIONS_TIER)

	elseif current_id == OPTIONS_EXPERIMENTALS_ID then -- [RFA]
		rawset(_UGGlobals, "Rfa_options_page", "experimentals") -- [RFA]
		set_next_screen("ui_options_gameplay", OPTIONS_TIER) -- [RFA]

	elseif current_id == OPTIONS_AUDIO_ID then
		set_next_screen("ui_options_audio", OPTIONS_TIER)

	elseif current_id == OPTIONS_VIDEO_ID then
		set_next_screen("ui_options_video", OPTIONS_TIER)
	
	elseif current_id == EXTRAS_PLAYER_STATS_ID then
		set_next_screen("ui_sp_stats", EXTRAS_TIER)
		
	elseif current_id == EXTRAS_CUTSCENES_ID then
		set_next_screen("ui_video_player", EXTRAS_TIER)
		
	elseif current_id == EXTRAS_AUDIO_LOGS_ID then
		set_next_screen("ui_audio_logs", EXTRAS_TIER)
		
	elseif current_id == EXTRAS_CREDITS_ID then
		set_next_screen(NEXT_SCREEN_CREDITS, EXTRAS_TIER)
	end
end

function ui_main_menu_button_b(event, acceleration)
	if Tween_done == true then
		if Global_last_main_menu_tier ~= MAIN_MENU_TIER then
			ui_main_menu_change_tier(MAIN_MENU_TIER, 1)
			play_ui_sound( "INT_BACK" )
		else
			-- If on the main tier and Esc (or B) is pressed, prompt to quit to desktop
			ui_main_menu_initiate_quit_game()
		end
	end
end

function ui_main_menu_mouse_click(event, target_handle)
	if target_handle == INVALID_VDO_HANDLE then
		return
	end

	if Hint_bar.hint_button_hit_boxes[1].handle == target_handle then
		ui_main_menu_button_b()
		return
	end

	local menu_list = (Global_last_main_menu_tier == MAIN_MENU_TIER) and List or Sub_list
	local new_index = menu_list:get_button_idx(target_handle)

	if new_index ~= 0 then
		menu_list:set_selection(new_index)
		ui_main_menu_button_a()
	end
end

function ui_main_menu_mouse_move(event, target_handle, mouse_x, mouse_y)
	if not Tween_done then
		return
	end
	
	local menu_list = (Global_last_main_menu_tier == MAIN_MENU_TIER) and List or Sub_list
	local new_index = menu_list:get_button_idx(target_handle)

	if new_index == 0 then
		if Hint_bar.hint_button_hit_boxes[1].handle == target_handle then
			Hint_bar:set_highlight(1)
		else
			Hint_bar:set_highlight(0)
		end
		return
	end

	if new_index ~= 0 then
		menu_list:set_selection(new_index)
		menu_list:set_highlight(true)
		Global_last_main_menu_option = menu_list:move_cursor(0, true)
		Hint_bar:set_highlight(0)
		
		if Global_last_main_menu_tier == MAIN_MENU_TIER then
			ui_main_menu_set_screen_hint()
		end
	end
end

-- Note: current_option is ignored when returning from another tier to the main menu tier
--
function ui_main_menu_change_tier(new_tier, current_option, init_set)
	local sub_data = {}
	local menu_dag_group = Vdo_base_object:new("menu_dag_group")
	
	Global_show_kb_controls = (pc_is_active_input_gamepad() == false)

	Mouse_input_tracker:remove_all()

	-- Set up the version for the main menu based on game saves, put the highlight in the right place for backing up
	if new_tier == MAIN_MENU_TIER then
		-- Get the selection pointing to the previous menu
		if Global_last_main_menu_tier == MULTIPLAYER_TIER then
			current_option = ui_main_menu_get_option_from_data(Top_menu_data, MULTIPLAYER_ID)
			sub_data = MENU_DATA.MULTIPLAYER_SUB_MENU
		elseif Global_last_main_menu_tier == OPTIONS_TIER then
			current_option = ui_main_menu_get_option_from_data(Top_menu_data, OPTIONS_ID)
			sub_data = MENU_DATA.OPTIONS_SUB_MENU
		elseif Global_last_main_menu_tier == EXTRAS_TIER then
			current_option = ui_main_menu_get_option_from_data(Top_menu_data, EXTRAS_ID)
			if is_signed_into_profile() then
				sub_data = MENU_DATA.EXTRAS_SUB_MENU
			else
				sub_data = MENU_DATA.CREDITS_SUB_MENU
			end			
		elseif Global_last_main_menu_tier == DLC_TIER then
			if dlc_sub_menu_is_valid() then
				current_option = ui_main_menu_get_option_from_data(Top_menu_data, DLC_ID)
				sub_data = ui_main_menu_construct_dlc_sub_menu()
			else
				current_option = ui_main_menu_get_option_from_data(Top_menu_data, DLC_ID)
				sub_data = Top_menu_data
			end
		else
			sub_data = Top_menu_data
		end

		--we need to show the first tier so fade/blur out the second tier
		--reset the scale
		List:set_property("scale", 1.0, 1.0)
		List:draw_items(Top_menu_data, 1)
		List.w,List.h = List:get_property("screen_size")
		List:draw_items(Top_menu_data, current_option)
		List:add_mouse_input("ui_main_menu", Mouse_input_tracker)
		Mouse_input_tracker:subscribe(true)
		List:set_highlight(true)
		ui_main_menu_set_screen_hint()

		ui_main_menu_reset_mask_size(1)

		--start the animations
		if init_set then
			-- Coming in to the top menu straight up
			Anims.menu_in:play()
			Sub_list:set_property("alpha", 0)
		else
			-- Coming back from a sub-menu
			Anims.list_in:play()
			Anims.sub_list_out:play()
		end
		Tween_done = false

		List:set_property("blur",0)
		Sub_list:set_property("blur",0)
		menu_dag_group:set_property("depth",-3)

		--draw the second list with temp data
		Sub_list:draw_items(sub_data, Global_last_main_menu_option)
		-- Hide the back button because it doesn't do anything
		Hint_bar:set_hints(nil)
	else
		if new_tier == MULTIPLAYER_TIER then
			-- Set up data
			sub_data = MENU_DATA.MULTIPLAYER_SUB_MENU
		elseif new_tier == OPTIONS_TIER then
			sub_data = MENU_DATA.OPTIONS_SUB_MENU
		elseif new_tier == EXTRAS_TIER then
			if is_signed_into_profile() then
				sub_data = MENU_DATA.EXTRAS_SUB_MENU
			else
				sub_data = MENU_DATA.CREDITS_SUB_MENU
			end	
		elseif new_tier == DLC_TIER then
			if dlc_sub_menu_is_valid() then
				sub_data = ui_main_menu_construct_dlc_sub_menu()
			else
				local dlc_option = ui_main_menu_get_option_from_data(Top_menu_data, DLC_ID)
				ui_main_menu_change_tier(MAIN_MENU_TIER, dlc_option, init_set)
				return
			end

		end

		Sub_list:set_property("scale", 1.0, 1.0 )
		-- Redraw the second list
		Sub_list:draw_items(sub_data, 1)
		Sub_list.w,Sub_list.h = Sub_list:get_property("screen_size")
		Sub_list:draw_items(sub_data, current_option)
		Sub_list:add_mouse_input("ui_main_menu", Mouse_input_tracker)

		ui_main_menu_reset_mask_size(2)

		--we are showing the second tier so fade/blur it in and push the first tier back
		List:set_property("alpha", 0 )
		Sub_list:set_property("alpha", 0 )
		Sub_list:set_property("blur", 0 )
		menu_dag_group:set_property("depth",-5)
		Anims.sub_list_in:play()

		if init_set then
			-- Coming in directly to a sub-menu
			Anims.list_in2:play()
			List:set_property("scale", 0.6,0.6 )
		else
			-- Coming in to a sub-menu from the top menu
			Anims.list_out:play()
		end

		--redraw the first list, incase we are coming back from a sub screen
		List:draw_items(Top_menu_data, 1)
		--unhighlight the first list
		List:set_highlight(false)
		List:set_property("blur",8.0)
		--shrink the first list
		ui_main_menu_set_screen_hint(new_tier)

		Tween_done = false

		-- Show the back button
		local back_button = get_button_name_for_action("menu_cancel")
		local hint_data = {back_button, "MENU_BACK"}
		Hint_bar:set_hints(hint_data)
		Hint_bar:add_mouse_input("ui_main_menu", Mouse_input_tracker)
		Mouse_input_tracker:subscribe(true)
	end

	-- Update the values
	Global_last_main_menu_tier = new_tier
	Global_last_main_menu_option = current_option
end

-- Matches an ID in an info table and returns the count
function ui_main_menu_get_option_from_data(data, id)
	for i, info in ipairs(data) do
		if info.id == id then
			return i
		end
	end

	return 1
end

function ui_main_menu_animate(tween_h, event_name)
	Tween_done = true
end

function ui_main_menu_load_screen(tween_h, event_name)
	Tween_done = true
	if Next_screen then
		-- Handle special codes
		if Next_screen == NEXT_SCREEN_LOAD_GAME then
			-- Don't push the screen in script, we need to wait for the game state to change
			rf4_main_menu_load_game()

		elseif Next_screen == NEXT_SCREEN_CREDITS then
			-- Need to change game state, use the special function
			rf4_main_menu_credits()

		-- Pass the screen directly through
		else
			rf4_push_ui(Next_screen)
		end
	end
end

-- Getting this means we need to redraw the top level
function ui_main_menu_update(di_h, event_type)
	local update = vint_dataitem_get(di_h)

	if update then
		-- If we're at the top tier already, save out the current selection ID so we
		-- can select it if it's still around after the update
		local saved_id = nil
		if Global_last_main_menu_tier == MAIN_MENU_TIER then
			saved_id = List:get_id()
		end

		ui_main_menu_update_top_menu_and_draw(saved_id)
	end
end

function ui_main_menu_reset_mask_size( list_num )
	local list_w,list_h
	local mask_pad = 20
	local mask_corner = Vdo_base_object:new( "mask_corner" )
	local mask_bg1 = Vdo_base_object:new( "mask_bg1" )
	local mask_bg2 = Vdo_base_object:new( "mask_bg2" )
	local corner_w,corner_h = mask_corner:get_property( "screen_size" )

	if list_num == 1 then
		mask_corner:set_property( "anchor", List.w - corner_w - mask_pad, -1*( List.h + mask_pad ) )
		mask_bg1:set_property( "scale", List.w - corner_w - mask_pad, -1*( List.h + mask_pad ) )
		mask_bg2:set_property( "scale", List.w - mask_pad, -1*( List.h + mask_pad - corner_w ) )
	elseif list_num == 2 then
		mask_corner:set_property( "anchor", Sub_list.w - corner_w - mask_pad, -1*( Sub_list.h + mask_pad ) )
		mask_bg1:set_property( "scale", Sub_list.w - corner_w - mask_pad, -1*( Sub_list.h + mask_pad ) )
		mask_bg2:set_property( "scale", Sub_list.w - mask_pad, -1*( Sub_list.h + mask_pad - corner_w ) )
	end
end
