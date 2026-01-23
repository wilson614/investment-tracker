<!--
=============================================================================
SYNC IMPACT REPORT
=============================================================================
Version Change: 0.0.0 → 1.0.0 (MAJOR - Initial constitution adoption)

Modified Principles: N/A (initial creation)

Added Sections:
  - Core Principles (5 principles)
    - I. Clean Architecture
    - II. Multi-Tenancy
    - III. Accuracy First
    - IV. Self-Hosted Friendly
    - V. Technology Stack
  - Technical Constraints
  - Quality Standards
  - Governance

Removed Sections: N/A (initial creation)

Templates Requiring Updates:
  ✅ .specify/templates/plan-template.md - Constitution Check section exists
  ✅ .specify/templates/spec-template.md - Compatible with current principles
  ✅ .specify/templates/tasks-template.md - Structure supports web app layout

Deferred TODOs: None

=============================================================================
-->

# Investment Management Platform Constitution

## Core Principles

### I. Clean Architecture

The system MUST strictly separate concerns into three distinct layers following .NET 8
conventions:

- **Domain Layer**: Contains business entities, value objects, domain services, and
  domain events. MUST NOT reference Infrastructure or API layers. MUST be the innermost
  layer with no external dependencies.

- **Infrastructure Layer**: Contains database contexts, repositories, external service
  clients, and framework-specific implementations. MUST only depend on Domain layer
  abstractions.

- **API Layer**: Contains controllers, DTOs, middleware, and presentation logic.
  MUST depend on Domain abstractions and use Infrastructure through dependency injection.

**Rationale**: Clean Architecture enables independent testing of business logic,
simplifies technology migrations, and ensures the domain model remains pure and
focused on business rules rather than technical concerns.

### II. Multi-Tenancy

The system MUST support multiple family members (Users) with strict data isolation:

- Each User MUST have their own isolated portfolio data by default.
- Cross-portfolio access MUST be explicitly granted through a sharing mechanism.
- All database queries MUST include tenant context filtering to prevent data leakage.
- API endpoints MUST validate user ownership before returning or modifying any data.
- Shared portfolios MUST maintain audit trails of who accessed or modified data.

**Rationale**: Family members require privacy for their individual investments while
retaining the option to share specific portfolios for joint financial planning.

### III. Accuracy First

Financial calculations MUST be precise and verifiable:

- XIRR (Extended Internal Rate of Return) calculations MUST use proven numerical
  methods (Newton-Raphson or Brent's method) with configurable tolerance.
- Weighted Average Cost calculations MUST handle stock splits, dividends, and
  partial sells correctly.
- All monetary values MUST use `decimal` type (not `double` or `float`) to avoid
  floating-point errors.
- Currency conversion rates MUST preserve full precision and include timestamp metadata.
- Every financial calculation MUST have corresponding unit tests with known edge cases.
- Rounding rules MUST be explicitly defined and consistently applied across the system.

**Rationale**: Investment tracking requires absolute precision. A miscalculation
in cost basis or returns could lead to incorrect tax reporting or poor financial
decisions.

### IV. Self-Hosted Friendly

The platform MUST be deployable on personal infrastructure:

- The entire stack MUST run in Docker containers with a single `docker-compose.yml`.
- PostgreSQL MUST be the primary database with support for data backup/restore.
- Resource consumption MUST be suitable for NAS/VPS environments (target: <512MB RAM
  idle, <2GB under load).
- Configuration MUST be environment-variable driven with sensible defaults.
- The system MUST function fully offline after initial deployment (no external
  service dependencies for core features).
- Upgrade paths MUST include database migration scripts that are non-destructive.

**Rationale**: Family users prefer to keep sensitive financial data on their own
hardware rather than trusting third-party cloud services.

### V. Technology Stack

The project MUST adhere to the following technology choices:

- **Backend**: C# with .NET 8 (latest LTS conventions)
- **Frontend**: JavaScript/TypeScript with React
- **Database**: PostgreSQL (primary), with Entity Framework Core as ORM
- **API Style**: RESTful with JSON responses; consider GraphQL only if complexity
  warrants it in future iterations
- **Testing**: xUnit for backend, Vitest + React Testing Library for frontend
- **Containerization**: Docker with multi-stage builds for production images

**Rationale**: This stack provides type safety, mature tooling, and aligns with
the maintainer's expertise for long-term sustainability.

## Technical Constraints

### Database Standards

- All tables MUST include `created_at` and `updated_at` timestamps.
- Soft deletes SHOULD be used for financial records to maintain audit trails.
- Foreign keys MUST be properly indexed for query performance.
- Migrations MUST be reversible where technically feasible.

### API Standards

- All endpoints MUST return consistent error response structures.
- Authentication MUST use JWT tokens with configurable expiration.
- Rate limiting SHOULD be configurable but disabled by default for self-hosted use.
- API versioning MUST be supported from the initial release.

### Security Requirements

- Passwords MUST be hashed using bcrypt or Argon2.
- Sensitive configuration (API keys, connection strings) MUST NOT be committed to
  version control.
- HTTPS MUST be enforced in production deployments.
- Input validation MUST occur at API boundary before reaching domain layer.

## Quality Standards

### Testing Requirements

- Domain layer business logic MUST have >80% unit test coverage.
- Financial calculation methods MUST have 100% coverage with edge case tests.
- Integration tests MUST cover all critical user journeys.
- Frontend components MUST have tests for user interactions and state changes.

### Code Quality

- All public APIs MUST have XML documentation comments.
- Magic numbers and strings MUST be extracted to named constants or configuration.
- Code MUST pass configured linting rules before merge.
- Complex business logic MUST include inline comments explaining the "why".

### Documentation

- API endpoints MUST be documented with OpenAPI/Swagger.
- Database schema MUST be documented with entity relationship diagrams.
- Deployment procedures MUST be documented in `README.md` or `docs/deployment.md`.

## Governance

This constitution supersedes all other development practices. Compliance is mandatory.

### Amendment Procedure

1. Propose changes via pull request to this file.
2. Document rationale for changes in PR description.
3. Amendments require maintainer approval.
4. Upon approval, update `CONSTITUTION_VERSION` following semantic versioning:
   - **MAJOR**: Principle removal or incompatible redefinition
   - **MINOR**: New principle or significant expansion
   - **PATCH**: Clarifications, wording improvements, typo fixes

### Compliance Review

- All pull requests MUST verify alignment with constitution principles.
- Code reviews SHOULD reference relevant principles when suggesting changes.
- Constitution violations MUST be justified in writing if temporarily permitted.

### Runtime Guidance

For day-to-day development decisions not covered by this constitution, refer to:
- `.specify/` directory for feature specifications and implementation plans
- `docs/` directory for technical documentation and ADRs (Architecture Decision Records)

**Version**: 1.0.1 | **Ratified**: 2026-01-06 | **Last Amended**: 2026-01-24
