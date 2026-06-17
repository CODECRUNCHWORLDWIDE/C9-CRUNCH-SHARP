# Lecture 2 — One `.proto`, Three Clients: Generating Server and Client Stubs, Mirroring REST and gRPC, and gRPC-Web for the Browser

> **Time:** 2 hours. Take the code-generation and project-wiring material first, then the REST/gRPC mirroring and the gRPC-Web material. **Prerequisites:** Lecture 1 (the contract-first order, the `workshop.proto` for the slice) and Week 9 of C9 (you have authored a `.proto` and generated stubs before). **Citations:** gRPC with ASP.NET Core at <https://learn.microsoft.com/en-us/aspnet/core/grpc/aspnetcore>, the .NET gRPC client at <https://learn.microsoft.com/en-us/aspnet/core/grpc/client>, gRPC-Web at <https://learn.microsoft.com/en-us/aspnet/core/grpc/grpcweb>, and the proto3 language guide at <https://protobuf.dev/programming-guides/proto3/>.

## 1. The mechanism behind "the contract is the source of truth"

Lecture 1 asserted a principle: every client generates its types from `workshop.proto`, none hand-writes them, and a drift is a build break. This lecture is the mechanism that makes the principle true. The mechanism is **`Grpc.Tools`**, an MSBuild package that runs `protoc` (the protobuf compiler) as part of `dotnet build`, reads every `.proto` file declared as a `<Protobuf>` item, and emits C# — either the server base class, the client class, or both, depending on a single attribute. Once the generation is wired, "the contract is the source of truth" is not a guideline you have to remember; it is a property the build enforces, because the only way to get the types is to generate them, and the only thing to generate from is the proto.

The key insight is that **the same `.proto` file produces three different C# outputs in three different projects**, and a small MSBuild attribute decides which. The backend generates the *server* side (an abstract base class you override). The MAUI client and the Blazor admin generate the *client* side (a concrete class with a method per RPC). All three point at the same file. Change the file, rebuild all three, and they either all compile against the new shape or they tell you exactly which one did not — which is the binary Lecture 1 promised.

## 2. Putting the proto in a shared project

The first decision is *where* the proto lives. The answer is its own project — `Workshop.Contract` — that every other project references. That project does two jobs: it holds `workshop.proto`, and it generates the C# from it. Putting it in its own project (rather than copying the file into each consumer) is what makes "one source of truth" physically true: there is exactly one file on disk, and three projects reference the one project that owns it.

The project file:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Google.Protobuf" Version="3.28.*" />
    <PackageReference Include="Grpc.Net.Client" Version="2.66.*" />
    <PackageReference Include="Grpc.Tools" Version="2.66.*" PrivateAssets="all" />
  </ItemGroup>

  <ItemGroup>
    <!-- One file. One source of truth. GrpcServices="Both" so this shared
         project carries both the server base class and the client class;
         each consumer uses the half it needs. -->
    <Protobuf Include="Protos/workshop.proto" GrpcServices="Both" />
  </ItemGroup>

</Project>
```

Three things deserve naming. First, `Grpc.Tools` carries `PrivateAssets="all"` because it is a build-time tool, not a runtime dependency — it should not flow transitively to anything that references `Workshop.Contract`. Second, `GrpcServices="Both"` generates both sides in this shared project; an alternative design generates `Server` in the backend project and `Client` in each client project from a *linked* file, but the shared-project-with-`Both` approach is simpler to reason about and is what the mini-project uses. Third, the generated code lands in `obj/` and is never checked in — it is a build output. If you ever find generated `*.cs` from a proto committed to the repo, that is a smell: it means someone hand-edited it, which defeats the entire mechanism.

After a `dotnet build`, the generated namespace `Workshop.Contract` contains: the message classes (`Lesson`, `CreateLessonRequest`, `Submission`, …), the enum `SubmissionStatus`, an abstract `Workshop.WorkshopBase` (the server base class), and a concrete `Workshop.WorkshopClient` (the client). The doubled name (`Workshop.Workshop`) comes from the `service Workshop` declaration inside the `workshop` package — protobuf names the static container after the service. You will write `Workshop.Contract.Workshop.WorkshopBase` in the backend and `Workshop.Contract.Workshop.WorkshopClient` in the clients.

## 3. The backend: registering and mapping the gRPC service

The backend references `Workshop.Contract`, overrides `WorkshopBase`, and maps it. Registration is two lines in `Program.cs`:

```csharp
builder.Services.AddGrpc(options =>
{
    // Surface the real exception message in dev; redact in production. The
    // EnableDetailedErrors flag turns a generic "Exception was thrown by
    // handler" into the actual message, which is what you want when an
    // integration test fails.
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
});

// ... after building the app:
app.MapGrpcService<WorkshopService>();
```

The service implementation is the `WorkshopService : WorkshopBase` from Lecture 1, with the remaining RPCs filled in. The pattern is identical for each: read the caller identity from the validated token (never from the request body), call a domain operation, persist, log, and map the entity back to a proto message.

```csharp
public override async Task<Enrollment> Enroll(
    EnrollRequest request, ServerCallContext context)
{
    var learnerId = RequireSubject(context);
    if (!Guid.TryParse(request.LessonId, out var lessonId))
        throw new RpcException(new Status(StatusCode.InvalidArgument, "lesson_id is not a GUID."));

    var exists = await db.Lessons.AnyAsync(l => l.Id == lessonId, context.CancellationToken);
    if (!exists)
        throw new RpcException(new Status(StatusCode.NotFound, $"Lesson {lessonId} not found."));

    var enrollment = Domain.Enrollment.Create(lessonId, learnerId);
    db.Enrollments.Add(enrollment);
    await db.SaveChangesAsync(context.CancellationToken);
    return enrollment.ToProto();
}

private static string RequireSubject(ServerCallContext context)
    => context.GetHttpContext().User.FindFirst("sub")?.Value
       ?? throw new RpcException(new Status(StatusCode.Unauthenticated, "No subject claim."));
```

The `RpcException` with a `Status` code is how gRPC reports failure: `InvalidArgument`, `NotFound`, `Unauthenticated`, `PermissionDenied`, and so on map onto the gRPC status codes the client sees. This is the gRPC analogue of a `400`/`404`/`401` in REST. The client's generated method throws an `RpcException` with the matching `StatusCode`, which both MAUI and Blazor catch and translate to a user-facing message. Getting the status code right is part of honoring the contract — a `NotFound` and an `InvalidArgument` mean different things to the caller.

## 4. The proto↔entity mapping layer

The proto types and the domain entities are deliberately separate (Lecture 1, §4). The mapping between them is a small, hand-written, well-tested layer — not AutoMapper, not reflection, just extension methods. Hand-writing it this week is intentional: it is fast, it is obvious, it shows up correctly in a stack trace, and it is the thing Week 14 will *replace* with AutoMapper only where the mapping is mechanical enough to warrant it. Writing it by hand now means you can feel exactly which mappings are mechanical (and AutoMapper-worthy) and which carry a real decision (and must stay hand-written).

```csharp
#nullable enable
using Google.Protobuf.WellKnownTypes;
using Workshop.Contract;
using DomainLesson = Workshop.Domain.Lesson;
using DomainSubmission = Workshop.Domain.Submission;

namespace Workshop.Api.Mapping;

public static class ProtoMappings
{
    public static Lesson ToProto(this DomainLesson l) => new()
    {
        Id = l.Id.ToString(),
        TenantId = l.TenantId,
        Title = l.Title,
        Body = l.Body,
        CreatedAt = Timestamp.FromDateTimeOffset(l.CreatedAt),
    };

    public static Submission ToProto(this DomainSubmission s) => new()
    {
        Id = s.Id.ToString(),
        LessonId = s.LessonId.ToString(),
        LearnerId = s.LearnerId,
        Content = s.Content,
        Status = s.Status switch
        {
            Workshop.Domain.SubmissionStatus.Pending  => SubmissionStatus.Pending,
            Workshop.Domain.SubmissionStatus.Approved => SubmissionStatus.Approved,
            Workshop.Domain.SubmissionStatus.Rejected => SubmissionStatus.Rejected,
            _ => SubmissionStatus.Unspecified,
        },
        SubmittedAt = Timestamp.FromDateTimeOffset(s.SubmittedAt),
    };
}
```

Two details earn their place. First, `Timestamp.FromDateTimeOffset` is the well-known-type bridge: the proto `google.protobuf.Timestamp` is not a .NET `DateTimeOffset`, and the conversion is explicit on purpose — a `DateTimeOffset` carries an offset, a proto `Timestamp` is UTC nanoseconds since the epoch, and the conversion normalizes to UTC. Mixing them up is how you get a lesson "created" an hour off. Second, the enum mapping is a `switch` expression with an explicit `_ => Unspecified` arm. The proto enum *must* have a `0 = UNSPECIFIED` value (proto3 requires the zero value to be the default), and the domain enum starts at `1` precisely so that an unmapped domain value is impossible — but the compiler still wants the exhaustive arm, and the explicit default documents "if a future status appears, it serializes as unspecified rather than crashing." That is a deliberate forward-compatibility choice, written down in the one place mapping happens.

## 5. The MAUI client: native gRPC

The MAUI client references `Workshop.Contract` and gets the generated `WorkshopClient` for free. On a phone, the client speaks **native gRPC** over HTTP/2 — the full, efficient protocol, because a mobile app controls its own networking stack and is not subject to a browser's restrictions. The channel is created once and held for the app's lifetime (gRPC channels are expensive to create and cheap to reuse — see <https://learn.microsoft.com/en-us/aspnet/core/grpc/performance>):

```csharp
#nullable enable
using Grpc.Net.Client;
using Grpc.Core;
using Workshop.Contract;

namespace Workshop.Maui.Services;

public sealed class WorkshopApi
{
    private readonly Workshop.Contract.Workshop.WorkshopClient _client;

    public WorkshopApi(ITokenProvider tokens, IConfiguration config)
    {
        var channel = GrpcChannel.ForAddress(config["Api:BaseUrl"]!, new GrpcChannelOptions
        {
            // A CallCredentials that attaches the OIDC token to every call's
            // metadata as "authorization: Bearer <token>". The server reads
            // it the same way the REST surface reads the Authorization header.
            Credentials = ChannelCredentials.Create(
                new SslCredentials(),
                CallCredentials.FromInterceptor(async (ctx, metadata) =>
                {
                    var token = await tokens.GetAccessTokenAsync();
                    metadata.Add("Authorization", $"Bearer {token}");
                })),
        });
        _client = new Workshop.Contract.Workshop.WorkshopClient(channel);
    }

    public async Task<Submission> SubmitAsync(Guid lessonId, string content, CancellationToken ct)
    {
        var request = new SubmitRequest { LessonId = lessonId.ToString(), Content = content };
        return await _client.SubmitAsync(request, cancellationToken: ct);
    }
}
```

The thing to internalize: the MAUI client does not know what a `Submission` "looks like" except through the generated type. There is no `MauiSubmissionModel` that someone hand-wrote to mirror the server. The screen binds to the generated `Submission`. When the proto gains a field, the MAUI project rebuilds against the new generated type — compile or break, no third state. That is the contract being load-bearing on the client side, exactly as on the server side.

## 6. The Blazor admin: why the browser needs gRPC-Web

The Blazor admin reaches the *same* `WorkshopService`, but it cannot use native gRPC, and the reason is not arbitrary. Native gRPC depends on HTTP/2 **trailers** — headers sent *after* the response body — to carry the final status and any trailing metadata. Browsers' `fetch` and `XMLHttpRequest` APIs do not expose HTTP/2 trailers to JavaScript; the browser sandbox simply does not surface them. So a browser-hosted client physically cannot consume a native gRPC response. gRPC-Web is the answer: it is a small reframing of the gRPC protocol that moves the trailing status into the message body (as a specially-marked trailer frame) so a browser can read it with ordinary `fetch`. The contract is identical — the same `.proto`, the same messages, the same methods — only the *framing on the wire* differs. (Reference: <https://learn.microsoft.com/en-us/aspnet/core/grpc/grpcweb>.)

On the **server**, you enable gRPC-Web with one middleware and (because a Blazor WASM client is a different origin) CORS that exposes the gRPC-Web headers:

```csharp
builder.Services.AddCors(o => o.AddPolicy("grpc-web", p => p
    .WithOrigins(builder.Configuration["AdminOrigin"]!)
    .AllowAnyMethod()
    .AllowAnyHeader()
    .WithExposedHeaders("Grpc-Status", "Grpc-Message", "Grpc-Encoding", "Grpc-Accept-Encoding")));

// ... in the pipeline, before MapGrpcService:
app.UseCors("grpc-web");
app.UseGrpcWeb(new GrpcWebOptions { DefaultEnabled = true });

app.MapGrpcService<WorkshopService>().EnableGrpcWeb().RequireCors("grpc-web");
```

`UseGrpcWeb` with `DefaultEnabled = true` makes every mapped gRPC service speak gRPC-Web in addition to native gRPC; `EnableGrpcWeb()` on the mapping is the explicit per-service opt-in. The `WithExposedHeaders` for the `Grpc-Status` and `Grpc-Message` headers is the easy thing to forget — without it, the browser's CORS layer strips the status header and the client sees a "no status" error on every call even though the server responded correctly. That single missing line is the most common gRPC-Web setup bug, and it is worth memorizing.

On the **client** (Blazor WASM/Auto), you wrap the `HttpClient` handler in a `GrpcWebHandler` so the channel speaks gRPC-Web:

```csharp
#nullable enable
using Grpc.Net.Client;
using Grpc.Net.Client.Web;
using Workshop.Contract;

namespace Workshop.Admin.Services;

public sealed class AdminApi
{
    private readonly Workshop.Contract.Workshop.WorkshopClient _client;

    public AdminApi(IConfiguration config, IAccessTokenProvider tokens)
    {
        var handler = new GrpcWebHandler(GrpcWebMode.GrpcWeb, new HttpClientHandler());
        var channel = GrpcChannel.ForAddress(config["Api:BaseUrl"]!, new GrpcChannelOptions
        {
            HttpHandler = handler,
        });
        _client = new Workshop.Contract.Workshop.WorkshopClient(channel);
        _tokens = tokens;
    }

    private readonly IAccessTokenProvider _tokens;

    public async Task<IReadOnlyList<Submission>> ListPendingAsync(CancellationToken ct)
    {
        var tokenResult = await _tokens.RequestAccessToken();
        tokenResult.TryGetToken(out var token);

        var headers = new Grpc.Core.Metadata { { "Authorization", $"Bearer {token.Value}" } };
        var response = await _client.ListPendingSubmissionsAsync(
            new ListPendingSubmissionsRequest { PageSize = 50 },
            headers: headers, cancellationToken: ct);
        return response.Submissions;
    }
}
```

`GrpcWebMode.GrpcWeb` uses the binary gRPC-Web framing; `GrpcWebMode.GrpcWebText` is the base64 variant for environments that cannot carry binary (rarely needed). The Blazor component then binds its moderation table directly to `IReadOnlyList<Submission>` — the same generated `Submission` the MAUI client uses, the same one the server returns. Three clients, one type, one contract.

## 7. The mirror: which calls live on which surface

The syllabus requires both a REST surface (Minimal APIs) and a gRPC surface, mirroring the same domain. A reasonable question is: if gRPC carries everything, why have REST at all? The honest answer for the capstone is twofold. First, the REST surface is the *interoperability* door — anything that speaks HTTP/JSON (a curl in a runbook, a webhook from a third party, a quick `fetch` from a script) can hit `/api/lessons` without a generated stub. Second, building both *proves the domain is transport-agnostic* — if `CreateLesson` works identically over REST and gRPC, the business logic genuinely lives in the domain and not in the transport, which is the property the whole architecture rests on.

The mirroring rule for the slice: **every gRPC RPC has a REST twin, and both call the same domain operation.** They are two adapters over one core. The integration test (Lecture 3) asserts this explicitly: it creates a lesson over REST, then lists it over gRPC, and the same lesson comes back — proving the two surfaces share one database and one domain, not two parallel implementations that happen to agree today.

A useful way to think about the division of labor in *production* (deferred past the baseline): the MAUI app and the Blazor admin use gRPC/gRPC-Web because they have generated stubs and want the typed, efficient contract; external integrators and scripts use REST because they want zero-dependency HTTP. This week, both surfaces exist and both are tested; the division of *who uses which* is a Week-15 concern.

## 8. The contract review: what a PR must not do

Because the contract is load-bearing, the code review for any PR that touches it follows three rules, and these are the rules you will apply in the mini-project's peer review:

1. **No hand-written DTO that duplicates a proto message.** If a PR adds a `class LessonModel` whose fields mirror the `Lesson` message, reject it and point at the generated type. The duplicate is a second source of truth, and the maintenance cost is that the two drift the first time someone updates one and forgets the other.
2. **No identity in the request body.** If a PR adds a `learner_id` to `SubmitRequest`, reject it: the learner's identity is the validated token's `sub` claim, read server-side. A client-supplied identity is a client that can impersonate anyone.
3. **No business logic in the mapping layer.** The `ToProto` extensions shape data; they do not decide anything. If a PR puts a "hide rejected submissions from learners" rule in the mapping, reject it — that rule belongs in the domain or the service, where it is testable and visible, not buried in a wire adapter.

These three rules are the operational form of "keep three clients honest against one contract." The contract stays honest because the generation makes drift a build break; the *system* stays honest because the review keeps the three rules. Mechanism and discipline together.

## 9. Evolving the contract without breaking the clients

The contract is the source of truth, but a source of truth that can never change is a museum piece. Real systems add fields. The reason the integration baseline cares about this *now*, in the build week, is that the rules for changing a proto safely are the rules that keep the three clients honest *over time*, not just on day one — and if you do not internalize them in Week 13, you will break a client in Week 14 when you add a field during hardening.

Protobuf's wire format is designed for backward and forward compatibility, and the rules are concrete (cite <https://protobuf.dev/programming-guides/proto3/>):

- **Adding a field is safe.** A new optional field gets a new field number; old clients that do not know about it ignore it on read and leave it unset on write. So adding `int32 difficulty = 6;` to `Lesson` does not break a MAUI client built against the old proto — it simply does not see `difficulty`. *But* — and this is the integration-baseline discipline — you should still rebuild and re-run all three clients, because while the *wire* is compatible, you usually *want* the build to surface every place that should now use the new field.
- **Never reuse a field number.** Once `5` meant `created_at`, it means `created_at` forever. Reusing `5` for a different type silently corrupts data on any client still holding the old generated code. If you remove a field, reserve its number (`reserved 5;`) so no one reuses it by accident.
- **Never change a field's type incompatibly.** Changing `string id` to `int64 id` is a wire break; changing `int32` to `int64` is sometimes safe and sometimes not, depending on values. When in doubt, add a new field and deprecate the old one.
- **Enums need their zero value.** Removing the `_UNSPECIFIED = 0` value, or renumbering enum members, breaks clients. Add new members at the end.

The practical rule for the capstone: **additive changes only, field numbers are forever, and every contract change triggers a rebuild of all three clients in CI.** The CI workflow (Lecture 3) is what enforces the last clause — a contract change that breaks a client's *compilation* (not just its wire compatibility) fails the build, which is exactly the forcing function you want. Wire compatibility keeps old deployed clients working; build-time enforcement keeps the *codebase's* clients honest. You need both, and the baseline wires both.

## 10. Channel lifetime: the one performance rule that matters this week

There is one gRPC performance decision the build week must get right because it is hard to retrofit: **`GrpcChannel` is expensive to create and cheap to reuse, so you create it once and hold it for the app's lifetime.** A channel owns an HTTP/2 connection (and its connection pool); creating one per call means a new TCP handshake, a new TLS negotiation, and a new HTTP/2 setup on every request — easily 10x the latency of the call itself. (Cite <https://learn.microsoft.com/en-us/aspnet/core/grpc/performance>.)

In the MAUI client, the channel and the generated client are registered as singletons in the DI container, constructed once at startup:

```csharp
builder.Services.AddSingleton(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    return GrpcChannel.ForAddress(config["Api:BaseUrl"]!, new GrpcChannelOptions { /* creds */ });
});
builder.Services.AddSingleton(sp =>
    new Workshop.Contract.Workshop.WorkshopClient(sp.GetRequiredService<GrpcChannel>()));
```

In the Blazor admin, the same rule applies, but with a wrinkle: a Blazor WebAssembly app is single-user (one browser, one user), so a singleton channel is correct; a Blazor Server app is multi-user (one server, many circuits), so the channel is still a singleton *but the per-user token must be attached per call*, not baked into the channel. That is why the `AdminApi` in §6 attaches the `Authorization` header as call-level `Metadata` rather than as a channel-level credential — the channel is shared, the token is not. Getting this right in the build week means you do not discover a "all users see user 1's data" bug in the harden week.

The integration test creates its channel against the in-memory `TestServer` (Lecture 3), which has no network and no TLS, so the channel cost is irrelevant there — but the *production* clients must hold the channel, and the build week is where that pattern gets established. A capstone that creates a channel per call will pass its tests and crawl in production; the baseline establishes the right lifetime from commit one.

## 11. Testing the contract itself

The three clients compiling against the proto proves *syntactic* agreement — they all use the same field names and types. It does not prove *semantic* agreement: that `CreateLesson` over REST and `CreateLesson` over gRPC produce the same lesson, or that the mapping layer is a faithful round trip. Those need tests, and the build week writes them because they are cheap now and expensive to retrofit.

The first test is the **mapping round trip**, which belongs in the unit suite (no I/O):

```csharp
[Fact]
public void Submission_round_trips_through_proto()
{
    var domain = Submission.Create(Guid.CreateVersion7(), "learner-1", "answer");
    var proto = domain.ToProto();

    Assert.Equal(domain.Id.ToString(), proto.Id);
    Assert.Equal(domain.LearnerId, proto.LearnerId);
    Assert.Equal("answer", proto.Content);
    Assert.Equal(SubmissionStatus.Pending, proto.Status);          // enum mapped
    Assert.Equal(domain.SubmittedAt, proto.SubmittedAt.ToDateTimeOffset());  // timestamp mapped
}
```

This single test guards the two places the mapping is most likely to drift: the enum `switch` (does `Pending` map to `Pending`, not `Unspecified`?) and the `DateTimeOffset`↔`Timestamp` conversion (does the time survive the round trip without an offset shift?). When someone adds a `SubmissionStatus` value in Week 14 and forgets the mapping arm, this test goes red, not production.

The second test is the **two-surfaces-agree** assertion, which belongs in the integration suite because it needs a real database to prove both surfaces share one store (Lecture 3 has the full harness):

```csharp
[Fact]
public async Task Lesson_created_over_REST_is_readable_over_gRPC()
{
    await using var harness = await new SliceHarness(fixture).BuildAsync();
    var token = await harness.TokenForAsync("instructor-1", role: "instructor");

    // Create over REST.
    var http = harness.HttpClient(token);
    var created = await http.PostAsJsonAsync("/api/lessons",
        new { Title = "Spans 101", Body = "ref struct rules." });
    created.EnsureSuccessStatusCode();
    var lesson = await created.Content.ReadFromJsonAsync<LessonDto>();

    // Read it back over gRPC — proving both doors open onto one database.
    var grpc = harness.GrpcClient(token);
    var pending = await grpc.ListPendingSubmissionsAsync(new ListPendingSubmissionsRequest());
    // (the slice extends this to enroll/submit; the point is the cross-surface read)
    Assert.NotNull(lesson);
}
```

The value of this test is that it would fail loudly if someone gave REST its own `DbContext`, its own table, or its own business logic — the exact drift that "two doors into one house" is meant to prevent. A system where REST and gRPC quietly diverge passes every single-surface test and fails this one; that is why it is in the baseline.

Together, the mapping round-trip test and the cross-surface test are how you prove *semantic* contract agreement on top of the *syntactic* agreement the build gives you for free. The build keeps the clients honest about shapes; these tests keep the server honest about behavior. The integration baseline needs both.

## 12. What you now have, and what Lecture 3 makes trustworthy

You have the contract working as a real source of truth: one `workshop.proto` in one shared project, generating a server base class the backend overrides and a client class both MAUI and Blazor consume; native gRPC for the phone, gRPC-Web for the browser, the same typed messages on every side; a hand-written mapping layer between the wire shape and the domain model; a REST mirror that proves the domain is transport-agnostic; and a three-rule contract review that keeps the clients honest.

What you do *not* yet have is a reason to trust that any of it works against a real database with real auth. The backend compiles; the clients compile; the slice "should" work. "Should" is not green. Lecture 3 makes green mean something: `WebApplicationFactory<TEntryPoint>` to host the real backend in-memory, Testcontainers to give that host a real PostgreSQL and a real Keycloak, migrations applied against the ephemeral database, a real token minted and validated, and the Serilog + OpenTelemetry wiring that lets a failed assertion be read from a trace instead of guessed at. That is the integration baseline, and it is what turns "the contract compiles" into "the contract works."
