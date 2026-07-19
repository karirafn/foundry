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

            // Backfill host from base_url — extract the normalized host matching the runtime behaviour.
            // At runtime, Credential.Host is set from baseUrl.Value.Host (System.Uri.Host), which
            // is already lowercased and port-stripped by the .NET Uri class.
            // To match that exactly in SQL we:
            //   1. Extract the authority (everything between "://" and the next "/" or end-of-string).
            //   2. Lowercase it via lower(...).
            //   3. Strip any trailing ":port" suffix — present in self-hosted URLs like
            //      "https://gitlab.corp.example.com:8443/" — by keeping only what precedes the first ":".
            //
            // A CTE deduplicates the authority extraction so each step is readable in isolation.
            migrationBuilder.Sql(
                """
                WITH extracted AS (
                    SELECT
                        id,
                        -- Raw authority: between "://" and the first "/" in the remainder (or end-of-string)
                        lower(CASE
                            WHEN instr(substr(base_url, instr(base_url, '://') + 3), '/') = 0
                                THEN substr(base_url, instr(base_url, '://') + 3)
                            ELSE
                                substr(
                                    base_url,
                                    instr(base_url, '://') + 3,
                                    instr(substr(base_url, instr(base_url, '://') + 3), '/') - 1)
                        END) AS authority
                    FROM accounts
                    WHERE host = ''
                )
                UPDATE accounts
                SET host = CASE
                    -- Strip ":port" if present in the lowercased authority
                    WHEN instr((SELECT authority FROM extracted WHERE extracted.id = accounts.id), ':') > 0
                        THEN substr(
                            (SELECT authority FROM extracted WHERE extracted.id = accounts.id),
                            1,
                            instr((SELECT authority FROM extracted WHERE extracted.id = accounts.id), ':') - 1)
                    -- No port — use the lowercased authority as-is
                    ELSE
                        (SELECT authority FROM extracted WHERE extracted.id = accounts.id)
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
            // full owner (all slug segments except the last) — insert one credential_namespace row.
            //
            // The slug is stored as "owner/reponame" where owner may be multi-segment
            // (e.g. "efla/databridge/reponame" has owner "efla/databridge").
            // This mirrors NamespaceDerivation.FromWritableRepositories, which uses slug[..lastSlash]
            // (i.e. everything before the LAST '/') to compute the owner.
            //
            // SQLite has no REVERSE or LASTINDEXOF, so we extract the full owner using the
            // rtrim character-set trick:
            //   rtrim(slug, replace(slug, '/', ''))
            // strips all non-'/' characters from the right of slug until it reaches a '/', giving
            // "owner/" (with a trailing slash). A second rtrim('/', ...) removes the slash.
            // This handles both single-segment GitHub owners ("octocat/project" → "octocat") and
            // multi-segment GitLab owners ("efla/databridge/repo" → "efla/databridge") correctly,
            // ensuring distinct sub-groups under different credentials resolve independently and
            // no cross-credential mis-routing can occur on the unique(host, namespace) constraint.
            //
            // De-dup uses GROUP BY on the derived owner so each (credential_id, host, owner) triple
            // produces exactly one INSERT OR IGNORE row.
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
                    -- Full owner = everything before the last '/' in the slug.
                    -- rtrim with the character set of all non-'/' slug chars strips the repo-name
                    -- segment from the right, leaving "owner/"; a second rtrim removes the slash.
                    rtrim(rtrim(r.slug, replace(r.slug, '/', '')), '/')
                FROM monitored_repositories r
                JOIN accounts a ON a.id = r.account_id
                WHERE instr(r.slug, '/') > 0
                GROUP BY r.account_id, a.host, rtrim(rtrim(r.slug, replace(r.slug, '/', '')), '/');
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
