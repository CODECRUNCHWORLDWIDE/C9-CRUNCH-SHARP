# Challenge 1 — Cross-Language Client: Python Calls the C# NumberService

> **Estimated time:** 2 hours. **Prerequisite:** Exercise 2 complete and the C# server runnable. **Citations:** the gRPC Python quickstart at <https://grpc.io/docs/languages/python/quickstart/>, the gRPC Python basics tutorial at <https://grpc.io/docs/languages/python/basics/>, and the `grpcio-tools` documentation at <https://pypi.org/project/grpcio-tools/>.

## The premise

You have a working C# gRPC server from Exercise 2: `NumberService` with one unary, one server-streaming, one client-streaming, and one bidirectional RPC. The promise of gRPC is that *any* language with a gRPC binding can call the same service over the same wire. This challenge tests that promise: you will write a Python client that exercises all four call types against the C# server you built on Tuesday.

The Python and C# implementations will generate code from the *identical* `.proto` file. If the wire format guarantee holds, the Python client will Just Work against the C# server with no server-side changes.

## What you will build

- A small Python project at `clients/python/` with a `requirements.txt`, the `.proto` file vendored or symlinked in, and a `client.py` that exercises all four call types.
- A virtual environment with `grpcio` and `grpcio-tools` installed.
- Generated Python stubs (`numbers_pb2.py`, `numbers_pb2_grpc.py`).
- A short `RESULTS.md` documenting that all four call types work, the wire bytes match the C# client's, and any cross-language gotchas you encountered.

## Setup

### 1. Install Python and create a virtual environment

```bash
python3 --version    # must be 3.10 or later

mkdir -p clients/python
cd clients/python

python3 -m venv .venv
source .venv/bin/activate    # macOS/Linux
# .venv\Scripts\activate     # Windows PowerShell

pip install --upgrade pip
pip install grpcio==1.62.0 grpcio-tools==1.62.0
```

Pin the versions. `grpcio` 1.62.0 is the contemporaneous Python release for the .NET 8 era; mixing major versions across `grpcio` and `grpcio-tools` will produce subtle parse errors.

### 2. Copy or symlink the `.proto`

```bash
cp ../../src/Ex02.Server/Protos/numbers.proto ./numbers.proto
# OR, on macOS/Linux:
ln -s ../../src/Ex02.Server/Protos/numbers.proto numbers.proto
```

The Python generator will work from this file directly. The `.proto` itself is unchanged — proto3 is language-agnostic.

### 3. Generate Python stubs

```bash
python3 -m grpc_tools.protoc \
    --proto_path=. \
    --python_out=. \
    --grpc_python_out=. \
    numbers.proto
```

This emits two files:

- `numbers_pb2.py` — the message types (`SquareRequest`, `SquareResponse`, `NumberMessage`, etc.).
- `numbers_pb2_grpc.py` — the `NumberServiceStub` (client) and `NumberServiceServicer` (server base) classes.

Skim both. The generated Python is human-readable: the messages are decorated `google.protobuf.Message` subclasses, and the stub class has methods named exactly as the RPCs (`Square`, `CountUp`, `Sum`, `Echo`).

### 4. Write `client.py`

The minimum-viable skeleton:

```python
"""Cross-language gRPC client — Python calling the C# NumberService."""

from __future__ import annotations

import time
from typing import Iterable

import grpc

import numbers_pb2 as pb
import numbers_pb2_grpc as pb_grpc


SERVER_ADDRESS = "localhost:5001"


def make_channel() -> grpc.Channel:
    """Build a secure channel against the C# server's HTTPS endpoint."""
    return grpc.secure_channel(SERVER_ADDRESS, grpc.ssl_channel_credentials())


def demo_unary(stub: pb_grpc.NumberServiceStub) -> None:
    print("[1] Unary: Square(7)")
    reply = stub.Square(pb.SquareRequest(input=7), timeout=2.0)
    print(f"    squared = {reply.squared}")


def demo_server_streaming(stub: pb_grpc.NumberServiceStub) -> None:
    print("[2] Server-streaming: CountUp(1..5)")
    request = pb.CountUpRequest(from_=1, to=5, step=1)
    for msg in stub.CountUp(request, timeout=5.0):
        print(f"    received {msg.value}")


def demo_client_streaming(stub: pb_grpc.NumberServiceStub) -> None:
    print("[3] Client-streaming: Sum(10, 20, 30, 40)")
    def gen() -> Iterable[pb.NumberMessage]:
        for n in (10, 20, 30, 40):
            yield pb.NumberMessage(value=n)
    summary = stub.Sum(gen(), timeout=5.0)
    print(f"    sum = {summary.sum}, count = {summary.count}")


def demo_bidirectional(stub: pb_grpc.NumberServiceStub) -> None:
    print("[4] Bidirectional: Echo('alpha', 'beta', 'gamma')")
    def gen() -> Iterable[pb.EchoRequest]:
        for text in ("alpha", "beta", "gamma"):
            yield pb.EchoRequest(text=text)
            time.sleep(0.05)
    for reply in stub.Echo(gen(), timeout=5.0):
        print(f"    echo: text={reply.text} sequence={reply.sequence}")


def main() -> None:
    print(f"Challenge 1 — Python client against {SERVER_ADDRESS}")
    print("-" * 60)
    with make_channel() as channel:
        stub = pb_grpc.NumberServiceStub(channel)
        demo_unary(stub)
        demo_server_streaming(stub)
        demo_client_streaming(stub)
        demo_bidirectional(stub)
    print("-" * 60)
    print("Done.")


if __name__ == "__main__":
    main()
```

### 5. Run

In one terminal, start the C# server (`dotnet run --project src/Ex02.Server`).
In a second, with the venv active: `python3 client.py`.

The output should mirror the C# client's output from Exercise 2.

## Acceptance criteria

- [ ] All four call types succeed.
- [ ] The `Square` unary call returns 49 for input 7.
- [ ] The `CountUp` server-streaming call yields 1, 2, 3, 4, 5 with ~50ms between each.
- [ ] The `Sum` client-streaming call returns `sum=100, count=4`.
- [ ] The `Echo` bidirectional call returns three messages with sequence numbers 1, 2, 3.
- [ ] The C# server's interceptor logs (if you wired one in) show the Python client's calls with the same method paths as the C# client.
- [ ] You produce a `RESULTS.md` answering the questions below.

## Reflection (write into RESULTS.md)

1. **The `from_` field on `CountUpRequest`.** Python protobuf renames `from` (a Python keyword) to `from_` in the generated Python. The wire format is unchanged. What other proto field names would conflict with Python keywords? With C# keywords? How does each generator handle the collision?

2. **The wire-byte comparison.** Run Wireshark (or `tcpdump`) on `localhost:5001`. Capture one `Square(7)` call from the C# client and one from the Python client. Compare the request body bytes. Are they identical? If they differ, where, and is the difference observable in either client's behaviour?

3. **TLS in the cross-language case.** The Python client uses `grpc.ssl_channel_credentials()` with no arguments — it trusts the system trust store. On macOS, `dotnet dev-certs https --trust` installed the dev certificate into the system keychain, which is what the Python client reads. On Linux and on some Windows configurations, this is not automatic; what would you change in `make_channel` to trust a specific certificate file?

4. **The deadline semantics.** Python's `timeout=2.0` parameter is a floating-point seconds value. C#'s `deadline:` parameter is an absolute `DateTime`. They produce the same wire bytes (the `grpc-timeout` header in seconds-with-suffix encoding). What happens if you pass `timeout=0` (zero seconds)? What about `timeout=None` (no deadline)? Read the Python docs at <https://grpc.io/docs/languages/python/basics/> to confirm.

5. **Error handling.** Provoke an `InvalidArgument` error from the Python client by calling `Square(input=4_000_000_000)` (which the server rejects as overflow-prone). Catch the resulting `grpc.RpcError`. Compare the API surface with C#'s `RpcException`. Which fields map to which?

## Stretch goals (optional)

- **Write a server in Python too.** Use the same `.proto` and have a C# client call the Python server. The cross-language guarantee runs both ways.
- **Add a Go client.** Repeat the exercise with `protoc-gen-go` and the Go gRPC bindings. Verify that the C# server serves all three clients identically.
- **Generate JavaScript/TypeScript stubs.** Use `protoc-gen-grpc-web` and run the C# server behind a `grpc-web` proxy. Call from a browser. This is the production path for "exposing a gRPC service to a SPA frontend."

## Submission

Place under `clients/python/`:

- `requirements.txt` with the pinned versions.
- `client.py` with all four call-type demos.
- `RESULTS.md` answering the five reflection questions.

Commit with the message `challenge-01: cross-language client (python) against numberservice`.
