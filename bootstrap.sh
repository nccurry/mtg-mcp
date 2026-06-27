#!/usr/bin/env sh
set -eu

script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
versions_file="${script_dir}/versions.env"
task_version="${TASK_VERSION:-}"

if [ -z "$task_version" ] && [ -f "$versions_file" ]; then
  task_version="$(sed -n 's/^GO_TASK_VERSION=//p' "$versions_file" | head -n 1)"
fi

if [ -z "$task_version" ]; then
  printf '%s\n' "GO_TASK_VERSION is missing from versions.env." >&2
  exit 1
fi

TASK_VERSION="$task_version"

if [ "$#" -eq 0 ]; then
  set -- setup
fi

task_os() {
  case "$(uname -s)" in
    Linux) printf '%s\n' linux ;;
    Darwin) printf '%s\n' darwin ;;
    *)
      printf '%s\n' "Unsupported operating system: $(uname -s)" >&2
      exit 1
      ;;
  esac
}

task_architecture() {
  case "$(uname -m)" in
    x86_64 | amd64) printf '%s\n' amd64 ;;
    arm64 | aarch64) printf '%s\n' arm64 ;;
    *)
      printf '%s\n' "Unsupported processor architecture: $(uname -m)" >&2
      exit 1
      ;;
  esac
}

download_task() {
  os=$1
  architecture=$2
  archive=$3
  url="https://github.com/go-task/task/releases/download/v${TASK_VERSION}/task_${os}_${architecture}.tar.gz"

  if command -v curl >/dev/null 2>&1; then
    curl -fsSL "$url" -o "$archive"
  elif command -v wget >/dev/null 2>&1; then
    wget -q "$url" -O "$archive"
  else
    printf '%s\n' "Task is not installed and neither curl nor wget is available." >&2
    exit 1
  fi
}

task_version_matches() {
  task_path=$1
  actual_version=$("$task_path" --version 2>/dev/null || true)
  [ "$actual_version" = "$TASK_VERSION" ]
}

task_command() {
  if command -v task >/dev/null 2>&1; then
    candidate=$(command -v task)
    if task_version_matches "$candidate"; then
      printf '%s\n' "$candidate"
      return
    fi
  fi

  os=$(task_os)
  architecture=$(task_architecture)
  task_dir="${script_dir}/.tools/task/v${TASK_VERSION}/${os}-${architecture}"
  task_path="${task_dir}/task"

  if [ -x "$task_path" ] && task_version_matches "$task_path"; then
    printf '%s\n' "$task_path"
    return
  fi

  mkdir -p "$task_dir"

  temp_dir=$(mktemp -d)
  archive="${temp_dir}/task.tar.gz"

  printf '%s\n' "Downloading Task v${TASK_VERSION} for ${os} ${architecture}..." >&2
  download_task "$os" "$architecture" "$archive"
  tar -xzf "$archive" -C "$temp_dir"

  if [ ! -f "${temp_dir}/task" ]; then
    printf '%s\n' "Task executable was not found in the downloaded archive." >&2
    rm -rf "$temp_dir"
    exit 1
  fi

  cp "${temp_dir}/task" "$task_path"
  chmod +x "$task_path"
  rm -rf "$temp_dir"

  printf '%s\n' "$task_path"
}

task_bin=$(task_command)

cd "$script_dir"
exec "$task_bin" "$@"
