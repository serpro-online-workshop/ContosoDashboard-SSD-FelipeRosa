# Tasks: Document Upload and Management

**Input**: Design documents from `specs/001-document-management/`  
**Prerequisites**: `plan.md`, `spec.md`, `research.md`, `data-model.md`, `contracts/document-management-ui.md`, `quickstart.md`

**Tests**: The plan calls for focused xUnit service tests using SQLite in-memory. These are included per user story; this is not a TDD mandate.

**Organization**: Tasks are grouped by the five prioritized user stories for independent implementation and verification.

## Phase 1: Setup

**Purpose**: Establish shared project and local-storage configuration.

- [X] T001 [P] Create the .NET 10 xUnit test project with a reference to the application and required test packages in `ContosoDashboard.Tests/ContosoDashboard.Tests.csproj`
- [X] T002 [P] Configure the private upload root, optional ClamAV command, and scan timeout settings in `ContosoDashboard/appsettings.json`
- [X] T003 [P] Ignore private uploaded files and local test database artifacts in `.gitignore`

---

## Phase 2: Foundational

**Purpose**: Establish shared persistence, storage, scanning, and service contracts before user-story implementation.

- [X] T004 [P] Create the Document entity with integer `DocumentId`; `Title` "Required, max 255 characters"; `Description` "Optional, max 2,000 characters"; `Category` "Required text, max 50 characters; allowlisted values"; `OriginalFileName` "Required, max 255 characters"; `StoragePath` "Required relative path, unique"; `FileType` "Required, max 255 characters"; `FileSizeBytes` "Required positive integer, at most 26,214,400"; required UTC upload time/uploader; optional project/task links; and scan outcome in `ContosoDashboard/Models/Document.cs`
- [X] T005 [P] Create the DocumentTag entity with required normalized tag text, max 50 characters, a document/tag composite key, and a tag index in `ContosoDashboard/Models/DocumentTag.cs`
- [X] T006 [P] Create the DocumentShare entity with integer key, nullable user or department recipient, department max 100 characters, grant/revocation actors and UTC dates, and exactly one recipient target in `ContosoDashboard/Models/DocumentShare.cs`
- [X] T007 [P] Create append-only DocumentActivity fields, including title and target snapshots max 255 characters, actor, type, and UTC time, without a cascading document foreign key in `ContosoDashboard/Models/DocumentActivity.cs`
- [X] T008 Configure document DbSets, relationships, delete behaviors, uniqueness, and query indexes in `ContosoDashboard/Data/ApplicationDbContext.cs` (depends on T004-T007)
- [X] T009 Add document navigation collections to User, Project, and TaskItem in `ContosoDashboard/Models/User.cs`, `ContosoDashboard/Models/Project.cs`, and `ContosoDashboard/Models/TaskItem.cs` (depends on T004)
- [X] T010 [P] Define validated storage-root and scanner configuration options in `ContosoDashboard/Services/DocumentStorageOptions.cs`
- [X] T011 [P] Define the local storage contract for staging, finalize, replace, read, and delete operations in `ContosoDashboard/Services/IFileStorageService.cs`
- [X] T012 Implement path normalization, generated GUID filenames, private staging, bounded file operations, and compensating cleanup outside `wwwroot` in `ContosoDashboard/Services/LocalFileStorageService.cs` (depends on T010-T011)
- [X] T013 [P] Define scanner verdicts for Clean, Infected, Unavailable, and Inconclusive in `ContosoDashboard/Services/IMalwareScanner.cs`
- [X] T014 Implement the optional ClamAV adapter with shell-free process invocation, bounded timeout, and distinct clean/infected/unavailable results in `ContosoDashboard/Services/ClamAvMalwareScanner.cs` (depends on T010, T013)
- [X] T015 Define upload, search/list, access, update, replacement, deletion, sharing, task/project, and report operations in `ContosoDashboard/Services/IDocumentService.cs`
- [X] T016 Create the EF Core migration for the document entities and indexes, and document a backed-up, non-destructive adoption path for existing `EnsureCreated` SQLite databases in `ContosoDashboard/Migrations/` and `specs/001-document-management/quickstart.md` (depends on T008-T009)
- [X] T017 Register storage/scanner options and services, register database migrations at startup instead of `EnsureCreated`, and preserve existing SQLite data in `ContosoDashboard/Program.cs` (depends on T010-T014, T016)

**Checkpoint**: The project has document schema, local storage/scanning services, and startup migration wiring; no user-story UI is required yet.

---

## Phase 3: User Story 1 - Upload a Work Document (Priority: P1)

**Goal**: Authenticated employees upload supported files with required metadata, progress, and clear scan/result messaging.

**Independent Test**: Upload a valid file under 25 MB and verify its metadata/list entry; verify unsupported/oversize/infected files are rejected, while unavailable or inconclusive scanning warns and permits continuation.

### Implementation

- [X] T018 [US1] Implement authorized per-file upload orchestration, allowlist/content validation, metadata validation, malware verdict handling, staging/finalization, scan-outcome persistence, and upload activity in `ContosoDashboard/Services/DocumentService.cs` (depends on T012-T015, T017)
- [X] T019 [P] [US1] Build the `/documents` upload form with multiple selection, required title/category, optional description/project/tags, bounded streaming progress, per-file warnings, result states, and a minimal My Documents list showing newly uploaded metadata in `ContosoDashboard/Pages/Documents.razor` (depends on T018)
- [X] T020 [P] [US1] Register `IDocumentService` and add a Documents navigation link in `ContosoDashboard/Program.cs` and `ContosoDashboard/Shared/NavMenu.razor` (depends on T018)

### Tests

- [X] T021 [P] [US1] Add SQLite-backed upload service tests for allowed types, invalid/mismatched content, 25 MB boundary, required metadata, clean/infected/unavailable/inconclusive scanner outcomes, and storage/metadata failure compensation in `ContosoDashboard.Tests/DocumentManagement/DocumentUploadTests.cs` (depends on T018)

**Checkpoint**: A logged-in employee can complete and independently verify the upload workflow.

---

## Phase 4: User Story 2 - Find and Use an Authorized Document (Priority: P1)

**Goal**: Users find only documents they may access, and retrieve them through protected download or preview routes.

**Independent Test**: With representative personal, shared, and project records, verify sorting/filtering/search and prove unauthorized list, search, download, and preview attempts expose no document content.

### Implementation

- [X] T022 [US2] Implement authorized paginated list, project list, sort, filter, and search queries across title, description, tags, uploader, and project in `ContosoDashboard/Services/DocumentService.cs` (depends on T018)
- [X] T023 [P] [US2] Extend `/documents` with My Documents listing, filters, sorting, pagination, search, and empty/error states in `ContosoDashboard/Pages/Documents.razor` (depends on T022)
- [X] T024 [P] [US2] Add authenticated download and PDF/JPEG/PNG preview routes that resolve only by document ID, re-check access, stream private bytes, set safe response headers, and record downloads in `ContosoDashboard/Program.cs` (depends on T022)
- [X] T025 [US2] Add an authorized project-documents list and download/preview links to project details in `ContosoDashboard/Pages/ProjectDetails.razor` (depends on T022-T024)

### Tests

- [X] T026 [US2] Add service tests for owner, recipient, project-member, project-manager, department-team, and administrator visibility, plus unauthorized list/search/download access in `ContosoDashboard.Tests/DocumentManagement/DocumentAccessTests.cs` (depends on T022-T024)

**Checkpoint**: Users can discover and retrieve accessible files, while denied identities cannot infer or retrieve private content.

---

## Phase 5: User Story 3 - Maintain and Share Documents (Priority: P2)

**Goal**: Owners and authorized team/project managers update, replace, delete, and share documents under explicit permission rules.

**Independent Test**: As an owner, edit metadata, replace, share, revoke, and delete after confirmation; verify recipient access, role boundaries, notifications, and retained deletion activity.

### Implementation

- [X] T027 [US3] Enforce owner, current-department Team Lead, project-manager, and administrator management rules in every document mutation in `ContosoDashboard/Services/DocumentService.cs`
- [X] T028 [US3] Implement authorized metadata updates for title, description, category, and tags with validation and activity recording in `ContosoDashboard/Services/DocumentService.cs` (depends on T027)
- [X] T029 [US3] Implement staged file replacement and confirmed permanent deletion with compensating filesystem cleanup and preserved deletion activity in `ContosoDashboard/Services/DocumentService.cs` and `ContosoDashboard/Services/LocalFileStorageService.cs` (depends on T027-T028)
- [X] T030 [US3] Implement user/department share creation and revocation, activity events, and share notifications; add DocumentShared notification type in `ContosoDashboard/Services/DocumentService.cs` and `ContosoDashboard/Models/Notification.cs` (depends on T027)
- [X] T031 [P] [US3] Add owner edit/replace/delete/share controls, validation, and deletion confirmation to `ContosoDashboard/Pages/Documents.razor` (depends on T028-T030)
- [X] T032 [P] [US3] Build Shared with Me for active direct and department shares and add its navigation link in `ContosoDashboard/Pages/SharedDocuments.razor` and `ContosoDashboard/Shared/NavMenu.razor` (depends on T030)

### Tests

- [X] T033 [US3] Add tests for owner-only edits, project-manager/team-lead scope, share/revoke access changes, notifications, replacement failure, deletion cleanup, and retained activity in `ContosoDashboard.Tests/DocumentManagement/DocumentManagementTests.cs` (depends on T027-T032)

**Checkpoint**: Authorized users can manage documents without granting recipients unintended mutation rights.

---

## Phase 6: User Story 4 - Use Documents With Projects, Tasks, and the Dashboard (Priority: P2)

**Goal**: Documents are usable in task/project context and surfaced through dashboard recents, counts, and relevant notifications.

**Independent Test**: Attach an authorized document to a task/project and verify context views, user-scoped dashboard recents/count, and project notifications.

### Implementation

- [X] T034 [US4] Validate task access and task/project association consistency during document upload and attachment in `ContosoDashboard/Services/DocumentService.cs` (depends on T018, T022, T027)
- [X] T035 [P] [US4] Add a task details route with authorized document listing, attachment, and upload actions in `ContosoDashboard/Pages/TaskDetails.razor` (depends on T034)
- [X] T036 [P] [US4] Extend dashboard queries and UI with the current user's document count and five most recent uploads in `ContosoDashboard/Services/DashboardService.cs` and `ContosoDashboard/Pages/Index.razor` (depends on T022)
- [X] T037 [P] [US4] Notify relevant project members on project document upload and add DocumentAddedToProject notification type in `ContosoDashboard/Services/DocumentService.cs` and `ContosoDashboard/Models/Notification.cs` (depends on T030, T034)

### Tests

- [X] T038 [US4] Add tests for task/project association, task access enforcement, user-scoped dashboard results, and project-member upload notifications in `ContosoDashboard.Tests/DocumentManagement/DocumentContextTests.cs` (depends on T034-T037)

**Checkpoint**: Document workflows are available in their task/project context and dashboard summaries remain user-scoped.

---

## Phase 7: User Story 5 - Review Document Activity (Priority: P3)

**Goal**: Administrators review activity and generate document-type, uploader, and access-pattern reports.

**Independent Test**: Perform uploads, downloads, shares, and deletions; verify each event is represented and administrators alone can retrieve reports.

### Implementation

- [X] T039 [US5] Implement administrator-scoped activity queries and aggregates for file types, uploaders, and access patterns in `ContosoDashboard/Services/DocumentService.cs` (depends on T018, T024, T029-T030, T037)
- [X] T040 [P] [US5] Build the administrator document activity/report page with report summaries and access-denied behavior in `ContosoDashboard/Pages/DocumentReports.razor` (depends on T039)
- [X] T041 [P] [US5] Apply the existing Administrator authorization policy to the report route and enforce administrator checks in the service in `ContosoDashboard/Program.cs` and `ContosoDashboard/Services/DocumentService.cs` (depends on T039)

### Tests

- [X] T042 [US5] Add tests for activity coverage, report aggregation, retained deletion events, and denial of report data to non-administrators in `ContosoDashboard.Tests/DocumentManagement/DocumentReportsTests.cs` (depends on T039-T041)

**Checkpoint**: Administrators can inspect audit summaries, and other roles cannot retrieve report data.

---

## Phase 8: Polish and Cross-Cutting Concerns

**Purpose**: Complete developer guidance and run feature-wide validation.

- [X] T043 Update .NET 10/SQLite prerequisites and document private storage, scan warnings, offline limits, and test commands in `README.md`
- [X] T044 Complete run, migration-adoption, manual scenario, and performance-measurement instructions in `specs/001-document-management/quickstart.md`
- [X] T045 Run the application build and focused test project, fixing failures in `ContosoDashboard/ContosoDashboard.csproj` and `ContosoDashboard.Tests/ContosoDashboard.Tests.csproj`
- [ ] T046 Measure upload, 500-document list/search, and preview targets in the documented training environment and record results in `specs/001-document-management/quickstart.md`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies. T001-T003 can run in parallel.
- **Foundational (Phase 2)**: Depends on Setup. Entity models T004-T007 can run in parallel; schema, relationships, storage, scanner, service contracts, migration, and startup wiring follow their listed dependencies. This phase blocks all user stories.
- **User Stories (Phases 3-7)**: Depend on the complete foundation and proceed in priority order. US2 builds on uploaded records and shares route/query surfaces with US1. US3 builds on discovery/access. US4 uses document, project, task, and notification services. US5 aggregates activity emitted by earlier stories.
- **Polish (Phase 8)**: Depends on all desired user stories.

### User Story Dependencies

- **US1 (P1)**: Starts after Phase 2; provides the first end-to-end document upload increment.
- **US2 (P1)**: Depends on US1's document records and upload service.
- **US3 (P2)**: Depends on US1 and US2 for existing documents and access/retrieval behavior.
- **US4 (P2)**: Depends on US1 and US2; integrates with existing task/project/dashboard services.
- **US5 (P3)**: Depends on activity events from US1-US4 before reports can be meaningfully verified.

### Parallel Opportunities

- Setup tasks T001-T003 are independent files and can run in parallel.
- Foundation entity tasks T004-T007 can run in parallel; T010-T011 and T013 can also proceed in parallel with those models.
- In US1, upload UI T019, service registration/navigation T020, and upload service tests T021 can proceed in parallel after T018.
- In US2, project detail UI T025 can proceed separately from the `/documents` list UI T023 after the query contract exists.
- In US3, management controls T031 and Shared with Me T032 are separate components after the service methods are available.
- In US4, task details T035 and dashboard summary T036 touch separate pages/services and can proceed in parallel after their service contracts are established.

### Parallel Example: User Story 1

```text
After T018:
- T019: Build upload form and minimal document list in ContosoDashboard/Pages/Documents.razor
- T020: Register service and navigation in ContosoDashboard/Program.cs and ContosoDashboard/Shared/NavMenu.razor
- T021: Add service tests in ContosoDashboard.Tests/DocumentManagement/DocumentUploadTests.cs
```

### Parallel Example: User Story 2

```text
After T022:
- T023: Build the searchable document list in ContosoDashboard/Pages/Documents.razor
- T024: Add protected download and preview routes in ContosoDashboard/Program.cs
```

### Parallel Example: User Story 3

```text
After T028, T029, and T030:
- T031: Add owner management controls in ContosoDashboard/Pages/Documents.razor
- T032: Build Shared with Me in ContosoDashboard/Pages/SharedDocuments.razor and navigation in ContosoDashboard/Shared/NavMenu.razor
```

### Parallel Example: User Story 4

```text
After T034:
- T035: Add task document context in ContosoDashboard/Pages/TaskDetails.razor
- T037: Add project upload notifications in ContosoDashboard/Services/DocumentService.cs and ContosoDashboard/Models/Notification.cs
T036 can proceed independently in ContosoDashboard/Services/DashboardService.cs and ContosoDashboard/Pages/Index.razor after T022.
```

### Parallel Example: User Story 5

```text
After T039:
- T040: Build the admin report page in ContosoDashboard/Pages/DocumentReports.razor
- T041: Enforce the admin policy in ContosoDashboard/Program.cs and ContosoDashboard/Services/DocumentService.cs
```

## Implementation Strategy

### MVP First

1. Complete Phase 1 Setup.
2. Complete Phase 2 Foundational work.
3. Complete Phase 3 User Story 1.
4. Validate upload success, invalid file rejection, confirmed infection rejection, and scan-unavailable warning behavior independently.
5. Continue to US2 for authorized discovery and protected retrieval before broadening collaboration.

### Incremental Delivery

1. Foundation → schema/storage/scanner contracts and migration path.
2. US1 → upload and persist files with metadata.
3. US2 → browse/search/download/preview with access checks.
4. US3 → update, replace, delete, and share.
5. US4 → task/project/dashboard integration.
6. US5 → admin activity reports.
7. Polish → documentation and performance validation.
