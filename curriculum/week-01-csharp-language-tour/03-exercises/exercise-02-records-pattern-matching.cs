// Exercise 2 — Records and pattern matching
//
// Goal: Practice modeling a small domain with records, and use switch
//       expressions and property patterns instead of if/else ladders.
//
// Estimated time: 40 minutes.
//
// HOW TO USE THIS FILE
//
// 1. Scaffold a fresh console project:
//
//      mkdir Payments && cd Payments
//      dotnet new console -n Payments.Cli -o src/Payments.Cli
//      cd src/Payments.Cli
//
//    Replace the generated Program.cs with the contents of THIS FILE.
//
// 2. Fill in the bodies marked `// TODO`. Do not change the public surface
//    (record shapes and method signatures). The Main below exercises the
//    code you fill in; if you wired it correctly, `dotnet run` prints the
//    expected output shown at the very bottom of this file.
//
// 3. Build with zero warnings: `dotnet build`. If you have warnings,
//    the exercise is not done.
//
// ACCEPTANCE CRITERIA
//
//   [ ] All TODOs implemented.
//   [ ] `dotnet build`: 0 Warning(s), 0 Error(s).
//   [ ] `dotnet run` prints the expected output at the bottom of this file.
//   [ ] You used a switch expression in `Classify` — no if/else.
//   [ ] You used a property pattern (or list pattern) at least once.
//   [ ] You used `with` at least once to produce a refunded copy of a payment.
//
// Inline hints are at the bottom of the file. Don't peek until you've tried
// for at least 15 minutes.

using System.Globalization;

// ----------------------------------------------------------------------------
// Domain types
// ----------------------------------------------------------------------------

public enum PaymentMethod { Card, Cash, BankTransfer, Unknown }

public record Payment(
    DateOnly Date,
    decimal Amount,
    string Memo,
    PaymentMethod Method);

// ----------------------------------------------------------------------------
// Functions to implement
// ----------------------------------------------------------------------------

public static class Payments
{
    /// <summary>
    /// Classify a payment by amount.
    ///   - Amount > 0  → "credit"
    ///   - Amount < 0  → "debit"
    ///   - Amount == 0 → "zero"
    ///
    /// You MUST use a switch expression, not if/else.
    /// </summary>
    public static string Classify(Payment p) =>
        // TODO: replace this body with a switch expression on p.Amount
        throw new NotImplementedException();

    /// <summary>
    /// Describe a payment in one line. Combine method + amount + memo using
    /// a property-pattern switch expression. Examples:
    ///
    ///   { Method: Card, Amount: > 0, Memo: "Coffee" }
    ///     → "card credit: Coffee (+10.00)"
    ///
    ///   { Method: Cash, Amount: < 0 }
    ///     → "cash refund (-5.00): Lunch"
    ///
    ///   { Method: Unknown } → "unknown payment: Misc"
    /// </summary>
    public static string Describe(Payment p) =>
        // TODO: a switch expression with property patterns
        throw new NotImplementedException();

    /// <summary>
    /// Produce a refunded copy of a payment: same fields, amount negated,
    /// and the memo prefixed with "Refund: ".
    /// You MUST use `with` to create the new record.
    /// </summary>
    public static Payment Refund(Payment p) =>
        // TODO: return p with { ... }
        throw new NotImplementedException();

    /// <summary>
    /// Given a sequence of payments, return the net total (credits minus debits).
    /// You may use LINQ.
    /// </summary>
    public static decimal Net(IEnumerable<Payment> payments) =>
        // TODO: one line of LINQ
        throw new NotImplementedException();
}

// ----------------------------------------------------------------------------
// Driver
// ----------------------------------------------------------------------------

internal static class Program
{
    private static void Main()
    {
        Payment[] paid =
        [
            new(DateOnly.Parse("2026-05-13", CultureInfo.InvariantCulture),  10.00m, "Coffee", PaymentMethod.Card),
            new(DateOnly.Parse("2026-05-13", CultureInfo.InvariantCulture),  22.50m, "Lunch",  PaymentMethod.Cash),
            new(DateOnly.Parse("2026-05-14", CultureInfo.InvariantCulture),  -5.00m, "Refund", PaymentMethod.Card),
            new(DateOnly.Parse("2026-05-14", CultureInfo.InvariantCulture),   0.00m, "Misc",   PaymentMethod.Unknown),
        ];

        foreach (var p in paid)
        {
            Console.WriteLine($"{p.Date}  {Payments.Classify(p),-6}  {Payments.Describe(p)}");
        }

        Console.WriteLine();

        var refundedLunch = Payments.Refund(paid[1]);
        Console.WriteLine($"Refunded lunch:   {refundedLunch}");
        Console.WriteLine($"Original lunch:   {paid[1]}    (must be unchanged)");
        Console.WriteLine($"Net of all paid:  {Payments.Net(paid):N2}");
    }
}

// ----------------------------------------------------------------------------
// Expected output (yours should match, modulo culture-specific number format)
// ----------------------------------------------------------------------------
//
// 2026-05-13  credit  card credit: Coffee (+10.00)
// 2026-05-13  credit  cash credit: Lunch (+22.50)
// 2026-05-14  debit   card refund (-5.00): Refund
// 2026-05-14  zero    unknown payment: Misc
//
// Refunded lunch:   Payment { Date = 2026-05-13, Amount = -22.50, Memo = Refund: Lunch, Method = Cash }
// Original lunch:   Payment { Date = 2026-05-13, Amount = 22.50, Memo = Lunch, Method = Cash }    (must be unchanged)
// Net of all paid:  27.50
//
// ----------------------------------------------------------------------------
// HINTS (read only if stuck >15 min)
// ----------------------------------------------------------------------------
//
// Classify:
//   public static string Classify(Payment p) => p.Amount switch
//   {
//       > 0m => "credit",
//       < 0m => "debit",
//       _    => "zero"
//   };
//
// Describe (sketch — fill in the remaining arms yourself):
//   public static string Describe(Payment p) => p switch
//   {
//       { Method: PaymentMethod.Unknown } => $"unknown payment: {p.Memo}",
//       { Amount: > 0m } => $"{p.Method.ToString().ToLowerInvariant()} credit: {p.Memo} ({p.Amount:+0.00;-0.00;0.00})",
//       { Amount: < 0m } => $"{p.Method.ToString().ToLowerInvariant()} refund ({p.Amount:+0.00;-0.00;0.00}): {p.Memo}",
//       _                => $"{p.Method.ToString().ToLowerInvariant()} zero: {p.Memo}",
//   };
//
// Refund:
//   public static Payment Refund(Payment p) =>
//       p with { Amount = -p.Amount, Memo = $"Refund: {p.Memo}" };
//
// Net:
//   public static decimal Net(IEnumerable<Payment> payments) =>
//       payments.Sum(p => p.Amount);
//
// ----------------------------------------------------------------------------
