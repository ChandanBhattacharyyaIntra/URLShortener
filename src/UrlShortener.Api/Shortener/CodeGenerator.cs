using System.Security.Cryptography;

namespace UrlShortener.Api.Shortener;

public static class CodeGenerator
{
    private const string Alphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "api", "health", "metrics", "admin", "orchestrator", "index.html"
    };

    public static bool IsReserved(string code) => Reserved.Contains(code);

    public static bool IsValidAlias(string alias) =>
        alias.Length is >= 3 and <= 32
        && alias.All(c => char.IsLetterOrDigit(c) || c is '_' or '-')
        && !IsReserved(alias);

    public static string Next(int length = 7)
    {
        Span<char> chars = stackalloc char[length];
        Span<byte> bytes = stackalloc byte[length];
        RandomNumberGenerator.Fill(bytes);
        for (var i = 0; i < length; i++)
            chars[i] = Alphabet[bytes[i] % Alphabet.Length];
        return new string(chars);
    }
}
