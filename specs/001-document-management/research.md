# Research: Document Upload and Management

**Feature**: `001-document-management`  
**Date**: 2026-10-20

## 1. Application Architecture and Integration

**Decision**: Extend the existing .NET 10 Blazor Server application with scoped document services, EF Core entities, and UI components. Keep database and file operations out of Razor pages. Add only the service abstractions required for local file storage and optional malware scanning.

**Rationale**: The application already uses a single Blazor Server host, scoped service interfaces, EF Core SQLite, claim-derived user IDs, and service-level authorization checks. There is no existing document storage, scanner, controller API, or test project. A separate API or repository layer would add architecture not justified by this feature.

**Alternatives considered**: A separate upload API or a generic repository layer. Neither is needed for the current single-application/offline training scope.

## 2. Local File Storage

**Decision**: Store file bytes on the local filesystem under a configurable root that defaults to `AppData/uploads` beneath the application content root. Keep this directory outside `wwwroot`. Store only a relative storage path and metadata in SQLite. Generate an opaque GUID-based name before any database insertion, and never use the client filename as a physical path. Organize storage beneath uploader and project-or-personal partitions. Use a private staging directory for incomplete uploads and replacements.

**Rationale**: This preserves offline operation and future storage-provider replacement while preventing direct static-file access and filename/path traversal. Unique server-generated paths avoid collisions. Relative paths avoid persisting machine-specific absolute paths. OWASP recommends allowlisted file types, generated filenames, size limits, authorization, and storage outside the web root when separate storage is not available.

**Alternatives considered**: Storing bytes in SQLite increases database size and read/write cost; storing under `wwwroot` bypasses authorization; cloud storage violates the feature's offline constraint.

## 3. Blazor Upload Streaming and Progress

**Decision**: Use Blazor `InputFile` for multi-file selection. Enforce a 25 MB maximum with `IBrowserFile.OpenReadStream(maxAllowedSize)`, then copy each selected file sequentially through a bounded buffer to a generated staging path. Update the UI with bytes copied while streaming. Do not buffer full documents in `MemoryStream`. Limit the number of files accepted in a single selection through a configurable batch limit, while allowing users to submit additional batches.

**Rationale**: Microsoft documents direct streaming to `FileStream` as the recommended approach for larger uploads and warns against loading entire files into memory in server-side components. `InputFile` supports multiple selection and progress can be computed during buffered reads. Sequential processing keeps working memory bounded and avoids overlapping reads from the same Blazor Server circuit. The server-enforced stream limit is authoritative; browser metadata is only an early UX check.

**Alternatives considered**: A full-file `MemoryStream` is simpler but adds avoidable memory pressure and denial-of-service risk. A separate multipart controller is supported but unnecessary for this server-interactive app.

## 4. File Validation and Malware-Scanner Policy

**Decision**: Enforce an extension allowlist and validate file content/type rather than trusting the browser-supplied MIME type. Reject empty files, unsupported formats, files over the limit, and confirmed infected files. Define an optional local-scanner interface with a configurable ClamAV command-line adapter. Invoke a configured executable without a shell, against a generated staging path, with a bounded timeout. If no scanner is configured, it cannot run, or its result is inconclusive, allow the upload only after displaying a clear warning and record that scan outcome in the document metadata. Never label such a file as clean.

**Rationale**: The accepted clarification explicitly chose warn-and-allow for unavailable or inconclusive scanning and rejection for confirmed infections. ClamAV documents `clamscan` as a local one-time scanner that does not require a daemon, which suits optional offline use. A pluggable boundary avoids a mandatory external package or cloud dependency. OWASP advises layered validation, generated names, explicit size limits, authorization, and malware scanning when available.

**Alternatives considered**: Failing closed would contradict the accepted clarification and prevent use on fresh offline training installations. Cloud scanning is unavailable offline. Treating a missing scanner as a clean result would misrepresent the security state and is rejected.

**Risk retained by the approved policy**: A document that cannot be scanned may contain malware. The UI must warn before the user proceeds, the file must remain outside the web root, and download/preview remain authorization-checked. This training behavior is not a production security guarantee.

## 5. Authorization and Sharing

**Decision**: Centralize access decisions in the document service and re-check them for list, search, metadata, share, delete, download, and preview operations. Derive the requester ID and role from the authenticated principal. Query the current user's department from the database for team scope rather than trusting a department value submitted by the browser or relying on a stale department claim. Apply the spec's scopes: owner, explicit user share, department share, project manager/member, department team lead, and administrator. A department share applies to current department membership.

**Rationale**: Existing login claims include user ID, display name, email, and role, but not department. Existing team-directory behavior uses the user's current department, and project membership is already persisted. Re-checking at each service boundary prevents list/download paths from drifting apart and addresses IDOR risks.

**Alternatives considered**: Trusting a UI-provided role, department, or file path is unsafe. Adding department to the cookie alone risks stale access when a user's department changes. A generic ACL framework is unnecessary for the specified owner/user/department/project rules.

## 6. Data Consistency and Schema Evolution

**Decision**: Keep file bytes and SQLite metadata separate. Write and validate a staged file before creating its document record; clean up staged/final files if the corresponding database operation fails. For replacements, stage a new uniquely named file and update metadata only after it is written and validated, then clean up the old file. Use a database transaction for related document/share/activity rows. File cleanup must be compensating, logged, and retryable; no failed filesystem operation may make a private path publicly accessible. Preserve deletion audit data independently of the hard-deleted document row.

Use EF Core migrations for schema evolution rather than relying on `EnsureCreated` to add tables to an existing database. The repository currently has no migration history; validate a fresh database and document a backup/recreation or one-time baseline procedure for existing training databases. Never silently delete a user's SQLite file at startup.

**Rationale**: Filesystem operations and SQLite commits cannot form one atomic transaction. Explicit staging and compensation minimize orphaned records and inaccessible file references. The current `EnsureCreated` initialization does not evolve an already-created schema, while migrations provide a repeatable schema history.

**Alternatives considered**: Persisting directly to the final path before validation exposes incomplete or rejected files. Keeping `EnsureCreated` alone leaves existing databases without the new tables. Automatically dropping an existing database risks data loss and is rejected.

## 7. Search and List Performance

**Decision**: Execute filtering, sorting, and search in SQLite through EF Core; paginate results rather than materializing all documents. Use case-insensitive matching across title, description, tags, uploader display name, and project name. Add indexes for uploader, project, task, upload time, category, and relevant share lookup columns. Begin with relational search (`Contains`/translated `LIKE`) and add SQLite FTS only if measured acceptance tests cannot meet the target for the supported dataset.

**Rationale**: The specification's list target is up to 500 documents, and current services use direct asynchronous EF Core queries. A full-text subsystem would increase schema and maintenance complexity before evidence requires it.

**Alternatives considered**: In-memory filtering loads unauthorized or unnecessary rows and cannot meet the intended paging behavior. SQLite FTS is a possible later optimization, not a prerequisite for this bounded dataset.

## 8. Download and Preview Delivery

**Decision**: Provide authenticated application routes for document download and preview. Resolve a document by ID through the authorization-aware service, then stream from the private storage service; never accept a filesystem path from the route or return a direct static-file URL. Use attachment disposition for downloads and inline disposition only for supported PDF and image previews, with a server-selected content type and `nosniff` behavior.

**Rationale**: This allows existing Blazor UI to use normal links while enforcing authorization on every retrieval. The app currently has no controller endpoints; a small authenticated endpoint can coexist with its Blazor fallback without adding a separate API project.

**Alternatives considered**: Static URLs under `wwwroot` cannot enforce per-user authorization. Returning local paths exposes implementation details and is not a valid browser contract.

## 9. Automated Test Strategy

**Decision**: Add a focused xUnit test project targeting .NET 10. Test document services against EF Core SQLite in-memory using an explicitly opened connection retained for each test's lifetime. Inject deterministic scanner and storage test doubles for infected, clean, unavailable, inconclusive, and I/O failure cases. Include authorization tests for owner, department, project, share, and administrator paths. Add a small set of end-to-end/manual UI checks in `quickstart.md`.

**Rationale**: No test project exists. SQLite in-memory exercises relational constraints and SQLite query translation more faithfully than EF Core's non-relational InMemory provider. Microsoft documents keeping the in-memory SQLite connection open because closing it destroys the database.

**Alternatives considered**: Testing only against the InMemory provider can miss relational behavior. Using production `ContosoDashboard.db` couples tests to local state and harms repeatability.

## 10. Developer Documentation

**Decision**: Update the root README as part of implementation to describe the actual .NET 10 and SQLite configuration, the private local document-storage root, the optional scanner and warn-and-allow fallback, and how to run the new focused tests. Correct stale SQL Server LocalDB and .NET 8 setup guidance.

**Rationale**: The current README still documents the previous .NET 8/SQL Server setup, while the project now targets .NET 10 and SQLite. The constitution requires relevant developer documentation to stay aligned with framework and persistence configuration. The new upload flow also needs a clear statement that unavailable scanning is not a clean result.

**Alternatives considered**: Leaving the README unchanged would direct developers to an unavailable LocalDB connection and misrepresent the application's security behavior.

## References

- [Microsoft: Blazor file uploads (.NET 10)](https://learn.microsoft.com/aspnet/core/blazor/file-uploads?view=aspnetcore-10.0) — bounded `OpenReadStream`, direct disk streaming, progress, untrusted browser metadata, and server-side file handling.
- [Microsoft: EF Core testing without the production database](https://learn.microsoft.com/ef/core/testing/testing-without-the-database#sqlite-in-memory) — SQLite in-memory setup and connection lifetime.
- [OWASP File Upload Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/File_Upload_Cheat_Sheet.html) — file validation, random filenames, authorization, size limits, storage location, and scanning.
- [ClamAV: Scanning](https://docs.clamav.net/manual/Usage/Scanning.html) — local `clamscan` operation and scanner setup considerations.
