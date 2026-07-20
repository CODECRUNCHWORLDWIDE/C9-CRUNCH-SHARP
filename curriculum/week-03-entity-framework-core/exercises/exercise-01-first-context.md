# Exercise 1 — Your first context

**Goal:** Scaffold a real EF Core 9 project from a blank folder, model a single `Book` entity, generate your first migration, apply it against a SQLite file, and inspect the schema EF Core generated with `sqlite3` — all from the terminal.

**Estimated time:** 40 minutes.

---

## Setup

You need the .NET 9 SDK installed. Verify:

```bash
dotnet --info
```

You should see `9.0.x` listed under "Host" and "SDKs installed." If you do not, install from <https://dotnet.microsoft.com/en-us/download/dotnet/9.0> before going further.

You also need the EF Core CLI tool. Install it once per machine:

```bash
dotnet tool install --global dotnet-ef
dotnet ef --version
```

You should see `9.0.x`. If you see an older version, run `dotnet tool update --global dotnet-ef`.

You also need `sqlite3` on your PATH:

```bash
sqlite3 --version
```

If you do not see a version, install it (`apt install sqlite3` on Debian/Ubuntu; preinstalled on macOS).

---

## Step 1 — Scaffold the solution

```bash
mkdir BookShelf && cd BookShelf
dotnet new sln -n BookShelf
dotnet new gitignore
git init
dotnet new console -n BookShelf -o src/BookShelf
dotnet sln add src/BookShelf/BookShelf.csproj
```

Add the EF Core packages to the project:

```bash
dotnet add src/BookShelf package Microsoft.EntityFrameworkCore
dotnet add src/BookShelf package Microsoft.EntityFrameworkCore.Sqlite
dotnet add src/BookShelf package Microsoft.EntityFrameworkCore.Design
```

A note: in real projects you mark `Microsoft.EntityFrameworkCore.Design` as `PrivateAssets="all"` so it does not flow as a transitive runtime dependency. For this exercise the default reference is fine.

You now have:

```
BookShelf/
├── BookShelf.sln
├── .gitignore
└── src/
    └── BookShelf/
        ├── BookShelf.csproj
        └── Program.cs
```

Commit:

```bash
git add .
git commit -m "Initial solution with EF Core packages"
```

---

## Step 2 — Model and context

Open `src/BookShelf/Program.cs` and replace its contents:

```csharp
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

await using var db = new BookContext();

Console.WriteLine($"Connected to {db.Database.GetConnectionString()}.");
Console.WriteLine($"Pending migrations: {string.Join(", ", await db.Database.GetPendingMigrationsAsync())}");

public sealed class Book
{
    public int Id { get; set; }

    [Required, MaxLength(200)]
    public string Title { get; set; } = "";

    [Required, MaxLength(120)]
    public string Author { get; set; } = "";

    public int PageCount { get; set; }

    public DateOnly Published { get; set; }
}

public sealed class BookContext : DbContext
{
    public DbSet<Book> Books => Set<Book>();

    protected override void OnConfiguring(DbContextOptionsBuilder options) =>
        options.UseSqlite("Data Source=bookshelf.db");
}
```

Build:

```bash
dotnet build
```

You should see `Build succeeded · 0 Warning(s) · 0 Error(s)`. If you see warnings, fix them before going further. Treat warnings as errors from now on — add `Directory.Build.props` if you haven't already.

Commit:

```bash
git add .
git commit -m "Add Book entity and BookContext"
```

---

## Step 3 — Generate the first migration

```bash
cd src/BookShelf
dotnet ef migrations add InitialCreate
cd ../..
```

EF Core creates a `Migrations/` folder in `src/BookShelf` with three files:

```
src/BookShelf/Migrations/
├── 20260513120000_InitialCreate.cs
├── 20260513120000_InitialCreate.Designer.cs
└── BookContextModelSnapshot.cs
```

Open `20260513120000_InitialCreate.cs` and read it end to end. You should see something close to:

```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.CreateTable(
        name: "Books",
        columns: table => new
        {
            Id = table.Column<int>(type: "INTEGER", nullable: false)
                .Annotation("Sqlite:Autoincrement", true),
            Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
            Author = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
            PageCount = table.Column<int>(type: "INTEGER", nullable: false),
            Published = table.Column<string>(type: "TEXT", nullable: false)
        },
        constraints: table =>
        {
            table.PrimaryKey("PK_Books", x => x.Id);
        });
}

protected override void Down(MigrationBuilder migrationBuilder)
{
    migrationBuilder.DropTable(name: "Books");
}
```

Three things to notice:

- The migration is **plain C#** — no SQL strings.
- The `Published` column is `TEXT`, not `DATE`, because SQLite has no native date type. `DateOnly` round-trips through ISO-8601.
- The `[Required]` and `[MaxLength]` annotations show up as `nullable: false` and `maxLength: 200`. EF Core read them when it built the model.

Commit:

```bash
git add .
git commit -m "Initial migration"
```

---

## Step 4 — Apply the migration

```bash
dotnet ef database update --project src/BookShelf
```

You should see something like:

```
Applying migration '20260513120000_InitialCreate'.
Done.
```

A new file `src/BookShelf/bookshelf.db` exists. That is your SQLite database.

Inspect it:

```bash
sqlite3 src/BookShelf/bookshelf.db ".tables"
```

```
Books               __EFMigrationsHistory
```

```bash
sqlite3 src/BookShelf/bookshelf.db ".schema Books"
```

```sql
CREATE TABLE "Books" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Books" PRIMARY KEY AUTOINCREMENT,
    "Title" TEXT NOT NULL,
    "Author" TEXT NOT NULL,
    "PageCount" INTEGER NOT NULL,
    "Published" TEXT NOT NULL
);
```

```bash
sqlite3 src/BookShelf/bookshelf.db "SELECT * FROM __EFMigrationsHistory;"
```

```
20260513120000_InitialCreate|9.0.x
```

The `__EFMigrationsHistory` table is how EF Core knows which migrations have already run. Never edit it by hand. If you delete it, EF Core will think the database is empty and try to re-apply every migration, which will fail because the tables already exist.

Add `bookshelf.db` to `.gitignore`:

```bash
echo "*.db" >> .gitignore
```

Commit:

```bash
git add .
git commit -m "Apply InitialCreate; ignore .db files"
```

---

## Step 5 — Insert and read rows

Replace `Program.cs` with:

```csharp
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

await using var db = new BookContext();
await db.Database.MigrateAsync();   // applies any pending migrations at startup

if (!await db.Books.AnyAsync())
{
    db.Books.AddRange(
        new Book { Title = "The C Programming Language",  Author = "Brian Kernighan",  PageCount = 272, Published = new DateOnly(1978, 2, 22) },
        new Book { Title = "The Pragmatic Programmer",    Author = "Andy Hunt",         PageCount = 320, Published = new DateOnly(1999, 10, 30) },
        new Book { Title = "Clean Code",                  Author = "Robert C. Martin", PageCount = 464, Published = new DateOnly(2008, 8, 1) }
    );
    await db.SaveChangesAsync();
    Console.WriteLine("Seeded 3 books.");
}

var books = await db.Books
    .AsNoTracking()
    .OrderBy(b => b.Published)
    .ToListAsync();

foreach (var b in books)
{
    Console.WriteLine($"{b.Id,3}  {b.Published}  {b.Title,-35}  {b.Author}");
}

public sealed class Book { /* unchanged */ }
public sealed class BookContext : DbContext { /* unchanged */ }
```

Run:

```bash
dotnet run --project src/BookShelf
```

You should see:

```
Seeded 3 books.
  1  1978-02-22  The C Programming Language          Brian Kernighan
  2  1999-10-30  The Pragmatic Programmer            Andy Hunt
  3  2008-08-01  Clean Code                          Robert C. Martin
```

Run it again — the seed is now skipped (because `AnyAsync` returns true), and you just see the three books printed.

Inspect with SQL:

```bash
sqlite3 src/BookShelf/bookshelf.db "SELECT Id, Title FROM Books ORDER BY PageCount;"
```

```
1|The C Programming Language
2|The Pragmatic Programmer
3|Clean Code
```

Commit:

```bash
git add .
git commit -m "Seed three books on first run; AsNoTracking read"
```

---

## Step 6 — Read the emitted SQL

Add logging to the context:

```csharp
public sealed class BookContext : DbContext
{
    public DbSet<Book> Books => Set<Book>();

    protected override void OnConfiguring(DbContextOptionsBuilder options) =>
        options.UseSqlite("Data Source=bookshelf.db")
               .LogTo(Console.WriteLine, Microsoft.Extensions.Logging.LogLevel.Information);
}
```

Run again. You should see EF Core's `info:` logs streaming past, including the exact SQL it ran:

```
info: Microsoft.EntityFrameworkCore.Database.Command[20101]
      Executed DbCommand (1ms) [Parameters=[], CommandType='Text', CommandTimeout='30']
      SELECT "b"."Id", "b"."Author", "b"."PageCount", "b"."Published", "b"."Title"
      FROM "Books" AS "b"
      ORDER BY "b"."Published"
```

That is the SQL your LINQ produced. Spend a minute looking at it. The `Where`, `OrderBy`, and `AsNoTracking` calls translated to `SELECT ... FROM Books ORDER BY Published`. No wasted columns. No client-side filtering.

Commit:

```bash
git add .
git commit -m "Log emitted SQL to the console"
```

---

## Acceptance criteria

You can mark this exercise done when:

- [ ] You have a `BookShelf/` folder with `BookShelf.sln`, `src/BookShelf/`, and a `.gitignore` that excludes `*.db`.
- [ ] `dotnet build` from the root prints `0 Warning(s)` and `0 Error(s)`.
- [ ] `src/BookShelf/Migrations/` contains the initial migration with `InitialCreate` in its file name.
- [ ] `dotnet ef database update` succeeds; `bookshelf.db` is created.
- [ ] `sqlite3 bookshelf.db ".schema Books"` shows the expected table.
- [ ] `dotnet run` on a fresh `.db` seeds three books and prints them in publication order.
- [ ] The console output includes the EF Core SQL log lines.
- [ ] You have at least 5 Git commits with sensible messages.

---

## Stretch

- Add a `[MaxLength(20)] string Isbn { get; set; }` property to `Book`. Generate a second migration named `AddIsbn`. Apply it. Inspect the new column in `sqlite3`.
- Add a `HasIndex` for `Books.Author` via the fluent API in `OnModelCreating`. Regenerate the migration. Note the `CREATE INDEX` statement.
- Switch the connection string to `Data Source=:memory:` and try to run the app. What happens? Why?
- Use `dotnet ef migrations script` to emit the equivalent SQL script. Read it.

---

## Hints

<details>
<summary>If <code>dotnet ef migrations add</code> says "no project was found"</summary>

You probably ran the command from the solution root. `dotnet ef` looks for a startup project in the current directory. Either `cd src/BookShelf` first, or pass `--project src/BookShelf --startup-project src/BookShelf` to every command.

</details>

<details>
<summary>If <code>dotnet ef</code> says the version is mismatched</summary>

The CLI version must match (or be newer than) the package version. Run `dotnet tool update --global dotnet-ef` until `dotnet ef --version` prints `9.0.x` or higher.

</details>

<details>
<summary>If you see a "model has pending changes" warning</summary>

You changed the model but didn't generate a migration. Either generate one (`dotnet ef migrations add ...`) or revert the model change. Never ignore this warning — at the next `database update` your live database will diverge from your model.

</details>

<details>
<summary>If <code>sqlite3</code> says "no such command"</summary>

Install the SQLite CLI. On macOS it is preinstalled. On Ubuntu/Debian: `sudo apt install sqlite3`. On Windows: download from <https://sqlite.org/download.html> and put it on your PATH.

</details>

---

When this exercise feels comfortable, move to [Exercise 2 — Relationships](exercise-02-relationships.cs).
