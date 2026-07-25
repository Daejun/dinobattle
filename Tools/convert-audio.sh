#!/usr/bin/env bash
# Normalize and convert a sound effect for Unity. Run from WSL (needs ffmpeg).
#
# Unity imports WAV and OGG happily, but raw CC0 files arrive at wildly different loudness and
# sample rates. This levels them so a roar and a bite sit at the same perceived volume in-game.
#
# Usage:
#   ./Tools/convert-audio.sh input.wav Assets/Audio/SFX/
#   ./Tools/convert-audio.sh input.wav Assets/Audio/SFX/roar_trex.ogg
#   ./Tools/convert-audio.sh --batch .assets-cache/sounds/ Assets/Audio/SFX/
#
# Defaults: mono, 44.1 kHz, OGG q5, peak-normalized to -1 dBFS with silence trimmed.
# Mono is deliberate — these are 3D positional sounds, so Unity spatializes them anyway and a
# stereo source doubles the memory for nothing.

set -euo pipefail

command -v ffmpeg >/dev/null 2>&1 || {
  echo "ffmpeg not found. Install it:  sudo apt install -y ffmpeg" >&2
  exit 1
}

SAMPLE_RATE=44100
QUALITY=5
PEAK_DB=-1

convert_one() {
  local input="$1" output="$2"

  mkdir -p "$(dirname "$output")"

  ffmpeg -hide_banner -loglevel error -y \
    -i "$input" \
    -ac 1 -ar "$SAMPLE_RATE" \
    -af "silenceremove=start_periods=1:start_threshold=-50dB:start_silence=0.02,areverse,silenceremove=start_periods=1:start_threshold=-50dB:start_silence=0.02,areverse,loudnorm=I=-16:TP=${PEAK_DB}:LRA=11" \
    -c:a libvorbis -q:a "$QUALITY" \
    "$output"

  printf '  %-45s -> %s (%s)\n' "$(basename "$input")" "$output" "$(du -h "$output" | cut -f1)"
}

if [[ "${1:-}" == "--batch" ]]; then
  src="${2:?usage: --batch <src-dir> <dest-dir>}"
  dest="${3:?usage: --batch <src-dir> <dest-dir>}"

  [[ -d "$src" ]] || { echo "Not a directory: $src" >&2; exit 1; }

  count=0
  while IFS= read -r -d '' file; do
    base="$(basename "${file%.*}")"
    # Unity asset names: lowercase, underscores, no spaces.
    safe="$(echo "$base" | tr '[:upper:] ' '[:lower:]_' | tr -cd 'a-z0-9_-')"
    convert_one "$file" "${dest%/}/${safe}.ogg"
    (( ++count ))
  done < <(find "$src" -type f \( -iname '*.wav' -o -iname '*.mp3' -o -iname '*.flac' -o -iname '*.aiff' \) -print0)

  echo
  echo "Converted $count file(s) into ${dest%/}/"
  echo "Remember to record the sources in ATTRIBUTIONS.md."
  exit 0
fi

input="${1:?usage: convert-audio.sh <input> <output-dir-or-file>}"
target="${2:?usage: convert-audio.sh <input> <output-dir-or-file>}"

[[ -f "$input" ]] || { echo "No such file: $input" >&2; exit 1; }

if [[ "$target" == */ || -d "$target" ]]; then
  base="$(basename "${input%.*}")"
  safe="$(echo "$base" | tr '[:upper:] ' '[:lower:]_' | tr -cd 'a-z0-9_-')"
  output="${target%/}/${safe}.ogg"
else
  output="$target"
fi

convert_one "$input" "$output"
