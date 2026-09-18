#!/usr/bin/env bash
set -euo pipefail
source "$(dirname -- "${BASH_SOURCE[0]}")/common.sh"
assert_owned
import_desktop_environment
systemctl --user start face-capture-agent.service
if [[ ${1:-} == --background ]]; then exit 0; fi
port=$(sed -nE 's/^[[:space:]]*listen_port[[:space:]]*=[[:space:]]*([0-9]+)[[:space:]]*(#.*)?$/\1/p' "$config/config.toml")
[[ "$port" =~ ^[0-9]{1,5}$ ]] && ((10#$port >= 1 && 10#$port <= 65535)) || { echo 'Invalid listen_port in config.toml.' >&2; exit 1; }
url="http://127.0.0.1:$port/test/"
for ((attempt=0; attempt<40; attempt++)); do
    if curl --silent --fail --max-time 1 --output /dev/null "$url"; then exec xdg-open "$url"; fi
    sleep 0.25
done
echo 'Agent did not become ready; inspect systemctl --user status face-capture-agent.service.' >&2
exit 1
