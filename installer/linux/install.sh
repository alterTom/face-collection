#!/usr/bin/env bash
set -euo pipefail
source "$(dirname -- "${BASH_SOURCE[0]}")/common.sh"
source_dir=$(realpath -e -- "${1:-$(dirname -- "${BASH_SOURCE[0]}")/..}")
[[ "$source_dir" != "$app" && "$source_dir" != "$app/"* ]] || { echo 'Install from an extracted release directory outside the installation.' >&2; exit 1; }
for file in FaceCaptureAgent.Linux config.toml libOpenCvSharpExtern.so Models/face_detection_yunet_2023mar.onnx Models/face_mesh_Nx3x192x192.onnx installer/common.sh installer/launch.sh installer/uninstall.sh; do
    [[ -s "$source_dir/$file" ]] || { echo "Missing release file: $file" >&2; exit 1; }
done
assert_managed_file "$unit"
assert_managed_file "$desktop"
assert_managed_file "$autostart"
if [[ -e "$app" ]]; then assert_owned; fi
systemctl --user show-environment >/dev/null
mkdir -p -- "$(dirname -- "$app")" "$config" "$(dirname -- "$unit")" "$(dirname -- "$desktop")" "$(dirname -- "$autostart")"
stage=$(mktemp -d "$install_home/.local/share/.FaceCaptureAgent-stage.XXXXXXXX")
trap 'if [[ -n ${stage:-} && $stage == "$install_home/.local/share/.FaceCaptureAgent-stage."* ]]; then rm -rf -- "$stage"; fi' EXIT
cp -a -- "$source_dir/." "$stage/"
printf '%s\n' "$marker" > "$stage/.installation-owner"
chmod 700 "$stage/FaceCaptureAgent.Linux" "$stage/installer/launch.sh"
if [[ ! -e "$config/config.toml" ]]; then
    install -m 600 "$source_dir/config.toml" "$config/config.toml"
fi
if [[ -e "$app" ]]; then
    systemctl --user stop face-capture-agent.service
    assert_owned
    rm -rf -- "$app"
fi
mv -- "$stage" "$app"
stage=''
cat > "$unit" <<'UNIT'
# Managed by FaceCaptureAgent installer v1
[Unit]
Description=Face capture local agent
PartOf=graphical-session.target
[Service]
Type=simple
WorkingDirectory=%h/.local/share/FaceCaptureAgent
ExecStart="%h/.local/share/FaceCaptureAgent/FaceCaptureAgent.Linux" --config "%h/.config/FaceCaptureAgent/config.toml"
Restart=on-failure
RestartSec=5
StandardOutput=null
StandardError=null
UMask=0077
UNIT
cat > "$desktop" <<DESKTOP
# Managed by FaceCaptureAgent installer v1
[Desktop Entry]
Type=Application
Name=刷脸认证
Comment=Start the local agent and open its test page
Exec="$app/installer/launch.sh"
Terminal=false
Categories=Utility;
DESKTOP
cat > "$autostart" <<DESKTOP
# Managed by FaceCaptureAgent installer v1
[Desktop Entry]
Type=Application
Name=刷脸认证
Exec="$app/installer/launch.sh" --background
Terminal=false
X-GNOME-Autostart-enabled=true
DESKTOP
systemctl --user daemon-reload
import_desktop_environment
systemctl --user start face-capture-agent.service
echo "Installed for this user: $app"
echo "Configuration preserved at: $config/config.toml"
