#!/usr/bin/env bash
# Filesystem fixture only: never contacts a real user service or runs the Agent.
set -euo pipefail
script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)
fixture=$(mktemp -d "${TMPDIR:-/tmp}/face-capture-test.XXXXXXXX")
trap '[[ "$fixture" == "${TMPDIR:-/tmp}/face-capture-test."* ]] && rm -rf -- "$fixture"' EXIT
test_home="$fixture/home"
mkdir -p "$test_home" "$fixture/bin" "$fixture/release/Models" "$fixture/release/installer"
export PATH="$fixture/bin:$PATH"
printf '#!/usr/bin/env bash\nexit 0\n' > "$fixture/bin/systemctl"
printf '#!/usr/bin/env bash\necho 1000\n' > "$fixture/bin/id"
chmod +x "$fixture/bin/"*
cp "$script_dir/"*.sh "$fixture/release/installer/"
for file in FaceCaptureAgent.Linux libOpenCvSharpExtern.so Models/face_detection_yunet_2023mar.onnx Models/face_mesh_Nx3x192x192.onnx; do
    printf 'fixture\n' > "$fixture/release/$file"
done
printf 'listen_port = 17653\n' > "$fixture/release/config.toml"
env HOME="$test_home" bash "$fixture/release/installer/install.sh"
printf 'listen_port = 19000\n' > "$test_home/.config/FaceCaptureAgent/config.toml"
env HOME="$test_home" bash "$fixture/release/installer/install.sh"
grep -qx 'listen_port = 19000' "$test_home/.config/FaceCaptureAgent/config.toml"
grep -qx 'StandardOutput=null' "$test_home/.config/systemd/user/face-capture-agent.service"
grep -q -- '--background' "$test_home/.config/autostart/face-capture-agent.desktop"
env HOME="$test_home" bash "$test_home/.local/share/FaceCaptureAgent/installer/launch.sh" --background
if env HOME="$test_home" bash "$fixture/release/installer/install.sh" "$test_home/.local/share/FaceCaptureAgent"; then echo 'Installed source accepted' >&2; exit 1; fi
env HOME="$test_home" bash "$test_home/.local/share/FaceCaptureAgent/installer/uninstall.sh"
[[ ! -e "$test_home/.local/share/FaceCaptureAgent" ]]
[[ ! -e "$test_home/.config/systemd/user/face-capture-agent.service" ]]
[[ ! -e "$test_home/.config/autostart/face-capture-agent.desktop" ]]
grep -qx 'listen_port = 19000' "$test_home/.config/FaceCaptureAgent/config.toml"
printf 'unrelated\n' > "$test_home/.config/autostart/face-capture-agent.desktop"
if env HOME="$test_home" bash "$fixture/release/installer/install.sh"; then echo 'Unmanaged autostart accepted' >&2; exit 1; fi
grep -qx 'unrelated' "$test_home/.config/autostart/face-capture-agent.desktop"
rm -- "$test_home/.config/autostart/face-capture-agent.desktop"
mkdir "$fixture/unrelated"
ln -s "$fixture/unrelated" "$test_home/.local/share/FaceCaptureAgent"
if [[ -L "$test_home/.local/share/FaceCaptureAgent" ]]; then
    if env HOME="$test_home" bash "$fixture/release/installer/install.sh"; then echo 'Symlink installation accepted' >&2; exit 1; fi
    [[ -d "$fixture/unrelated" ]]
    rm -- "$test_home/.local/share/FaceCaptureAgent"
else
    echo 'SKIP symlink refusal: this shell copied the directory instead of creating a symbolic link.'
    # The fixture source is empty: rmdir refuses unexpected contents, never recurses.
    rmdir -- "$test_home/.local/share/FaceCaptureAgent"
fi
mkdir -p "$test_home/.local/share/FaceCaptureAgent"
printf 'unrelated\n' > "$test_home/.local/share/FaceCaptureAgent/keep.txt"
if env HOME="$test_home" bash "$fixture/release/installer/install.sh"; then echo 'Unmanaged directory accepted' >&2; exit 1; fi
[[ -f "$test_home/.local/share/FaceCaptureAgent/keep.txt" ]]
echo 'Installer fixture tests: PASS (systemctl mocked; no Linux runtime validation)'
