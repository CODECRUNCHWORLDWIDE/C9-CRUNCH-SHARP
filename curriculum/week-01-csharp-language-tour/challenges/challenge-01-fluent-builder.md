# Challenge 1 — Fluent Builder with Records and `with`

**Time estimate:** ~90 minutes.

## Problem statement

Build a small, type-safe **fluent builder API** for constructing an `HttpRequest` value object. The builder must:

1. Use a `record` for the immutable result (`HttpRequest`).
2. Use a separate `record` for the in-progress builder, with `with`-expressions for every step (**no mutable fields**).
3. Provide a fluent API like this:

```csharp
HttpRequest req = HttpRequest
    .Get("https://api.example.com/users")
    .Header("Accept", "application/json")
    .Header("X-Request-Id", Guid.NewGuid().ToString())
    .Query("page", "1")
    .Query("limit", "20")
    .Timeout(TimeSpan.FromSeconds(5))
    .Build();
```

4. Validate at `Build()` time that the URL is well-formed and the method is one of `GET`, `POST`, `PUT`, `PATCH`, `DELETE`, `HEAD`, `OPTIONS`. Throw a meaningful exception otherwise.

5. Expose factory methods `Get`, `Post`, `Put`, `Patch`, `Delete`, `Head`, `Options` that all return the builder.

6. For `Post`, `Put`, and `Patch`, support `.JsonBody(object body)` that captures a payload object.

## Acceptance criteria

- [ ] A solution with one library project (`Http.Builder`) and one xUnit test project (`Http.Builder.Tests`).
- [ ] `dotnet build`: 0 warnings, 0 errors.
- [ ] `dotnet test`: at least **10 passing tests**, covering:
  - Building a GET request with multiple headers and query strings.
  - Building a POST request with a JSON body.
  - That repeated headers accumulate (multi-value).
  - That `Build()` throws on a malformed URL.
  - That `Build()` throws when the body method tries to send a body on a `GET`.
  - That the resulting `HttpRequest` is an immutable record (equality, `with`).
- [ ] **No mutable state** in the builder. Every step returns a *new* builder. Use records and `with`.
- [ ] **Zero `!` operators** in the implementation. Nullability is honest.
- [ ] A short `README.md` in the repo root explaining the API and showing the example above.
- [ ] Code is committed to your Week 1 GitHub repo under `challenges/challenge-01/`.

## Stretch

- Add a `.RetryOn(params HttpStatusCode[] codes)` step that captures retry policy in the request. Validate that it cannot be set twice.
- Add a `.Send(HttpClient http, CancellationToken ct)` extension that actually issues the request against a real `HttpClient`. Test against `https://httpbin.org/anything` (use only in stretch — keep the core challenge offline-friendly).
- Implement `IEquatable<HttpRequest>` value-equality tests with two requests that have the same headers in different declaration order — should they be equal? Make a deliberate choice and document it.

## Hints

<details>
<summary>Builder skeleton</summary>

```csharp
namespace Http.Builder;

public enum HttpMethod { Get, Post, Put, Patch, Delete, Head, Options }

public record HttpRequest(
    HttpMethod Method,
    Uri Url,
    IReadOnlyList<KeyValuePair<string, string>> Headers,
    IReadOnlyList<KeyValuePair<string, string>> Query,
    object? JsonBody,
    TimeSpan? Timeout);

public record HttpRequestBuilder(
    HttpMethod Method,
    string Url,
    IReadOnlyList<KeyValuePair<string, string>> Headers,
    IReadOnlyList<KeyValuePair<string, string>> Query,
    object? JsonBody,
    TimeSpan? Timeout)
{
    public static HttpRequestBuilder Get(string url) =>
        new(HttpMethod.Get, url, [], [], null, null);

    // ... other factories ...

    public HttpRequestBuilder Header(string name, string value) =>
        this with { Headers = [..Headers, new(name, value)] };

    public HttpRequestBuilder Query(string name, string value) =>
        this with { Query = [..Query, new(name, value)] };

    public HttpRequestBuilder Timeout(TimeSpan t) =>
        this with { Timeout = t };

    public HttpRequest Build()
    {
        if (!Uri.TryCreate(Url, UriKind.Absolute, out var uri))
            throw new ArgumentException($"Malformed URL: '{Url}'", nameof(Url));

        return new HttpRequest(Method, uri, Headers, Query, JsonBody, Timeout);
    }
}
```

</details>

<details>
<summary>How to make `JsonBody` only valid on POST/PUT/PATCH</summary>

Option A — runtime check in `Build()`:

```csharp
if (JsonBody is not null && Method is HttpMethod.Get or HttpMethod.Head or HttpMethod.Delete or HttpMethod.Options)
    throw new InvalidOperationException($"{Method} requests cannot have a JSON body.");
```

Option B (advanced) — encode it in the type. Have two builder types: `HttpRequestBuilder` (no body) and `HttpRequestBuilderWithBody`. `Get/Head/Delete/Options` return the first; `Post/Put/Patch` return the second. Only the second has a `.JsonBody` method.

Option B is much more "type-safe" and is the canonical advanced answer in the C9 style. Try Option A first; if you have time, refactor to Option B as a stretch.

</details>

<details>
<summary>How to write an xUnit test that asserts a throw</summary>

```csharp
[Fact]
public void Build_throws_on_malformed_url()
{
    var b = HttpRequestBuilder.Get("not a url");
    var ex = Assert.Throws<ArgumentException>(() => b.Build());
    Assert.Contains("Malformed URL", ex.Message);
}
```

</details>

## Submission

Commit your `Http.Builder` solution under `challenges/challenge-01/` in your Week 1 GitHub repo. Make sure `dotnet build` and `dotnet test` both pass on a fresh clone — that means committing a `.gitignore` that excludes `bin/` and `obj/`, and ensuring no checked-in NuGet caches.

## Why this matters

The fluent-builder-with-records-and-`with` pattern shows up everywhere in modern .NET:

- **`HttpRequestMessage`** in the BCL, used through extension methods.
- **`WebApplicationBuilder`** in ASP.NET Core (Week 5).
- **`DbContextOptionsBuilder`** in EF Core (Week 6).
- **`PolicyBuilder`** in Polly (Week 8).

If you internalize "records + `with` is how you build immutable builders" now, every framework you meet later will feel familiar.
