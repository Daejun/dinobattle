#!/usr/bin/env bash
# Static sanity checks for the Unity project. Run from WSL.
#
# There is no C# compiler in this workspace (Unity is the compiler), so this catches the classes of
# breakage that would otherwise only surface when you open the editor:
#
#   1. SerializedObject.FindProperty("x") naming a field that no longer exists
#      -> silently returns null, then NullReferenceException inside a menu command
#   2. Unity APIs renamed in Unity 6 (Rigidbody.velocity, FindObjectOfType, ...)
#   3. Scripts outside the DinoBattle.* namespace
#   4. Animator parameter names drifting between code and docs
#
# Usage:
#   ./Tools/check-project.sh
#
# Exit code 0 = clean, 1 = problems found.

set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$(dirname "$SCRIPT_DIR")"
cd "$PROJECT_DIR"

RUNTIME_GLOB='Assets/Scripts'
EDITOR_GLOB='Assets/Editor'

FAILURES=0
CHECKS=0

pass() { printf '  \033[32mok\033[0m    %s\n' "$1"; }
fail() { printf '  \033[31mFAIL\033[0m  %s\n' "$1"; FAILURES=$((FAILURES + 1)); }
info() { printf '        %s\n' "$1"; }
head2() { printf '\n\033[1m%s\033[0m\n' "$1"; }

# strip CR so files edited on Windows do not break the regexes
cs_files() { find "$@" -name '*.cs' -type f 2>/dev/null | sort; }

# ---------------------------------------------------------------- 1. FindProperty targets
head2 "1. SerializedObject.FindProperty targets resolve to a real field"

declare -a MISSING=()
while IFS= read -r line; do
  file="${line%%:*}"
  prop="${line#*:}"
  CHECKS=$((CHECKS + 1))

  # A serialized field declaration looks like:  [SerializeField] private Foo bar;   or   public Foo bar = x;
  if ! grep -rhoE "(private|public|protected|internal)[^;(){}]*\\b${prop}\\b\\s*(=|;)" \
        --include='*.cs' "$RUNTIME_GLOB" >/dev/null 2>&1; then
    MISSING+=("$prop  (referenced in ${file#./})")
  fi
done < <(
  cs_files "$EDITOR_GLOB" | while IFS= read -r f; do
    tr -d '\r' < "$f" \
      | grep -oE 'FindProperty\("[A-Za-z_][A-Za-z0-9_]*"\)' \
      | sed -E 's/FindProperty\("([^"]+)"\)/\1/' \
      | sort -u \
      | sed "s|^|$f:|"
  done
)

if (( ${#MISSING[@]} == 0 )); then
  pass "$CHECKS FindProperty reference(s) all resolve"
else
  fail "${#MISSING[@]} FindProperty reference(s) point at nonexistent fields"
  for m in "${MISSING[@]}"; do info "$m"; done
fi

# ---------------------------------------------------------------- 2. renamed / removed Unity 6.5 APIs
head2 "2. No APIs renamed or removed in Unity 6.5"

check_forbidden() {
  local pattern="$1" message="$2" replacement="$3"
  local hits
  hits="$(grep -rnE "$pattern" --include='*.cs' "$RUNTIME_GLOB" "$EDITOR_GLOB" 2>/dev/null || true)"

  if [[ -z "$hits" ]]; then
    pass "$message"
  else
    fail "$message  -> use $replacement"
    while IFS= read -r hit; do info "${hit#./}"; done <<< "$hits"
  fi
}

# Rigidbody.velocity was renamed; Rigidbody2D and ParticleSystem still use .velocity, hence the guard.
check_forbidden '\b(body|rb|rigidbody|Rigidbody)[A-Za-z0-9_]*\.velocity\b' \
  "Rigidbody.velocity not used" "linearVelocity"
check_forbidden '\bFindObjectOfType\b' \
  "FindObjectOfType not used" "FindAnyObjectByType / FindFirstObjectByType"
check_forbidden '\bFindObjectsOfType\b' \
  "FindObjectsOfType not used" "FindObjectsByType"
# Unity 6.5 turned these long-obsolete component accessors into hard compile errors.
check_forbidden '\b(gameObject|GameObject|transform)\.(rigidbody|camera|light|animation|collider|renderer|audio|particleSystem)\b' \
  "no obsolete component property accessors" "GetComponent<T>()"
check_forbidden '\bAddComponent\("' \
  "AddComponent(string) not used" "AddComponent<T>()"
check_forbidden '\bLowLevelPhysics2D\b' \
  "LowLevelPhysics2D not used" "Unity.U2D.Physics.PhysicsCore2D"

# Android floor moved to API 26 in Unity 6.5; a lower value makes the editor reject the build.
if grep -rqE 'minSdkVersion\s*=\s*AndroidSdkVersions\.AndroidApiLevel(2[0-5]|1[0-9])\b' \
     --include='*.cs' "$EDITOR_GLOB" 2>/dev/null; then
  fail "minSdkVersion is below API 26, which Unity 6.5 rejects  -> AndroidApiLevel26 or higher"
  grep -rnE 'minSdkVersion\s*=' --include='*.cs' "$EDITOR_GLOB" | while IFS= read -r hit; do info "${hit#./}"; done
else
  pass "Android minSdkVersion is API 26 or higher"
fi

# ---------------------------------------------------------------- 3. namespaces
head2 "3. Every script declares a DinoBattle.* namespace"

declare -a BAD_NS=()
while IFS= read -r f; do
  # Match the file directly rather than piping through tr: `grep -q` exits on the first hit, which
  # SIGPIPEs the upstream process, and `pipefail` would report that as a failed check.
  # The trailing [[:space:]]* tolerates the CR on Windows-edited files.
  if ! grep -qE '^[[:space:]]*namespace[[:space:]]+DinoBattle(\.[A-Za-z0-9_]+)*[[:space:]]*$' "$f"; then
    BAD_NS+=("${f#./}")
  fi
done < <(cs_files "$RUNTIME_GLOB" "$EDITOR_GLOB")

if (( ${#BAD_NS[@]} == 0 )); then
  pass "all scripts namespaced under DinoBattle"
else
  fail "${#BAD_NS[@]} script(s) missing a DinoBattle.* namespace"
  for f in "${BAD_NS[@]}"; do info "$f"; done
fi

# ---------------------------------------------------------------- 4. animator parameters
head2 "4. Animator parameter names are consistent"

# These string literals must match the Animator Controller you build in Unity. Docs/assets.md and
# Docs/roadmap.md tell the artist which names to use, so drift here breaks the art handoff silently.
declare -A EXPECTED=( [Speed]="CreatureBrain" [Attack]="MeleeAttack" )
for param in "${!EXPECTED[@]}"; do
  owner="${EXPECTED[$param]}"
  if grep -rqE "ParameterName\s*=\s*\"${param}\"|=\s*\"${param}\"\s*;" \
       --include="${owner}.cs" "$RUNTIME_GLOB" 2>/dev/null; then
    pass "\"$param\" default is declared in $owner.cs"
  else
    fail "\"$param\" default not found in $owner.cs — update Docs/assets.md if it was renamed"
  fi

  # Require the backticked name specifically — a bare grep for "Speed" would match "moveSpeed"
  # and give false confidence that the parameter was documented.
  if grep -rqF "\`$param\`" Docs/ 2>/dev/null; then
    pass "\"$param\" is documented for the art handoff"
  else
    fail "\"$param\" is not documented in Docs/ — the artist will not know to create it"
  fi
done

# ---------------------------------------------------------------- 5. hygiene
head2 "5. Repository hygiene"

if [[ -d Library ]] && git check-ignore -q Library 2>/dev/null; then
  pass "Library/ exists and is gitignored"
elif [[ ! -d Library ]]; then
  pass "Library/ not present yet (Unity has not opened the project)"
else
  fail "Library/ is NOT gitignored — check .gitignore"
fi

if git config --get filter.lfs.clean >/dev/null 2>&1; then
  pass "git-lfs is initialized"
else
  fail "git-lfs not initialized — run 'git lfs install' before committing any FBX/PNG/WAV"
fi

# The project targets Unity 6.5 (6000.5.x). Unity rewrites this file when the project is opened
# with a different editor, so a mismatch here means someone opened it on another version — which
# also means Docs/setup.md and the minSdkVersion floor may no longer match reality.
EXPECTED_UNITY_MINOR="6000.5"

if [[ -f ProjectSettings/ProjectVersion.txt ]]; then
  ACTUAL_VERSION="$(tr -d '\r' < ProjectSettings/ProjectVersion.txt | grep -m1 '^m_EditorVersion:' | awk '{print $2}')"
  if [[ "$ACTUAL_VERSION" == "$EXPECTED_UNITY_MINOR".* ]]; then
    pass "ProjectVersion.txt targets Unity $ACTUAL_VERSION"
  else
    fail "ProjectVersion.txt says $ACTUAL_VERSION but this project targets $EXPECTED_UNITY_MINOR.x"
    info "if the editor version changed on purpose, update EXPECTED_UNITY_MINOR here and Docs/setup.md"
  fi
else
  fail "ProjectSettings/ProjectVersion.txt missing — Unity Hub will not recognize the project"
fi

# ---------------------------------------------------------------- summary
head2 "Summary"
if (( FAILURES == 0 )); then
  printf '  \033[32mAll checks passed.\033[0m\n\n'
  exit 0
fi

printf '  \033[31m%d check(s) failed.\033[0m\n\n' "$FAILURES"
exit 1
