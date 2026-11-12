namespace Mozaic.Core.Config;

public static class DefaultConfig
{
    public const string Text = """
# =============================================================================
#  Mozaic configuration — i3 syntax (https://i3wm.org/docs/userguide.html)
#  Reload with $mod+Shift+c. Errors are shown as a notification and logged.
# =============================================================================

# Modifier key. Alt (Mod1) is the default because Windows reserves many
# Win-key combinations (Win+L can never be overridden). For the Win key use:
#   set $mod Win
set $mod Alt

font pango:Segoe UI 10

# Programs
set $term wt.exe

# Gaps in pixels (at 100% display scaling; scaled automatically per monitor)
gaps inner 6
gaps outer 2
smart_gaps off

# 0 = rely on the Windows 11 colored window border; N = draw an N pixel frame
default_border pixel 0
# Rounded corners of that frame, in pixels
border_radius 10

# Title bars drawn by Mozaic above every tiled window (Windows apps have their
# own, so this is off by default; tabbed/stacked containers always have tabs)
titlebars no
# Application icon at the start of title bars and tabs
titlebar_icons yes
# Rounded corners of title bars and tabs, in pixels
titlebar_radius 6
# Buttons on title bars and the active tab, acting on that tile:
# split (where the next window opens), layout (split/stacked/tabbed),
# floating, fullscreen, close — or none
titlebar_buttons split layout floating fullscreen close

default_orientation auto
workspace_layout default
focus_follows_mouse no
mouse_warping output
focus_wrapping yes
workspace_auto_back_and_forth no
focus_on_window_activation smart

# Hold $mod and drag with the left mouse button to move windows (tiled windows
# are dropped next to / swapped with the window under the cursor), right button
# to resize.
floating_modifier $mod

# Dragging a tiled window by its title bar re-tiles it where you drop it;
# dragging a tiled window's edge resizes its neighbours.
tiling_drag on

# --- Windows 11 integration (Mozaic extensions) ----------------------------
# Color the native window border with the client.* colors below
window_border_colors yes
# default | square | round | small
corner_style default
# Keep floating windows above tiled windows of the same workspace (like i3)
floating_always_on_top yes
# Remove split containers that are left with a single child (no "useless nesting")
flatten_containers yes
# Restore the layout after restarting Mozaic (windows that still exist)
remember_layout yes
# New windows split the focused window along its longer side (spiral layout,
# like the popular i3 "autotiling" script). Off = classic i3 behaviour.
autotiling no
# Briefly show the workspace name / mode in the middle of the screen
osd yes
# Open the shortcut guide (Mozaic Settings) every time Mozaic starts
welcome yes
# Lines in the gaps between windows of the same tile (a part of the workspace you
# split into several windows, e.g. with $mod+v). A current flows along them when
# the focus or layout changes and while $mod is held.
# on | always (current flows all the time) | off
group_lines on
group_lines_color #2e9ef4
# Fade windows by how recently they were used: the active window is opaque, the
# previously used one 90%, older ones less, down to 70%. "off" disables it.
inactive_opacity 90 70
# Windows that always stay opaque:
# opacity_ignore [process="^mpv$"]

# --- Colors ------------------------------------------------------------------
# class                 border  backgr. text    indicator child_border
client.focused          #4c7899 #285577 #ffffff #2e9ef4   #2e9ef4
client.focused_inactive #333333 #5f676a #ffffff #484e50   #5f676a
client.unfocused        #333333 #222222 #888888 #292d2e   #222222
client.urgent           #2f343a #900000 #ffffff #900000   #900000

# --- Window rules -------------------------------------------------------------
# Criteria: class (window class or process name), process, title, window_type,
# con_mark, floating, tiling, id, con_id. Class/process are case-insensitive.
for_window [window_type="dialog"] floating enable
for_window [title="^Picture.in.[Pp]icture$"] floating enable, sticky enable
for_window [process="^CalculatorApp$"] floating enable
# assign [process="spotify"] workspace number 9
# ignore [process="^SomeOverlay$"]

# --- Keybindings ----------------------------------------------------------------
bindsym $mod+Return exec $term
bindsym $mod+Shift+q kill

# Built-in launcher (start menu apps + PATH), window switcher, and a
# searchable list of all keybindings / commands
bindsym $mod+d launcher
bindsym $mod+Shift+d launcher windows
bindsym $mod+F1 launcher commands

# Hold to show every shortcut on a keyboard (like the guide); hold Shift too
# for the $mod+Shift shortcuts. Release the keys and it disappears.
bindsym $mod+grave cheatsheet
bindsym $mod+Shift+grave cheatsheet

# Jump back to the previously focused window (any workspace)
bindsym $mod+o focus last

# Change focus (vim keys)
bindsym $mod+h focus left
bindsym $mod+j focus down
bindsym $mod+k focus up
bindsym $mod+l focus right

# Move the focused window
bindsym $mod+Shift+h move left
bindsym $mod+Shift+j move down
bindsym $mod+Shift+k move up
bindsym $mod+Shift+l move right

# Arrow keys are left unbound with Alt as $mod, because Alt+Left/Right is
# "back/forward" in browsers and Alt+Up/Down moves lines in editors. Uncomment
# them if you use the Win key as $mod:
# bindsym $mod+Left focus left
# bindsym $mod+Down focus down
# bindsym $mod+Up focus up
# bindsym $mod+Right focus right
# bindsym $mod+Shift+Left move left
# bindsym $mod+Shift+Down move down
# bindsym $mod+Shift+Up move up
# bindsym $mod+Shift+Right move right

# Split in horizontal / vertical orientation
bindsym $mod+b split h
bindsym $mod+v split v

bindsym $mod+f fullscreen toggle

# Container layout (stacked, tabbed, toggle split)
bindsym $mod+s layout stacking
bindsym $mod+w layout tabbed
bindsym $mod+e layout toggle split

# Floating
bindsym $mod+Shift+space floating toggle
bindsym $mod+space focus mode_toggle

# Focus the parent / child container
bindsym $mod+a focus parent
bindsym $mod+Shift+a focus child

# Scratchpad
bindsym $mod+Shift+minus move scratchpad
bindsym $mod+minus scratchpad show

# Make all windows on the workspace the same size (Mozaic extension)
bindsym $mod+Shift+b balance

# Workspaces
set $ws1 "1"
set $ws2 "2"
set $ws3 "3"
set $ws4 "4"
set $ws5 "5"
set $ws6 "6"
set $ws7 "7"
set $ws8 "8"
set $ws9 "9"
set $ws10 "10"

bindsym $mod+1 workspace number $ws1
bindsym $mod+2 workspace number $ws2
bindsym $mod+3 workspace number $ws3
bindsym $mod+4 workspace number $ws4
bindsym $mod+5 workspace number $ws5
bindsym $mod+6 workspace number $ws6
bindsym $mod+7 workspace number $ws7
bindsym $mod+8 workspace number $ws8
bindsym $mod+9 workspace number $ws9
bindsym $mod+0 workspace number $ws10

bindsym $mod+Shift+1 move container to workspace number $ws1
bindsym $mod+Shift+2 move container to workspace number $ws2
bindsym $mod+Shift+3 move container to workspace number $ws3
bindsym $mod+Shift+4 move container to workspace number $ws4
bindsym $mod+Shift+5 move container to workspace number $ws5
bindsym $mod+Shift+6 move container to workspace number $ws6
bindsym $mod+Shift+7 move container to workspace number $ws7
bindsym $mod+Shift+8 move container to workspace number $ws8
bindsym $mod+Shift+9 move container to workspace number $ws9
bindsym $mod+Shift+0 move container to workspace number $ws10

bindsym $mod+n workspace next_on_output
bindsym $mod+p workspace prev_on_output
bindsym $mod+Shift+n move workspace to output right
bindsym $mod+Shift+p move workspace to output left

# Reload the config / restart Mozaic in place (keeps the layout) / exit
bindsym $mod+Shift+c reload
bindsym $mod+Shift+r restart
bindsym $mod+Shift+e confirm "Exit Mozaic? Windows on hidden workspaces will be shown again." exit
# Exit Mozaic at once, without the question
bindsym $mod+Shift+x exit

# Resize mode
mode "resize" {
    bindsym h resize shrink width 10 px or 5 ppt
    bindsym j resize grow height 10 px or 5 ppt
    bindsym k resize shrink height 10 px or 5 ppt
    bindsym l resize grow width 10 px or 5 ppt

    bindsym Left resize shrink width 10 px or 5 ppt
    bindsym Down resize grow height 10 px or 5 ppt
    bindsym Up resize shrink height 10 px or 5 ppt
    bindsym Right resize grow width 10 px or 5 ppt

    bindsym Return mode "default"
    bindsym Escape mode "default"
    bindsym $mod+r mode "default"
}
bindsym $mod+r mode "resize"

# Gaps mode (i3-gaps style)
mode "gaps" {
    bindsym plus gaps inner current plus 2
    bindsym minus gaps inner current minus 2
    bindsym 0 gaps inner current set 0
    bindsym Shift+plus gaps outer current plus 2
    bindsym Shift+minus gaps outer current minus 2
    bindsym Return mode "default"
    bindsym Escape mode "default"
}
bindsym $mod+Shift+g mode "gaps"

# --- Bar ------------------------------------------------------------------------
bar {
    position top
    # Built-in status blocks: cpu memory battery disk datetime
    status_blocks "cpu memory battery datetime"
    datetime_format "ddd d MMM  HH:mm"
    # Or use any i3bar-compatible status program (plain text or JSON protocol):
    # status_command powershell -NoProfile -File C:\path\to\status.ps1
    show_title yes
    workspace_icons yes
    # Outline the icons of windows that share a tile
    workspace_groups yes

    colors {
        background #000000
        statusline #ffffff
        separator  #666666

        focused_workspace  #4c7899 #285577 #ffffff
        active_workspace   #333333 #5f676a #ffffff
        inactive_workspace #333333 #222222 #888888
        urgent_workspace   #2f343a #900000 #ffffff
        binding_mode       #2f343a #900000 #ffffff
    }
}

# Programs to start with Mozaic (exec = once, exec_always = also on reload)
# exec --no-startup-id some-program.exe
""";
}
