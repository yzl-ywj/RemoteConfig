# ADR-0003: Configuration Rollback Strategy

## Status

Accepted (2024-11)

## Context

When a bad config causes device issues, we need to revert quickly. Options:
- Overwrite current config with old values (destructive)
- Create new version from old snapshot (non-destructive)
- Store diffs only (compact but complex)

## Decision

**Non-destructive rollback**: Create a new version by copying the target version's JSON.
Mark the target as `RolledBack`. Keep all intermediate versions.

## Rationale

- Full audit trail preserved (every change is a new row)
- Can rollback to ANY historical version, not just previous
- Simple implementation: just re-save old JSON as new version
- Diff engine provides human-readable change summary
- Meets compliance requirement for tamper-proof audit log

## Consequences

- Storage grows with version count (mitigated by `MaxVersionsPerDevice=50`)
- Rollback creates a new version number (not the original)
- Diff display needs to handle non-contiguous versions
- Stored procedure `sp_cleanup_old_versions` prunes beyond limit
