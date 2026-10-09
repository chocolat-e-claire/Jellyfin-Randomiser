#!/usr/bin/env bash
set -euo pipefail

ROOT="${1:-smoke-media}"
ROOT_ABS="$(pwd)/${ROOT}"
FFMPEG_IMAGE="${FFMPEG_IMAGE:-jellyfin/jellyfin:10.10.7}"

USE_CONTAINER_FFMPEG=false
if command -v ffmpeg >/dev/null 2>&1; then
  FFMPEG_COMMAND=(ffmpeg)
else
  if ! command -v docker >/dev/null 2>&1; then
    echo "ffmpeg is unavailable and Docker is not installed." >&2
    exit 1
  fi
  USE_CONTAINER_FFMPEG=true
fi

make_video() {
  local path="$1"
  local color="$2"
  local relative="${path#"${ROOT}/"}"

  mkdir -p "$(dirname "$path")"

  if [ "$USE_CONTAINER_FFMPEG" = false ]; then
    "${FFMPEG_COMMAND[@]}" -hide_banner -loglevel error -y       -f lavfi -i "color=c=${color}:s=320x180:d=1"       -f lavfi -i "anullsrc=r=48000:cl=mono"       -t 1       -c:v libx264 -pix_fmt yuv420p       -c:a aac -shortest       -movflags +faststart       "$path"
    return
  fi

  docker run --rm     --entrypoint /usr/lib/jellyfin-ffmpeg/ffmpeg     -v "${ROOT_ABS}:/out"     "${FFMPEG_IMAGE}"     -hide_banner -loglevel error -y     -f lavfi -i "color=c=${color}:s=320x180:d=1"     -f lavfi -i "anullsrc=r=48000:cl=mono"     -t 1     -c:v libx264 -pix_fmt yuv420p     -c:a aac -shortest     -movflags +faststart     "/out/${relative}"
}

rm -rf "$ROOT"
mkdir -p "$ROOT/allowed-movies/Allowed Movie 1"
mkdir -p "$ROOT/blocked-movies/Blocked Movie 1"
mkdir -p "$ROOT/allowed-shows/Test Show A/Season 01"
mkdir -p "$ROOT/allowed-shows/Test Show B/Season 01"

make_video "$ROOT/allowed-movies/Allowed Movie 1/Allowed Movie 1.mp4" blue
make_video "$ROOT/allowed-movies/Allowed Movie 2/Allowed Movie 2.mp4" cyan
cat > "$ROOT/allowed-movies/Allowed Movie 1/Allowed Movie 1.nfo" <<'EOF'
<?xml version="1.0" encoding="utf-8" standalone="yes"?>
<movie>
  <title>Allowed Movie 1</title>
  <genre>Comedy</genre>
</movie>
EOF
cat > "$ROOT/allowed-movies/Allowed Movie 2/Allowed Movie 2.nfo" <<'EOF'
<?xml version="1.0" encoding="utf-8" standalone="yes"?>
<movie>
  <title>Allowed Movie 2</title>
  <genre>Comedy</genre>
  <genre>Drama</genre>
</movie>
EOF
make_video "$ROOT/blocked-movies/Blocked Movie 1/Blocked Movie 1.mp4" red
make_video "$ROOT/allowed-shows/Test Show A/Season 01/Test Show A - S01E01.mp4" green
make_video "$ROOT/allowed-shows/Test Show A/Season 01/Test Show A - S01E02.mp4" yellow
make_video "$ROOT/allowed-shows/Test Show B/Season 01/Test Show B - S01E01.mp4" purple
make_video "$ROOT/allowed-shows/Test Show B/Season 01/Test Show B - S01E02.mp4" orange

cat > "$ROOT/allowed-shows/Test Show A/tvshow.nfo" <<'EOF'
<?xml version="1.0" encoding="utf-8" standalone="yes"?>
<tvshow>
  <title>Test Show A</title>
  <genre>Comedy</genre>
</tvshow>
EOF
cat > "$ROOT/allowed-shows/Test Show B/tvshow.nfo" <<'EOF'
<?xml version="1.0" encoding="utf-8" standalone="yes"?>
<tvshow>
  <title>Test Show B</title>
  <genre>Comedy</genre>
  <genre>Drama</genre>
</tvshow>
EOF

find "$ROOT" -type f -name '*.mp4' -print | sort
