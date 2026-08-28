## Description

<!-- Provide a clear description of the changes in this PR -->

## Type of Change

- [ ] 🆕 New feature
- [ ] 🐛 Bug fix
- [ ] 📝 Documentation update
- [ ] ♻️ Refactor (no functional change)
- [ ] 🔒 Security patch
- [ ] 🚀 Performance improvement

## Scope

- [ ] Remote Config API (`/api/v1/configs`)
- [ ] Command API (`/api/v1/commands`)
- [ ] Infrastructure / Bicep
- [ ] CI/CD pipeline
- [ ] Tests only

## Checklist

- [ ] Code compiles without warnings (`dotnet build -warnaserror`)
- [ ] All unit tests pass (`dotnet test`)
- [ ] New code has ≥80% line coverage
- [ ] No `{{PLACEHOLDER}}` values in `appsettings.json` or infra files
- [ ] No secrets committed (verified by gitleaks)
- [ ] Swagger/OpenAPI annotations added/updated
- [ ] `appsettings.json` changes do NOT contain real credentials
- [ ] Dockerfile builds successfully
- [ ] ADR added for significant architectural decisions

## Security Checklist

- [ ] Input validation on all new endpoints
- [ ] Authentication/Authorization policies verified
- [ ] No sensitive data in logs
- [ ] SQL queries use parameterized statements (Dapper)
- [ ] Redis keys use proper namespacing

## Test Plan

<!-- Describe how reviewers can verify your changes -->

## Related Issues

<!-- Link to GitHub issues or ADRs -->
