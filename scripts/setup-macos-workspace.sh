#!/usr/bin/env bash
set -euo pipefail

usage() {
    cat <<'EOF'
Usage: ./scripts/setup-macos-workspace.sh [options]

Create or update the sibling Unity-version clones used to test unity-rlottie.
Run this script from an initial unity-rlottie clone on the Mac.

Options:
  --workspace DIR       Parent directory for all clones (default: repository parent)
  --remote-url URL      Repository URL used for new clones (default: origin URL)
  --source-branch NAME  Branch for unity-rlottie_webgl (default: dev)
  --no-webgl            Do not create or update unity-rlottie_webgl
  --skip-submodules     Do not initialize or update recursive submodules
  --dry-run             Print the mutating commands without running them
  -h, --help            Show this help
EOF
}

info() {
    printf '[INFO] %s\n' "$*"
}

die() {
    printf '[ERROR] %s\n' "$*" >&2
    exit 1
}

quote_command() {
    printf '  +'
    printf ' %q' "$@"
    printf '\n'
}

run() {
    quote_command "$@"
    if [[ "${DRY_RUN}" == false ]]; then
        "$@"
    fi
}

require_clean_clone() {
    local repository="$1"
    local status

    status="$(git -C "${repository}" status --porcelain --untracked-files=all --ignore-submodules=none)"
    if [[ -n "${status}" ]]; then
        printf '%s\n' "${status}" >&2
        die "Refusing to update dirty clone: ${repository}"
    fi
}

update_submodules() {
    local repository="$1"

    if [[ "${SKIP_SUBMODULES}" == false ]]; then
        run git -C "${repository}" submodule update --init --recursive
    fi
}

prepare_clone() {
    local branch="$1"
    local repository="$2"
    local current_branch
    local existing_url

    printf '\n'
    info "Preparing $(basename "${repository}") on ${branch}"

    if [[ ! -e "${repository}" ]]; then
        run git clone --branch "${branch}" --single-branch "${REMOTE_URL}" "${repository}"
        if [[ "${DRY_RUN}" == true ]]; then
            if [[ "${SKIP_SUBMODULES}" == false ]]; then
                quote_command git -C "${repository}" submodule update --init --recursive
            fi
            return
        fi
    elif ! git -C "${repository}" rev-parse --git-dir >/dev/null 2>&1; then
        die "Target exists but is not a Git clone: ${repository}"
    fi

    require_clean_clone "${repository}"

    existing_url="$(git -C "${repository}" remote get-url origin 2>/dev/null || true)"
    if [[ -z "${existing_url}" ]]; then
        run git -C "${repository}" remote add origin "${REMOTE_URL}"
    elif [[ "${existing_url}" != "${REMOTE_URL}" ]]; then
        info "Keeping existing origin URL for ${repository}: ${existing_url}"
    fi

    run git -C "${repository}" fetch --prune origin "${branch}"

    current_branch="$(git -C "${repository}" branch --show-current)"
    if [[ -z "${current_branch}" ]]; then
        die "Clone has a detached HEAD; expected ${branch}: ${repository}"
    fi
    if [[ "${current_branch}" != "${branch}" ]]; then
        die "Clone is on ${current_branch}; expected ${branch}: ${repository}"
    fi

    run git -C "${repository}" merge --ff-only "origin/${branch}"
    update_submodules "${repository}"
}

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
SOURCE_REPOSITORY="$(cd "${SCRIPT_DIR}/.." && pwd -P)"
WORKSPACE_ROOT="$(cd "${SOURCE_REPOSITORY}/.." && pwd -P)"
REMOTE_URL=""
SOURCE_BRANCH="dev"
INCLUDE_WEBGL=true
SKIP_SUBMODULES=false
DRY_RUN=false

while [[ $# -gt 0 ]]; do
    case "$1" in
        --workspace)
            [[ $# -ge 2 ]] || die '--workspace requires a directory'
            WORKSPACE_ROOT="$2"
            shift 2
            ;;
        --remote-url)
            [[ $# -ge 2 ]] || die '--remote-url requires a URL'
            REMOTE_URL="$2"
            shift 2
            ;;
        --source-branch)
            [[ $# -ge 2 ]] || die '--source-branch requires a branch name'
            SOURCE_BRANCH="$2"
            shift 2
            ;;
        --no-webgl)
            INCLUDE_WEBGL=false
            shift
            ;;
        --skip-submodules)
            SKIP_SUBMODULES=true
            shift
            ;;
        --dry-run)
            DRY_RUN=true
            shift
            ;;
        -h|--help)
            usage
            exit 0
            ;;
        *)
            usage >&2
            die "Unknown option: $1"
            ;;
    esac
done

command -v git >/dev/null 2>&1 || die 'git is required'
git -C "${SOURCE_REPOSITORY}" rev-parse --git-dir >/dev/null 2>&1 || \
    die "Script is not inside a Git clone: ${SOURCE_REPOSITORY}"

if [[ -z "${REMOTE_URL}" ]]; then
    REMOTE_URL="$(git -C "${SOURCE_REPOSITORY}" remote get-url origin 2>/dev/null || true)"
    [[ -n "${REMOTE_URL}" ]] || die 'The source clone has no origin remote; pass --remote-url'
fi

if [[ ! -d "${WORKSPACE_ROOT}" ]]; then
    if [[ "${DRY_RUN}" == true ]]; then
        quote_command mkdir -p "${WORKSPACE_ROOT}"
    else
        run mkdir -p "${WORKSPACE_ROOT}"
    fi
fi
WORKSPACE_ROOT="$(cd "${WORKSPACE_ROOT}" 2>/dev/null && pwd -P || printf '%s' "${WORKSPACE_ROOT}")"

info "Source clone: ${SOURCE_REPOSITORY}"
info "Workspace: ${WORKSPACE_ROOT}"
info "Remote: ${REMOTE_URL}"

run git -C "${SOURCE_REPOSITORY}" fetch --prune origin
update_submodules "${SOURCE_REPOSITORY}"

UNITY_BRANCHES="$(git -C "${SOURCE_REPOSITORY}" for-each-ref \
    --format='%(refname:strip=3)' 'refs/remotes/origin/unity/*' | LC_ALL=C sort)"
[[ -n "${UNITY_BRANCHES}" ]] || die 'No origin/unity/* branches were found'

clone_count=0
for branch in ${UNITY_BRANCHES}; do
    suffix="${branch#unity/}"
    prepare_clone "${branch}" "${WORKSPACE_ROOT}/unity-rlottie-${suffix}"
    clone_count=$((clone_count + 1))
done

if [[ "${INCLUDE_WEBGL}" == true ]]; then
    if ! git -C "${SOURCE_REPOSITORY}" show-ref --verify --quiet "refs/remotes/origin/${SOURCE_BRANCH}"; then
        die "Source branch does not exist on origin: ${SOURCE_BRANCH}"
    fi
    prepare_clone "${SOURCE_BRANCH}" "${WORKSPACE_ROOT}/unity-rlottie_webgl"
fi

printf '\n[SUMMARY] Prepared %d Unity-version clones' "${clone_count}"
if [[ "${INCLUDE_WEBGL}" == true ]]; then
    printf ' and the WebGL clone'
fi
printf ' under %s.\n' "${WORKSPACE_ROOT}"
