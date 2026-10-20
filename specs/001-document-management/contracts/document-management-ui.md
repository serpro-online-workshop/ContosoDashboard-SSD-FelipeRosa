# UI and Delivery Contract: Document Management

This application has no separate public API client. The contract below describes the user-facing Blazor routes and the authenticated file-delivery routes used by those views.

## User Interface

| Route or context | User-visible behavior | Access |
|---|---|---|
| `/documents` | My Documents: upload, list owned documents, search, sort, filter, edit metadata, replace, delete after confirmation, share, download, and preview supported files. | Authenticated user; own documents and documents otherwise authorized for display. |
| `/documents/shared` | Shared with Me: list documents granted directly or through the current department, with download/preview where supported. | Authenticated recipient; active shares only. |
| `/projects/{projectId:int}` | Project details include its authorized project documents, upload action, and project-document notices. | Project manager or member; only project manager can manage all project documents. |
| `/tasks/{taskId:int}` | Task context lists and attaches documents associated with the task; upload inherits the task's project association. | User authorized to view the task. |
| `/` | Dashboard shows the current user's five latest uploads and their document count. | Authenticated user; counts and recents are user-scoped. |
| `/documents/reports` | Administrator view of document type, uploader, and access-pattern reports. | Administrator only. |

A Documents navigation entry links to `/documents`. Forms show per-file upload progress and explicit validation, scan-warning, success, or error results. No warning may claim that a file is clean when scanning was unavailable or inconclusive.

## Protected File Delivery

- `GET /documents/{documentId:int}/download` streams an authorized document as an attachment.
- `GET /documents/{documentId:int}/preview` streams an authorized PDF or JPEG/PNG inline. Other types are not previewed.
- Both routes require the existing authenticated cookie and re-check the requesting user through the document service. Unknown and unauthorized document IDs must not reveal filesystem paths or document contents.
- The client supplies only a document ID. The server resolves the relative storage path internally; it never accepts a path or filename as a route parameter.
- The server selects the response content type from validated metadata, applies `X-Content-Type-Options: nosniff`, and does not serve stored content from `wwwroot`.
- The download operation records a document activity event. Preview access is not included in the required activity event set unless planning later adds it.

## Role Boundaries

- Owners manage and share their own documents.
- Shared recipients can read, download, and preview but do not acquire owner permissions.
- Department scope is determined from the current user's department in the database.
- Project membership grants read access; project managers manage documents for their projects.
- Administrators can access all documents and reports.
