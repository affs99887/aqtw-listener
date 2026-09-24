FROM mcr.microsoft.com/dotnet/sdk:10.0-noble

# Cross-platform build/test dependencies. Python 3 is used by the repository's
# audio/reference tooling scripts under scripts/.
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        git \
        curl \
        ca-certificates \
        python3 \
        sudo \
    && rm -rf /var/lib/apt/lists/*

# Cursor Cloud Agents expect a non-root "ubuntu" user with passwordless sudo.
RUN if ! id ubuntu >/dev/null 2>&1; then useradd -m -s /bin/bash ubuntu; fi \
    && echo 'ubuntu ALL=(ALL) NOPASSWD:ALL' > /etc/sudoers.d/ubuntu \
    && chmod 0440 /etc/sudoers.d/ubuntu

ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
    NUGET_XMLDOC_MODE=skip

USER ubuntu
