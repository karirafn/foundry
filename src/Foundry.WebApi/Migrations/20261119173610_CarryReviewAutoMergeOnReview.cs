using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class CarryReviewAutoMergeOnReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A review row written before ReviewIssue carried the flag has none; false hands a retried review's
            // approval to a person rather than merging it.
            migrationBuilder.Sql(
                "UPDATE issues SET review_auto_merge = 0 WHERE state = 'review' AND review_auto_merge IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE issues SET review_auto_merge = NULL WHERE state = 'review'");
        }
    }
}
