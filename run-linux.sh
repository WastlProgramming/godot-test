#!/usr/bin/env bash
# Startet das Spiel unter Linux/WSL auf der Grafikkarte (OpenGL über Mesa-D3D12, X11).
cd "$(dirname "$0")"
export GALLIUM_DRIVER=d3d12
exec .tools/godot/Godot_v4.6.3-stable_mono_linux.x86_64 --path . --display-driver x11 --rendering-driver opengl3 --maximized "$@"
