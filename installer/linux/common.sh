#!/usr/bin/env bash
set -euo pipefail
[[ $(id -u) != 0 ]] || { echo 'Run as the desktop user, without sudo.' >&2; exit 1; }
[[ ${HOME:-} == /* && $HOME != / && $HOME != *['"%\']* && $HOME != *$'\n'* ]] || { echo 'Unsupported HOME path.' >&2; exit 1; }
install_home=$(realpath -e -- "$HOME")
app="$install_home/.local/share/FaceCaptureAgent"
config="$install_home/.config/FaceCaptureAgent"
unit="$install_home/.config/systemd/user/face-capture-agent.service"
desktop="$install_home/.local/share/applications/face-capture-agent.desktop"
autostart="$install_home/.config/autostart/face-capture-agent.desktop"
marker='FaceCaptureAgent user installation v1'
assert_path() {
    [[ $(realpath -m -- "$1") == "$1" ]] || { echo "Refusing symlinked path: $1" >&2; exit 1; }
}
for path in "$app" "$config" "$unit" "$desktop" "$autostart"; do assert_path "$path"; done
import_desktop_environment() {
    local name
    local variables=()
    for name in DISPLAY WAYLAND_DISPLAY XAUTHORITY DBUS_SESSION_BUS_ADDRESS; do
        if [[ -v "$name" ]]; then variables+=("$name"); fi
    done
    if ((${#variables[@]})); then systemctl --user import-environment "${variables[@]}"; fi
}
assert_owned() {
    [[ -f "$app/.installation-owner" && ! -L "$app/.installation-owner" && $(cat "$app/.installation-owner") == "$marker" ]] || {
        echo "Refusing unmanaged installation: $app" >&2; exit 1;
    }
}
assert_managed_file() {
    [[ ! -e "$1" ]] || grep -qxF '# Managed by FaceCaptureAgent installer v1' "$1" || {
        echo "Refusing unmanaged file: $1" >&2; exit 1;
    }
}
