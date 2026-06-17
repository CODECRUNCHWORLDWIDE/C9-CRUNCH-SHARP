# Challenge 2 — Schema Evolution: v1 Clients Against v2 Servers

> **Estimated time:** 2 hours. **Prerequisite:** Exercise 1 complete; the proto3 wire-format section of Lecture 1 understood. **Citations:** the proto3 language guide's "updating message types" section at <https://protobuf.dev/programming-guides/proto3/#updating>, and the field-presence guide at <https://protobuf.dev/programming-guides/field_presence/>.

## The premise

Schema evolution is the production part of gRPC that the textbooks gloss over. Your v1 service has been in production for nine months. Tomorrow you ship v2 with three changes: a renamed field, a new field, and a removed field. Wire compatibility means that during the rolling deployment — when half your servers are v1 and half are v2, and clients are some mix of both versions — *every combination* must continue to work. v1 clients against v2 servers. v2 clients against v1 servers. Servers receiving an interleaved mix of both.

This challenge walks you through one such evolution, deliberately exercises the cross-version paths, and asks you to identify which combinations are safe and which would have been a production incident.

## What you will build

A small two-version repository:

- `protos/v1/order.proto` — the original schema.
- `protos/v2/order.proto` — the evolved schema.
- `src/Server.V1/` — a server compiled against v1.
- `src/Server.V2/` — a server compiled against v2.
- `src/Client.V1/` — a client compiled against v1.
- `src/Client.V2/` — a client compiled against v2.
- `tests/CrossVersionTests/` — xUnit tests that exercise every (client-version × server-version) pair.

## Setup

### 1. Scaffold

```bash
mkdir Challenge02-SchemaEvolution && cd Challenge02-SchemaEvolution
mkdir -p protos/v1 protos/v2
mkdir -p src/Server.V1 src/Server.V2 src/Client.V1 src/Client.V2
mkdir -p tests/CrossVersionTests

dotnet new sln -n SchemaEvolution
```

### 2. The v1 `.proto`

`protos/v1/order.proto`:

```proto
syntax = "proto3";

package crunch.order.v1;
option csharp_namespace = "Crunch.Order.V1";

import "google/protobuf/timestamp.proto";

message Order {
  string id = 1;
  string customer_email = 2;
  int64 total_cents = 3;
  google.protobuf.Timestamp created_at = 4;
  string discount_code = 5;             // will be removed in v2
}

message PlaceOrderRequest {
  Order order = 1;
}

message PlaceOrderResponse {
  string id = 1;
  bool accepted = 2;
}

service OrderService {
  rpc PlaceOrder(PlaceOrderRequest) returns (PlaceOrderResponse);
}
```

### 3. The v2 `.proto`

`protos/v2/order.proto`:

```proto
syntax = "proto3";

package crunch.order.v2;
option csharp_namespace = "Crunch.Order.V2";

import "google/protobuf/timestamp.proto";

message Order {
  string id = 1;
  string customer_email = 2;
  int64 total_cents = 3;
  google.protobuf.Timestamp created_at = 4;

  // v2 removes discount_code (field 5). Reserve to prevent reuse.
  reserved 5;
  reserved "discount_code";

  // v2 adds optional currency_code. Old clients omit it; new clients send it.
  optional string currency_code = 6;       // ISO 4217, e.g. "USD".

  // v2 splits customer_email into a structured customer block. The OLD field
  // is left in place for backward compatibility; new clients populate the
  // structured block in addition.
  message Customer {
    string email = 1;
    string display_name = 2;
  }
  Customer customer = 7;
}

message PlaceOrderRequest {
  Order order = 1;
}

message PlaceOrderResponse {
  string id = 1;
  bool accepted = 2;
  string warning = 3;          // v2 adds a free-text warning channel.
}

service OrderService {
  rpc PlaceOrder(PlaceOrderRequest) returns (PlaceOrderResponse);
}
```

Note two deliberate departures from "obvious" evolution:

- **The package name was bumped to `crunch.order.v2`.** This is doctrine: when you make a breaking-feeling change, bump the version so both v1 and v2 servers can coexist on different routes. (In this challenge we will *also* test the case where the package was *not* bumped — to understand why the doctrine matters.)
- **`customer_email` was retained at field 3** (oh wait — re-read; field 3 is `total_cents`. `customer_email` is field 2). The point: we did not change any existing field's number or type. The only structural changes are *adding* fields (`currency_code`, `customer`, `warning`) and *removing* a field (`discount_code`).

### 4. Server and client implementations

Server v1 and v2 each implement `PlaceOrder` to print the incoming order's fields, validate that `total_cents > 0`, and return `accepted = true`.

Client v1 and v2 each call `PlaceOrder` with a small populated `Order` and print the response.

You write four projects — but the heart of the challenge is the **tests/CrossVersionTests/** project which exercises the four combinations.

## The cross-version test matrix

| Test | Client | Server | Expected behaviour |
|------|--------|--------|--------------------|
| T1   | v1     | v1     | Round-trips correctly. Baseline.                                                                 |
| T2   | v2     | v2     | Round-trips correctly. New fields (`currency_code`, `customer`, `warning`) are populated.        |
| T3   | v1     | v2     | Round-trips. Server reads `customer_email` from v1's field 2; the v2 `customer` block is empty.  |
| T4   | v2     | v1     | Round-trips. Server reads `customer_email`, `total_cents`. Ignores v2's new fields. **Critical** |

The "critical" mark on T4 deserves explanation. **When a v2 client sends `currency_code = "USD"` to a v1 server, the v1 server reads the wire bytes, sees field 6, does not know what field 6 is, and *silently skips it*.** The server does not error. The currency information is dropped on the floor. This is the proto3 *forward-compatibility* guarantee, and it is the reason "you may add new fields freely" is the schema-evolution mantra.

You should write each test and prove the behaviour:

```csharp
// tests/CrossVersionTests/T3_V1ClientV2Server.cs
[Fact]
public async Task V1Client_To_V2Server_RoundTrips()
{
    using var server = StartServerV2();   // helper that boots a v2 server on a random port
    using var channel = GrpcChannel.ForAddress(server.Address);
    var client = new Crunch.Order.V1.OrderService.OrderServiceClient(channel);

    var request = new Crunch.Order.V1.PlaceOrderRequest
    {
        Order = new Crunch.Order.V1.Order
        {
            Id = "O001",
            CustomerEmail = "alice@example.com",
            TotalCents = 12345,
            CreatedAt = Timestamp.FromDateTime(DateTime.UtcNow),
            DiscountCode = "SUMMER50",      // v1 still sets this; v2 server ignores
        }
    };

    var reply = await client.PlaceOrderAsync(request);

    Assert.True(reply.Accepted);
    Assert.Equal("O001", reply.Id);
    // The v2 server saw the v1 message; verify in the v2 server's log that
    // currency_code was empty and customer block was empty.
}
```

Repeat for T1, T2, T4.

## Acceptance criteria

- [ ] All four projects build with 0 warnings, 0 errors.
- [ ] All four cross-version tests pass.
- [ ] The v1 → v2 server case (T3) demonstrates that the v2 server reads `customer_email` from field 2 and the new `customer` block is empty.
- [ ] The v2 → v1 server case (T4) demonstrates that the v1 server reads `customer_email`, `total_cents`, and `created_at`, and the v2-only fields are silently dropped.
- [ ] The `discount_code` reservation in v2 prevents accidental reuse — try adding `string new_field = 5;` to v2 and confirm the proto compiler errors.

## Reflection (write into RESULTS.md)

1. **Why bump the package name?** This challenge deliberately did. What would happen if v2 had stayed `package crunch.order.v1;`? Could v1 and v2 servers run side-by-side on the same load balancer? At what level (URL path, hostname, port) does the version distinction need to live for clean rolling deploys?

2. **The forward-compatibility skipping rule.** A v1 server reading a v2 message sees an unknown field tag. The proto3 spec says the server *silently skips* it. In some workflows this is exactly wrong — you might want the server to reject unknown fields as a malformed-input signal. Is there a way to opt into "strict" parsing? (Answer: yes, `IMessage.MergeFrom` has a `DiscardUnknownFields` flag in some bindings; investigate.)

3. **Field-type changes.** This challenge deliberately did not change any field's type. Suppose v2 changed `total_cents` from `int64` to `string` (a "string-encoded amount" for currency precision). Would v1 clients still work against v2 servers? Would v2 clients still work against v1 servers? Where exactly does the wire-level breakage manifest?

4. **Removing required behaviour.** The v2 schema removed `discount_code`. v1 clients still send it; the v2 server discards it. If the v1 client's semantics *depended* on the discount being honoured, the discount silently no longer applies. How would you catch this kind of behavioural regression in tests? (Hint: cross-version tests are a static check; the behavioural check needs domain-specific assertions on the *result* of the call.)

5. **The `optional` keyword on `currency_code`.** v2 declared it `optional`. Why? What changes about the v2-client-to-v2-server case if the keyword is dropped? What changes about the v2-client-to-v1-server case?

## Stretch goals (optional)

- **Set up `buf breaking` and run it on the diff between v1 and v2.** `buf` is a free protobuf linter and breaking-change detector. Citation: <https://buf.build/docs/breaking/overview>. It enforces the rules manually here; running it as a CI check is the production discipline.
- **Add a v3 that introduces a wire-incompatible change** (changing a field's type). Run the cross-version tests and predict each failure mode before running.
- **Document a deprecation pipeline** — how would your team plan the v1 → v2 migration over a six-week window, with safe gates at each step?

## Submission

Place all artifacts under `challenges/challenge-02/`. Commit with:

```
challenge-02: cross-version schema evolution; four-cell test matrix passing
```

Include `RESULTS.md` with the five reflection answers and a one-paragraph summary of which evolution patterns are safe and which are not.
