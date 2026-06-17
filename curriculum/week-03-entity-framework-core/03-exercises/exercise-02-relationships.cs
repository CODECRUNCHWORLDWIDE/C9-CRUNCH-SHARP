// Exercise 2 — Relationships
//
// Goal: Model an entity graph that exercises every relationship cardinality
//       in EF Core 9 — one-to-one, one-to-many, many-to-many — using the
//       fluent API via IEntityTypeConfiguration<T> classes, then query
//       across the relationships with Include, projection, and AsNoTracking.
//
// Estimated time: 45 minutes.
//
// HOW TO USE THIS FILE
//
// 1. Scaffold a fresh console project:
//
//      mkdir BlogDb && cd BlogDb
//      dotnet new console -n BlogDb -o src/BlogDb
//      dotnet add src/BlogDb package Microsoft.EntityFrameworkCore
//      dotnet add src/BlogDb package Microsoft.EntityFrameworkCore.Sqlite
//      dotnet add src/BlogDb package Microsoft.EntityFrameworkCore.Design
//
//    Replace the generated src/BlogDb/Program.cs with the contents of THIS FILE.
//
// 2. Fill in the bodies marked `// TODO`. Do not change the public entity
//    shape (property names and types) — they are the contract the relationships
//    are configured against.
//
// 3. Generate the migration and apply it:
//
//      cd src/BlogDb
//      dotnet ef migrations add InitialCreate
//      dotnet ef database update
//
// 4. Run the program. It should seed two authors, four posts, and three tags,
//    and print the queries described in the SMOKE OUTPUT section below.
//
// ACCEPTANCE CRITERIA
//
//   [ ] All TODOs implemented.
//   [ ] `dotnet build`: 0 Warning(s), 0 Error(s).
//   [ ] Migration generated and applied; `sqlite3 blogdb.db ".tables"` shows
//       Authors, Posts, Tags, AuthorProfiles, PostTag, __EFMigrationsHistory.
//   [ ] `dotnet run` produces the SMOKE OUTPUT below (modulo dates).
//   [ ] Every read query uses either AsNoTracking() or a projection.
//   [ ] No `Include` produces a cartesian explosion: where you Include two
//       collections, you use AsSplitQuery().
//
// SMOKE OUTPUT (target)
//
//   == Authors with their profiles (one-to-one) ==
//   Ada Lovelace               — bio: "First programmer."
//   Grace Hopper               — bio: "COBOL pioneer."
//
//   == Posts with author and tags (one-to-many + many-to-many) ==
//   1  Ada Lovelace      'Notes on the Analytical Engine'    tags: [history, math]
//   2  Ada Lovelace      'On the Difference Engine'          tags: [history]
//   3  Grace Hopper      'A Compiler is a Translator'        tags: [compilers, history]
//   4  Grace Hopper      'Nanoseconds'                       tags: [hardware]
//
//   == Tag usage counts (projection + aggregate) ==
//   history     3
//   compilers   1
//   hardware    1
//   math        1
//
// Inline hints are at the bottom of the file.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

await using var db = new BlogContext();
await db.Database.MigrateAsync();

if (!await db.Authors.AnyAsync())
{
    await SeedAsync(db);
}

await PrintAuthorsWithProfilesAsync(db);
await PrintPostsWithTagsAsync(db);
await PrintTagUsageAsync(db);

// ---------------------------------------------------------------------------
// Queries — fill in the TODOs
// ---------------------------------------------------------------------------

static async Task PrintAuthorsWithProfilesAsync(BlogContext db)
{
    Console.WriteLine();
    Console.WriteLine("== Authors with their profiles (one-to-one) ==");

    // TODO: query db.Authors with Include(a => a.Profile), AsNoTracking,
    //       ordered by Name. Print "{Name,-26} — bio: \"{Profile.Bio}\"".
    //       Hint: a is null-trustworthy here because the seed ensures every
    //       author has a profile.
    throw new NotImplementedException();
}

static async Task PrintPostsWithTagsAsync(BlogContext db)
{
    Console.WriteLine();
    Console.WriteLine("== Posts with author and tags (one-to-many + many-to-many) ==");

    // TODO: query db.Posts with Include(p => p.Author), Include(p => p.Tags),
    //       AsSplitQuery, AsNoTracking, ordered by Id. Print each as:
    //
    //         {Id}  {Author.Name,-18}  '{Title,-35}'  tags: [{tag1}, {tag2}, ...]
    //
    //       Use string.Join(", ", p.Tags.Select(t => t.Name).OrderBy(...))
    //       for the tag listing.
    throw new NotImplementedException();
}

static async Task PrintTagUsageAsync(BlogContext db)
{
    Console.WriteLine();
    Console.WriteLine("== Tag usage counts (projection + aggregate) ==");

    // TODO: project db.Tags into a record { Name, Count } where Count is
    //       t.Posts.Count(). Order by Count descending, then by Name.
    //       AsNoTracking. Print "{Name,-12}{Count}".
    throw new NotImplementedException();
}

// ---------------------------------------------------------------------------
// Seed — provided so you focus on the queries, not the fixture
// ---------------------------------------------------------------------------

static async Task SeedAsync(BlogContext db)
{
    var ada = new Author
    {
        Name = "Ada Lovelace",
        Profile = new AuthorProfile { Bio = "First programmer." }
    };
    var grace = new Author
    {
        Name = "Grace Hopper",
        Profile = new AuthorProfile { Bio = "COBOL pioneer." }
    };
    var history   = new Tag { Name = "history" };
    var math      = new Tag { Name = "math" };
    var compilers = new Tag { Name = "compilers" };
    var hardware  = new Tag { Name = "hardware" };

    ada.Posts.Add(new Post
    {
        Title = "Notes on the Analytical Engine",
        Body = "...",
        Tags = { history, math }
    });
    ada.Posts.Add(new Post
    {
        Title = "On the Difference Engine",
        Body = "...",
        Tags = { history }
    });
    grace.Posts.Add(new Post
    {
        Title = "A Compiler is a Translator",
        Body = "...",
        Tags = { compilers, history }
    });
    grace.Posts.Add(new Post
    {
        Title = "Nanoseconds",
        Body = "...",
        Tags = { hardware }
    });

    db.Authors.AddRange(ada, grace);
    await db.SaveChangesAsync();
}

// ---------------------------------------------------------------------------
// Entities — DO NOT change the property names or types
// ---------------------------------------------------------------------------

public sealed class Author
{
    public int Id { get; set; }

    [Required, MaxLength(120)]
    public string Name { get; set; } = "";

    public AuthorProfile? Profile { get; set; }
    public List<Post> Posts { get; set; } = [];
}

public sealed class AuthorProfile
{
    public int Id { get; set; }

    [Required, MaxLength(2000)]
    public string Bio { get; set; } = "";

    public int AuthorId { get; set; }
    public Author Author { get; set; } = null!;
}

public sealed class Post
{
    public int Id { get; set; }

    [Required, MaxLength(200)]
    public string Title { get; set; } = "";

    [Required]
    public string Body { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public int AuthorId { get; set; }
    public Author Author { get; set; } = null!;

    public List<Tag> Tags { get; set; } = [];
}

public sealed class Tag
{
    public int Id { get; set; }

    [Required, MaxLength(40)]
    public string Name { get; set; } = "";

    public List<Post> Posts { get; set; } = [];
}

// ---------------------------------------------------------------------------
// Context + fluent configuration
// ---------------------------------------------------------------------------

public sealed class BlogContext : DbContext
{
    public DbSet<Author>         Authors         => Set<Author>();
    public DbSet<AuthorProfile>  AuthorProfiles  => Set<AuthorProfile>();
    public DbSet<Post>           Posts           => Set<Post>();
    public DbSet<Tag>            Tags            => Set<Tag>();

    protected override void OnConfiguring(DbContextOptionsBuilder options) =>
        options.UseSqlite("Data Source=blogdb.db");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BlogContext).Assembly);
    }
}

public sealed class AuthorConfiguration : IEntityTypeConfiguration<Author>
{
    public void Configure(EntityTypeBuilder<Author> b)
    {
        // TODO: configure the one-to-one relationship between Author and AuthorProfile.
        //       - Author has one Profile.
        //       - AuthorProfile has one Author.
        //       - The foreign key lives on AuthorProfile (AuthorId).
        //       - On delete: cascade (deleting the author deletes the profile).
        // Hint: b.HasOne(a => a.Profile).WithOne(p => p.Author).HasForeignKey<AuthorProfile>(p => p.AuthorId).OnDelete(DeleteBehavior.Cascade);
        throw new NotImplementedException();
    }
}

public sealed class PostConfiguration : IEntityTypeConfiguration<Post>
{
    public void Configure(EntityTypeBuilder<Post> b)
    {
        // TODO: configure the one-to-many relationship between Author and Post.
        //       - Post has one Author.
        //       - Author has many Posts.
        //       - The foreign key lives on Post (AuthorId).
        //       - On delete: cascade.
        //
        // TODO: configure the many-to-many relationship between Post and Tag.
        //       - Use the implicit (skip-navigation) form: HasMany().WithMany().
        //       - Let EF Core create the join table automatically; name it "PostTag".
        //
        // TODO: add an index on Post.CreatedAt for the "recent posts" query.
        throw new NotImplementedException();
    }
}

public sealed class TagConfiguration : IEntityTypeConfiguration<Tag>
{
    public void Configure(EntityTypeBuilder<Tag> b)
    {
        // TODO: enforce a unique index on Tag.Name. Two tags with the same name
        //       would be a domain bug.
        throw new NotImplementedException();
    }
}

// ---------------------------------------------------------------------------
// EXPECTED OUTPUTS (modulo dates)
// ---------------------------------------------------------------------------
//
// First run:
//
//   == Authors with their profiles (one-to-one) ==
//   Ada Lovelace               — bio: "First programmer."
//   Grace Hopper               — bio: "COBOL pioneer."
//
//   == Posts with author and tags (one-to-many + many-to-many) ==
//   1  Ada Lovelace      'Notes on the Analytical Engine'    tags: [history, math]
//   2  Ada Lovelace      'On the Difference Engine'          tags: [history]
//   3  Grace Hopper      'A Compiler is a Translator'        tags: [compilers, history]
//   4  Grace Hopper      'Nanoseconds'                       tags: [hardware]
//
//   == Tag usage counts (projection + aggregate) ==
//   history     3
//   compilers   1
//   hardware    1
//   math        1
//
// ---------------------------------------------------------------------------
// HINTS (read only if stuck >15 min)
// ---------------------------------------------------------------------------
//
// AuthorConfiguration:
//   public void Configure(EntityTypeBuilder<Author> b)
//   {
//       b.HasIndex(a => a.Name);
//       b.HasOne(a => a.Profile)
//        .WithOne(p => p.Author)
//        .HasForeignKey<AuthorProfile>(p => p.AuthorId)
//        .OnDelete(DeleteBehavior.Cascade);
//   }
//
// PostConfiguration:
//   public void Configure(EntityTypeBuilder<Post> b)
//   {
//       b.HasOne(p => p.Author)
//        .WithMany(a => a.Posts)
//        .HasForeignKey(p => p.AuthorId)
//        .OnDelete(DeleteBehavior.Cascade);
//
//       b.HasMany(p => p.Tags)
//        .WithMany(t => t.Posts)
//        .UsingEntity(j => j.ToTable("PostTag"));
//
//       b.HasIndex(p => p.CreatedAt);
//   }
//
// TagConfiguration:
//   public void Configure(EntityTypeBuilder<Tag> b)
//   {
//       b.HasIndex(t => t.Name).IsUnique();
//   }
//
// PrintAuthorsWithProfilesAsync:
//   var authors = await db.Authors
//       .AsNoTracking()
//       .Include(a => a.Profile)
//       .OrderBy(a => a.Name)
//       .ToListAsync();
//   foreach (var a in authors)
//       Console.WriteLine($"{a.Name,-26} — bio: \"{a.Profile?.Bio}\"");
//
// PrintPostsWithTagsAsync:
//   var posts = await db.Posts
//       .AsNoTracking()
//       .Include(p => p.Author)
//       .Include(p => p.Tags)
//       .AsSplitQuery()
//       .OrderBy(p => p.Id)
//       .ToListAsync();
//   foreach (var p in posts)
//   {
//       var tagNames = string.Join(", ", p.Tags.Select(t => t.Name).OrderBy(n => n));
//       Console.WriteLine($"{p.Id}  {p.Author.Name,-18}  '{p.Title,-35}'  tags: [{tagNames}]");
//   }
//
// PrintTagUsageAsync:
//   var rows = await db.Tags
//       .AsNoTracking()
//       .Select(t => new { t.Name, Count = t.Posts.Count() })
//       .OrderByDescending(x => x.Count)
//       .ThenBy(x => x.Name)
//       .ToListAsync();
//   foreach (var r in rows)
//       Console.WriteLine($"{r.Name,-12}{r.Count}");
//
// ---------------------------------------------------------------------------
// WHY THIS MATTERS
// ---------------------------------------------------------------------------
//
// Every relationship cardinality you exercised here shows up on the real
// schemas you'll build from Week 6 onward (Sharp Notes uses 1:N for notes-
// per-user, M:N for note-tags, 1:1 for note-stats). The fluent-API style with
// IEntityTypeConfiguration<T> scales to dozens of entities; OnModelCreating
// itself becomes a single `ApplyConfigurationsFromAssembly` call and stays
// readable. AsSplitQuery + AsNoTracking + projection is the read-path triad
// you will return to in every mini-project.
//
// ---------------------------------------------------------------------------
