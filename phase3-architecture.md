# Phase 3 — Remote Configuration & Command Architecture

## Overview

Phase 3 delivers the **Remote Config & Command Service** (`iot-remote-config`), a .NET 8 microservice that enables IT administrators to:

1. **Push configuration changes** to individual devices or device groups (desired properties)
2. **Send one-way commands** (reboot, restart app, toggle GPIO, etc.) with ACK/timeout tracking
3. **Audit every config change and command** with full rollback capability
4. **Batch operations** with rate-limiting to avoid overwhelming devices

The command delivery path supports two modes, selectable via config (`RemoteConfig:DispatchMode`):

- **`IoTHub`** — API service calls IoT Hub Service SDK directly (simplest; good for getting end-to-end working first).
- **`ServiceBus`** (default) — API enqueues commands to Azure Service Bus; the new **`CommandBridge.Function`** (Azure Function v4, .NET 8 Isolated) consumes the queue and forwards each command to IoT Hub as a Cloud-to-Device message. This decouples the API from IoT Hub, adds buffering/retry/dead-lettering, and is the recommended production topology.

> **Note on Azure Portal configuration:** IoT Hub Message Routing only supports *device-to-cloud* routing (Device → Service Bus). There is no Portal setting to make Service Bus forward back into IoT Hub for C2D — that bridge is exactly what `CommandBridge.Function` implements in code (Service Bus Trigger → `ServiceClient.SendAsync`).

---

## Architecture Diagram

```mermaid
flowchart TB
    subgraph ADMIN["🖥️ Admin Portal (Angular 22)"]
        UI1[Config Editor UI]
        UI2[Command Console]
        UI3[Batch Operations Wizard]
    end

    subgraph APIGW["🌐 API Gateway Layer"]
        GW[Azure API Management]
    end

    subgraph RCS["⚙️ Remote Config Service (AKS)"]
        RC_API[REST API<br/>ASP.NET Core 8]
        RC_SVC[RemoteConfigService<br/>Business Logic]
        CMD_SVC[CommandService<br/>C2D + Direct Methods]
        AUDIT[SqlAuditLogger<br/>Append-Only Log]
        ROLLBACK[ConfigRollbackEngine<br/>Version Snapshots]
    end

    subgraph BRIDGE["🌉 Command Bridge (NEW)"]
        FUNC[Azure Function v4<br/>.NET 8 Isolated<br/>Service Bus Trigger]
    end

    subgraph MESSAGING["📨 Azure Messaging"]
        SB_CMD[Service Bus<br/>iot-commands queue + DLQ]
        EH_EVT[Event Hub<br/>Command Results]
        IOTHUB[Azure IoT Hub<br/>C2D + Direct Methods]
    end

    subgraph DEVICES["📡 Devices"]
        D1[MQTT Device<br/>listens: devices/{id}/cmd]
        D2[Edge Gateway<br/>LwM2M / CoAP]
        D3[Rich Device<br/>WebSocket]
    end

    subgraph DATA["💾 Data Layer"]
        SQL[(Azure SQL<br/>Configs + Audit + Commands)]
        REDIS[(Redis Cache<br/>Pending Commands + ACK)]
        KV[Azure Key Vault<br/>Certs + Secrets]
    end

    subgraph OBS["📊 Observability"]
        AI[Azure Monitor / App Insights]
        GRAF[Grafana Dashboard]
    end

    ADMIN -->|HTTPS + JWT| GW
    GW -->|authenticated| RC_API
    RC_API --> RC_SVC
    RC_API --> CMD_SVC
    RC_SVC --> AUDIT
    RC_SVC --> ROLLBACK

    CMD_SVC -->|enqueue CommandRequest JSON| SB_CMD
    SB_CMD -->|ServiceBusTrigger| FUNC
    FUNC -->|ServiceClient.SendAsync<br/>C2D Message| IOTHUB
    IOTHUB -->|MQTT push| D1
    IOTHUB -->|LwM2M| D2
    IOTHUB -->|WebSocket| D3

    D1 -->|ACK / response| IOTHUB
    IOTHUB -->|telemetry route| EH_EVT
    EH_EVT -->|persist result| SQL

    RC_SVC -->|cache pending| REDIS
    CMD_SVC -->|check timeout| REDIS
    AUDIT --> SQL
    ROLLBACK --> SQL

    RC_API --> SQL
    RC_API --> REDIS
    RC_API --> KV

    RCS --> AI
    FUNC --> AI
    AI --> GRAF
```

### Why the Bridge Function?

| Requirement | How the Bridge satisfies it |
|---|---|
| Decouple API from IoT Hub rate limits | API only enqueues to Service Bus; Bridge throttles sends to IoT Hub |
| Retry / dead-letter on C2D failures | Service Bus built-in retry + DLQ; Bridge logs poison messages |
| Per-device ordering | Service Bus sessions can be enabled per `deviceId` |
| Managed Identity in Azure | Bridge uses `DefaultAzureCredential` → `IotHubTokenCredential` (role: *Azure IoT Hub Data Sender*) |
| Local development | Bridge falls back to `IoTHubServiceConnection` connection string |

---

## Key Design Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| **Command transport** | Azure IoT Hub C2D + Direct Methods | Native device-agnostic transport; Direct Methods for sync, C2D for async |
| **Command queuing** | Azure Service Bus (`iot-commands` queue + DLQ) | Decouples API from IoT Hub; built-in retry + dead-letter |
| **Service Bus → IoT Hub bridge** | `CommandBridge.Function` (Azure Function v4, .NET 8 Isolated) | No Portal-native forwarding exists for C2D; code bridge is the supported pattern |
| **Bridge auth (Azure)** | Managed Identity + `IotHubTokenCredential` | No shared keys stored; least-privilege role assignment |
| **Bridge auth (local)** | Shared-access connection string (`IoTHubServiceConnection`) | Simplest local debugging |
| **Command wire format** | Serialized `CommandRequest` JSON (UTF-8) | Reuses Core model; payload sent as UTF-8 bytes so devices parse JSON directly |
| **Config storage** | Azure SQL (configs + version history) | ACID transactions; easy rollback |
| **Pending command cache** | Redis | Sub-second ACK lookup; TTL auto-expiry |
| **Audit log** | Append-only SQL table | Tamper-proof trail |
| **Batch operations** | Background worker with rate limiter | Prevents thundering herd |
| **Rollback** | Versioned snapshots (JSON diff) | One-click revert |
| **Authentication** | Azure AD (Entra ID) + Scope policies | `Config.Read` / `Config.Write` / `Command.Read` / `Command.Execute` |
| **Idempotency** | Client-generated `CommandId` (Guid) + Redis dedup | Safe retries; no duplicate commands |

---

## Task Breakdown (6 Sub-Phases)

### Sub-Phase 3A — Project Scaffold & Data Models (Week 1, Days 1-2)

| # | Task | Deliverable |
|---|------|-------------|
| 3A.1 | Create solution structure (Api/Core/Infrastructure/Tests/Bridge) | `.sln` + 5 `.csproj` files |
| 3A.2 | Define core models: `DeviceConfig`, `ConfigVersionInfo`, `CommandRequest`, `CommandResult`, `AuditEntry` | C# POCOs in Core |
| 3A.3 | Define interfaces: `IConfigRepository`, `ICommandDispatcher`, `ICommandService`, `IAuditLogger`, `IConfigRollback` | Interface contracts |
| 3A.4 | Define enums: `ConfigStatus`, `CommandType`, `CommandStatus`, `CommandDeliveryMode` | Enum types |
| 3A.5 | Configure DI, appsettings, .editorconfig, Directory.Build.props | Consistent with other services |
| 3A.6 | GitHub Actions CI/CD (same 6-step pipeline) | `.github/workflows/ci.yml` + `cd.yml` |

### Sub-Phase 3B — Config Management API (Week 1, Days 3-5)

| # | Task | Deliverable |
|---|------|-------------|
| 3B.1 | `GET /api/v1/configs/{deviceId}` — get current config + version history | Controller + Service |
| 3B.2 | `PUT /api/v1/configs/{deviceId}` — push desired config (creates new version) | Full implementation |
| 3B.3 | `POST /api/v1/configs/{deviceId}/rollback/{versionId}` — rollback to version | Rollback engine |
| 3B.4 | `GET /api/v1/configs/{deviceId}/versions` — list config versions | Paginated list |
| 3B.5 | `POST /api/v1/configs/batch` — batch push to device group | Batch with rate-limit |
| 3B.6 | SQL DDL for `device_configs`, `config_versions`, `audit_log`, `commands` tables | `infra/sql/schema.sql` |
| 3B.7 | Dapper repository implementation | `SqlConfigRepository`, `SqlCommandStore`, `SqlAuditLogger` |

### Sub-Phase 3C — Command Dispatcher & Bridge (Week 2, Days 1-3)

| # | Task | Deliverable |
|---|------|-------------|
| 3C.1 | `POST /api/v1/commands` — send command to device(s) | Controller + Service |
| 3C.2 | `GET /api/v1/commands/{commandId}` — query command status | Status tracking |
| 3C.3 | `GET /api/v1/commands` — list commands with filters | Paginated search |
| 3C.4 | IoT Hub C2D message sender (direct mode) | `IoTHubCommandDispatcher` |
| 3C.5 | Service Bus command queue sender (default mode) | `ServiceBusCommandDispatcher` |
| 3C.6 | **`CommandBridge.Function` — Service Bus Trigger → IoT Hub C2D** | `CommandBridge.Function` project |
| 3C.7 | Redis pending command store (ACK tracking + TTL) | `RedisCommandStore` |

### Sub-Phase 3D — Audit, Rollback & Batch Engine (Week 2, Days 4-5)

| # | Task | Deliverable |
|---|------|-------------|
| 3D.1 | Append-only audit logger (SQL) | `SqlAuditLogger` |
| 3D.2 | Config diff/rollback engine (JSON snapshot comparison) | `ConfigRollbackEngine` |
| 3D.3 | Batch operation worker with rate limiter | `CommandService.SendBatchCommandAsync` |
| 3D.4 | Dead-letter handling (failed commands → DLQ → alert) | Service Bus DLQ |
| 3D.5 | Command timeout detector (Redis TTL → status=timeout) | `CommandTimeoutWatcher` |

### Sub-Phase 3E — Infrastructure as Code (Week 3, Day 1)

| # | Task | Deliverable |
|---|------|-------------|
| 3E.1 | Bicep main.bicep (App Service + SQL + Redis + Service Bus + IoT Hub ref) | `infra/bicep/main.bicep` |
| 3E.2 | Bicep module for Command Bridge Function App (Consumption) | `infra/bicep/modules/command-bridge.bicep` |
| 3E.3 | K8s deployment.yaml + command-bridge.yaml | `infra/k8s/` |
| 3E.4 | Dockerfile (Api, multi-stage non-root) + Bridge Dockerfile | `src/.../Dockerfile` |

### Sub-Phase 3F — Tests & Documentation (Week 3, Days 2-3)

| # | Task | Deliverable |
|---|------|-------------|
| 3F.1 | Unit tests: ConfigService, CommandService, RollbackEngine, Bridge parser | xUnit + Moq |
| 3F.2 | Integration tests: Controllers (REST endpoint tests) | xUnit + TestServer |
| 3F.3 | ADR documents (3 ADRs) | `docs/adr/` |
| 3F.4 | Swagger/OpenAPI annotations | Full API docs |
| 3F.5 | README with local run + Portal configuration notes | Complete README |

---

## REST API Contract Summary

### Config Endpoints

| Method | Path | Scope | Description |
|--------|------|-------|-------------|
| GET | `/api/v1/configs/{deviceId}` | `Config.Read` | Get current desired config |
| PUT | `/api/v1/configs/{deviceId}` | `Config.Write` | Push new config (creates version) |
| GET | `/api/v1/configs/{deviceId}/versions` | `Config.Read` | List version history |
| POST | `/api/v1/configs/{deviceId}/rollback/{versionId}` | `Config.Write` | Rollback to specific version |
| POST | `/api/v1/configs/batch` | `Config.Write` | Batch push to multiple devices |
| GET | `/api/v1/configs/groups/{groupId}` | `Config.Read` | Get group-level config template |

### Command Endpoints

| Method | Path | Scope | Description |
|--------|------|-------|-------------|
| POST | `/api/v1/commands` | `Command.Execute` | Send command to device(s) |
| GET | `/api/v1/commands/{commandId}` | `Command.Read` | Get command status + result |
| GET | `/api/v1/commands` | `Command.Read` | List commands (filter by device/status/date) |
| DELETE | `/api/v1/commands/{commandId}` | `Command.Execute` | Cancel pending command |

---

## Command Flow Sequence

```
Admin → PUT /configs/{deviceId} → RemoteConfigService
                                    ↓
                              Validate config JSON
                                    ↓
                              Save new version (SQL)
                                    ↓
                              Audit log entry
                                    ↓
                              Enqueue ConfigChangeEvent → Service Bus
                                    ↓
                              CommandBridge.Function → IoT Hub → C2D → Device
                                    ↓
                              Device applies config
                                    ↓
                              Device → reports result → Event Hub
                                    ↓
                              CommandResult persisted → SQL
                                    ↓
                              Redis ACK cleared
                                    ↓
                              Admin polls GET /commands/{id} → sees "success"
```

---

## Security Considerations

- All config changes require `Config.Write` scope (Azure AD policy)
- All commands require `Command.Execute` scope
- Config payloads are JSON Schema validated before dispatch
- Audit log is append-only (no UPDATE/DELETE permissions for app identity)
- Device identity verified via IoT Hub (X.509 certificates)
- Command payloads signed with HMAC-SHA256 for tamper detection
- Rate limiting: max 10 commands/sec per device, 100/sec per tenant
- Bridge Function uses Managed Identity in Azure; no connection strings stored at rest

---

## Local Development & Build (Visual Studio 2022)

1. **Prerequisites:** Visual Studio 2022 17.8+ with the *ASP.NET and web development* and *.NET 8 SDK* workloads installed.
2. Open `src\RemoteConfig.sln` — the solution contains 5 projects: `RemoteConfig.Core`, `RemoteConfig.Infrastructure`, `RemoteConfig.Api`, `RemoteConfig.Tests`, and `CommandBridge.Function`.
3. On first open, Visual Studio restores NuGet packages automatically (all projects use `PackageReference` against `Microsoft.NET.Sdk` / `Web` / `Worker` where applicable).
4. Set `RemoteConfig.Api` as the startup project (F5) to launch the Swagger UI locally.
5. For the Bridge Function, open the `CommandBridge.Function` project in **Visual Studio 2022 17.8+** (Azure Functions tooling) and set `local.settings.json` values (`ServiceBusConnection`, `IoTHubServiceConnection` or `IoTHubHost`) — replace the `{{...}}` placeholders before running.
6. Replace all `{{...}}` placeholders in `appsettings*.json` / Bicep / K8s files with real values before deploying.

## Success Criteria

| Metric | Target |
|--------|--------|
| Config push success rate | >99.9% |
| Command delivery P99 latency | <2s (direct method), <5s (C2D via Bridge) |
| Config rollback time | <10s for any historical version |
| Audit log completeness | 100% (every write operation logged) |
| Batch operation throughput | 1000 devices in <5 min |
| Test coverage | ≥80% line coverage |
| VS2022 build | Clean build, zero errors, packages restore successfully |
