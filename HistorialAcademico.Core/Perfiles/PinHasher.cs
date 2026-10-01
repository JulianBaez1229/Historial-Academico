using System.Security.Cryptography;

namespace HistorialAcademico.Core.Perfiles;

/// <summary>
/// Guarda y comprueba el PIN (o contraseña) local de un perfil sin guardarlo nunca en texto plano: PBKDF2 con SHA-256, una sal aleatoria
/// por perfil y muchas iteraciones. El resultado es un texto «pbkdf2-sha256$iteraciones$sal$hash» que lleva dentro todo lo necesario
/// para verificar, así que subir las iteraciones en el futuro no invalida los PIN ya guardados.
/// </summary>
public static class PinHasher
{
    public const int IteracionesPredeterminadas = 600_000;   // recomendación de OWASP para PBKDF2-HMAC-SHA256
    public const int MinCaracteres = 4;
    public const int MaxCaracteres = 64;
    private const int BytesSal = 16, BytesHash = 32;
    private const string Prefijo = "pbkdf2-sha256";

    /// <summary>Por qué un PIN no sirve (null si sirve): entre 4 y 64 caracteres, sin espacios en los extremos.</summary>
    public static string? ErrorDePin(string? pin)
    {
        if (string.IsNullOrEmpty(pin)) return "Escribe un PIN o una contraseña.";
        if (pin != pin.Trim()) return "El PIN no puede empezar ni terminar con espacios.";
        if (pin.Length < MinCaracteres) return $"El PIN debe tener al menos {MinCaracteres} caracteres.";
        if (pin.Length > MaxCaracteres) return $"El PIN puede tener como máximo {MaxCaracteres} caracteres.";
        return null;
    }

    public static string Hash(string pin, int iteraciones = IteracionesPredeterminadas)
    {
        if (iteraciones < 1) throw new ArgumentOutOfRangeException(nameof(iteraciones));
        var sal = RandomNumberGenerator.GetBytes(BytesSal);
        var hash = Rfc2898DeriveBytes.Pbkdf2(pin, sal, iteraciones, HashAlgorithmName.SHA256, BytesHash);
        return $"{Prefijo}${iteraciones}${Convert.ToBase64String(sal)}${Convert.ToBase64String(hash)}";
    }

    /// <summary>Comprueba el PIN en tiempo constante. Un texto guardado que no se entiende nunca coincide.</summary>
    public static bool Verificar(string? pin, string? guardado)
    {
        if (pin is null || guardado is null) return false;
        var partes = guardado.Split('$');
        if (partes.Length != 4 || partes[0] != Prefijo || !int.TryParse(partes[1], out var iteraciones) || iteraciones < 1) return false;
        try
        {
            var sal = Convert.FromBase64String(partes[2]);
            var esperado = Convert.FromBase64String(partes[3]);
            var calculado = Rfc2898DeriveBytes.Pbkdf2(pin, sal, iteraciones, HashAlgorithmName.SHA256, esperado.Length);
            return CryptographicOperations.FixedTimeEquals(calculado, esperado);
        }
        catch (FormatException) { return false; }
    }
}
