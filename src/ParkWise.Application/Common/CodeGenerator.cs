using System.Security.Cryptography;

namespace ParkWise.Application.Common;

/// <summary>Short human-friendly codes for tickets and receipts. Uniqueness is guaranteed by a unique index, not by luck.</summary>
public static class CodeGenerator
{
    // No I, L, O, U, 0 or 1: they are easy to misread when a ticket is read out or typed at the gate.
    private const string Alphabet = "ABCDEFGHJKMNPQRSTVWXYZ23456789";

    public static string Ticket() => "PW-" + Random(6);

    public static string ReceiptNumber(DateTime utcNow) => $"RC-{utcNow:yyyyMMdd}-{Random(6)}";

    private static string Random(int length)
        => string.Create(length, 0, static (span, _) =>
        {
            for (var i = 0; i < span.Length; i++)
                span[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)]; // unbiased CSPRNG pick
        });
}
