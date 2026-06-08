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
    libxml2
    tar
    tzdata
    unzip
    xz-utils
    zip
    zlib1g
)

if [[ "${1:-}" == "--check" ]]; then
    missing=()
    for package in "${packages[@]}"; do
        if ! dpkg -l 2>/dev/null | grep -q "^ii  ${package}[[:space:]]"; then
            missing+=("$package")
        fi
    done

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
"${sudo_cmd[@]}" apt-get install -y --no-install-recommends "${packages[@]}"

if [[ "${CI:-}" == "true" || "${MTG_MCP_CLEAN_APT:-}" == "true" ]]; then
    "${sudo_cmd[@]}" apt-get clean
    "${sudo_cmd[@]}" rm -rf /var/lib/apt/lists/*
fi
