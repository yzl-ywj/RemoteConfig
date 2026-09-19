# IoT Remote Config Service

> Part of the Tietoevry IoT Device Management Platform — Phase 3

A .NET 8 microservice for **remote device configuration** and **command execution**. Enables IT administrators to push config changes, send commands, audit all operations, and roll back to previous versions.

---

## 🏗️ Architecture

```
Admin Portal → API Gateway → Remote Config API (.NET 8 on AKS)
                                    ↓
                    ┌───────────────┴───────────────┐
                    ↓                                 ↓
            Config Service                   Command Service
                    ↓                                 ↓
            SQL (versions)              Service Bus → IoT Hub
                    ↓                                 ↓
            Audit Logger                  Device (MQTT/AMQP)
                    ↓
            Redis (pending cmds)
```

See `phase3-architecture.md` for the full Mermaid diagram and design rationale.

---

## 🚀 Quick Start (Local Development)

### Prerequisites

- **Visual Studio 2022** (17.8+) or VS Code
- **.NET 8 SDK**
- **SQL Server** (local or Docker)
- **Redis** (Docker: `docker run -d -p 6379:6379 redis:7-alpine`)
- **Azure Service Bus** emulator or live instance

### 1. Clone & Open

```bash
git clone https://github.com/Tietoevry-IoT/iot-remote-config.git
cd iot-remote-config
```

Open `src/RemoteConfig.sln` in **Visual Studio 2022** → F5 to build & run.

Swagger UI opens at `https://localhost:5001/swagger`.

### 2. Database Setup

```bash
# Using Docker for SQL Server
docker run -d --name sql-dev \
  -e "ACCEPT_EULA=Y" \
  -e "SA_PASSWORD=DevP@ssw0rd!2024" \
  -p 1433:1433 \
  mcr.microsoft.com/mssql/server:2022-latest

# Execute schema
sqlcmd -S localhost -U sa -P DevP@ssw0rd!2024 \
  -i infra/sql/schema.sql
```

### 3. Update `appsettings.Development.json`

```json
{
  "Database": {
    "ConnectionString": "Server=localhost,1433;Database=iot-remote-config-dev;User Id=sa;Password=DevP@ssw0rd!2024;TrustServerCertificate=True;"
  },
  "Redis": {
    "ConnectionString": "localhost:6379"
  }
}
```

### 4. Run Tests

```bash
dotnet test src/RemoteConfig.sln -c Release --collect:"XPlat Code Coverage"
```

---

## 📡 REST API Endpoints

### Config Endpoints

| Method | Path | Scope | Description |
|--------|------|-------|-------------|
| GET | `/api/v1/configs/{deviceId}` | `Config.Read` | Get active config |
| PUT | `/api/v1/configs/{deviceId}` | `Config.Write` | Push new config (new version) |
| GET | `/api/v1/configs/{deviceId}/versions` | `Config.Read` | List version history |
| POST | `/api/v1/configs/{deviceId}/rollback/{version}` | `Config.Write` | Rollback to version |
| GET | `/api/v1/configs/{deviceId}/diff?from=X&to=Y` | `Config.Read` | Diff two versions |
| POST | `/api/v1/configs/batch` | `Config.Write` | Batch push to devices |

### Command Endpoints

| Method | Path | Scope | Description |
|--------|------|-------|-------------|
| POST | `/api/v1/commands` | `Command.Execute` | Send command to device |
| GET | `/api/v1/commands/{commandId}` | `Command.Read` | Get command status |
| GET | `/api/v1/commands` | `Command.Read` | List commands (filter) |
| DELETE | `/api/v1/commands/{commandId}` | `Command.Execute` | Cancel pending command |
| POST | `/api/v1/commands/batch` | `Command.Execute` | Batch command |

---

## 🔐 Authentication & Authorization

- **Azure AD (Entra ID)** JWT Bearer tokens
- Scopes: `Config.Read`, `Config.Write`, `Command.Read`, `Command.Execute`
- Configured in `appsettings.json` → `AzureAd` section

---

## 🏭 Deployment

### Docker

```bash
docker build -f src/RemoteConfig.Api/Dockerfile -t iot-remote-config src/
docker run -p 8080:8080 --env-file .env iot-remote-config
```

### Azure (Bicep)

```bash
az deployment group create \
  --resource-group iot-dev-rg \
  --template-file infra/bicep/main.bicep \
  --parameters infra/bicep/parameters.dev.json
```

### Kubernetes (AKS)

```bash
export ACR_NAME=youracr
export IMAGE_TAG=latest
export ENVIRONMENT=dev
envsubst < infra/k8s/deployment.yaml | kubectl apply -f -
```

---

## 📁 Project Structure

```
iot-remote-config/
├── .github/
│   ├── workflows/ci.yml          ← 6-step CI pipeline
│   ├── workflows/cd.yml          ← Environment-gated CD
│   ├── CODEOWNERS
│   └── pull_request_template.md
├── src/
│   ├── RemoteConfig.sln           ← VS2022 solution
│   ├── RemoteConfig.Core/         ← Models + Interfaces (no deps)
│   ├── RemoteConfig.Infrastructure/ ← Dapper, Redis, Service Bus, IoT Hub
│   ├── RemoteConfig.Api/         ← ASP.NET Core 8 REST API
│   └── coverlet.runsettings
├── tests/
│   └── RemoteConfig.Tests/       ← xUnit + Moq + FluentAssertions
├── infra/
│   ├── bicep/main.bicep          ← Azure infra as code
│   ├── bicep/parameters.dev.json
│   ├── k8s/deployment.yaml      ← K8s + HPA + Ingress
│   └── sql/schema.sql            ← DB schema + stored procs
├── docs/adr/                      ← Architecture Decision Records
├── .editorconfig
├── Directory.Build.props
└── README.md
```

---

## 🧪 Testing

| Test Suite | Count | Coverage Target |
|------------|-------|----------------|
| RemoteConfigServiceTests | 6 | Config push/rollback/versioning |
| CommandServiceTests | 6 | Send/cancel/batch/status |
| ConfigRollbackEngineTests | 4 | Diff generation |
| ConfigsControllerTests | 6 | REST endpoints |
| CommandsControllerTests | 5 | REST endpoints |
| **Total** | **27** | **≥80%** |

---

## 📋 Phase 3 Task Completion Matrix

| Sub-Phase | Task | Status |
|------------|------|--------|
| 3A | Project scaffold & data models | ✅ |
| 3B | Config management API | ✅ |
| 3C | Command dispatcher | ✅ |
| 3D | Audit, rollback & batch engine | ✅ |
| 3E | Infrastructure as Code | ✅ |
| 3F | Tests & documentation | ✅ |

---

## 🔗 Related Services

- `iot-device-registry` — Device registration & lifecycle
- `iot-inventory` — Inventory management & state sync
- `iot-telemetry-ingest` — Telemetry ingestion & TimescaleDB
- `iot-admin-portal` — Angular 22 management UI

---

## License

Proprietary — Tietoevry 2024
