FROM mcr.microsoft.com/dotnet/sdk:10.0
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1
WORKDIR /src
COPY apps/api apps/api
COPY tests/api tests/api
RUN dotnet restore tests/api/RabbitHole.Api.Tests.csproj
ENTRYPOINT ["dotnet", "test", "tests/api/RabbitHole.Api.Tests.csproj", "-c", "Release", "--no-restore", "--filter", "FullyQualifiedName~RecommendationEndpointTests", "-v:minimal"]
