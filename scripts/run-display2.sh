#!/usr/bin/env bash
#
# Launch MdNotes on the :2 test display with a usable xdg-desktop-portal.
#
# Why the private bus: the portal is what draws the "Open vault" folder picker and
# what answers "does this desktop prefer dark?" for ThemeMode.System. The desktop
# session's own portal was started with DISPLAY=:0, so on :2 the picker is drawn on
# the real desktop - from :2 it looks as if Ctrl+Shift+O did nothing at all.
# Unsetting DBUS_SESSION_BUS_ADDRESS avoids that, but it also removes the only
# channel the dark-mode preference travels over, so System silently becomes Light.
#
# A private session bus started while DISPLAY is already :2 fixes both: D-Bus
# activates the portal on demand and that portal inherits DISPLAY=:2, so the picker
# and the appearance setting both land on the test display.
#
# Why the portal is warmed first: Avalonia reads the colour scheme once, during
# startup, and does not re-read it when the portal appears later. First activation
# of xdg-desktop-portal takes tens of seconds here (it waits out a secrets-service
# timeout), so an app started at the same moment wins the race and stays Light for
# the rest of its life. Warming the portal first makes System resolve to Dark.
#
# Usage: scripts/run-display2.sh [file.md]
#   TEST_DISPLAY=:3           use another display
#   MDNOTES_CONFIG_HOME=DIR   use a throwaway settings/vault config instead of the real one
#   SKIP_BUILD=1              run the existing build

set -euo pipefail

repo=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
display=${TEST_DISPLAY:-:2}
dotnet=${DOTNET:-$([ -x "$HOME/.dotnet/dotnet" ] && echo "$HOME/.dotnet/dotnet" || echo dotnet)}

if ! DISPLAY=$display xdpyinfo >/dev/null 2>&1; then
    echo "error: display $display is not available (start Xephyr/Xvfb on it first)" >&2
    exit 1
fi

[ "${SKIP_BUILD:-}" = 1 ] || "$dotnet" build "$repo/MdNotes/MdNotes.csproj" -c Debug

app=$repo/MdNotes/bin/Debug/net10.0/MdNotes.dll
[ -f "$app" ] || { echo "error: $app not found" >&2; exit 1; }

export DISPLAY=$display

bus_pid=
cleanup() {
    # The portals were activated by this bus and exit with it; killing the bus leaves
    # the real :0 session's own portal untouched.
    [ -n "$bus_pid" ] && kill "$bus_pid" 2>/dev/null
}
trap cleanup EXIT

bus_info=$(dbus-daemon --session --fork --print-address=1 --print-pid=1)
DBUS_SESSION_BUS_ADDRESS=$(printf '%s\n' "$bus_info" | sed -n 1p)
bus_pid=$(printf '%s\n' "$bus_info" | sed -n 2p)
export DBUS_SESSION_BUS_ADDRESS

echo "warming xdg-desktop-portal on $display (first activation is slow)..."
if ! timeout 120 gdbus call --session \
        --dest org.freedesktop.portal.Desktop \
        --object-path /org/freedesktop/portal/desktop \
        --method org.freedesktop.portal.Settings.Read \
        org.freedesktop.appearance color-scheme; then
    echo "warning: portal did not answer; the folder picker and ThemeMode.System may misbehave" >&2
fi

# MDNOTES_CONFIG_HOME is applied to MdNotes alone, not exported above: XDG_CONFIG_HOME
# is also where dconf keeps the user database the portal reads the GTK theme from, so
# pointing the whole environment at a throwaway directory makes the portal report "no
# preference" and ThemeMode.System comes out Light for a reason that is not the app's.
# Not exec'd, so the EXIT trap still runs and takes the bus (and its portals) down.
if [ -n "${MDNOTES_CONFIG_HOME:-}" ]; then
    XDG_CONFIG_HOME=$MDNOTES_CONFIG_HOME "$dotnet" "$app" "$@"
else
    "$dotnet" "$app" "$@"
fi
