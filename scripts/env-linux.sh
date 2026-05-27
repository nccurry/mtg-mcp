#!/usr/bin/env bash

if [[ "${BASH_SOURCE[0]}" == "$0" ]]; then
    echo "Source this file instead of executing it: source scripts/env-linux.sh" >&2
    exit 1
fi

mtgmcp_env_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

prepend_path_once() {
    case ":$PATH:" in
        *":$1:"*) ;;
        *) PATH="$1:$PATH" ;;
    esac
}

export DOTNET_ROOT="${MTGMCP_DOTNET_ROOT:-$mtgmcp_env_root/.dotnet}"
export DOTNET_CLI_HOME="${DOTNET_CLI_HOME:-$mtgmcp_env_root/.dotnet-home}"
export LocalAppData="${LocalAppData:-$mtgmcp_env_root/.local/share}"
export MSBuildUserExtensionsPath="${MSBuildUserExtensionsPath:-$mtgmcp_env_root/.msbuild}"
export NUGET_PACKAGES="${NUGET_PACKAGES:-$mtgmcp_env_root/.nuget/packages}"
export NUGET_HTTP_CACHE_PATH="${NUGET_HTTP_CACHE_PATH:-$mtgmcp_env_root/.nuget/http-cache}"
export NUGET_PLUGINS_CACHE_PATH="${NUGET_PLUGINS_CACHE_PATH:-$mtgmcp_env_root/.nuget/plugins-cache}"
export XDG_CACHE_HOME="${XDG_CACHE_HOME:-$mtgmcp_env_root/.local/cache}"
export XDG_CONFIG_HOME="${XDG_CONFIG_HOME:-$mtgmcp_env_root/.local/config}"
export XDG_DATA_HOME="${XDG_DATA_HOME:-$mtgmcp_env_root/.local/share}"
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE="${DOTNET_SKIP_FIRST_TIME_EXPERIENCE:-1}"
export DOTNET_NOLOGO="${DOTNET_NOLOGO:-1}"
prepend_path_once "$DOTNET_ROOT"
prepend_path_once "$mtgmcp_env_root/.tools/bin"
prepend_path_once "$DOTNET_CLI_HOME/.dotnet/tools"
prepend_path_once "$HOME/.dotnet/tools"
prepend_path_once "$HOME/.local/bin"
export PATH

unset -f prepend_path_once
unset mtgmcp_env_root
