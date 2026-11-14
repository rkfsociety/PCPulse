#!/usr/bin/env bash
# Install a portable PCPulse Linux release for the current user, without root.
set -euo pipefail

SOURCE_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
if [[ ! -f "$SOURCE_DIR/PCPulse" || ! -f "$SOURCE_DIR/PCPulse.png" ]]; then
  echo "Запустите скрипт из распакованного каталога релиза PCPulse для Linux." >&2
  exit 1
fi

DATA_HOME="${XDG_DATA_HOME:-$HOME/.local/share}"
APP_DIR="$DATA_HOME/pcpulse-app"
DESKTOP_DIR="$DATA_HOME/applications"
mkdir -p "$APP_DIR" "$DESKTOP_DIR"

install -m 755 "$SOURCE_DIR/PCPulse" "$APP_DIR/PCPulse"
install -m 644 "$SOURCE_DIR/PCPulse.png" "$APP_DIR/PCPulse.png"
if [[ -f "$SOURCE_DIR/install.sh" ]]; then
  install -m 755 "$SOURCE_DIR/install.sh" "$APP_DIR/install.sh"
fi

cat > "$DESKTOP_DIR/pcpulse.desktop" <<DESKTOP
[Desktop Entry]
Type=Application
Name=PCPulse
Comment=Единый центр модулей экосистемы *Pulse
Exec="$APP_DIR/PCPulse"
Icon=$APP_DIR/PCPulse.png
Terminal=false
Categories=System;Utility;
DESKTOP
chmod 644 "$DESKTOP_DIR/pcpulse.desktop"

echo "PCPulse установлен: $APP_DIR"
echo "Ярлык меню: $DESKTOP_DIR/pcpulse.desktop"
