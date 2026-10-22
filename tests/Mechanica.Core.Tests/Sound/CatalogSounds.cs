namespace Mechanica.Core.Tests.Sound;

/// <summary>
/// Every sound event id in CATALOG_IDS.md ("Sound events (complete core set)"), with the material and bot
/// patterns expanded. Vanilla must register each one (chunk 16).
/// </summary>
internal static class CatalogSounds
{
    public static readonly IReadOnlyList<string> Ids =
    [
        "place_wood", "break_wood", "impact_wood", "place_metal", "break_metal", "impact_metal", "place_concrete",
        "break_concrete", "impact_concrete", "place_glass", "break_glass", "impact_glass", "place_scrap", "break_scrap",
        "impact_scrap", "place_plastic", "break_plastic", "impact_plastic", "place_tile", "break_tile", "impact_tile",
        "place_cardboard", "break_cardboard", "impact_cardboard", "place_fabric", "break_fabric", "impact_fabric",
        "burn_wood", "vehicle_crash", "impact_soft", "part_attach", "part_detach", "remove_block",
        "lift_place", "lift_raise", "lift_lower", "lift_motor", "lift_capture", "lift_release", "lift_deny", "connect_success", "connect_fail", "disconnect", "weld_start",
        "weld_complete", "paint_spray", "paint_erase", "smash_hit", "smash_break", "sledgehammer_swing", "engine_start", "engine_idle",
        "engine_rev", "engine_sputter", "electric_start", "electric_whirr", "battery_insert", "battery_drain",
        "fuel_pour", "bearing_start", "bearing_spin_loop", "bearing_stop", "piston_extend", "piston_retract", "piston_stall",
        "suspension_compress", "suspension_extend", "thruster_ignite", "thruster_loop", "thruster_cut", "rotor_spin_up",
        "rotor_loop", "rotor_spin_down", "hover_ignite", "hover_hum", "hover_cut", "air_whoosh", "wing_stall",
        "surface_move", "surface_lock", "seat_enter", "seat_exit", "seat_click", "scrap_seat_enter", "scrap_seat_exit",
        "scrap_seat_creak", "controller_start", "controller_tick",
        "controller_stop", "logic_click", "logic_activate", "timer_tick", "timer_complete", "sensor_detect", "sensor_lose",
        "sensor_ping", "switch_toggle", "button_press", "horn_honk", "saw_spin", "saw_cut", "drill_spin", "drill_hit",
        "drill_break", "water_cannon_fire", "birch_hit", "birch_fall", "leafy_hit", "leafy_fall", "spruce_hit", "spruce_fall",
        "pine_hit", "pine_fall", "chest_place", "chest_open", "chest_close", "container_open", "fabricator_start",
        "fabricator_work", "fabricator_complete", "refiner_start", "refiner_work", "refiner_complete", "bed_use",
        "bed_set", "radio_on", "radio_tune", "radio_off", "light_on", "light_off", "beacon_activate", "beacon_ping",
        "pickup_metal", "drop_metal", "pickup_wood", "drop_wood", "pickup_component", "drop_component", "pickup_fuel",
        "pickup_battery", "pickup_produce", "seed_place", "seed_pickup", "eat_crunch", "eat_soft", "blaster_fire",
        "blaster_reload", "scattergun_fire", "scattergun_pump", "launcher_fire", "popper_explode", "bucket_fill", "bucket_throw",
        "water_splash", "scuttler_idle",
        "scuttler_alert", "scuttler_attack", "scuttler_death", "rakehand_idle", "rakehand_alert", "rakehand_attack",
        "rakehand_death", "spooler_idle", "spooler_alert", "spooler_attack", "spooler_death", "thresher_idle",
        "thresher_alert", "thresher_attack", "thresher_death", "grazer_idle", "grazer_alert", "grazer_attack",
        "grazer_death", "scuttler_charge", "scuttler_stomp", "scuttler_explode", "rakehand_chase", "spooler_fire",
        "player_hurt", "player_heal", "heartbeat_low", "footstep_default", "footstep_metal", "footstep_wood",
        "footstep_water", "player_jump", "player_land", "player_land_hard", "swim_stroke", "water_enter", "water_exit", "tool_equip",
        "ui_click", "ui_hover",
        "ui_open", "ui_close", "ui_success", "ui_error", "inventory_open", "inventory_close", "inventory_full",
        "craft_start", "craft_complete", "raid_horn", "wind_light", "wind_strong", "birds_day", "night_insects",
        "water_lap", "machinery_hum", "music_explore", "music_raid", "music_menu", "radio_loop_01", "radio_loop_02",
        "radio_loop_03", "radio_loop_04", "radio_loop_05", "radio_loop_06",
        "alarm_clock_bell", "alarm_clock_beep", "alarm_clock_chime", "alarm_clock_set", "scuttler_head_blip",
        "scuttler_head_bass", "scuttler_head_drum", "scuttler_head_voice", "cash_register_open", "cash_register_close",
        "canister_fuse", "canister_explode", "plasma_drill_start", "plasma_drill_loop", "plasma_drill_stop",
        "sticky_wheel_pump", "sticky_wheel_loop", "freezer_start", "freezer_work", "freezer_complete", "extinguisher_spray",
    ];
}
