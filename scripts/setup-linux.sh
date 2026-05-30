#!/usr/bin/env bash
set -euo pipefail

if [[ "$(uname -s)" != "Linux" ]]; then
    echo "scripts/setup-linux.sh only supports Linux." >&2
    exit 1
fi

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/.." && pwd)"
dotnet_dir="${DOTNET_DIR:-$repo_root/.dotnet}"
tools_dir="${TOOLS_DIR:-$repo_root/.tools}"
tools_bin="$tools_dir/bin"
dotnet_cli_home="${DOTNET_CLI_HOME:-$repo_root/.dotnet-home}"
local_app_data="${LocalAppData:-$repo_root/.local/share}"
msbuild_user_extensions="${MSBuildUserExtensionsPath:-$repo_root/.msbuild}"
nuget_packages="${NUGET_PACKAGES:-$repo_root/.nuget/packages}"
nuget_http_cache="${NUGET_HTTP_CACHE_PATH:-$repo_root/.nuget/http-cache}"
nuget_plugins_cache="${NUGET_PLUGINS_CACHE_PATH:-$repo_root/.nuget/plugins-cache}"
xdg_cache_home="${XDG_CACHE_HOME:-$repo_root/.local/cache}"
xdg_config_home="${XDG_CONFIG_HOME:-$repo_root/.local/config}"
xdg_data_home="${XDG_DATA_HOME:-$repo_root/.local/share}"
versions_file="$repo_root/versions.env"
default_task_version="3.51.1"
if [[ -f "$versions_file" ]]; then
    configured_task_version="$(sed -n 's/^GO_TASK_VERSION=//p' "$versions_file" | head -n 1)"
    if [[ -n "$configured_task_version" ]]; then
        default_task_version="$configured_task_version"
    fi
fi

task_version="${TASK_VERSION:-$default_task_version}"
powershell_version="${POWERSHELL_VERSION:-7.6.2}"
sdk_version="${DOTNET_SDK_VERSION:-}"

if [[ -z "$sdk_version" ]]; then
    sdk_version="$(sed -n 's/.*"version"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' "$repo_root/global.json" | head -n 1)"
fi

if [[ -z "$sdk_version" ]]; then
    echo "Could not determine the required .NET SDK version from global.json." >&2
    exit 1
fi

mkdir -p \
    "$dotnet_dir" \
    "$tools_bin" \
    "$dotnet_cli_home" \
    "$local_app_data" \
    "$msbuild_user_extensions" \
    "$nuget_packages" \
    "$nuget_http_cache" \
    "$nuget_plugins_cache" \
    "$xdg_cache_home" \
    "$xdg_config_home" \
    "$xdg_data_home"

export DOTNET_ROOT="$dotnet_dir"
export DOTNET_CLI_HOME="$dotnet_cli_home"
export LocalAppData="$local_app_data"
export MSBuildUserExtensionsPath="$msbuild_user_extensions"
export NUGET_PACKAGES="$nuget_packages"
export NUGET_HTTP_CACHE_PATH="$nuget_http_cache"
export NUGET_PLUGINS_CACHE_PATH="$nuget_plugins_cache"
export XDG_CACHE_HOME="$xdg_cache_home"
export XDG_CONFIG_HOME="$xdg_config_home"
export XDG_DATA_HOME="$xdg_data_home"
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export DOTNET_NOLOGO=1
export PATH="$dotnet_dir:$tools_bin:$dotnet_cli_home/.dotnet/tools:$HOME/.dotnet/tools:$HOME/.local/bin:$PATH"

install_dotnet() {
    local installed_sdks=""
    if [[ -x "$dotnet_dir/dotnet" ]]; then
        installed_sdks="$("$dotnet_dir/dotnet" --list-sdks || true)"
    fi

    if grep -q "^$sdk_version " <<< "$installed_sdks"; then
        echo ".NET SDK $sdk_version is already installed in $dotnet_dir."
        return
    fi

    local installer="$tools_dir/dotnet-install.sh"
    echo "Installing .NET SDK $sdk_version into $dotnet_dir."
    curl -fsSL https://dot.net/v1/dotnet-install.sh -o "$installer"
    chmod +x "$installer"
    "$installer" --version "$sdk_version" --install-dir "$dotnet_dir" --no-path
}

install_task() {
    if command -v task >/dev/null 2>&1; then
        echo "Task is already available: $(command -v task)"
        return
    fi

    local architecture
    case "$(uname -m)" in
        x86_64) architecture="amd64" ;;
        aarch64|arm64) architecture="arm64" ;;
        *)
            echo "Unsupported Task architecture: $(uname -m)" >&2
            exit 1
            ;;
    esac

    local install_dir="$tools_dir/task-$task_version-linux-$architecture"
    local archive="$tools_dir/task-$task_version-linux-$architecture.tar.gz"
    local url="https://github.com/go-task/task/releases/download/v$task_version/task_linux_${architecture}.tar.gz"

    echo "Installing Task $task_version into $install_dir."
    mkdir -p "$install_dir"
    curl --location --fail --silent --show-error "$url" -o "$archive"
    tar -xzf "$archive" -C "$install_dir" task
    chmod +x "$install_dir/task"
    ln -sfn "$install_dir/task" "$tools_bin/task"
}

install_powershell() {
    if command -v pwsh >/dev/null 2>&1 \
        && pwsh -NoProfile -Command '$PSVersionTable.PSVersion.ToString()' >/dev/null 2>&1; then
        echo "PowerShell is already available: $(command -v pwsh)"
        return
    fi

    local architecture
    case "$(uname -m)" in
        x86_64) architecture="x64" ;;
        aarch64|arm64) architecture="arm64" ;;
        *)
            echo "Unsupported PowerShell architecture: $(uname -m)" >&2
            exit 1
            ;;
    esac

    local install_dir="$tools_dir/powershell-$powershell_version-linux-$architecture"
    local archive="$tools_dir/powershell-$powershell_version-linux-$architecture.tar.gz"
    local url="https://github.com/PowerShell/PowerShell/releases/download/v$powershell_version/powershell-$powershell_version-linux-$architecture.tar.gz"

    echo "Installing PowerShell $powershell_version into $install_dir."
    rm -rf "$install_dir"
    mkdir -p "$install_dir"
    curl --location --fail --silent --show-error "$url" -o "$archive"
    tar -xzf "$archive" -C "$install_dir"
    chmod +x "$install_dir/pwsh"
    ln -sfn "$install_dir/pwsh" "$tools_bin/pwsh"
}

install_dotnet
install_task
install_powershell

echo "Restoring repo tools and packages."
task setup

cat <<EOF

Linux development prerequisites are ready.
For future shells, run:

  source scripts/env-linux.sh

Then use:

  task test
  task lint
  task install:local
EOF
