#!/usr/bin/env bash
# Build the Android player headlessly from WSL (or native Linux).
#
# The Unity Editor lives on Windows; WSL can launch Windows executables directly, so this script
# calls Unity.exe through the interop layer and translates paths with wslpath. If a native Linux
# Unity is installed inside WSL, that is used instead.
#
# Usage:
#   ./Tools/build-android.sh              # APK
#   ./Tools/build-android.sh --aab        # Play Store bundle
#   UNITY_PATH=/mnt/c/.../Unity.exe ./Tools/build-android.sh
#
# Configure the editor path once in Tools/local.build.props (gitignored):
#   UnityPath=C:\Program Files\Unity\Hub\Editor\6000.5.0f1\Editor\Unity.exe

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$(dirname "$SCRIPT_DIR")"

BUILD_METHOD="DinoBattle.EditorTools.AndroidBuilder.BuildApk"
for arg in "$@"; do
  case "$arg" in
    --aab) BUILD_METHOD="DinoBattle.EditorTools.AndroidBuilder.BuildAab" ;;
    --apk) BUILD_METHOD="DinoBattle.EditorTools.AndroidBuilder.BuildApk" ;;
    -h|--help) sed -n '2,16p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "Unknown argument: $arg" >&2; exit 2 ;;
  esac
done

is_wsl() { [[ -n "${WSL_DISTRO_NAME:-}" ]] || grep -qi microsoft /proc/version 2>/dev/null; }

# ---------------------------------------------------------------- locate the editor
resolve_unity() {
  if [[ -n "${UNITY_PATH:-}" ]]; then echo "$UNITY_PATH"; return; fi

  local props="$SCRIPT_DIR/local.build.props"
  if [[ -f "$props" ]]; then
    local configured
    configured="$(grep -E '^UnityPath=' "$props" | head -n1 | cut -d= -f2- | tr -d '"' | tr -d '\r')"
    if [[ -n "$configured" ]]; then
      # The props file holds a Windows path so PowerShell and bash can share one config file.
      if is_wsl && [[ "$configured" == [A-Za-z]:* ]]; then
        wslpath -u "$configured"
      else
        echo "$configured"
      fi
      return
    fi
  fi

  # Native Linux editor inside WSL, if the user installed one.
  local linux_hub="$HOME/Unity/Hub/Editor"
  if [[ -d "$linux_hub" ]]; then
    local found
    found="$(find "$linux_hub" -maxdepth 3 -name Unity -type f -perm -u+x 2>/dev/null | sort -r | head -n1)"
    [[ -n "$found" ]] && { echo "$found"; return; }
  fi

  # Windows editor via interop — the normal path for this project.
  if is_wsl; then
    local hub="/mnt/c/Program Files/Unity/Hub/Editor"
    if [[ -d "$hub" ]]; then
      local exe
      exe="$(find "$hub" -maxdepth 3 -name 'Unity.exe' 2>/dev/null | sort -r | head -n1)"
      [[ -n "$exe" ]] && { echo "$exe"; return; }
    fi
  fi

  echo ""
}

UNITY="$(resolve_unity)"

if [[ -z "$UNITY" || ! -f "$UNITY" ]]; then
  cat >&2 <<'EOF'
Unity editor not found.

Fix it one of these ways:
  1. Create Tools/local.build.props containing the Windows path:
       UnityPath=C:\Program Files\Unity\Hub\Editor\<version>\Editor\Unity.exe
  2. Export UNITY_PATH before running this script.

Install Unity 6.5 (6000.5.x) with the "Android Build Support" module via Unity Hub on Windows first.
See Docs/setup.md.
EOF
  exit 1
fi

# ---------------------------------------------------------------- path translation
IS_WINDOWS_EDITOR=0
[[ "$UNITY" == *.exe ]] && IS_WINDOWS_EDITOR=1

LOG_DIR="$PROJECT_DIR/Logs"
mkdir -p "$LOG_DIR"
LOG_FILE="$LOG_DIR/android-build.log"

if (( IS_WINDOWS_EDITOR )); then
  # Unity.exe is a Windows process and cannot read /mnt/... or \\wsl$\... reliably.
  ARG_PROJECT="$(wslpath -w "$PROJECT_DIR")"
  ARG_LOG="$(wslpath -w "$LOG_FILE")"

  if [[ "$ARG_PROJECT" == '\\wsl'* ]]; then
    cat >&2 <<EOF
The project lives inside the WSL filesystem ($PROJECT_DIR), which maps to a UNC path:
  $ARG_PROJECT

The Windows Unity editor does not build reliably from UNC paths. Either move the project onto
the Windows filesystem (e.g. /mnt/c/Users/<you>/dino_battle) or install a native Linux Unity.
EOF
    exit 1
  fi
else
  ARG_PROJECT="$PROJECT_DIR"
  ARG_LOG="$LOG_FILE"
fi

echo "Editor : $UNITY"
echo "Method : $BUILD_METHOD"
echo "Project: $ARG_PROJECT"
echo "Log    : $LOG_FILE"
echo

# ---------------------------------------------------------------- run
set +e
"$UNITY" \
  -quit -batchmode -nographics \
  -projectPath "$ARG_PROJECT" \
  -executeMethod "$BUILD_METHOD" \
  -logFile "$ARG_LOG"
EXIT_CODE=$?
set -e

if (( EXIT_CODE != 0 )); then
  echo
  echo "--- last 60 log lines ---" >&2
  [[ -f "$LOG_FILE" ]] && tail -n 60 "$LOG_FILE" >&2
  echo >&2
  echo "Unity exited with code $EXIT_CODE" >&2
  exit "$EXIT_CODE"
fi

echo
echo "Build succeeded."
ls -lh "$PROJECT_DIR/Build/Android" 2>/dev/null || true
