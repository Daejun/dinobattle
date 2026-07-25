#!/usr/bin/env bash
# Fetch CC0 assets for the project. Run from WSL.
#
# Nothing is downloaded until you pass an explicit target — this script will not silently pull
# hundreds of megabytes into the repo. Every source here was license-verified; see Docs/assets.md.
#
# Usage:
#   ./Tools/fetch-assets.sh --list        # show what is available, download nothing
#   ./Tools/fetch-assets.sh --sounds      # clone the CC0 sound library into .assets-cache/
#   ./Tools/fetch-assets.sh --models      # print the manual steps for the Quaternius packs
#   ./Tools/fetch-assets.sh --deps        # check for git-lfs / ffmpeg / blender
#
# Downloads land in .assets-cache/ (gitignored). You then copy only the files you actually use
# into Assets/, and record them in ATTRIBUTIONS.md.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$(dirname "$SCRIPT_DIR")"
CACHE_DIR="$PROJECT_DIR/.assets-cache"

SOUNDS_REPO="https://github.com/lavenderdotpet/CC0-Public-Domain-Sounds"

bold() { printf '\033[1m%s\033[0m\n' "$1"; }
warn() { printf '\033[33m%s\033[0m\n' "$1" >&2; }

show_list() {
  bold "Models + animations (manual download — no stable direct URL)"
  cat <<'EOF'
  Quaternius / Animated Dinosaur Pack        CC0   6 dinosaurs, idle+walk+run+attack+death+jump
    https://quaternius.com/packs/animateddinosaurs.html
    https://quaternius.itch.io/animated-lowpoly-dinosaurs

  Quaternius / Ultimate Animated Animals     CC0   12 animals, 12+ animations each
    https://quaternius.com/packs/ultimateanimatedanimals.html

  OpenGameArt / CC0 3D Animals & Creatures   CC0   Dromaeosaur, Brontosaurus, misc
    https://opengameart.org/content/cc0-3d-animals-creatures

EOF
  bold "Sounds (scriptable)"
  cat <<EOF
  CC0-Public-Domain-Sounds                   CC0   creature SFX, hit/impact SFX, RPG SFX
    $SOUNDS_REPO
    -> ./Tools/fetch-assets.sh --sounds

  OpenGameArt / CC0 Deep Monster Roar        CC0
    https://opengameart.org/content/cc0-deep-monster-roar

EOF
  bold "Rejected licenses"
  echo "  CC-BY-SA (copyleft spreads to your game), CC-NC (no ads/paid), CC-ND. See Docs/legal.md."
}

fetch_sounds() {
  mkdir -p "$CACHE_DIR"
  local dest="$CACHE_DIR/CC0-Public-Domain-Sounds"

  bold "Cloning $SOUNDS_REPO"
  echo "  destination: $dest"
  echo "  license    : CC0 1.0 Universal"
  echo
  read -r -p "This downloads a few hundred MB. Continue? [y/N] " reply
  [[ "$reply" =~ ^[Yy]$ ]] || { echo "Aborted."; return 0; }

  if [[ -d "$dest/.git" ]]; then
    git -C "$dest" pull --ff-only
  else
    git clone --depth 1 "$SOUNDS_REPO" "$dest"
  fi

  echo
  bold "Folders relevant to this project:"
  find "$dest" -maxdepth 1 -type d \
    \( -iname '*creature*' -o -iname '*beast*' -o -iname '*animal*' \
       -o -iname '*hit*' -o -iname '*rpg*' \) \
    -printf '  %f\n' 2>/dev/null || true

  cat <<'EOF'

Next steps:
  1. Audition the files and pick the few you want (roar, bite, impact, death).
  2. Convert them: ./Tools/convert-audio.sh <file.wav> Assets/Audio/SFX/
  3. Record each one in ATTRIBUTIONS.md.

Do NOT copy the whole clone into Assets/ — Unity would import every file and the repo would balloon.
EOF
}

show_models() {
  cat <<'EOF'
Quaternius packs are behind a website download button, so there is no direct URL to curl.

  1. Open https://quaternius.com/packs/animateddinosaurs.html
  2. Download the FBX archive
  3. Extract to .assets-cache/quaternius-dinosaurs/
  4. Copy the .fbx files into Assets/Art/Models/
  5. In Unity: select each FBX -> Inspector -> Rig -> Animation Type = Generic, assign Root node
  6. Follow the prefab-swap steps in Docs/assets.md section 4

License: CC0 — free for commercial use, attribution not required (record it anyway in ATTRIBUTIONS.md).
EOF
}

check_deps() {
  local missing=0
  for tool in git git-lfs ffmpeg blender; do
    if command -v "$tool" >/dev/null 2>&1; then
      printf '  \033[32m%-10s\033[0m %s\n' "$tool" "$(command -v "$tool")"
    else
      printf '  \033[31m%-10s\033[0m missing\n' "$tool"
      missing=1
    fi
  done

  if (( missing )); then
    echo
    warn "Install the missing tools:"
    cat >&2 <<'EOF'
  sudo apt update
  sudo apt install -y git git-lfs ffmpeg
  # Blender is only needed if you plan to retarget or author animations:
  sudo snap install blender --classic
EOF
  fi
}

case "${1:-}" in
  --list|"")  show_list ;;
  --sounds)   fetch_sounds ;;
  --models)   show_models ;;
  --deps)     check_deps ;;
  -h|--help)  sed -n '2,17p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//' ;;
  *)          echo "Unknown option: $1" >&2; echo "Try --list" >&2; exit 2 ;;
esac
