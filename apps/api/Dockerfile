FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY apps/api/RabbitHole.Api.csproj apps/api/
RUN dotnet restore apps/api/RabbitHole.Api.csproj
COPY apps/api/ apps/api/
COPY config/curated-source-pack.json config/
RUN dotnet publish apps/api/RabbitHole.Api.csproj -c Release -o /out --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
COPY apps/api/Ingestion/yt-dlp-requirements.txt /tmp/yt-dlp-requirements.txt
RUN apt-get update && apt-get install -y --no-install-recommends curl python3 python3-venv && rm -rf /var/lib/apt/lists/* \
    && python3 -m venv /opt/yt-dlp \
    && /opt/yt-dlp/bin/python -m pip install --disable-pip-version-check --no-cache-dir --require-hashes --no-deps --only-binary=:all: -r /tmp/yt-dlp-requirements.txt \
    && rm /tmp/yt-dlp-requirements.txt \
    && mkdir -p /data/uploads /data/auth-keys && chown -R app:app /data
WORKDIR /app
COPY --from=build /out .
USER app
ENV ASPNETCORE_URLS=http://+:8080 DOTNET_CLI_TELEMETRY_OPTOUT=1 YouTube__YtDlpPython=/opt/yt-dlp/bin/python
EXPOSE 8080
ENTRYPOINT ["dotnet", "RabbitHole.Api.dll"]
