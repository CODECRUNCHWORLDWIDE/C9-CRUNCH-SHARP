// Exercise 3 — Nullable references
//
// Goal: This file is intentionally full of nullability warnings. Fix every
//       warning WITHOUT reaching for the `!` (null-forgiving) operator.
//       The point is to learn how the compiler narrows nullable types
//       through `is not null`, `is { }`, `??`, early returns, and required
//       members — not how to silence it.
//
// Estimated time: 30 minutes.
//
// HOW TO USE THIS FILE
//
//   mkdir Profiles && cd Profiles
//   dotnet new console -n Profiles.Cli -o src/Profiles.Cli
//   cd src/Profiles.Cli
//
// Replace the generated Program.cs with the contents of THIS FILE.
// Verify <Nullable>enable</Nullable> is set in your .csproj — the default
// `dotnet new console` template includes it. If it's missing, add it.
//
// Then run:
//
//   dotnet build
//
// You will see several CS86xx warnings. Fix them all by editing the code
// below. The final program must:
//
//   - Build with 0 Warning(s), 0 Error(s).
//   - Run end-to-end and print the four expected lines at the bottom.
//   - Use the `!` operator ZERO times. (`!=` and `!`-as-logical-not are fine;
//     we mean the postfix null-forgiving `!`.)
//
// ACCEPTANCE CRITERIA
//
//   [ ] dotnet build: 0 Warning(s), 0 Error(s).
//   [ ] dotnet run prints the four expected lines (see bottom).
//   [ ] No postfix `!` operator anywhere in the file.
//   [ ] `Display` uses `??` to default missing nicknames.
//   [ ] `LengthOfName` handles `null` profiles via pattern or early return.
//   [ ] `Person.Name` is `required` so a blank `Person { }` is a compile error.

// ----------------------------------------------------------------------------
// Domain
// ----------------------------------------------------------------------------

public class Person
{
    // TODO: Make `Name` required so a Person cannot be constructed without one.
    //       Hint: the `required` keyword (C# 11 / .NET 7+).
    public string Name { get; init; }

    // Nickname is genuinely optional. Make the type accurately reflect that.
    public string Nickname { get; init; }
}

public class Profile
{
    public Person Owner { get; init; }
    public string Bio { get; init; }
}

// ----------------------------------------------------------------------------
// Pure functions over Person / Profile
// ----------------------------------------------------------------------------

public static class Profiles
{
    /// <summary>
    /// Format a person as "Nickname (Name)" when a nickname exists,
    /// otherwise just "Name".
    ///
    /// Currently this method warns. Fix it WITHOUT `!`. Use `??` or
    /// an `is { }` pattern.
    /// </summary>
    public static string Display(Person p)
    {
        // TODO: this expression dereferences a possibly-null Nickname.
        return $"{p.Nickname.ToLowerInvariant()} ({p.Name})";
    }

    /// <summary>
    /// Return the length of the person's name in characters, or 0 if the
    /// given profile (or its owner) is null. The signature accepts a
    /// nullable Profile by design — callers may pass null.
    /// </summary>
    public static int LengthOfName(Profile profile)
    {
        // TODO: the parameter type does not yet say "this may be null",
        //       but the body assumes it can be. Decide the right signature
        //       and fix the body.
        return profile.Owner.Name.Length;
    }

    /// <summary>
    /// Find the first profile whose bio contains the given fragment.
    /// Returns null when nothing matches. Callers must handle null.
    /// </summary>
    public static Profile FirstWithBioContaining(IEnumerable<Profile> profiles, string fragment)
    {
        // TODO: this method already returns null on no-match (FirstOrDefault),
        //       so the return type should advertise that.
        return profiles.FirstOrDefault(p => p.Bio.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }
}

// ----------------------------------------------------------------------------
// Driver
// ----------------------------------------------------------------------------

internal static class Program
{
    private static void Main()
    {
        var ada = new Person { Name = "Ada Lovelace", Nickname = "ada" };
        var grace = new Person { Name = "Grace Hopper" }; // no nickname
        var profiles = new List<Profile>
        {
            new() { Owner = ada,   Bio = "Wrote the first algorithm intended for a machine." },
            new() { Owner = grace, Bio = "Compiler pioneer; coined the word 'bug' for software." },
        };

        Console.WriteLine(Profiles.Display(ada));     // expect: "ada (Ada Lovelace)"
        Console.WriteLine(Profiles.Display(grace));   // expect: "Grace Hopper"

        Console.WriteLine(Profiles.LengthOfName(profiles[0])); // expect: 12
        Console.WriteLine(Profiles.LengthOfName(null));        // expect: 0
    }
}

// ----------------------------------------------------------------------------
// Expected output
// ----------------------------------------------------------------------------
//
// ada (Ada Lovelace)
// Grace Hopper
// 12
// 0
//
// ----------------------------------------------------------------------------
// HINTS (read only if stuck >15 min)
// ----------------------------------------------------------------------------
//
// Person:
//   public required string Name { get; init; }
//   public string? Nickname { get; init; }
//
// Profile:
//   public required Person Owner { get; init; }
//   public required string Bio { get; init; }
//
// Display:
//   public static string Display(Person p) =>
//       p.Nickname is { Length: > 0 } nick
//           ? $"{nick.ToLowerInvariant()} ({p.Name})"
//           : p.Name;
//
//   Or equivalently with ??:
//
//   public static string Display(Person p)
//   {
//       string? nick = p.Nickname;
//       return nick is null
//           ? p.Name
//           : $"{nick.ToLowerInvariant()} ({p.Name})";
//   }
//
// LengthOfName:
//   public static int LengthOfName(Profile? profile) =>
//       profile?.Owner.Name.Length ?? 0;
//
// FirstWithBioContaining:
//   public static Profile? FirstWithBioContaining(IEnumerable<Profile> profiles, string fragment) =>
//       profiles.FirstOrDefault(p => p.Bio.Contains(fragment, StringComparison.OrdinalIgnoreCase));
//
// ----------------------------------------------------------------------------
// WHY THIS MATTERS
// ----------------------------------------------------------------------------
//
// `!` is a sharp tool: it tells the compiler "trust me, this isn't null." When
// you're wrong, you get a NullReferenceException at run time — the exact
// failure mode nullable refs were introduced to prevent. Reach for `!` only
// when YOU have an invariant the compiler cannot see (e.g. a constructor
// guarantees a field is set even though the analysis can't prove it).
//
// Almost every `!` you'll see in production C# code is wrong. Train the
// instinct to write `??`, `is { }`, or `?.` first.
//
// ----------------------------------------------------------------------------
