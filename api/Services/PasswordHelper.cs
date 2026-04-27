namespace EntornExamen.Api.Services;

public static class PasswordHelper
{
    private const string Chars = "abcdefghjkmnpqrstuvwxyzABCDEFGHJKMNPQRSTUVWXYZ23456789";

    public static string Generate(int length = 10) =>
        new(Enumerable.Range(0, length)
            .Select(_ => Chars[Random.Shared.Next(Chars.Length)]).ToArray());

    public static string Hash(string password) =>
        BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12);

    public static bool Verify(string password, string hash) =>
        BCrypt.Net.BCrypt.Verify(password, hash);
}
