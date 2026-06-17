// Exercise 2 — JWT Bearer Authentication in ASP.NET Core 9
//
// Goal: Replace the cookie authentication from Exercise 1 with JWT bearer
//       authentication. Issue a JWT from /auth/token, validate it on every
//       request via TokenValidationParameters, read the principal off
//       HttpContext.User on /me, and prove the chain by decoding the
//       returned token at jwt.io.
//
// Estimated time: 60 minutes.
//
// HOW TO USE THIS FILE
//
// 1. Scaffold a fresh web project:
//
//      mkdir JwtBearer && cd JwtBearer
//      dotnet new web -n JwtBearer -o src/JwtBearer
//
//    Add the JWT bearer package (Cookies ships with the framework reference
//    but JWT bearer does not):
//
//      cd src/JwtBearer
//      dotnet add package Microsoft.AspNetCore.Authentication.JwtBearer
//      dotnet add package System.IdentityModel.Tokens.Jwt
//      cd ../..
//
//    Replace src/JwtBearer/Program.cs with the contents of THIS FILE.
//
// 2. Generate a signing key and store it with `dotnet user-secrets`:
//
//      cd src/JwtBearer
//      dotnet user-secrets init
//      dotnet user-secrets set "Jwt:Key"      "$(openssl rand -base64 32)"
//      dotnet user-secrets set "Jwt:Issuer"   "https://c9.local"
//      dotnet user-secrets set "Jwt:Audience" "c9-exercise-2"
//      cd ../..
//
// 3. Fill in the four TODOs below.
//
// 4. Run:
//
//      dotnet run --project src/JwtBearer
//
//    Use curl in another terminal:
//
//      # 1) Hit /me without a token — should be 401.
//      curl -i http://localhost:5000/me
//
//      # 2) Mint a token.
//      curl -i -X POST http://localhost:5000/auth/token \
//           -H "Content-Type: application/json" \
//           -d '{"username":"ada","password":"correct"}'
//
//      # Copy the access_token field from the response body.
//      export TOKEN="<paste-it-here>"
//
//      # 3) Hit /me with the token — should be 200 + body.
//      curl -i -H "Authorization: Bearer $TOKEN" http://localhost:5000/me
//
//      # 4) Hit /admin with the token — should be 200 for ada, 403 for others.
//      curl -i -H "Authorization: Bearer $TOKEN" http://localhost:5000/admin
//
//      # 5) Tamper the token (change one character in the payload) and re-hit /me.
//      #    Should be 401 — signature validation fails.
//      curl -i -H "Authorization: Bearer ${TOKEN}TAMPERED" http://localhost:5000/me
//
// 5. Paste the token into https://jwt.io/ (CLIENT-SIDE only; do not paste
//    real production tokens). Confirm you see iss, aud, sub, exp, iat, jti,
//    and the role claim. The signature panel will not validate at jwt.io
//    unless you paste the same Jwt:Key — that is normal.
//
// ACCEPTANCE CRITERIA
//
//   [ ] /me returns 401 without an Authorization header.
//   [ ] /auth/token with correct credentials returns 200 + JSON {access_token, expires_in}.
//   [ ] /auth/token with wrong credentials returns 401.
//   [ ] /me with a valid Bearer token returns 200 + JSON {name, email, role}.
//   [ ] /admin returns 200 for ada, 403 for any other authenticated user.
//   [ ] A tampered token (any byte changed) returns 401 — NOT 200.
//   [ ] dotnet build: 0 Warning(s), 0 Error(s).
//   [ ] The signing key comes from user-secrets, NOT a hardcoded literal.
//
// SMOKE OUTPUT (target — abbreviated)
//
//   $ curl -i http://localhost:5000/me
//   HTTP/1.1 401 Unauthorized
//
//   $ curl -X POST .../auth/token -d '{"username":"ada","password":"correct"}'
//   {"access_token":"eyJhbGciOiJIUzI1NiIs...","expires_in":3600}
//
//   $ curl -H "Authorization: Bearer eyJ..." http://localhost:5000/me
//   {"name":"ada","email":"ada@example.com","role":"admin"}
//
//   Build succeeded · 0 warnings · 0 errors
//
// Inline hints at the bottom.

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// In-memory user store (same as Exercise 1).
// ---------------------------------------------------------------------------

var users = new Dictionary<string, (string Email, string Role, string Password)>
{
    ["ada"]   = ("ada@example.com",   "admin", "correct"),
    ["linus"] = ("linus@example.com", "user",  "kernel-1991"),
    ["grace"] = ("grace@example.com", "user",  "compilers-rule"),
};

var jwtKey      = builder.Configuration["Jwt:Key"]
                ?? throw new InvalidOperationException("Jwt:Key not configured. Run `dotnet user-secrets set Jwt:Key ...`.");
var jwtIssuer   = builder.Configuration["Jwt:Issuer"]   ?? "https://c9.local";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "c9-exercise-2";

var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));

// ---------------------------------------------------------------------------
// TODO 1 — Register JWT bearer authentication.
//
// builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
//     .AddJwtBearer(options =>
//     {
//         options.TokenValidationParameters = new TokenValidationParameters
//         {
//             ValidateIssuer           = true,
//             ValidIssuer              = jwtIssuer,
//             ValidateAudience         = true,
//             ValidAudience            = jwtAudience,
//             ValidateIssuerSigningKey = true,
//             IssuerSigningKey         = signingKey,
//             ValidateLifetime         = true,
//             ClockSkew                = TimeSpan.FromSeconds(30),
//         };
//     });
// ---------------------------------------------------------------------------

// YOUR CODE HERE

// ---------------------------------------------------------------------------
// TODO 2 — Register authorization with one named policy "AdminsOnly".
//
// builder.Services.AddAuthorization(options =>
// {
//     options.AddPolicy("AdminsOnly", policy => policy.RequireRole("admin"));
// });
// ---------------------------------------------------------------------------

// YOUR CODE HERE

var app = builder.Build();

// ---------------------------------------------------------------------------
// TODO 3 — Add the two middleware calls. Authentication BEFORE authorization.
//
// app.UseAuthentication();
// app.UseAuthorization();
// ---------------------------------------------------------------------------

// YOUR CODE HERE

// ---------------------------------------------------------------------------
// TODO 4 — Implement the three endpoints.
//
// app.MapPost("/auth/token", (LoginRequest req) =>
// {
//     if (!users.TryGetValue(req.Username, out var u) || u.Password != req.Password)
//         return Results.Unauthorized();
//
//     var claims = new List<Claim>
//     {
//         new(JwtRegisteredClaimNames.Sub,   req.Username),
//         new(JwtRegisteredClaimNames.Email, u.Email),
//         new(JwtRegisteredClaimNames.Jti,   Guid.NewGuid().ToString()),
//         new(ClaimTypes.Role,               u.Role),
//     };
//
//     var creds = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);
//
//     var token = new JwtSecurityToken(
//         issuer:             jwtIssuer,
//         audience:           jwtAudience,
//         claims:             claims,
//         notBefore:          DateTime.UtcNow,
//         expires:            DateTime.UtcNow.AddHours(1),
//         signingCredentials: creds);
//
//     var jwt = new JwtSecurityTokenHandler().WriteToken(token);
//     return Results.Ok(new { access_token = jwt, expires_in = 3600 });
// });
//
// app.MapGet("/me", (HttpContext ctx) =>
// {
//     var user = ctx.User;
//     return Results.Ok(new
//     {
//         name  = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
//                 ?? user.FindFirst(JwtRegisteredClaimNames.Sub)?.Value,
//         email = user.FindFirst(ClaimTypes.Email)?.Value,
//         role  = user.FindFirst(ClaimTypes.Role)?.Value,
//     });
// })
// .RequireAuthorization();
//
// app.MapGet("/admin", () => Results.Ok(new { message = "welcome, admin" }))
//    .RequireAuthorization("AdminsOnly");
// ---------------------------------------------------------------------------

// YOUR CODE HERE

app.Run();

public sealed record LoginRequest(string Username, string Password);

// ---------------------------------------------------------------------------
// HINTS
// ---------------------------------------------------------------------------
//
// 1) The signing key MUST be at least 256 bits (32 bytes) for HS256. If you
//    use a short key, the JwtSecurityTokenHandler will throw with a
//    "key size must be greater than or equal to 256 bits" message. The
//    `openssl rand -base64 32` command produces exactly 256 bits.
//
// 2) The Sub claim is the canonical subject. The JWT bearer handler maps
//    `sub` to `ClaimTypes.NameIdentifier` automatically on the receive side
//    (see JwtSecurityTokenHandler.DefaultMapInboundClaims). If you turn the
//    mapping off, read `JwtRegisteredClaimNames.Sub` instead.
//
// 3) `ClaimTypes.Role` is mapped to the JWT claim name `role` on the wire.
//    Other JWT issuers (Auth0, Keycloak, IdentityServer) use namespaced
//    role claims like `https://schemas.microsoft.com/ws/2008/06/identity/claims/role`.
//    For Week 6 we issue our own tokens; the names match.
//
// 4) `ValidateIssuerSigningKey = true` is THE security boundary. Never
//    disable it. If you must support multiple keys during rotation, supply
//    an `IssuerSigningKeyResolver` instead.
//
// 5) ClockSkew defaults to 5 minutes. That is a relic from the era of badly
//    synchronized servers. On modern .NET 9 + NTP-synced clocks, 30 seconds
//    is plenty. Drop it lower (or to Zero) for very-short-lived tokens.
//
// 6) The tampered-token test (step 5 in the smoke sequence) is the highest-
//    ROI test in a JWT setup. If a tampered token returns 200, you forgot
//    `ValidateIssuerSigningKey`. There is no other explanation.
//
// 7) Production JWTs include a `iat` (issued-at) claim that the handler adds
//    automatically. You do not need to add it yourself unless you want to
//    override the value.
//
// 8) Paste the token at https://jwt.io/ and look at the three sections.
//    The header is `{"alg":"HS256","typ":"JWT"}`. The payload contains
//    your claims plus `iss`, `aud`, `exp`, `iat`, `nbf`, `jti`. The
//    signature is opaque base64url unless you paste the key to validate.
//    DO NOT paste production tokens at jwt.io — the page is client-side
//    but the habit is bad; use scratch tokens for inspection.
