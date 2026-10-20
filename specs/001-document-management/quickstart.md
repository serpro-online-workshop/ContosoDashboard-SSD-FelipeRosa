# Quickstart: Validate Document Upload and Management

## Prerequisites

- .NET 10 SDK.
- Local write access to the application content directory for the SQLite database and configured private upload root.
- No cloud account or internet connection is required for application data or file storage. If no local malware scanner is configured, upload proceeds only after the user sees the specified scan-unavailable warning.

## Setup and Test

From the repository root:

```sh
dotnet restore ContosoDashboard/ContosoDashboard.csproj
dotnet test ContosoDashboard.Tests/ContosoDashboard.Tests.csproj
dotnet run --project ContosoDashboard/ContosoDashboard.csproj
```

The app applies EF Core migrations to its SQLite database. A fresh database gets the full schema from the initial migration and the document compatibility migration. For a complete legacy database created by `EnsureCreated`, startup verifies the seven legacy tables and required key columns, records the initial migration as a baseline, then applies the idempotent document-schema migration. Existing application rows are not dropped or reseeded during this baseline. Back up `ContosoDashboard/ContosoDashboard.db` before upgrading. If the legacy schema is partial or unrecognized, startup stops without resetting the file; resolve it from a backup before retrying. Uploaded files are stored under the configured private directory outside `wwwroot`.

Open the local URL printed by `dotnet run`, then sign in through the mock login selector. Seeded identities include Ni Kang (Employee), Floris Kregel (Team Lead), Camille Nicole (Project Manager), and System Administrator. Ni Kang and Floris Kregel are in Engineering; the sample project includes both as members.

## Validation Scenarios

1. **Successful upload and warning state**: As Ni Kang, upload a supported PDF under 25 MB with a title and required category. With no scanner configured, verify the first attempt displays a warning and does not save the file. Explicitly opt in and retry; confirm the upload succeeds with an `Unavailable` scan outcome and appears in My Documents. Confirm the private file is not directly addressable under `wwwroot`.
2. **Upload rejection**: Submit a file larger than 25 MB, an unsupported extension, an empty file, and a controlled scanner fixture that returns infected. Verify each failure is clear and no document is available to list, search, download, or preview. Use a deterministic scanner fake for automated tests; do not use real malware samples.
3. **File validation and metadata**: Test supported PDF, DOCX, XLSX, PPTX, TXT, JPEG, and PNG fixtures; verify that mismatched or invalid content is rejected even if the browser reports an allowed MIME type. Confirm categories remain text values and file type accepts up to 255 characters.
4. **Browse and search**: Create documents across the supported categories and project/personal scopes. Verify title/date/category/size sorting, category/project/date filters, pagination, and search by title, description, tags, uploader, and project. Only authorized documents may appear.
5. **Authorization isolation**: As an employee, attempt to guess another user's document ID and access download/preview directly; verify no content is returned. Verify an active user share and department share grants read access, and revocation or department changes remove that access. Verify project members can read project documents and only the project manager can manage all documents in that project.
6. **Update and deletion**: As the owner, edit metadata, replace a file, and permanently delete after confirmation. Confirm replacement does not retain historical versions, deleted documents are unavailable from lists/search/download/preview, and the deletion activity snapshot remains available to administrators.
7. **Task, project, dashboard, and notifications**: Verify project documents appear in project details, task-associated uploads inherit the task's project, dashboard recents show the latest five owned uploads and a user-scoped count, and share/project-upload events create the specified in-app notifications.
8. **Administrator reporting**: Verify an administrator can see document type/uploader/access reports and a non-administrator cannot access the report route or its data.
9. **Offline behavior**: Start the app with no external services and no scanner. Verify upload, local storage, browsing, project/task access, and notification behavior operate locally. The first unavailable-scan attempt must warn and require explicit opt-in before retry.

## Performance Checks

Use a recorded local training environment (OS, CPU, memory, and whether the browser/app share a host). Seed 500 documents and measure list, search, and preview flows after warm-up; record repeat-run results and verify the targets in the spec. Use supported 25 MB files and time upload excluding user interaction. Performance results are environment-specific and must not be represented as production guarantees.

## Expected Result

The project builds and tests successfully; each scenario enforces the access boundaries and upload behavior in the spec; unsupported, over-limit, and confirmed infected files are rejected; unavailable/inconclusive scan states warn but are not represented as clean; stored file paths remain private and server-generated.
