FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY apps/api/RabbitHole.Api.csproj apps/api/
RUN dotnet restore apps/api/RabbitHole.Api.csproj
COPY apps/api/ apps/api/
RUN dotnet publish apps/api/RabbitHole.Api.csproj -c Release -o /out --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /data/uploads && chown -R app:app /data
WORKDIR /app
COPY --from=build /out .
USER app
ENV ASPNETCORE_URLS=http://+:8080 DOTNET_CLI_TELEMETRY_OPTOUT=1
EXPOSE 8080
ENTRYPOINT ["dotnet", "RabbitHole.Api.dll"]
