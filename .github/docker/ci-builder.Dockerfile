ARG CI_IMAGE_BASE
FROM ${CI_IMAGE_BASE}

SHELL ["/bin/bash", "-o", "pipefail", "-c"]

WORKDIR /workspace

COPY versions.env /tmp/versions.env
COPY global.json /tmp/global.json
COPY scripts/install-linux-native-deps.sh /tmp/install-linux-native-deps.sh

RUN chmod +x /tmp/install-linux-native-deps.sh \
    && MTG_MCP_CLEAN_APT=true /tmp/install-linux-native-deps.sh

RUN source /tmp/versions.env \
    && DOTNET_SDK_VERSION="$(jq -r '.sdk.version' /tmp/global.json)" \
    && : "${DOTNET_SDK_VERSION:?}" \
    && : "${GO_TASK_VERSION:?}" \
    && : "${POWERSHELL_VERSION:?}" \
    && curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh \
    && chmod +x /tmp/dotnet-install.sh \
    && mkdir -p /usr/share/dotnet \
    && /tmp/dotnet-install.sh --jsonfile /tmp/global.json --install-dir /usr/share/dotnet --no-path \
    && ln -s /usr/share/dotnet/dotnet /usr/local/bin/dotnet \
    && case "$(uname -m)" in x86_64|amd64) task_arch=amd64; pwsh_arch=x64 ;; aarch64|arm64) task_arch=arm64; pwsh_arch=arm64 ;; *) echo "Unsupported architecture: $(uname -m)" >&2; exit 1 ;; esac \
    && curl -fsSL "https://github.com/go-task/task/releases/download/v${GO_TASK_VERSION}/task_linux_${task_arch}.tar.gz" -o /tmp/task.tar.gz \
    && tar -xzf /tmp/task.tar.gz -C /usr/local/bin task \
    && chmod +x /usr/local/bin/task \
    && mkdir -p /opt/microsoft/powershell/7 \
    && curl -fsSL "https://github.com/PowerShell/PowerShell/releases/download/v${POWERSHELL_VERSION}/powershell-${POWERSHELL_VERSION}-linux-${pwsh_arch}.tar.gz" -o /tmp/powershell.tar.gz \
    && tar -xzf /tmp/powershell.tar.gz -C /opt/microsoft/powershell/7 \
    && chmod +x /opt/microsoft/powershell/7/pwsh \
    && ln -s /opt/microsoft/powershell/7/pwsh /usr/local/bin/pwsh \
    && rm -f /tmp/dotnet-install.sh /tmp/task.tar.gz /tmp/powershell.tar.gz

ENV DOTNET_ROOT=/usr/share/dotnet \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=true \
    DOTNET_CLI_TELEMETRY_OPTOUT=true \
    DOTNET_NOLOGO=true \
    PATH="/usr/share/dotnet:/root/.dotnet/tools:${PATH}"

COPY dotnet-tools.json /tmp/mtg-mcp-tools/dotnet-tools.json

RUN dotnet tool restore --tool-manifest /tmp/mtg-mcp-tools/dotnet-tools.json \
    && dotnet --version \
    && task --version \
    && pwsh -NoProfile -Command '$PSVersionTable.PSVersion.ToString()'
