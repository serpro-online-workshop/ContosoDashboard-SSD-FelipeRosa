using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContosoDashboard.Migrations
{
    /// <inheritdoc />
    public partial class DocumentSchemaCompatibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS "Documents" (
                    "DocumentId" INTEGER NOT NULL CONSTRAINT "PK_Documents" PRIMARY KEY AUTOINCREMENT,
                    "Title" TEXT NOT NULL CONSTRAINT "CK_Documents_Title_Length" CHECK (length("Title") <= 255),
                    "Description" TEXT NULL,
                    "Category" TEXT NOT NULL CONSTRAINT "CK_Documents_Category_Length" CHECK (length("Category") <= 50),
                    "OriginalFileName" TEXT NOT NULL CONSTRAINT "CK_Documents_OriginalFileName_Length" CHECK (length("OriginalFileName") <= 255),
                    "StoragePath" TEXT NOT NULL CONSTRAINT "CK_Documents_StoragePath_Length" CHECK (length("StoragePath") <= 1024),
                    "FileType" TEXT NOT NULL CONSTRAINT "CK_Documents_FileType_Length" CHECK (length("FileType") <= 255),
                    "FileSizeBytes" INTEGER NOT NULL CONSTRAINT "CK_Documents_FileSize" CHECK ("FileSizeBytes" BETWEEN 1 AND 26214400),
                    "UploadedAtUtc" TEXT NOT NULL,
                    "UploadedByUserId" INTEGER NOT NULL,
                    "ProjectId" INTEGER NULL,
                    "TaskId" INTEGER NULL,
                    "MalwareScanOutcome" INTEGER NOT NULL,
                    CONSTRAINT "FK_Documents_Projects_ProjectId" FOREIGN KEY ("ProjectId") REFERENCES "Projects" ("ProjectId") ON DELETE RESTRICT,
                    CONSTRAINT "FK_Documents_Tasks_TaskId" FOREIGN KEY ("TaskId") REFERENCES "Tasks" ("TaskId") ON DELETE RESTRICT,
                    CONSTRAINT "FK_Documents_Users_UploadedByUserId" FOREIGN KEY ("UploadedByUserId") REFERENCES "Users" ("UserId") ON DELETE RESTRICT
                );

                CREATE TABLE IF NOT EXISTS "DocumentActivities" (
                    "DocumentActivityId" INTEGER NOT NULL CONSTRAINT "PK_DocumentActivities" PRIMARY KEY AUTOINCREMENT,
                    "DocumentId" INTEGER NULL,
                    "DocumentTitleSnapshot" TEXT NOT NULL CONSTRAINT "CK_DocumentActivities_Title_Length" CHECK (length("DocumentTitleSnapshot") <= 255),
                    "ActivityType" INTEGER NOT NULL,
                    "ActorUserId" INTEGER NOT NULL,
                    "OccurredAtUtc" TEXT NOT NULL,
                    "TargetSnapshot" TEXT NULL CONSTRAINT "CK_DocumentActivities_Target_Length" CHECK ("TargetSnapshot" IS NULL OR length("TargetSnapshot") <= 255),
                    CONSTRAINT "FK_DocumentActivities_Users_ActorUserId" FOREIGN KEY ("ActorUserId") REFERENCES "Users" ("UserId") ON DELETE RESTRICT
                );

                CREATE TABLE IF NOT EXISTS "DocumentShares" (
                    "DocumentShareId" INTEGER NOT NULL CONSTRAINT "PK_DocumentShares" PRIMARY KEY AUTOINCREMENT,
                    "DocumentId" INTEGER NOT NULL,
                    "RecipientUserId" INTEGER NULL,
                    "RecipientDepartment" TEXT NULL CONSTRAINT "CK_DocumentShares_Department_Length" CHECK ("RecipientDepartment" IS NULL OR length("RecipientDepartment") <= 100),
                    "GrantedByUserId" INTEGER NOT NULL,
                    "GrantedAtUtc" TEXT NOT NULL,
                    "RevokedByUserId" INTEGER NULL,
                    "RevokedAtUtc" TEXT NULL,
                    CONSTRAINT "CK_DocumentShare_Recipient" CHECK (("RecipientUserId" IS NOT NULL AND "RecipientDepartment" IS NULL) OR ("RecipientUserId" IS NULL AND "RecipientDepartment" IS NOT NULL)),
                    CONSTRAINT "FK_DocumentShares_Documents_DocumentId" FOREIGN KEY ("DocumentId") REFERENCES "Documents" ("DocumentId") ON DELETE CASCADE,
                    CONSTRAINT "FK_DocumentShares_Users_GrantedByUserId" FOREIGN KEY ("GrantedByUserId") REFERENCES "Users" ("UserId") ON DELETE RESTRICT,
                    CONSTRAINT "FK_DocumentShares_Users_RecipientUserId" FOREIGN KEY ("RecipientUserId") REFERENCES "Users" ("UserId") ON DELETE RESTRICT,
                    CONSTRAINT "FK_DocumentShares_Users_RevokedByUserId" FOREIGN KEY ("RevokedByUserId") REFERENCES "Users" ("UserId") ON DELETE RESTRICT
                );

                CREATE TABLE IF NOT EXISTS "DocumentTags" (
                    "DocumentId" INTEGER NOT NULL,
                    "Tag" TEXT NOT NULL CONSTRAINT "CK_DocumentTags_Tag_Length" CHECK (length("Tag") <= 50),
                    CONSTRAINT "PK_DocumentTags" PRIMARY KEY ("DocumentId", "Tag"),
                    CONSTRAINT "FK_DocumentTags_Documents_DocumentId" FOREIGN KEY ("DocumentId") REFERENCES "Documents" ("DocumentId") ON DELETE CASCADE
                );

                CREATE UNIQUE INDEX IF NOT EXISTS "IX_Documents_StoragePath" ON "Documents" ("StoragePath");
                CREATE INDEX IF NOT EXISTS "IX_Documents_UploadedByUserId" ON "Documents" ("UploadedByUserId");
                CREATE INDEX IF NOT EXISTS "IX_Documents_ProjectId" ON "Documents" ("ProjectId");
                CREATE INDEX IF NOT EXISTS "IX_Documents_TaskId" ON "Documents" ("TaskId");
                CREATE INDEX IF NOT EXISTS "IX_Documents_UploadedAtUtc" ON "Documents" ("UploadedAtUtc");
                CREATE INDEX IF NOT EXISTS "IX_Documents_Category" ON "Documents" ("Category");
                CREATE INDEX IF NOT EXISTS "IX_DocumentActivities_DocumentId" ON "DocumentActivities" ("DocumentId");
                CREATE INDEX IF NOT EXISTS "IX_DocumentActivities_ActivityType" ON "DocumentActivities" ("ActivityType");
                CREATE INDEX IF NOT EXISTS "IX_DocumentActivities_ActorUserId" ON "DocumentActivities" ("ActorUserId");
                CREATE INDEX IF NOT EXISTS "IX_DocumentActivities_OccurredAtUtc" ON "DocumentActivities" ("OccurredAtUtc");
                CREATE INDEX IF NOT EXISTS "IX_DocumentShares_DocumentId" ON "DocumentShares" ("DocumentId");
                CREATE INDEX IF NOT EXISTS "IX_DocumentShares_RecipientUserId" ON "DocumentShares" ("RecipientUserId");
                CREATE INDEX IF NOT EXISTS "IX_DocumentShares_RecipientDepartment" ON "DocumentShares" ("RecipientDepartment");
                CREATE INDEX IF NOT EXISTS "IX_DocumentShares_RevokedAtUtc" ON "DocumentShares" ("RevokedAtUtc");
                CREATE INDEX IF NOT EXISTS "IX_DocumentTags_Tag" ON "DocumentTags" ("Tag");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The compatibility migration is intentionally additive and idempotent.
            // The initial migration owns the schema rollback for fresh databases.
        }
    }
}
