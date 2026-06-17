// Exercise 3 — Migrations workflow
//
// Goal: Evolve a schema across two migrations. Add a column. Generate the
//       migration. Read the auto-generated Up/Down. Then make a *deliberate*
//       change that the EF Core generator gets wrong (a column rename) and
//       hand-fix the migration so it preserves data. Verify with sqlite3.
//       Finally, emit an idempotent SQL script suitable for CI/CD review.
//
// Estimated time: 35 minutes.
//
// HOW TO USE THIS FILE
//
// 1. Start from the project you produced in Exercise 1 (the BookShelf project
//    with the Book entity and InitialCreate migration). If you don't have it
//    handy, copy the model from Exercise 1 verbatim into a fresh project.
//
// 2. Work through each STEP below in order. Each step has a hand-on command
//    sequence followed by a verification step.
//
// 3. The file is structured as comments rather than executable code because
//    the work itself is in the CLI and in the migration files EF Core
//    generates; the C# changes are tiny.
//
// ACCEPTANCE CRITERIA
//
//   [ ] Two new migrations: AddAuthorBio, then RenameMemoToTitle.
//   [ ] The RenameMemoToTitle migration uses RenameColumn(), not Drop+Add.
//   [ ] `dotnet ef database update` applies both cleanly on a fresh database.
//   [ ] `dotnet ef database update <Previous>` rolls back to either migration
//       without errors.
//   [ ] An idempotent script (`schema.sql`) is produced and checked into the repo.
//   [ ] `dotnet build`: 0 Warning(s), 0 Error(s).
//
// ===========================================================================
// STEP 1 — Add an optional Bio column to Book
// ===========================================================================
//
// 1.1  Open src/BookShelf/Program.cs and add a Bio property to Book:
//
//          public sealed class Book
//          {
//              public int Id { get; set; }
//              [Required, MaxLength(200)] public string Title { get; set; } = "";
//              [Required, MaxLength(120)] public string Author { get; set; } = "";
//              public int PageCount { get; set; }
//              public DateOnly Published { get; set; }
//
//              [MaxLength(2000)]
//              public string? Bio { get; set; }    // ← NEW: nullable string
//          }
//
//      Why nullable? Because adding a NOT NULL column to a table that already
//      has rows requires a default value. Nullable is the safer first step.
//      Once every row has a value, you can tighten to NOT NULL in a second
//      migration.
//
// 1.2  Generate the migration:
//
//          cd src/BookShelf
//          dotnet ef migrations add AddAuthorBio
//
// 1.3  Open the new migration file. You should see something close to:
//
//          protected override void Up(MigrationBuilder mb)
//          {
//              mb.AddColumn<string>(
//                  name: "Bio",
//                  table: "Books",
//                  type: "TEXT",
//                  maxLength: 2000,
//                  nullable: true);
//          }
//
//          protected override void Down(MigrationBuilder mb)
//          {
//              mb.DropColumn(name: "Bio", table: "Books");
//          }
//
//      The Down was generated for free because Add/Drop are symmetric.
//
// 1.4  Apply the migration:
//
//          dotnet ef database update
//
// 1.5  Verify with sqlite3:
//
//          sqlite3 bookshelf.db ".schema Books"
//
//      You should see the new Bio TEXT column with no NOT NULL constraint.
//
// 1.6  Commit:
//
//          git add .
//          git commit -m "Add nullable Book.Bio column"
//
// ===========================================================================
// STEP 2 — Roll back, then re-apply
// ===========================================================================
//
// 2.1  Roll back to the prior migration:
//
//          dotnet ef database update InitialCreate
//
//      EF Core executes the AddAuthorBio Down method, dropping the Bio column.
//      Verify:
//
//          sqlite3 bookshelf.db ".schema Books"     # ← no Bio column
//          sqlite3 bookshelf.db "SELECT * FROM __EFMigrationsHistory;"
//
//      You should see only InitialCreate recorded.
//
// 2.2  Roll forward again:
//
//          dotnet ef database update
//
//      The Bio column is back. The history table shows both migrations.
//
// 2.3  Lesson: every migration must have a working Down for non-production
//      cases (CI tear-down, local dev rewinds). EF Core generates them for
//      symmetric operations like Add/Drop. For asymmetric operations (renames,
//      type changes, data migrations) you write the Down by hand — Step 3.
//
// ===========================================================================
// STEP 3 — The rename trap (and the fix)
// ===========================================================================
//
// Suppose we decide that Book.Author was a poor name (an Author is really a
// person, not a string column). We want to rename it to AuthorName for clarity.
// The naive approach is to just rename the property in C# and generate a
// migration. That has a sharp edge.
//
// 3.1  Rename the property:
//
//          [Required, MaxLength(120)]
//          public string AuthorName { get; set; } = "";   // was: Author
//
// 3.2  Generate the migration:
//
//          dotnet ef migrations add RenameAuthorToAuthorName
//
//      Open the file. EF Core almost certainly generated something like:
//
//          protected override void Up(MigrationBuilder mb)
//          {
//              mb.DropColumn(name: "Author", table: "Books");
//              mb.AddColumn<string>(
//                  name: "AuthorName", table: "Books",
//                  type: "TEXT", maxLength: 120, nullable: false,
//                  defaultValue: "");
//          }
//
//      ⚠ Read that carefully. Drop, then Add. If you ran this migration
//      against your production database, **every Author value would be lost**
//      and AuthorName would default to "" for every row. Catastrophic.
//
// 3.3  Hand-fix the Up method to use RenameColumn instead:
//
//          protected override void Up(MigrationBuilder mb)
//          {
//              mb.RenameColumn(
//                  name: "Author",
//                  table: "Books",
//                  newName: "AuthorName");
//          }
//
//          protected override void Down(MigrationBuilder mb)
//          {
//              mb.RenameColumn(
//                  name: "AuthorName",
//                  table: "Books",
//                  newName: "Author");
//          }
//
//      RenameColumn is a single ALTER on databases that support it; on SQLite
//      EF Core emits the table-rebuild dance under the hood. Either way the
//      data survives.
//
// 3.4  Apply:
//
//          dotnet ef database update
//
//      Verify:
//
//          sqlite3 bookshelf.db "SELECT Id, AuthorName, Title FROM Books;"
//
//      You should see the three books with their original author values
//      preserved.
//
// 3.5  This is the single most important lesson of the week:
//
//          READ EVERY GENERATED MIGRATION BEFORE YOU COMMIT IT.
//
//      EF Core's generator is good but not psychic. A rename looks identical
//      to a "drop one + add another" at the C# level; only you know the
//      intent.
//
// 3.6  Commit:
//
//          git add .
//          git commit -m "Rename Book.Author to AuthorName (hand-fixed migration)"
//
// ===========================================================================
// STEP 4 — Produce an idempotent script for CI/CD
// ===========================================================================
//
// 4.1  Generate the script:
//
//          dotnet ef migrations script 0 --idempotent --output schema.sql
//
//      The `0` is the "from" migration (i.e. "from empty"). `--idempotent`
//      wraps each migration block in a guard like:
//
//          BEGIN TRANSACTION;
//          INSERT INTO ... __EFMigrationsHistory ...
//          ... migration body ...
//          COMMIT;
//
//      …only if the migration is not already recorded. Re-running the
//      script against a partially-migrated database is safe.
//
// 4.2  Open schema.sql. Note that:
//
//      - Every migration is wrapped in `IF NOT EXISTS (SELECT 1 FROM __EFMigrationsHistory WHERE MigrationId = '...')`.
//      - The DDL statements look exactly like what the live database has.
//      - The final block records every applied migration in __EFMigrationsHistory.
//
//      This is the artifact your DBA reviews. This is what CI/CD runs against
//      production. Read every line before you ship it.
//
// 4.3  Commit:
//
//          git add schema.sql
//          git commit -m "Generated idempotent schema.sql"
//
// ===========================================================================
// STEP 5 — Verify, then ship
// ===========================================================================
//
// 5.1  From a fresh clone (or after `rm bookshelf.db`):
//
//          dotnet ef database update --project src/BookShelf
//
//      All three migrations apply in order. The final schema matches the
//      model. The data inserted by Program.cs's seed block reappears on next
//      run.
//
// 5.2  Run the program one more time to confirm everything is wired up:
//
//          dotnet run --project src/BookShelf
//
//      You should see the three books printed, this time with `AuthorName`
//      populated.
//
// ===========================================================================
// ACCEPTANCE CHECKLIST
// ===========================================================================
//
//   [ ] Migrations/ contains InitialCreate, AddAuthorBio, RenameAuthorToAuthorName.
//   [ ] RenameAuthorToAuthorName uses RenameColumn in both Up and Down — not Drop+Add.
//   [ ] `dotnet ef database update InitialCreate` rolls all the way back without errors.
//   [ ] `dotnet ef database update` rolls all the way forward without errors.
//   [ ] schema.sql is committed and is non-empty.
//   [ ] All three migrations are committed together with their model snapshot updates.
//   [ ] `dotnet build`: 0 Warning(s), 0 Error(s).
//
// ===========================================================================
// STRETCH
// ===========================================================================
//
// - Add a third migration that converts Book.AuthorName from nullable to NOT
//   NULL (after writing a `Sql("UPDATE Books SET AuthorName = '' WHERE
//   AuthorName IS NULL")` call in Up). Generate, hand-edit, apply.
//
// - Add a migration that introduces an Authors table and a Books.AuthorId FK,
//   migrates the existing AuthorName strings into Author rows, then drops the
//   AuthorName column. This is the "expand and contract" pattern — usually
//   done across two deploys in production.
//
// - Read the generated 20260513_*.Designer.cs file. Note that it carries a
//   point-in-time model snapshot of the WHOLE model as of that migration.
//   EF Core uses it to diff for the NEXT migration. Never edit it.
//
// ===========================================================================
// HINTS (read only if stuck >15 min)
// ===========================================================================
//
// If `dotnet ef migrations add` says "the model has pending changes":
//
//   You changed the model but EF Core notices the previous migration is also
//   not yet applied. Apply it first (`dotnet ef database update`) or remove
//   the previous unapplied migration (`dotnet ef migrations remove`) before
//   generating the next one.
//
// If `dotnet ef database update <Target>` says "unable to roll back":
//
//   The Down method is incomplete or throws. Open the relevant migration's
//   Down body and fix it. EF Core will NOT silently skip a broken Down.
//
// If the rename appears to "work" but the data is gone:
//
//   You committed before reading the migration. Roll back the commit, fix the
//   migration to use RenameColumn, regenerate the model snapshot, re-apply.
//   Use this exercise as the cautionary tale.
//
// If `schema.sql` is empty:
//
//   You ran `migrations script` without `0` as the From argument and EF Core
//   thought "the database is already up to date, no diff to emit." Pass `0`
//   explicitly.
//
// ===========================================================================
// WHY THIS MATTERS
// ===========================================================================
//
// Every production EF Core codebase has at least one war story about a
// migration that silently lost data. The rename trap is the most common.
// Reading every migration before committing it is the single highest-impact
// habit you will build this week. It costs three minutes; it saves your team
// a database restore.
//
// The idempotent script is the production-grade output that lives in your
// CI/CD pipeline. From Week 13 onward your capstone will generate one of
// these on every PR and post the diff in the pull-request body. Now is the
// right time to internalize the workflow.
//
// ===========================================================================
