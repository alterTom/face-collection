#!/usr/bin/env bash
set -euo pipefail
source "$(dirname -- "${BASH_SOURCE[0]}")/common.sh"
assert_owned
assert_managed_file "$unit"
assert_managed_file "$desktop"
assert_managed_file "$autostart"
systemctl --user stop face-capture-agent.service
rm -f -- "$unit" "$desktop" "$autostart"
systemctl --user daemon-reload
assert_path "$app"
assert_owned
rm -rf -- "$app"
echo "Removed this user's installation. Configuration retained at: $config"
