# SCons options for HBHT's export template (scons profile=tools/godot-template/hbht.py),
# which tools/godot-template.ps1 builds from the pinned Godot's source on each platform.
# docs/systems/releases.md#the-engine says what each choice costs and saves.
#
# A platform's own defaults (d3d12 on Windows) only give way on the command line, so
# godot-template.ps1 passes those.

target = "template_release"
module_mono_enabled = "yes"
production = "yes"
optimize = "size"
lto = "full"
debug_symbols = "no"

# No physics, navigation or XR: bodies move by HBHT.Core's own collision. Only the modules
# go, not the disable_physics_3d family of options: those take methods off classes the apps
# use (Viewport.set_physics_object_picking, MeshInstance3D.create_trimesh_collision,
# RenderingServer.viewport_set_use_xr), and GodotSharp looks up every method of a class the
# first time the class is used, so the class fails whole. Without their modules the servers
# are Godot's dummies.
module_godot_physics_2d_enabled = "no"
module_godot_physics_3d_enabled = "no"
module_jolt_physics_enabled = "no"
module_vhacd_enabled = "no"
module_navigation_2d_enabled = "no"
module_navigation_3d_enabled = "no"
module_openxr_enabled = "no"
module_mobile_vr_enabled = "no"
module_webxr_enabled = "no"

# No ZIP packs or ZIPReader.
minizip = "no"
# WinRT is HDR output, the emoji picker and text-to-speech, none of which the apps use, and
# needs a newer Windows SDK than Visual Studio 2019's.
winrt = "no"

# The modules neither app uses. Kept: mono, gltf (actors and chunks are glTF), freetype and a
# text server (fonts), svg (the default theme's icons), webp (the project icon's import),
# glslang (Vulkan's shaders), meshoptimizer, bcdec, and astcenc, which ANGLE links against.
module_basis_universal_enabled = "no"
module_bmp_enabled = "no"
module_camera_enabled = "no"
module_csg_enabled = "no"
module_dds_enabled = "no"
module_enet_enabled = "no"
module_etcpak_enabled = "no"
module_fbx_enabled = "no"
module_gdscript_enabled = "no"
module_gridmap_enabled = "no"
module_hdr_enabled = "no"
module_interactive_music_enabled = "no"
module_jpg_enabled = "no"
module_jsonrpc_enabled = "no"
module_ktx_enabled = "no"
module_mbedtls_enabled = "no"
module_mp3_enabled = "no"
module_msdfgen_enabled = "no"
module_multiplayer_enabled = "no"
module_noise_enabled = "no"
module_ogg_enabled = "no"
module_raycast_enabled = "no"
module_regex_enabled = "no"
module_tga_enabled = "no"
module_theora_enabled = "no"
module_tinyexr_enabled = "no"
module_upnp_enabled = "no"
module_visual_shader_enabled = "no"
module_vorbis_enabled = "no"
module_webrtc_enabled = "no"
module_websocket_enabled = "no"
