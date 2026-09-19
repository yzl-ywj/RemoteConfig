# ADR-0001: Device Configuration Storage

## Status

Accepted (2024-11)

## Context

Device configurations need versioning, auditability, and fast reads. Options considered:
- Azure SQL (relational, ACID, JSON support)
- Cosmos DB (NoSQL, global distribution)
- Azure Blob (cheap, but no query)

## Decision

**Azure SQL** with JSON columns for configuration payload.

## Rationale

- Strong consistency for config versioning (no lost updates)
- JSON support in SQL Server 2022 allows querying config fields
- Cheaper than Cosmos DB at our scale (<1M configs)
- Existing SQL expertise in the team
- Dapper provides lightweight, fast access

## Consequences

- Config payload size limited to 1MB (NVARCHAR(MAX) practical limit)
- Schema migrations needed for new metadata fields
- Backup/restore via SQL native tools
