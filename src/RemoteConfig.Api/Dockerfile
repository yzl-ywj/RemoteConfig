# ═════════════════════════════════════════════════════════
# Stage 1: Build
# ═════════════════════════════════════════════════════════
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy solution and project files
COPY ["src/RemoteConfig.sln", "."]
COPY ["src/RemoteConfig.Core/RemoteConfig.Core.csproj", "src/RemoteConfig.Core/"]
COPY ["src/RemoteConfig.Infrastructure/RemoteConfig.Infrastructure.csproj", "src/RemoteConfig.Infrastructure/"]
COPY ["src/RemoteConfig.Api/RemoteConfig.Api.csproj", "src/RemoteConfig.Api/"]
COPY ["tests/RemoteConfig.Tests/RemoteConfig.Tests.csproj", "tests/RemoteConfig.Tests/"]

# Copy Directory.Build.props and .editorconfig
COPY [".editorconfig", "."]
COPY ["Directory.Build.props", "."]

# Restore packages
RUN dotnet restore "src/RemoteConfig.sln"

# Copy source code
COPY ["src/", "src/"]
COPY ["tests/", "tests/"]

# Build (Release, treat warnings as errors)
WORKDIR /src
RUN dotnet build "src/RemoteConfig.sln" -c Release --no-restore -warnaserror

# Run tests
RUN dotnet test "src/RemoteConfig.sln" -c Release --no-build --no-restore

# Publish API
RUN dotnet publish "src/RemoteConfig.Api/RemoteConfig.Api.csproj" -c Release -o /app/publish --no-build --no-restore

# ═════════════════════════════════════════════════════════
# Stage 2: Runtime
# ═════════════════════════════════════════════════════════
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Create non-root user
RUN groupadd -r appuser && useradd -r -g appuser -d /app -s /sbin/nologin appuser
RUN chown -R appuser:appuser /app
USER appuser

# Copy published output
COPY --from=build /app/publish .

# Health check
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1

# Expose port
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "RemoteConfig.Api.dll"]
