# Sky LIS API — build from the repository root:
#   docker build -f src/SkyLIS.Api/Dockerfile -t skylis/api .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props ./
COPY src/ src/
RUN dotnet publish src/SkyLIS.Api -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
# curl for the health check; libfontconfig1 for QuestPDF's native text stack
RUN apt-get update && apt-get install -y --no-install-recommends curl libfontconfig1 \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080
HEALTHCHECK --interval=15s --timeout=5s --start-period=30s --retries=5 \
    CMD curl -fsS http://localhost:8080/health || exit 1
ENTRYPOINT ["dotnet", "SkyLIS.Api.dll"]
