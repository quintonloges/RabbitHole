FROM node:24-bookworm-slim AS node
FROM mcr.microsoft.com/dotnet/sdk:10.0
COPY --from=node /usr/local/ /usr/local/
RUN apt-get update && apt-get install -y --no-install-recommends python3 restic \
    && rm -rf /var/lib/apt/lists/*
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 NEXT_TELEMETRY_DISABLED=1 CI=true
WORKDIR /src
COPY global.json RabbitHole.slnx package.json package-lock.json ./
COPY apps/web/package.json apps/web/package-lock.json apps/web/
RUN npm ci && npm ci --prefix apps/web && npx playwright install --with-deps chromium
COPY apps/api/RabbitHole.Api.csproj apps/api/
COPY tests/api/RabbitHole.Api.Tests.csproj tests/api/
COPY tests/host/RabbitHole.E2eHost.csproj tests/host/
RUN dotnet restore RabbitHole.slnx && dotnet restore tests/host/RabbitHole.E2eHost.csproj
COPY apps/api apps/api
COPY apps/web apps/web
COPY config config
COPY scripts scripts
COPY tests tests
COPY playwright.config.ts ./
CMD ["bash", "tests/release/check.sh"]
