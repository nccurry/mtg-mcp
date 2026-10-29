#!/usr/bin/env bash
set -euo pipefail

if [[ "$(uname -s)" != "Linux" ]]; then
    echo "scripts/install-linux-native-deps.sh only supports Linux." >&2
    exit 1
fi

packages=(
    bash
    ca-certificates
    curl
    git
    gnupg
    jq
    libicu-dev
    libssl-dev
    libstdc++6
    tar
    tzdata
    unzip
    xz-utils
    zip
    zlib1g
)

# The libxml2 runtime package was renamed for its new ABI (libxml2-16 from Ubuntu 26.04), so either name satisfies it.
libxml2_packages=(libxml2 libxml2-16)

is_installed() {
    dpkg-query -W -f='${Status}' "$1" 2>/dev/null | grep -q "install ok installed"
}

if [[ "${1:-}" == "--check" ]]; then
    missing=()
    for package in "${packages[@]}"; do
        if ! is_installed "$package"; then
            missing+=("$package")
        fi
    done

    libxml2_found=false
    for package in "${libxml2_packages[@]}"; do
        if is_installed "$package"; then
            libxml2_found=true
        fi
    done

    if [[ "$libxml2_found" != "true" ]]; then
        missing+=("${libxml2_packages[0]}")
    fi

    if [[ "${#missing[@]}" -ne 0 ]]; then
        printf 'Missing Linux native dependencies: %s\n' "${missing[*]}" >&2
        exit 1
    fi

    echo "Linux native dependencies are installed."
    exit 0
fi

sudo_cmd=()
if [[ "${EUID:-$(id -u)}" -ne 0 ]]; then
    sudo_cmd=(sudo)
fi

"${sudo_cmd[@]}" apt-get update

libxml2_package=""
for package in "${libxml2_packages[@]}"; do
    candidate="$(apt-cache policy "$package" 2>/dev/null | awk '/Candidate:/ { print $2 }')"
    if [[ -n "$candidate" && "$candidate" != "(none)" ]]; then
        libxml2_package="$package"
        break
    fi
done

if [[ -z "$libxml2_package" ]]; then
    printf 'No installable libxml2 package found (tried: %s).\n' "${libxml2_packages[*]}" >&2
    exit 1
fi

"${sudo_cmd[@]}" apt-get install -y --no-install-recommends "${packages[@]}" "$libxml2_package"

if [[ "${CI:-}" == "true" || "${MTG_MCP_CLEAN_APT:-}" == "true" ]]; then
    "${sudo_cmd[@]}" apt-get clean
    "${sudo_cmd[@]}" rm -rf /var/lib/apt/lists/*
fi
