"""Crunch Counter Python client (grpcio) for the Week 9 mini-project.

Place at clients/python/client.py. Requires a venv with:

    pip install grpcio==1.62.0 grpcio-tools==1.62.0

Before running, regenerate the stubs from counter.proto:

    python3 -m grpc_tools.protoc \
        --proto_path=. \
        --python_out=. \
        --grpc_python_out=. \
        counter.proto

CLI:
    python3 client.py inc <name> <delta>
    python3 client.py watch <name>
"""

from __future__ import annotations

import sys
import uuid
from typing import Iterator

import grpc

import counter_pb2 as pb
import counter_pb2_grpc as pb_grpc


SERVER_ADDRESS = "localhost:5001"


def make_channel() -> grpc.Channel:
    """Build a TLS channel against the C# server.

    The C# server uses the dotnet dev-cert, which is installed in the
    system trust store on macOS by `dotnet dev-certs https --trust`.
    grpc.ssl_channel_credentials() with no arguments uses the system store.
    """
    return grpc.secure_channel(SERVER_ADDRESS, grpc.ssl_channel_credentials())


def correlation_id() -> tuple[tuple[str, str], ...]:
    """Generate the metadata tuple to attach to outbound calls."""
    return (("x-correlation-id", uuid.uuid4().hex[:12]),)


def do_increment(name: str, delta: int) -> int:
    with make_channel() as channel:
        stub = pb_grpc.CounterStub(channel)
        try:
            reply = stub.Increment(
                pb.IncrementRequest(counter_name=name, delta=delta),
                timeout=2.0,
                metadata=correlation_id(),
            )
            print(f"{reply.counter_name} = {reply.new_value}")
            return 0
        except grpc.RpcError as e:
            print(f"increment failed: {e.code().name} {e.details()}", file=sys.stderr)
            return 1 if e.code() == grpc.StatusCode.INVALID_ARGUMENT else 2


def do_watch(name: str) -> int:
    with make_channel() as channel:
        stub = pb_grpc.CounterStub(channel)
        try:
            for ev in stub.Subscribe(
                pb.SubscribeRequest(counter_name=name),
                timeout=3600.0,
                metadata=correlation_id(),
            ):
                kind = pb.CounterEvent.Kind.Name(ev.kind)
                print(f"{ev.at.ToDatetime():%H:%M:%S.%f}  {kind:<15}  {ev.counter_name} = {ev.value}")
            return 0
        except KeyboardInterrupt:
            return 0
        except grpc.RpcError as e:
            if e.code() in (grpc.StatusCode.CANCELLED,):
                return 0
            print(f"watch failed: {e.code().name} {e.details()}", file=sys.stderr)
            return 2


def usage() -> int:
    print("Usage:", file=sys.stderr)
    print("  python3 client.py inc <name> <delta>", file=sys.stderr)
    print("  python3 client.py watch <name>", file=sys.stderr)
    return 64


def main(argv: list[str]) -> int:
    if len(argv) < 2:
        return usage()
    cmd = argv[1]
    if cmd == "inc":
        if len(argv) != 4:
            return usage()
        try:
            delta = int(argv[3])
        except ValueError:
            return usage()
        return do_increment(argv[2], delta)
    if cmd == "watch":
        if len(argv) != 3:
            return usage()
        return do_watch(argv[2])
    return usage()


if __name__ == "__main__":
    sys.exit(main(sys.argv))
