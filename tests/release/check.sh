#!/usr/bin/env bash
set -euo pipefail

dotnet --version
node --version
python3 -m unittest discover -s tests/scripts -v
dotnet test tests/api/RabbitHole.Api.Tests.csproj -c Release
dotnet format RabbitHole.slnx --verify-no-changes --no-restore
dotnet build tests/host/RabbitHole.E2eHost.csproj
RABBITHOLE_TEST_POSTGRES='Host=db;Database=rabbithole_recommendation_test_release;Username=rabbithole;Password=fixture-ci-password' \
    dotnet test tests/api/RabbitHole.Api.Tests.csproj -c Release --no-build \
    --filter 'FullyQualifiedName~RecommendationEndpointTests|FullyQualifiedName~HostedPostgresTests'
npm --prefix apps/web run lint
npm --prefix apps/web run typecheck
npm --prefix apps/web run build
npm run test:e2e
for file in scripts/hosted/*.sh; do bash -n "$file"; done
python3 -m py_compile scripts/hosted/*.py scripts/preservation-manifest.py
python3 -m unittest discover -s scripts/hosted -p 'test_*.py' -v
