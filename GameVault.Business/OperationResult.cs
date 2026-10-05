namespace GameVault.Business;

/// <summary>Servis işlemlerinin sonucu: başarı, bulunamadı ya da alan bazlı doğrulama hataları.</summary>
public sealed class OperationResult<T>
{
    private OperationResult(bool succeeded, bool notFound, T? value, IReadOnlyDictionary<string, string> errors)
    {
        Succeeded = succeeded;
        NotFound = notFound;
        Value = value;
        Errors = errors;
    }

    public bool Succeeded { get; }
    public bool NotFound { get; }
    public T? Value { get; }

    /// <summary>Alan adı → mesaj. Genel hatalar boş anahtarla ("") döner.</summary>
    public IReadOnlyDictionary<string, string> Errors { get; }

    public static OperationResult<T> Success(T value) => new(true, false, value, new Dictionary<string, string>());

    public static OperationResult<T> Missing() => new(false, true, default, new Dictionary<string, string>());

    public static OperationResult<T> Invalid(IReadOnlyDictionary<string, string> errors) => new(false, false, default, errors);

    public static OperationResult<T> Invalid(string field, string message) =>
        Invalid(new Dictionary<string, string> { [field] = message });
}
