#!/usr/bin/env bash
set -euo pipefail

PID_TO_WAIT="${1:-}"
PACKAGE_PATH="${2:-}"
TARGET_DIR="${3:-}"
LOG_PATH="${4:-}"

if [[ -z "$PID_TO_WAIT" || -z "$PACKAGE_PATH" || -z "$TARGET_DIR" ]]; then
  echo "Usage: apply-update.sh <pid> <package.tar.gz> <target_dir> [log_path]" >&2
  exit 1
fi

while kill -0 "$PID_TO_WAIT" 2>/dev/null; do
  sleep 0.2
done

STAGE_DIR="$(mktemp -d)"
cleanup() {
  rm -rf "$STAGE_DIR"
  rm -f "$PACKAGE_PATH"
}
trap cleanup EXIT

if ! tar -xzf "$PACKAGE_PATH" -C "$STAGE_DIR"; then
  if [[ -n "$LOG_PATH" ]]; then
    mkdir -p "$(dirname "$LOG_PATH")"
    printf '[%s] [ERROR] [updater] Failed to extract %s\n' "$(date -Is)" "$PACKAGE_PATH" >> "$LOG_PATH" || true
  fi
  exit 1
fi

mkdir -p "$TARGET_DIR"
cp -a "$STAGE_DIR"/. "$TARGET_DIR"/
if [[ -f "$TARGET_DIR/PCPulse" ]]; then
  chmod 755 "$TARGET_DIR/PCPulse"
fi

if [[ -f "$TARGET_DIR/PCPulse" ]]; then
  nohup "$TARGET_DIR/PCPulse" >/dev/null 2>&1 &
fi
