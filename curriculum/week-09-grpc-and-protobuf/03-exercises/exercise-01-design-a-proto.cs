// Exercise 1 — Round-trip a Shipment through protobuf and predict its size.
//
// This file is the C# driver for the .proto file in
// exercise-01-design-a-proto.proto. Once you have completed the TODOs in the
// .proto and dotnet build succeeds, replace Class1.cs (or Program.cs) in your
// Ex01 project with the contents of this file and run.
//
// You will need to switch the .csproj from a classlib (Sdk="Microsoft.NET.Sdk")
// to an exe (add <OutputType>Exe</OutputType>), or scaffold a second console
// project that depends on Ex01.
//
// ACCEPTANCE CRITERIA
//
//   [ ] The program constructs a Shipment with id = "S0001", IN_TRANSIT status,
//       one ResidentialAddress destination, and one PICKED_UP event.
//   [ ] It serialises to bytes via .ToByteArray() and back via the parser.
//   [ ] It prints the predicted size from .CalculateSize() and the actual
//       length of the resulting byte[]. They must agree exactly.
//   [ ] It demonstrates oneof discrimination by setting two different
//       destination subtypes in sequence and observing DestinationCase.
//   [ ] It demonstrates "optional" presence by checking HasCarrier before
//       and after Carrier is assigned.
//   [ ] dotnet run -c Release succeeds with 0 warnings, 0 errors.

#nullable enable

using System;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Crunch.Shipment.V1;

namespace Ex01;

public static class Program
{
    public static void Main()
    {
        Console.WriteLine("Exercise 1 — proto3 round-trip and size prediction");
        Console.WriteLine(new string('-', 60));

        // Build a Shipment.
        var shipment = new Shipment
        {
            Id = "S0001",
            Status = ShipmentStatus.ShipmentStatusInTransit,
            SlaWindow = Duration.FromTimeSpan(TimeSpan.FromHours(48)),
        };

        // Set the oneof destination to a residential address.
        shipment.Residential = new ResidentialAddress
        {
            Street = "123 Code Crunch Way",
            City = "Miami",
            Region = "FL",
            PostalCode = "33174",
            CountryCode = "US",
            // BuzzerCode is optional; we leave it unset to demonstrate presence.
        };

        // Add an event.
        shipment.Events.Add(new ShipmentEvent
        {
            Kind = ShipmentEvent.Types.Kind.PickedUp,
            OccurredAt = Timestamp.FromDateTime(DateTime.UtcNow),
            FacilityCode = "MIA-01",
            Notes = string.Empty,  // default; will not be emitted on the wire.
        });

        // Demonstrate the oneof discriminant before serialising.
        Console.WriteLine($"DestinationCase = {shipment.DestinationCase}");

        // Demonstrate "optional" presence.
        Console.WriteLine($"HasCarrier (before set) = {shipment.HasCarrier}");
        shipment.Carrier = "ups";
        Console.WriteLine($"HasCarrier (after set)  = {shipment.HasCarrier}");
        Console.WriteLine($"Carrier                 = {shipment.Carrier}");

        // Round-trip.
        var predictedSize = shipment.CalculateSize();
        var wire = shipment.ToByteArray();
        var actualSize = wire.Length;

        Console.WriteLine();
        Console.WriteLine($"Predicted size = {predictedSize} bytes");
        Console.WriteLine($"Actual size    = {actualSize} bytes");
        if (predictedSize != actualSize)
        {
            Console.Error.WriteLine("MISMATCH — Predicted and actual sizes disagree.");
            Environment.Exit(1);
        }
        Console.WriteLine("Sizes match.");

        // Parse the bytes back to a fresh Shipment.
        var parsed = Shipment.Parser.ParseFrom(wire);

        Console.WriteLine();
        Console.WriteLine("Parsed Shipment:");
        Console.WriteLine($"  Id              = {parsed.Id}");
        Console.WriteLine($"  Status          = {parsed.Status}");
        Console.WriteLine($"  DestinationCase = {parsed.DestinationCase}");
        if (parsed.DestinationCase == Shipment.DestinationOneofCase.Residential)
        {
            Console.WriteLine($"  Residential.Street = {parsed.Residential.Street}");
            Console.WriteLine($"  Residential.HasBuzzerCode = {parsed.Residential.HasBuzzerCode}");
        }
        Console.WriteLine($"  Events.Count    = {parsed.Events.Count}");
        Console.WriteLine($"  SlaWindow       = {parsed.SlaWindow.ToTimeSpan()}");
        Console.WriteLine($"  Carrier         = {parsed.Carrier} (HasCarrier={parsed.HasCarrier})");

        // Demonstrate oneof reassignment clearing the previous field.
        Console.WriteLine();
        Console.WriteLine("Switching destination from residential to locker...");
        parsed.Locker = new LockerAddress { LockerId = "L0099", CarrierCode = "amzn", Region = "FL" };
        Console.WriteLine($"DestinationCase after reassignment = {parsed.DestinationCase}");
        Console.WriteLine($"Residential is now: {(parsed.Residential is null ? "null" : "not null")}");
        // Note: the C# generated property still returns a non-null Residential
        // unless explicitly cleared — read DestinationCase, not the property.

        Console.WriteLine();
        Console.WriteLine("Done. See REFLECTION QUESTIONS in the .proto file.");
    }
}

// HINTS (read after a serious attempt):
//
// 1. The generated enum value name is ShipmentStatus.ShipmentStatusInTransit,
//    not ShipmentStatus.InTransit. The generator strips the enum-name prefix
//    in newer versions of Grpc.Tools (3.x), but keeps it in 2.60. Match what
//    your generator emits.
//
// 2. For the "optional string carrier" field, the generated accessors are
//    HasCarrier (bool) and ClearCarrier() (void). Setting Carrier = "" is
//    distinct from leaving it unset; HasCarrier reflects the difference.
//
// 3. The Duration.FromTimeSpan helper handles nanosecond precision; do not
//    construct a Duration by hand unless you specifically need to.
//
// 4. CalculateSize() walks the message tree once without serialising; it is
//    cheaper than ToByteArray().Length. Use it when you only need the size.
//
// 5. DestinationOneofCase is the discriminant enum the generator emits for
//    the oneof. Its values are DestinationOneofCase.None plus one per field.
