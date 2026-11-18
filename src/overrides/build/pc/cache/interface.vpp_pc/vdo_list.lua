-- Override of vdo_list.lua from <game>\build\pc\cache\interface.vpp_pc (the list of a toggle's choices that
-- opens in the menus' option lists). rfa_loader.dll gives the game this file instead of its own copy.
--
-- A list of more than RFA_LIST_SHOWN choices (such as Options > Main Menu > Music's 36) shows that many at a
-- time and scrolls as the highlight moves (the game's own lists, all shorter, look as before): the rest went
-- off the screen. Every line we added or changed is marked [RFA]; everything else is the game's original
-- script. Only the [RFA] lines are this project's (GNU GPL version 3 or later, see LICENSE); the rest belongs
-- to the game's owners. To compare with the original, extract it with the editor.

local RFA_LIST_SHOWN = 8 -- [RFA] choices shown at once

function vdo_list_init()
end

function vdo_list_cleanup()
end

-- Inherited from Vdo_base_object
Vdo_list = Vdo_base_object:new_base()

function Vdo_list:init()
	-- Hide the initial button
	vint_set_property(vint_object_find("button_base", self.handle), "visible", false)
	
	-- Get the correct select button for the platform
	self.select_button = get_button_name_for_action("menu_select")
	
	self.highlight = Vdo_button_highlight:new("button_highlight", self.handle)
	self.highlight:show_button( self.select_button )
end

-- Deletes the internal data and destroys the button clones
function Vdo_list:cleanup()
	if self.list_data ~= nil then
		for idx, button in pairs(self.list_data.buttons) do
			button:object_destroy()
		end
		self.list_data = nil
	end
end

function Vdo_list:set_size(width)
	--set the button width
	for idx, button in pairs(self.list_data.buttons) do
		button:set_width(width)
	end
	
	self.highlight:set_width(width)
	self.highlight:show_button( self.select_button )
end

function Vdo_list:draw_items(labels, current_option, width)
	-- Nuke the shit out of whatever was previously here
	self:cleanup()
	
	width = width or 300

	-- Set up handle for base button
	local button_copy = Vdo_generic_button:new("button_base", self.handle)
	if button_copy.handle == INVALID_VDO_HANDLE then
		debug_print("vint", "Unable to find object \"button_base\"")
		return
	end

	button_copy.x, button_copy.y = button_copy:get_property("anchor")
	
	-- Set up tables to manage handles and data for this object
	self.list_data = {}
	self.list_data.buttons = {}
	
	self.list_data.num_buttons = #labels
	self.list_data.base_x, self.list_data.base_y = button_copy.x, button_copy.y -- [RFA] for the scrolling
	self.list_data.window_start = 1 -- [RFA] the first choice shown
	
	for i = self.list_data.num_buttons, 1, -1 do
		-- Assign the button copy to the table that manages data for this object
		self.list_data.buttons[i] = Vdo_generic_button:clone(button_copy.handle)
		
		-- Set the button text
		local button = self.list_data.buttons[i]
		
		local val = labels[i]
		
		button:set_label(labels[i])
		
		-- Move the button copy into the correct position
		local new_y = button_copy.y - (LIST_BUTTON_SPACING * (self.list_data.num_buttons - i))
		button:set_property("anchor", button_copy.x, new_y)
		
		-- Make the button visible
		button:set_property("visible", true)
	end

	-- Set initial cursor position
	if current_option == nil or current_option < 1 then
		current_option = 1
	elseif current_option > self.list_data.num_buttons then
		current_option = self.list_data.num_buttons
	end
	
	self.list_data.current_idx = current_option
	
	--set the list size
	--self:set_size(width)

	-- Draw the cursor
	self:move_cursor(0)
end

function Vdo_list:move_cursor(direction, is_index_set)
	is_index_set = is_index_set or false

	-- Clear out old highlight
	for idx, button in pairs(self.list_data.buttons) do
		button:set_highlight(false)
		button:set_property("depth", -11)
		button:set_property("blur", 2.0)
		button:set_property("alpha", 0.6)
	end
	
	-- Play sound effect
	if direction ~= 0 or is_index_set then
		play_ui_sound( "INT_TOGGLE_UD" )
	end
	
	if not is_index_set then
		-- Increment the direction
		self.list_data.current_idx = wrap_index(self.list_data.current_idx, direction, self.list_data.num_buttons)
	end
	
	-- Get current button
	local current_button = self.list_data.buttons[self.list_data.current_idx]
	
	self:rfa_scroll_to(self.list_data.current_idx) -- [RFA]

	-- Highlight current button
	local highlight_icon = Vdo_base_object:new("hint_icon", self.highlight.handle)
	current_button:set_highlight(true, highlight_icon:get_visible())
	current_button:set_property("depth", -12)
	current_button:set_property("blur", 0)
	current_button:set_property("alpha", 1.0)
	local button_x, button_y = current_button:get_property("anchor")
	
	self.highlight:set_property( "anchor", button_x, button_y )
	self.highlight:set_width(current_button:get_width())
	self.highlight:set_property( "depth",  -1)
	--if there is only one button don't animate highlight
	if self.list_data.num_buttons > 1 then
		self.highlight:set_highlight()
	end
	return self.list_data.current_idx
end

-- [RFA] Shows the RFA_LIST_SHOWN choices around index (the window moves only as far as it must), in the places
-- [RFA] the last RFA_LIST_SHOWN of a short list have: the last shown where the last choice is, the rest above it.
function Vdo_list:rfa_scroll_to(index) -- [RFA]
	local data = self.list_data -- [RFA]
	if data == nil or data.num_buttons <= RFA_LIST_SHOWN or data.base_y == nil then return end -- [RFA]
	if index < data.window_start then -- [RFA]
		data.window_start = index -- [RFA]
	elseif index > data.window_start + RFA_LIST_SHOWN - 1 then -- [RFA]
		data.window_start = index - RFA_LIST_SHOWN + 1 -- [RFA]
	end -- [RFA]
	local window_end = data.window_start + RFA_LIST_SHOWN - 1 -- [RFA]
	for i, button in pairs(data.buttons) do -- [RFA]
		local shown = (i >= data.window_start and i <= window_end) -- [RFA]
		button:set_property("visible", shown) -- [RFA]
		if shown then -- [RFA]
			button:set_property("anchor", data.base_x, data.base_y - (LIST_BUTTON_SPACING * (window_end - i))) -- [RFA]
		end -- [RFA]
	end -- [RFA]
end -- [RFA]

function Vdo_list:get_selection()
	-- Spit back out the current highlight position
	return self.list_data.current_idx
end

function Vdo_list:set_selection(new_index)
	self.list_data.current_idx = new_index
end

function Vdo_list:get_button_index(target_handle)
	for idx, button in pairs(self.list_data.buttons) do
		if button.handle == target_handle then
			return idx
		end
	end
	
	return 0
end

function Vdo_list:set_highlight(is_highlighted)
	-- Get current button
	local current_button = self.list_data.buttons[self.list_data.current_idx]
	local highlight_icon = Vdo_base_object:new("hint_icon", self.highlight.handle)

	-- Highlight/Unhighlight current button
	current_button:set_highlight(is_highlighted, highlight_icon:get_visible())
end


-------------------[[ MOUSE SPECIFIC FUNCTIONS ]] --------------------

function Vdo_list:add_mouse_input(func_prefix, Input_tracker, priority)
	priority = priority or 50

	local mouse_click_function = func_prefix.."_mouse_click"
	local mouse_move_function = func_prefix.."_mouse_move"

	for idx, button in pairs(self.list_data.buttons) do
		Input_tracker:add_mouse_input("mouse_click", mouse_click_function, priority, button.handle)
		Input_tracker:add_mouse_input("mouse_move", mouse_move_function, priority, button.handle)
	end
end