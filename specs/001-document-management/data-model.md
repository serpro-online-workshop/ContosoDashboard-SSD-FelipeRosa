# Data Model: Document Upload and Management

## Document

Represents one uploaded file and its searchable metadata.

| Field | Type / rule | Meaning |
|---|---|---|
| `DocumentId` | Integer primary key | Stable application identifier, consistent with existing entities. |
| `Title` | Required, max 255 characters | User-provided display title. |
| `Description` | Optional, max 2,000 characters | User-provided searchable description. |
| `Category` | Required text, max 50 characters; allowlisted values | One of Project Documents, Team Resources, Personal Files, Reports, Presentations, Other. |
| `OriginalFileName` | Required, max 255 characters | Untrusted name retained for display only; never used to construct a storage path. |
| `StoragePath` | Required relative path, unique | Server-generated GUID name under the configured storage root. Must not be absolute or escape the root. |
| `FileType` | Required, max 255 characters | Server-validated MIME type. Browser-supplied content type is not authoritative. |
| `FileSizeBytes` | Required positive integer, at most 26,214,400 | Size of the accepted file. |
| `UploadedAtUtc` | Required UTC timestamp | Upload time. |
| `UploadedByUserId` | Required FK to User | Owner/uploader. |
| `ProjectId` | Optional FK to Project | Project association. |
| `TaskId` | Optional FK to TaskItem | Task association. If the task belongs to a project, the associated project must match. |
| `MalwareScanOutcome` | Required enum: Clean, Unavailable, Inconclusive | Records whether scanning succeeded. Infected files are rejected and are never represented as available documents. |

**Relationships and indexes**: User, Project, and TaskItem each relate to zero or more Documents. Restrict deletion of related users/projects/tasks while referenced, or require explicit reassignment/removal. Add indexes for uploader, project, task, upload time, category, and a unique index for `StoragePath`.

**Validation**: The database stores category labels as text. Validate title/category, allowlisted extension and file content, maximum bytes, and all associations before persistence. Keep the generated storage path relative and verify its normalized full path remains beneath the configured storage root.

## DocumentTag

Associates normalized search tags with a document.

| Field | Type / rule | Meaning |
|---|---|---|
| `DocumentId` | FK to Document | Tagged document. |
| `Tag` | Required normalized text, max 50 characters | One custom search tag. |

Use a composite primary/unique key on (`DocumentId`, normalized `Tag`) and an index on `Tag`. Deleting a document removes its tag rows.

## DocumentShare

Represents active or revoked access granted by an owner.

| Field | Type / rule | Meaning |
|---|---|---|
| `DocumentShareId` | Integer primary key | Share identifier. |
| `DocumentId` | Required FK to Document | Shared document. |
| `RecipientUserId` | Nullable FK to User | Specific user recipient. |
| `RecipientDepartment` | Nullable text, max 100 characters | Department recipient, resolved against current department membership. |
| `GrantedByUserId` | Required FK to User | Owner who granted access. |
| `GrantedAtUtc` | Required UTC timestamp | Grant time. |
| `RevokedByUserId` | Optional FK to User | Owner/admin who revoked access. |
| `RevokedAtUtc` | Optional UTC timestamp | Revocation time; null means active. |

Exactly one of `RecipientUserId` or `RecipientDepartment` must be populated. A user share applies to that user; a department share applies to users currently in that department. Add indexes for document, recipient user, department, and active/revoked lookup. Deleting a document removes its shares.

## DocumentActivity

Append-only record for document lifecycle and access reporting. It must survive permanent document deletion.

| Field | Type / rule | Meaning |
|---|---|---|
| `DocumentActivityId` | Integer primary key | Activity identifier. |
| `DocumentId` | Nullable integer, no cascading FK | Original identifier, retained as a snapshot after document deletion. |
| `DocumentTitleSnapshot` | Required text, max 255 characters | Title at activity time. |
| `ActivityType` | Required enum | Uploaded, Replaced, Downloaded, Deleted, Shared, ShareRevoked. |
| `ActorUserId` | Required FK to User | User who performed the action. |
| `OccurredAtUtc` | Required UTC timestamp | Activity time. |
| `TargetSnapshot` | Optional text, max 255 characters | User or department recipient for share events. |

Add indexes for occurrence time, activity type, actor, and document snapshot. Do not cascade-delete activity when a document is permanently removed. Activity retention follows the local database lifetime; no independent retention/expiry policy is specified.

## Access Rules

- Owner: read, edit metadata, replace file, delete, and share own documents.
- Explicit user or department recipient: read and download; no edit/delete/share rights from the grant alone.
- Project member: read project documents; project manager: upload and manage documents associated with projects they manage.
- Team lead: view/manage documents uploaded by users in the same department; this does not grant access to documents outside that department.
- Administrator: full document access and reporting.
- Every list, search, preview, download, and mutation re-evaluates access in the document service. Department scope is resolved from the current User row, not submitted by the client.

## Lifecycle

1. Validate metadata, size, allowlisted type, associations, and authorization.
2. Stream bytes to a generated staging path outside `wwwroot`; scan if a local scanner is configured.
3. Reject and remove the staged file if invalid or confirmed infected. For unavailable/inconclusive scan results, warn the user and record the outcome.
4. Move the accepted file to a generated private path, then create metadata and upload activity. Compensate by deleting staged/final bytes if the database write fails.
5. Replacement stages and validates a new file before updating the current document; no historical version is retained.
6. Sharing records a user/department grant and notification. Revocation timestamps the grant and removes its access.
7. Permanent deletion removes the document and dependent tags/shares, preserves a deletion activity snapshot, and cleans up private storage. Filesystem cleanup failures are logged and retried without making the file available through the application.
