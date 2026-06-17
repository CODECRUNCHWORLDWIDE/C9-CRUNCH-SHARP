# Lecture 1 — Protocol Buffers 3: the Language, the Wire Format, and the Generated C# Code

> **Time:** 2 hours. Take the language section in one sitting, the wire-format section in a second sitting, and the `Grpc.Tools` MSBuild section last. **Prerequisites:** Week 2 (`.csproj` literacy) and Week 8 (`IAsyncEnumerable<T>`). **Citations:** the protobuf 3 language guide at <https://protobuf.dev/programming-guides/proto3/>, the wire-format reference at <https://protobuf.dev/programming-guides/encoding/>, and Microsoft Learn's gRPC basics page at <https://learn.microsoft.com/en-us/aspnet/core/grpc/basics>.

## 1. Why protobuf, and why now

Every networked system has to answer one question: when process A sends bytes to process B, what shape are those bytes? The answer the web converged on in the 2000s was JSON — readable, schemaless, ubiquitous. The cost of that choice is that JSON is verbose (every field is encoded as a string, every brace is a byte, every comma is a byte), slow to parse (a JSON parser has to find keys by string match), and unenforced at the wire level (the receiver has no schema to validate against). For a public API serving browsers, JSON is the right answer. For two backend services trading a million messages a second, JSON is the answer that makes you buy three more machines than you should have.

Protobuf was Google's internal answer to the same question in the early 2000s, open-sourced in 2008. The model is the inverse of JSON: instead of "the message describes itself with string keys", protobuf says "the message has a schema you both agreed on in advance, and the wire bytes are tagged with small integer numbers that index into that schema." A field that JSON would encode as `"name":"alice"` (12 bytes plus the comma) protobuf encodes as a 1-byte tag followed by a 1-byte length followed by the 5 bytes of `"alice"` — 7 bytes total. Over a million messages, the saving is meaningful; over a billion, it pays for the cost of the schema-management discipline several times over.

The version of protobuf you will use this week is **proto3**, the third major revision, released in 2016 and stable since. Proto2 is still around in legacy systems but is not the default for new work. Proto3 is what `Grpc.Tools` generates against, what `grpc-dotnet` expects, and what every major language's gRPC binding agrees on. We will not cover proto2.

Read this section alongside the protobuf 3 language guide at <https://protobuf.dev/programming-guides/proto3/>. The guide is approximately 8,000 words; you will return to it many times. Read once now end-to-end, then come back to it as a reference.

## 2. The proto3 file: structure and syntax

A `.proto` file is a small text file with three concerns: the file's own metadata (syntax version, package, options), the message types, and the service definitions. A minimal file looks like:

```proto
syntax = "proto3";

package crunch.counter.v1;

option csharp_namespace = "Crunch.Counter.V1";

message IncrementRequest {
  string counter_name = 1;
  int64 delta = 2;
}

message IncrementResponse {
  int64 new_value = 1;
}

service Counter {
  rpc Increment(IncrementRequest) returns (IncrementResponse);
}
```

Eleven lines, four constructs. Let us walk each one.

### 2.1 `syntax = "proto3";`

This is the *first non-comment, non-whitespace line* of every proto3 file. It tells `protoc` (and `Grpc.Tools`) to apply the proto3 rules. Without this declaration, `protoc` defaults to proto2 and a long list of small semantic differences kick in — most subtly, the difference in how missing fields are handled. Always include it. Always make it the first non-comment line.

### 2.2 `package crunch.counter.v1;`

The `package` declaration scopes message and service names within the file. Two files with `package foo.bar;` may not both declare a `message Baz` — the fully-qualified name `foo.bar.Baz` would collide. Two files with `package foo.bar.v1;` and `package foo.bar.v2;` may both declare `message Baz`; they are `foo.bar.v1.Baz` and `foo.bar.v2.Baz`.

The convention for package names is `lower_snake_case.dotted`, with a *version suffix* like `.v1` or `.v2`. The version suffix is doctrine, not decoration: when you make a breaking change to a schema you bump the package version, generate a new set of types in a new namespace, and run both old and new servers concurrently during the migration. See the gRPC API versioning guidelines at <https://cloud.google.com/apis/design/versioning> for the long-form reasoning.

### 2.3 `option csharp_namespace = "Crunch.Counter.V1";`

`option` lines configure code-generator behaviour. The most important one for us is `csharp_namespace`, which controls the C# namespace into which the generated types are emitted. Without this option, `Grpc.Tools` would synthesise a namespace from the package — `Crunch.Counter.V1` in this case — but the synthesis sometimes picks a casing you do not like, so the option is the safer bet. Set it. Match your `.csproj`'s root namespace where you can.

Other languages have their own `option foo_package` directives: `java_package`, `go_package`, `php_namespace`, and so on. Set the ones for the languages you will generate against; ignore the rest.

### 2.4 `message IncrementRequest { ... }`

A `message` is a named struct on the wire. Each field has three parts:

- a **type** (a scalar like `int64`, another message, an enum, or a special form like `repeated int32` or `oneof`)
- a **name** in `lower_snake_case` (`counter_name`, not `counterName` — the C# generator will rename it to `CounterName`)
- a **field number**, a positive integer, *unique within the message*

The field number is **the load-bearing part**. The wire bytes contain field numbers, not field names. Renaming a field is a no-op on the wire; changing its field number is a breaking change. Changing its *type* is a breaking change unless the new type happens to share the same wire encoding (see Section 4). Removing a field and reusing its number for something else is a breaking change *and* an outage waiting to happen — old clients will write old data into the new field's slot, and the server will read garbage. We will return to this in Section 6.

Field numbers 1 through 15 encode as a single byte on the wire (tag = `field_number << 3 | wire_type`, packed into a varint; values 0-127 fit in one varint byte). Field numbers 16 through 2047 cost two bytes. Reserve the low numbers for fields you write on every message; spend higher numbers on rarely-set fields.

### 2.5 `service Counter { rpc Increment(...) returns (...); }`

A `service` block lists RPC methods. Each method has a name, a request type, and a response type. The four call shapes use the `stream` keyword:

```proto
service Counter {
  rpc Get(GetRequest) returns (GetResponse);                       // unary
  rpc Subscribe(SubscribeRequest) returns (stream CounterEvent);   // server-streaming
  rpc BatchIncrement(stream IncrementRequest) returns (BatchSummary); // client-streaming
  rpc LiveOps(stream OpRequest) returns (stream OpResponse);       // bidirectional
}
```

We will return to call shapes in Lecture 2. For this lecture, focus on the message types; the service block is just a thin wrapper.

## 3. The proto3 type system

### 3.1 Scalar types

The proto3 scalar types and their C# mappings (citation: <https://protobuf.dev/programming-guides/proto3/#scalar>):

| proto3 type | C# type        | Wire type | Notes                                                                  |
|-------------|----------------|-----------|------------------------------------------------------------------------|
| `double`    | `double`       | I64       | IEEE 754, 8 bytes                                                      |
| `float`     | `float`        | I32       | IEEE 754, 4 bytes                                                      |
| `int32`     | `int`          | VARINT    | Variable-length; small values are 1 byte; negatives use 10 bytes       |
| `int64`     | `long`         | VARINT    | Variable-length; same caveat                                           |
| `uint32`    | `uint`         | VARINT    | Variable-length, no negatives                                          |
| `uint64`    | `ulong`        | VARINT    | Variable-length, no negatives                                          |
| `sint32`    | `int`          | VARINT    | Variable-length, ZigZag-encoded — efficient for negatives              |
| `sint64`    | `long`         | VARINT    | ZigZag, as above                                                       |
| `fixed32`   | `uint`         | I32       | Always 4 bytes                                                         |
| `fixed64`   | `ulong`        | I64       | Always 8 bytes                                                         |
| `sfixed32`  | `int`          | I32       | Always 4 bytes, signed                                                 |
| `sfixed64`  | `long`         | I64       | Always 8 bytes, signed                                                 |
| `bool`      | `bool`         | VARINT    | 1 byte                                                                 |
| `string`    | `string`       | LEN       | UTF-8                                                                  |
| `bytes`     | `ByteString`   | LEN       | Arbitrary bytes; the C# wrapper has `ToByteArray()` and `Span<byte>`   |

Two rules of thumb. First, prefer `int32` and `int64` for values that are usually positive and small; the variable-length encoding pays off. Use `sint32`/`sint64` when negatives are common (the ZigZag transform avoids the 10-byte-per-negative penalty). Use `fixed32`/`fixed64` when the values are uniformly distributed across the type's range — counters where you genuinely have 64 bits of entropy, hashes, identifiers.

Second, `string` is UTF-8 only. The wire format does not carry an encoding marker; if you put non-UTF-8 bytes into a `string` field, some implementations will silently corrupt them and others will throw. Use `bytes` for arbitrary binary data and explicitly encode it (base64, hex, your choice) only if a human needs to read it.

### 3.2 The default-value model and the `optional` keyword

Proto3 has an unusual default-value model: **every scalar field has a fixed default value** (zero for numeric types, empty for `string` and `bytes`, `false` for `bool`, the first enum value for enums), **and the wire does not distinguish "field set to default" from "field absent"**. If you write `IncrementRequest { counter_name = "", delta = 0 }`, the wire bytes are empty — both fields are at their defaults, so neither is serialised. The receiver reads an empty message and sees `counter_name = ""` and `delta = 0`. There is no way to distinguish "the sender meant to send an empty string" from "the sender did not set the field." This was a deliberate design choice in proto3, optimising for terseness and forward-compatibility.

It was also widely considered a mistake. The 2016 design left you with no way to say "this field is genuinely optional and I want to know if the sender omitted it." In 2020, proto3 added the `optional` keyword back (citation: the design discussion at <https://github.com/protocolbuffers/protobuf/issues/1606>):

```proto
message Search {
  optional string filter = 1;   // tracks presence; has a HasFilter accessor in C#
  optional int32 limit = 2;     // same
}
```

The C# generator emits a `HasFilter` boolean and a `ClearFilter()` method. Without `optional`, the generator does not. **Use `optional` whenever "is this field set?" is a question your code needs to answer.** For required-feel fields, you can leave `optional` off, but then the sentinel for "not set" is the default value and you have lost information.

### 3.3 Enums

Enums are integer-valued types with named members:

```proto
enum CounterKind {
  COUNTER_KIND_UNSPECIFIED = 0;
  COUNTER_KIND_INCREMENTING = 1;
  COUNTER_KIND_DECREMENTING = 2;
  COUNTER_KIND_BIDIRECTIONAL = 3;
}
```

Three rules. First, **the zero value must exist** and conventionally is named `XXX_UNSPECIFIED`. This is the default value when the field is unset, so it must be a meaningful "no information" sentinel. Second, **enum value names are typed in `SCREAMING_SNAKE_CASE` and prefixed with the enum name**, because enum values in proto are in the *enclosing scope* — `COUNTER_KIND_INCREMENTING` exists in the package, not just inside `CounterKind`. Prefixing avoids collisions. Third, **enums are open**: a wire byte for an unknown enum value is decoded to the integer with no error. This is forward-compatibility doctrine: a v2 server can add `COUNTER_KIND_CIRCULAR = 4`, and a v1 client that doesn't know about it sees a `CounterKind` whose integer is 4 and whose name (via reflection) is unknown. The C# generated code surfaces this as the integer value cast to the enum type.

### 3.4 `repeated` — lists

To say "this field is a list of T", prefix the type with `repeated`:

```proto
message BatchIncrement {
  string counter_name = 1;
  repeated int64 deltas = 2;
}
```

The C# generator emits `RepeatedField<long> Deltas { get; }` (a thin `IList<long>` implementation). For scalar `repeated` types, the wire encoding is **packed** by default in proto3: all the elements are concatenated into a single LEN-wire-type entry, which is dramatically smaller than the proto2 default of one tag per element. For message-typed `repeated` fields, the elements are still emitted one-tag-per-element — there is no packed encoding for messages.

### 3.5 `oneof` — discriminated unions

`oneof` is the proto3 equivalent of `OneOf<T1, T2, ...>` or `Either<L, R>`: a message can hold one of N fields, and setting one clears the others. The wire format includes only the field that is set.

```proto
message Outcome {
  oneof result {
    int64 new_value = 1;
    string error_message = 2;
    google.protobuf.Empty noop = 3;
  }
}
```

The C# generated code emits:

- A property per field (`long NewValue`, `string ErrorMessage`, `Empty Noop`)
- A `ResultCase` enum with values `None`, `NewValue`, `ErrorMessage`, `Noop` and an instance `ResultOneofCase Result` discriminator
- Setters that clear the other fields when one is set

Use `oneof` for genuine union semantics. Do not use it as a "pick one of these enum values" tool — use an `enum` for that. The discriminant-plus-payload shape is what `oneof` is for: success-or-error, request-by-id-or-request-by-name, the natural shape of a tagged union.

### 3.6 Nested messages

Messages can declare other messages inside them. The nested type is scoped to the outer message:

```proto
message Counter {
  string name = 1;
  int64 value = 2;

  message History {
    repeated int64 deltas = 1;
  }

  History recent = 3;
}
```

The C# emit is `Counter.Types.History` — the generator interposes a `.Types` container because C# does not allow types nested inside types to have the same name as their outer type. Live with it.

Nested messages are a *style choice*, not a *semantic* choice. They produce the same wire bytes as top-level messages. Prefer nesting when the type is genuinely only used in one place; promote to top-level when it is shared.

### 3.7 `map<K, V>`

```proto
message LabeledCounters {
  map<string, int64> counter_values = 1;
}
```

The C# emit is `MapField<string, long> CounterValues { get; }`. Map keys must be scalar types other than `float`, `double`, or `bytes`; map values can be any type. The wire encoding is identical to `repeated MapEntry`, where `MapEntry` is a synthesised message with field 1 = key and field 2 = value — meaning maps are forward-compatible with `repeated MapEntry` if you later need ordering.

### 3.8 Well-known types

The protobuf project ships a small collection of canonical message types that every implementation supports. Citation: <https://protobuf.dev/reference/protobuf/google.protobuf/>. The ones you will reach for this week:

- **`google.protobuf.Timestamp`** — a moment in time, expressed as seconds + nanoseconds since the Unix epoch. C# generator emits a `Timestamp` class with `ToDateTimeOffset()`, `ToDateTime()`, `FromDateTime(...)`. Use this for "when did this happen" fields, not raw `int64` Unix timestamps — the well-known type is unambiguous and has conversion helpers.
- **`google.protobuf.Duration`** — a span of time, seconds + nanoseconds. C# emit: `Duration` with `ToTimeSpan()`. Use for timeouts, intervals, ages.
- **`google.protobuf.Empty`** — the unit type. Use as a response type when "the call succeeded, here is nothing to report." More idiomatic than declaring an empty message.
- **`google.protobuf.Any`** — a type-erased message envelope: a `type_url` string plus a `bytes value`. Use when the schema genuinely cannot know the inner type — extension points, plugin protocols, audit logs.
- **`google.protobuf.FieldMask`** — a list of field paths, used for partial updates (the "I want to update only `user.email` and `user.phone`, not `user.address`" pattern). The standard Google API design pattern; less common in non-Google ecosystems.
- **The wrapper types** — `google.protobuf.Int32Value`, `StringValue`, `BoolValue`, etc. These are messages wrapping a single scalar, used when you want presence semantics for a scalar and don't want to use `optional`. Less idiomatic now that `optional` is back; prefer `optional`.

To use a well-known type, you import the relevant `.proto`:

```proto
import "google/protobuf/timestamp.proto";
import "google/protobuf/duration.proto";
import "google/protobuf/empty.proto";

message Event {
  google.protobuf.Timestamp occurred_at = 1;
  google.protobuf.Duration uptime = 2;
}

service Ops {
  rpc Heartbeat(google.protobuf.Empty) returns (google.protobuf.Empty);
}
```

`Grpc.Tools` finds these imports in the protobuf project's well-known-types directory automatically; you do not need to vendor them.

### 3.9 `reserved` — the compatibility primitive

When you remove a field, you do not just delete it from the file. You **reserve its field number and name**:

```proto
message LegacyRequest {
  reserved 4, 7 to 9;
  reserved "old_username", "deprecated_flag";

  string new_username = 1;
  int64 timestamp_ms = 2;
}
```

This produces a compile-time error if anyone, ever, tries to add `int32 something = 4;` or `string old_username = ...;` to the message. It is the schema-evolution safety net. **Always reserve when you remove.** Always.

## 4. The proto3 wire format

You will not, day-to-day, need to decode wire bytes by hand. But you should be able to predict, for any given message, roughly how big the bytes will be — and to do that you need to understand the encoding. The full reference is at <https://protobuf.dev/programming-guides/encoding/>; this section is the working summary.

### 4.1 Varints

The protobuf integer encoding is the **varint**: a variable-length integer where the most significant bit of each byte is a continuation flag, and the lower 7 bits are payload. A varint is 1 byte for values 0–127, 2 bytes for 128–16383, 3 bytes for 16384–2097151, and so on. The maximum is 10 bytes for `int64`/`uint64`.

The varint encoding of 300:

```
300 = 0b00000010 0b00101100   (big-endian, 2 bytes of payload)

split into 7-bit groups, least-significant-first:
  0b0101100, 0b0000010

set the MSB on every byte except the last:
  0b10101100, 0b00000010
  = 0xAC, 0x02
```

You will not encode this by hand. You should be able to look at a hex dump and recognise that the byte pattern is two-byte varint encoding 300.

### 4.2 ZigZag for signed integers

The varint encoding treats integers as unsigned. A negative `int32` cast to `uint64` becomes a huge number (because two's-complement sign extension), which costs 10 bytes. The ZigZag transform — `(n << 1) ^ (n >> 31)` for `int32`, similarly for `int64` — maps small negatives to small unsigned values: 0 → 0, -1 → 1, 1 → 2, -2 → 3, 2 → 4, ... This is what `sint32` and `sint64` use. Pick `sint*` when negatives are common; pick `int*` when they are rare.

### 4.3 Tags and wire types

Every field on the wire starts with a **tag**, a varint that packs the field number and a 3-bit wire-type discriminator:

```
tag = (field_number << 3) | wire_type
```

The four wire types you will see in practice:

| Wire type | Value | Used for                                  |
|-----------|-------|-------------------------------------------|
| VARINT    | 0     | `int32`, `int64`, `uint*`, `sint*`, `bool`, `enum` |
| I64       | 1     | `fixed64`, `sfixed64`, `double`           |
| LEN       | 2     | `string`, `bytes`, embedded messages, packed `repeated` |
| I32       | 5     | `fixed32`, `sfixed32`, `float`            |

(Wire types 3 and 4 are deprecated proto2 group markers; ignore them.)

A LEN field is followed by a varint length and then that many payload bytes. A VARINT field is followed by the value directly. An I32 or I64 field is followed by 4 or 8 bytes of payload, little-endian.

### 4.4 Size estimation

Given a message:

```proto
message IncrementRequest {
  string counter_name = 1;
  int64 delta = 2;
}
```

with `counter_name = "alice"` (5 bytes UTF-8) and `delta = 7`:

- Field 1 (counter_name): tag = `(1 << 3) | 2` = 10 = 0x0A (1 byte), length = 5 (1 byte), payload = `alice` (5 bytes) = **7 bytes**.
- Field 2 (delta): tag = `(2 << 3) | 0` = 16 = 0x10 (1 byte), value = 7 (1 byte) = **2 bytes**.

Total: **9 bytes**. The same data as JSON (`{"counter_name":"alice","delta":7}`) is **34 bytes**. The 3.7x ratio is typical for small messages with short field names. For large messages with long field names, the ratio gets larger.

### 4.5 Forward and backward compatibility

The wire format is *self-describing* at the field-tag level but *not* at the value level. A reader that does not know about field N reads the tag, sees the wire type, and skips that many bytes — it does not throw, it does not corrupt. This is the basis of the proto3 compatibility model:

- **You may add new fields.** Old readers ignore them.
- **You may remove fields.** New writers stop emitting them; old readers see the field absent. But you must `reserved` the number.
- **You may change a field's name** in the `.proto` file. The wire does not care.
- **You may not change a field's number.** Old data with the old number is lost.
- **You may not change a field's wire type.** Changing `int32` → `string` is a wire-incompatible change. Changing `int32` → `int64` is *technically* wire-compatible (both VARINT), but the C# generated code's type changes, so it is a *source*-incompatible change for your clients.
- **You may not reuse a removed number** for a different field. The `reserved` keyword enforces this.

Memorise these rules. The mini-project's schema-evolution exercise will test all of them.

## 5. The `Grpc.Tools` MSBuild integration

The `.proto` file is text. To use it from C# you need generated code. The mechanism in .NET is **`Grpc.Tools`**, a NuGet package that hooks into MSBuild and runs `protoc` plus the C# and gRPC plugins as part of every `dotnet build`. The integration is documented at <https://github.com/grpc/grpc/blob/master/src/csharp/BUILD-INTEGRATION.md>.

### 5.1 Server-side `.csproj`

A typical gRPC server `.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>Crunch.Counter.Server</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Grpc.AspNetCore" Version="2.60.0" />
  </ItemGroup>

  <ItemGroup>
    <Protobuf Include="Protos\counter.proto" GrpcServices="Server" />
  </ItemGroup>

</Project>
```

`Grpc.AspNetCore` is a *meta-package* that depends on `Grpc.Tools`, `Grpc.AspNetCore.Server`, `Grpc.AspNetCore.Server.ClientFactory`, and `Google.Protobuf`. The `<Protobuf>` item is the schema, and `GrpcServices="Server"` tells the generator to emit the abstract server base class (`CounterBase`) without the client class.

The generator runs every build, reads `Protos\counter.proto`, and writes:

```
obj/Debug/net8.0/Protos/Counter.cs        // message types
obj/Debug/net8.0/Protos/CounterGrpc.cs    // service base class + (omitted here) client
```

Both files are added to the compilation. You never edit them.

### 5.2 Client-side `.csproj`

A client (console app, test harness, anything that *calls* a gRPC service):

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <RootNamespace>Crunch.Counter.Client</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Grpc.Net.Client" Version="2.60.0" />
    <PackageReference Include="Grpc.Tools" Version="2.60.0" PrivateAssets="All" />
    <PackageReference Include="Google.Protobuf" Version="3.25.0" />
  </ItemGroup>

  <ItemGroup>
    <Protobuf Include="..\Server\Protos\counter.proto" GrpcServices="Client" Link="Protos\counter.proto" />
  </ItemGroup>

</Project>
```

Three differences from the server. First, the package is `Grpc.Net.Client` (the channel + client runtime), and `Grpc.Tools` must be added explicitly. Second, `GrpcServices="Client"` — emit the client class, omit the server base. Third, the `<Protobuf>` item *links* the same physical `.proto` file from the server project; both sides generate against the identical schema.

For a project that contains *both* a server and a client (a service that calls another service), use `GrpcServices="Both"`.

### 5.3 The generated C# shape

Given the `Counter` service from Section 2, the generator emits roughly:

```csharp
namespace Crunch.Counter.V1;

// Messages
public sealed partial class IncrementRequest : IMessage<IncrementRequest>
{
    public string CounterName { get; set; }
    public long Delta { get; set; }
    // serialisation glue, parsing glue, Equals, GetHashCode, Clone, ...
}

public sealed partial class IncrementResponse : IMessage<IncrementResponse>
{
    public long NewValue { get; set; }
}

// Service
public static partial class Counter
{
    public abstract partial class CounterBase
    {
        public virtual Task<IncrementResponse> Increment(
            IncrementRequest request, ServerCallContext context)
            => throw new RpcException(new Status(StatusCode.Unimplemented, ""));
    }

    public partial class CounterClient : ClientBase<CounterClient>
    {
        public CounterClient(ChannelBase channel) : base(channel) { }

        public virtual AsyncUnaryCall<IncrementResponse> IncrementAsync(
            IncrementRequest request, CallOptions options) { ... }

        public virtual IncrementResponse Increment(
            IncrementRequest request, CallOptions options) { ... }

        // Convenience overloads with deadline/cancellation/metadata
    }
}
```

The generated classes are `partial`. If you want to add helper methods or computed properties, put them in a partial file alongside (do not edit `obj/`); your additions compose with the generated code.

### 5.4 `ProtoRoot` and import paths

When your `.proto` files import each other or import well-known types, the generator needs to know the *import root*. Default is the directory of the `.proto` file. If you have a tree like:

```
src/
  Server/
    Protos/
      common/
        types.proto
      counter/
        counter.proto   (imports common/types.proto)
```

then in the `.csproj`:

```xml
<ItemGroup>
  <Protobuf Include="Protos\counter\counter.proto" GrpcServices="Server" ProtoRoot="Protos" />
  <Protobuf Include="Protos\common\types.proto" GrpcServices="None" ProtoRoot="Protos" />
</ItemGroup>
```

`ProtoRoot="Protos"` makes the import statement `import "common/types.proto";` resolve correctly. `GrpcServices="None"` on the common file says "generate the message types, no service code."

## 6. Schema evolution in practice

The compatibility rules from Section 4.5 are wire-level. The *operational* rules — what an on-call engineer cares about — are the practical translation:

1. **Always bump the package version on breaking changes.** Going from `crunch.counter.v1` to `crunch.counter.v2` lets v1 clients keep calling v1 servers and v2 clients call v2 servers while you run both side-by-side. Renaming the package retroactively is the source of the worst incidents.

2. **Always `reserved` removed field numbers and names.** A `reserved 5;` plus `reserved "old_field";` line is a permanent gravestone. The compiler refuses to let you accidentally resurrect the number.

3. **Never change a field's type.** Even compatible-ish changes (`int32` → `int64`) break clients that have already generated code with the old type. If you need a wider field, add a new field with a new number and `reserved` the old one.

4. **New fields must be safe to default.** If your v2 server requires `mandatory_new_field = 5`, every v1 client (which does not send it) will hit the default-value sentinel. Server code must treat unset = legacy = "use the old behaviour."

5. **Deprecate before removing.** Mark a field `[deprecated = true]` for a release before reserving it. Clients see a compile-time deprecation warning and have time to migrate.

```proto
message Old {
  string deprecated_field = 4 [deprecated = true];
}
```

6. **Run the cross-version test.** For every breaking change, write an integration test that runs v(N-1) client against v(N) server, and v(N) client against v(N-1) server. If both work, your change is safe.

## 7. Wrap-up — the protobuf checklist

When you write a `.proto` file from scratch this week:

- [ ] `syntax = "proto3";` is the first non-comment line.
- [ ] `package` is `lower_snake_case.dotted.v1` with a version suffix.
- [ ] `option csharp_namespace = "..."` matches the C# project's expected namespace.
- [ ] Every message field has an explicit, unique field number.
- [ ] Field numbers 1–15 are used for hot fields; 16+ for cold ones.
- [ ] Enum `0` is `XXX_UNSPECIFIED`.
- [ ] `optional` is used on fields where presence matters.
- [ ] `oneof` is used for discriminated unions.
- [ ] Well-known types (`Timestamp`, `Duration`, `Empty`) are used where they fit.
- [ ] Every `import` is either a well-known type or a project-local file with a `ProtoRoot`-resolvable path.
- [ ] Removed fields are `reserved`. Always.

Read the protobuf 3 language guide end-to-end before Tuesday — <https://protobuf.dev/programming-guides/proto3/>. The exercise for this lecture (`exercise-01-design-a-proto`) will check most of this list against your output.

Next lecture: gRPC itself — the four call types, the C# server and client, the cross-language story.
