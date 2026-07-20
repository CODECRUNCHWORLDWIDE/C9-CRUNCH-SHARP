# Week 6 — Exercises

Three coding exercises that drill the week's mechanical skills. Each is a `.cs` file you can drop into a fresh ASP.NET Core 9 project and complete by filling in the `TODO`s. None should take more than 60 minutes; if you spend longer, read the hints at the bottom of the file.

| Exercise | Time | What you'll exercise |
|----------|-----:|----------------------|
| [exercise-01-cookie-auth.cs](./exercise-01-cookie-auth.cs) | 60 min | Wire cookie authentication into an ASP.NET Core 9 minimal API. Sign-in, sign-out, `[Authorize]`, role-based protection. Prove it with curl. |
| [exercise-02-jwt-bearer.cs](./exercise-02-jwt-bearer.cs) | 60 min | Replace cookies with JWT bearer authentication. Mint a token, validate it, decode it at `jwt.io`. Tamper-test it. |
| [exercise-03-policy-handlers.cs](./exercise-03-policy-handlers.cs) | 60 min | Write `MinimumAccountAgeRequirement` + handler. Wire it as a policy. Test it with `WebApplicationFactory<T>` and a `TestAuthHandler`. |

## Acceptance criteria (all three)

- `dotnet build`: 0 warnings, 0 errors.
- The smoke output in each file matches (modulo cookie values, JWT values, and timing).
- `app.UseAuthentication()` is called *before* `app.UseAuthorization()` in every exercise. Reversing them is the silent bug.
- Every `[Authorize(Policy = "...")]` references a policy registered in `AddAuthorization(...)`.
- No production secret (JWT signing key, database connection string with a password) lives in source. Use `dotnet user-secrets` for dev; environment variables for CI; a key vault for production.
- Every endpoint that requires authentication has either `.RequireAuthorization(...)` on the route or `[Authorize]` on the handler. The `FallbackPolicy` is the production guard rail, not the only line of defense.

## Setup

For each exercise:

```bash
mkdir Exercise01 && cd Exercise01
dotnet new web -n Exercise01 -o src/Exercise01
# Replace src/Exercise01/Program.cs with the contents of the exercise file.
dotnet run --project src/Exercise01
```

Exercise 2 needs two extra NuGet packages:

```bash
cd src/Exercise02
dotnet add package Microsoft.AspNetCore.Authentication.JwtBearer
dotnet add package System.IdentityModel.Tokens.Jwt
```

Exercise 3 needs a test project alongside the API:

```bash
mkdir Exercise03 && cd Exercise03
dotnet new sln -n Exercise03
dotnet new web   -n Exercise03.Api   -o src/Exercise03.Api
dotnet new xunit -n Exercise03.Tests -o tests/Exercise03.Tests
dotnet sln add src/Exercise03.Api tests/Exercise03.Tests
dotnet add tests/Exercise03.Tests reference src/Exercise03.Api
dotnet add tests/Exercise03.Tests package Microsoft.AspNetCore.Mvc.Testing
```

## What you'll have when you're done

Three small ASP.NET Core 9 applications that, together, exercise the entire authentication-and-authorization surface you will use in the rest of the curriculum: cookies for browser-facing surfaces, JWT bearer for API surfaces, and policy-based authorization with a custom requirement and handler for any business rule that doesn't fit `Require*`. Commit each exercise to your Week 6 Git repository. Future-you in Week 7 (when you replace the local JWT issuer with Keycloak's OIDC) will be glad you have the reference — the authentication scheme changes; everything downstream (`[Authorize(Policy = "...")]`, the handlers, the tests) does not.

## A note on testing

Exercise 3 introduces `WebApplicationFactory<T>` and the `TestAuthHandler` pattern. These are the two most-load-bearing testing primitives in the Week 6 mini-project. The cost of writing them once is high; the cost of *every subsequent integration test* drops to ~10 lines. If you finish Exercise 3 ahead of the 60-minute budget, write a fourth test that asserts `/health` is reachable without `X-Test-User` headers (no auth required). That muscle memory turns every authenticated endpoint into a four-test cluster (200, 403, 401, 200-anonymous-allowed-where-applicable) that catches the regressions production users hit.
