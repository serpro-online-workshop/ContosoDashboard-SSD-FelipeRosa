<!--
Sync Impact Report
Version change: uninitialized -> 1.0.0
Modified principles:
- Principle 1 placeholder -> I. Training-First Scope
- Principle 2 placeholder -> II. Layered Application Design
- Principle 3 placeholder -> III. Authenticated Access and User Isolation
- Principle 4 placeholder -> IV. Offline Persistence and Data Integrity
- Principle 5 placeholder -> V. Verifiable, Incremental Delivery
Added sections: Technical and Training Constraints; Development Workflow and Quality Gates
Removed sections: None
Follow-up TODOs: Confirm the original ratification date.
This report is temporary and must be removed before the amendment is committed.
-->

# ContosoDashboard Constitution

## Core Principles

### I. Training-First Scope
ContosoDashboard MUST remain a fictional training application and MUST NOT be represented as
production-ready. Features MUST be suitable for local training without requiring cloud accounts
or external services unless a feature specification explicitly authorizes that dependency. Mock
authentication and sample data MUST be described as training implementations, not as production
security controls.

### II. Layered Application Design
UI components MUST present and collect information, application services MUST own business
operations, and the data layer MUST own persistence. New dependencies MUST be registered through
dependency injection. Pages MUST NOT access the database context directly. Infrastructure
abstractions MUST be used when they provide a real implementation choice, such as local versus
cloud storage, rather than adding abstraction without a concrete need.

### III. Authenticated Access and User Isolation
Protected pages MUST require authentication. User identity MUST be obtained from the authenticated
claims rather than trusted from user-controlled input. Service operations that read or change
user- or project-scoped data MUST verify that the requesting user is authorized by ownership,
project membership, or an applicable role before returning or changing that data. Role-sensitive
operations MUST enforce the corresponding authorization policy in addition to page visibility.

### IV. Offline Persistence and Data Integrity
Application data MUST use the configured EF Core persistence layer and local SQLite by default.
New behavior MUST work without network access unless its approved specification requires an
external integration. Data writes MUST validate authorization and input before persistence, and
related records MUST preserve the model's declared relationships and integrity constraints.

### V. Verifiable, Incremental Delivery
Feature work MUST be defined by testable user outcomes before implementation. Implementation
tasks MUST be traceable to the approved specification and plan. Changes MUST be validated with a
successful project build and focused tests; where automated tests are not available, the change
MUST include a reproducible manual verification procedure. Scope MUST remain limited to the
approved feature and its necessary dependencies.

## Technical and Training Constraints

The application targets .NET 10 and uses ASP.NET Core Blazor Server, Entity Framework Core, and
SQLite. Changes to the framework, persistence provider, or hosting model MUST update the project
configuration and relevant developer documentation together. The mock login flow is for training
only; production authentication, deployment, availability, and compliance requirements are out
of scope unless separately specified and reviewed.

## Development Workflow and Quality Gates

Feature development MUST follow the repository's Spec Kit workflow: specify user outcomes, clarify
material ambiguity, plan the technical approach, generate traceable tasks, and implement against
those artifacts. Specifications and plans MUST identify relevant authorization, offline operation,
data integrity, and verification requirements. Before completion, contributors MUST build the
project and run the focused tests or documented manual checks. Reviews MUST verify compliance
with this constitution and identify any unmet acceptance criteria.

## Governance

This constitution governs feature specifications, plans, tasks, implementation, and review. Any
conflict MUST be resolved in favor of this constitution unless the constitution is amended first.
Amendments MUST state their rationale and impact, update the version and amendment date, and be
reviewed before dependent feature artifacts are changed. Compliance MUST be reviewed during
specification and plan approval and verified again during implementation review.

Versioning follows semantic versioning: MAJOR for backward-incompatible principle or governance
changes, MINOR for added principles or materially expanded requirements, and PATCH for clarifying
or non-semantic edits. A new constitution starts at version 1.0.0.

**Version**: 1.0.0 | **Ratified**: TODO(RATIFICATION_DATE): Confirm original adoption date | **Last Amended**: 2026-10-20
