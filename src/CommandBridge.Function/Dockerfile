# ===== Stage 1: Build =====
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy solution and restore (leveraging layer caching)
COPY ["iot-remote-config.sln", "."]
COPY ["src/CommandBridge.Function/CommandBridge.Function.csproj", "src/CommandBridge.Function/"]
COPY ["src/RemoteConfig.Core/RemoteConfig.Core.csproj", "src/RemoteConfig.Core/"]
RUN dotnet restore "src/CommandBridge.Function/CommandBridge.Function.csproj"

COPY . .
WORKDIR "/src/src/CommandBridge.Function"
RUN dotnet publish "CommandBridge.Function.csproj" -c Release -o /out

# ===== Stage 2: Runtime =====
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
RUN groupadd -r appgroup && useradd -r -g appgroup -d /app -s /sbin/nologin appuser
WORKDIR /app
COPY --from=build /out ./
USER appuser
EXPOSE 8080
ENTRYPOINT ["dotnet", "CommandBridge.Function.dll"]
