// DeploySmokeCheck — the "verify it worked" half of every deploy and rollback.
// Polls /health, asserts 200 + "Healthy", reports the served revision, and exits
// non-zero if the service is not healthy within the timeout (so it can gate a
// script or a pipeline step). Used by the pipeline AND by RUNBOOK procedures 1/2.
//
// Build:  dotnet new console -n Workshop.Smoke -f net9.0 && cd Workshop.Smoke
//         # replace Program.cs with this file
// Run:    dotnet run -- https://workshop-api.<region>.azurecontainerapps.io
//         dotnet run -- https://... workshop-api--sha1a2b3c4   # assert which revision

using System.Diagnostics;
using System.Net;

var baseUrl = args.Length > 0 ? args[0].TrimEnd('/') : "http://localhost:8080";
var expectedRevision = args.Length > 1 ? args[1] : null; // optional: assert the served revision
var timeout = TimeSpan.FromSeconds(60);

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
var sw = Stopwatch.StartNew();

Console.WriteLine($"smoke-checking {baseUrl}/health for up to {timeout.TotalSeconds}s ...");

while (sw.Elapsed < timeout)
{
    try
    {
        using var resp = await http.GetAsync($"{baseUrl}/health");
        var body = await resp.Content.ReadAsStringAsync();

        if (resp.StatusCode == HttpStatusCode.OK &&
            body.Contains("\"Healthy\"", StringComparison.OrdinalIgnoreCase))
        {
            // If the caller passed an expected revision, the served body must
            // name it — this is what proves a ROLLBACK re-pointed traffic at the
            // known-good revision rather than just restarting the bad one.
            if (expectedRevision is not null &&
                !body.Contains(expectedRevision, StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine(
                    $"healthy, but the WRONG revision answered (wanted {expectedRevision}); body: {body}");
                return 2;
            }

            Console.WriteLine($"HEALTHY in {sw.ElapsedMilliseconds} ms");
            Console.WriteLine($"served body: {body}");
            return 0;
        }

        Console.WriteLine($"not healthy yet: {(int)resp.StatusCode} {body}");
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
    {
        // Scale-from-zero cold start or a mid-flight rollback — keep polling.
        Console.WriteLine($"transient: {ex.GetType().Name} — retrying");
    }

    await Task.Delay(TimeSpan.FromSeconds(3));
}

Console.Error.WriteLine(
    $"NEVER went healthy within {timeout.TotalSeconds}s — investigate before declaring the deploy/rollback done.");
return 1;
