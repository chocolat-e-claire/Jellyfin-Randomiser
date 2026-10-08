#!/usr/bin/env bash
set -euo pipefail

ROOT="${1:-smoke-media}"

if ! command -v ffmpeg >/dev/null 2>&1; then
  sudo apt-get update
  sudo apt-get install -y ffmpeg
fi

make_video() {
  local path="$1"
  local color="$2"
  mkdir -p "$(dirname "$path")"
  ffmpeg -hide_banner -loglevel error -y \
    -f lavfi -i "color=c=${color}:s=320x180:d=1" \
    -f lavfi -i "anullsrc=r=48000:cl=mono" \
    -t 1 \
    -c:v libx264 -pix_fmt yuv420p \
    -c:a aac -shortest \
    -movflags +faststart \
    "$path"
}

rm -rf "$ROOT"
mkdir -p "$ROOT/allowed-movies/Allowed Movie 1"
mkdir -p "$ROOT/blocked-movies/Blocked Movie 1"
mkdir -p "$ROOT/allowed-shows/Test Show A/Season 01"
mkdir -p "$ROOT/allowed-shows/Test Show B/Season 01"

make_video "$ROOT/allowed-movies/Allowed Movie 1/Allowed Movie 1.mp4" blue
make_video "$ROOT/allowed-movies/Allowed Movie 2/Allowed Movie 2.mp4" cyan
make_video "$ROOT/blocked-movies/Blocked Movie 1/Blocked Movie 1.mp4" red
make_video "$ROOT/allowed-shows/Test Show A/Season 01/Test Show A - S01E01.mp4" green
make_video "$ROOT/allowed-shows/Test Show A/Season 01/Test Show A - S01E02.mp4" yellow
make_video "$ROOT/allowed-shows/Test Show B/Season 01/Test Show B - S01E01.mp4" purple
make_video "$ROOT/allowed-shows/Test Show B/Season 01/Test Show B - S01E02.mp4" orange

find "$ROOT" -type f -name '*.mp4' -print | sort
