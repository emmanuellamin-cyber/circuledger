using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CircuLedger.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Database-level protection for the append-only ledger, behind the DbContext SaveChanges check:
    ///  1. REVOKE UPDATE, DELETE, TRUNCATE from PUBLIC and from the role that runs migrations (the table owner).
    ///  2. A trigger that rejects UPDATE/DELETE/TRUNCATE for every role, including owners and superusers,
    ///     which privilege checks alone do not restrict.
    /// For production, run the app as a separate non-owner role that only has SELECT/INSERT (see README).
    /// </summary>
    public partial class LedgerAppendOnlyProtection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                REVOKE UPDATE, DELETE, TRUNCATE ON TABLE ledger_entries FROM PUBLIC;
                REVOKE UPDATE, DELETE, TRUNCATE ON TABLE ledger_entries FROM CURRENT_USER;

                CREATE OR REPLACE FUNCTION ledger_entries_block_mutation() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'ledger_entries is append-only: % is not allowed', TG_OP
                        USING ERRCODE = 'insufficient_privilege';
                END;
                $$;

                CREATE TRIGGER trg_ledger_entries_no_update_delete
                    BEFORE UPDATE OR DELETE ON ledger_entries
                    FOR EACH ROW EXECUTE FUNCTION ledger_entries_block_mutation();

                CREATE TRIGGER trg_ledger_entries_no_truncate
                    BEFORE TRUNCATE ON ledger_entries
                    FOR EACH STATEMENT EXECUTE FUNCTION ledger_entries_block_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_ledger_entries_no_truncate ON ledger_entries;
                DROP TRIGGER IF EXISTS trg_ledger_entries_no_update_delete ON ledger_entries;
                DROP FUNCTION IF EXISTS ledger_entries_block_mutation();
                GRANT UPDATE, DELETE, TRUNCATE ON TABLE ledger_entries TO CURRENT_USER;
                """);
        }
    }
}
