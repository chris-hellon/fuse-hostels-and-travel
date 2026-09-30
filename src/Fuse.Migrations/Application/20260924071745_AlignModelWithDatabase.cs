using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fuse.Migrations.Application
{
    /// <inheritdoc />
    public partial class AlignModelWithDatabase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty. These column types (tightened lengths, nullability) were applied
            // by hand to the real database long ago; this migration only brings the EF model and
            // snapshot in line so new databases are created the same way. See HandTunedColumns.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty: see Up().
        }
    }
}
