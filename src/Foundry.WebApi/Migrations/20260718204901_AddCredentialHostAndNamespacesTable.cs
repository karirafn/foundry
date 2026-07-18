using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddCredentialHostAndNamespacesTable : Migration
    {
        // Auto-generated migration — arrays are not mutated by CreateIndex
        private static readonly string[] CredentialNamespaceIndexColumns = ["host", "value"];
        private static readonly string[] AccountBaseUrlNameIndexColumns = ["base_url", "name"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Keep the physical table name "accounts" — a rename is cosmetic and would require
            // updating the account_id FK column in monitored_repositories, adding migration risk.
            // The accounts table will continue to store credentials through Step 5 of this feature.

            migrationBuilder.DropIndex(
                name: "ix_accounts_base_url_name",
                table: "accounts");

            migrationBuilder.AddColumn<string>(
                name: "host",
                table: "accounts",
                type: "TEXT",
                unicode: false,
                maxLength: 253,
                nullable: false,
                defaultValue: "");

            // Backfill host from base_url — extract the DNS host from the stored URL.
            // base_url is stored as a full URL (e.g. "https://github.com" or "https://gitlab.example.com/").
            // Extract the segment after "://" up to the first "/" or end-of-string.
            migrationBuilder.Sql(
                """
                UPDATE accounts
                SET host = CASE
                    WHEN instr(substr(base_url, instr(base_url, '://') + 3), '/') = 0
                        THEN substr(base_url, instr(base_url, '://') + 3)
                    ELSE
                        substr(
                            base_url,
                            instr(base_url, '://') + 3,
                            instr(substr(base_url, instr(base_url, '://') + 3), '/') - 1)
                END
                WHERE host = '';
                """);

            migrationBuilder.CreateTable(
                name: "credential_namespaces",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    credential_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    host = table.Column<string>(type: "TEXT", unicode: false, maxLength: 253, nullable: false),
                    value = table.Column<string>(type: "TEXT", unicode: false, maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_credential_namespaces", x => x.id);
                    table.ForeignKey(
                        name: "FK_credential_namespaces_accounts_credential_id",
                        column: x => x.credential_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_credential_namespaces_credential_id",
                table: "credential_namespaces",
                column: "credential_id");

            migrationBuilder.CreateIndex(
                name: "ix_credential_namespaces_host_value",
                table: "credential_namespaces",
                columns: CredentialNamespaceIndexColumns,
                unique: true);

            // Seed credential_namespaces from existing monitored_repositories.
            // For each distinct (credential_id, host, namespace) triple — where namespace is the
            // first path segment of the repository slug's owner — insert one credential_namespace row.
            //
            // The slug is stored as "owner/reponame" where owner may be multi-segment
            // (e.g. "efla/databridge/reponame" has owner "efla/databridge").
            // The first path segment of the owner is always substr(slug, 1, instr(slug, '/') - 1),
            // which gives "efla" for the example above, or the full owner for single-segment owners
            // like "octocat" in "octocat/project".
            //
            // This guarantees that existing repositories resolve to the same credential after the
            // namespace model is activated in Steps 4-5.
            migrationBuilder.Sql(
                """
                INSERT OR IGNORE INTO credential_namespaces (id, credential_id, host, value)
                SELECT
                    lower(hex(randomblob(4))) || '-' ||
                    lower(hex(randomblob(2))) || '-' ||
                    '4' || substr(lower(hex(randomblob(2))), 2) || '-' ||
                    substr('89ab', abs(random()) % 4 + 1, 1) ||
                    substr(lower(hex(randomblob(2))), 2) || '-' ||
                    lower(hex(randomblob(6))),
                    r.account_id,
                    a.host,
                    substr(r.slug, 1, instr(r.slug, '/') - 1)
                FROM monitored_repositories r
                JOIN accounts a ON a.id = r.account_id
                GROUP BY r.account_id, a.host, substr(r.slug, 1, instr(r.slug, '/') - 1);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Note: Down drops the credential_namespaces table and the host column, and restores the
            // (base_url, name) unique index. However, Down cannot restore the seeded namespace rows
            // or reverse the dedup deletions made by the previous unique-index migration. This is
            // lossy by design — development databases are disposable.
            migrationBuilder.DropTable(
                name: "credential_namespaces");

            migrationBuilder.DropColumn(
                name: "host",
                table: "accounts");

            migrationBuilder.CreateIndex(
                name: "ix_accounts_base_url_name",
                table: "accounts",
                columns: AccountBaseUrlNameIndexColumns,
                unique: true);
        }
    }
}
