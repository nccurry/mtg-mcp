#!/usr/bin/env sh
set -eu

repo_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)

if command -v mise >/dev/null 2>&1; then
  mise_bin=$(command -v mise)
else
  curl -fsSL https://mise.run | sh
  mise_bin="${HOME}/.local/bin/mise"
fi

cd "$repo_dir"
"$mise_bin" trust --yes mise.toml
"$mise_bin" install
"$mise_bin" exec -- task setup
printf '%s\n' "Bootstrap complete. Run 'task <command>'; Task uses mise for .NET. If task is not on PATH, activate mise or use 'mise exec -- task <command>'."
