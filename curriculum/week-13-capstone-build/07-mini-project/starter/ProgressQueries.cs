// Workshop.Api / Analytics/ProgressQueries.cs — the Dapper analytics escape
// hatch (SYLLABUS: "Dapper for the analytics queries").
//
// The rule (Week 6): EF Core for the transactional domain, Dapper for the hot
// aggregate read. This query — submissions-per-lesson over a rolling window — is
// exactly the shape EF Core would translate awkwardly (a GROUP BY with a date
// filter and a left join to lessons) and Dapper expresses directly in SQL the
// database planner is happy with.
//
// Citations:
//   Dapper:        https://github.com/DapperLib/Dapper
//   Npgsql:        https://www.npgsql.org/
//   When to drop to Dapper: C9 Week 6 lecture on EF Core query translation.

#nullable enable
using Dapper;
using Npgsql;
using System.Diagnostics;
using Workshop.Api.Observability;

namespace Workshop.Api.Analytics;

// The wire/read shape for one row of the analytics result. Distinct from the
// domain entities — analytics reads do not materialize aggregates.
public sealed record LessonProgress(
    Guid LessonId,
    string Title,
    int SubmissionCount,
    int ApprovedCount,
    int PendingCount);

public sealed class ProgressQueries(NpgsqlConnection connection)
{
    // Submissions per lesson for a tenant over the last `days` days, with the
    // approved/pending breakdown. One round trip, one index-friendly query.
    //
    // The (Status, SubmittedAt) index from WorkshopDbContext backs the date
    // filter; the join to lessons is on the primary key. FILTER (WHERE ...) is
    // the Postgres idiom for conditional aggregation — cleaner and faster than
    // CASE-summing, and EF Core cannot translate it at all.
    public async Task<IReadOnlyList<LessonProgress>> SubmissionsPerLessonAsync(
        string tenantId, int days, CancellationToken ct)
    {
        using var activity = WorkshopTelemetry.Activity.StartActivity("Analytics.SubmissionsPerLesson");
        activity?.SetTag("workshop.tenant_id", tenantId);
        activity?.SetTag("workshop.window_days", days);

        const string sql = """
            SELECT  l."Id"            AS LessonId,
                    l."Title"         AS Title,
                    COUNT(s."Id")                                            AS SubmissionCount,
                    COUNT(s."Id") FILTER (WHERE s."Status" = 2)              AS ApprovedCount,
                    COUNT(s."Id") FILTER (WHERE s."Status" = 1)              AS PendingCount
            FROM    "Lessons" l
            LEFT JOIN "Submissions" s
                   ON s."LessonId" = l."Id"
                  AND s."SubmittedAt" >= @since
            WHERE   l."TenantId" = @tenantId
            GROUP BY l."Id", l."Title"
            ORDER BY SubmissionCount DESC, l."Title";
            """;

        var since = DateTimeOffset.UtcNow.AddDays(-Math.Abs(days));
        var command = new CommandDefinition(
            sql,
            new { tenantId, since },
            cancellationToken: ct);

        var rows = await connection.QueryAsync<LessonProgress>(command);
        var result = rows.ToList();
        activity?.SetTag("workshop.result_rows", result.Count);
        return result;
    }
}

// Wire it into Program.cs with:
//   builder.Services.AddScoped<ProgressQueries>();
// and expose it on a Minimal-API endpoint (instructor-only) or a gRPC RPC:
//   app.MapGet("/api/analytics/progress", async (
//       ProgressQueries q, HttpContext http, CancellationToken ct) =>
//   {
//       var tenant = http.User.FindFirst("tenant")?.Value ?? "default";
//       return Results.Ok(await q.SubmissionsPerLessonAsync(tenant, days: 7, ct));
//   }).RequireAuthorization();
//
// The status integers (1 = Pending, 2 = Approved) match the Workshop.Domain
// SubmissionStatus enum values — a small coupling between the raw SQL and the
// enum that is worth a comment here and a unit test that asserts the mapping.
