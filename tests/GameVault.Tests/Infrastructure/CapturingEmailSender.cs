using System.Collections.Concurrent;
using GameVault.Business.Email;

namespace GameVault.Tests.Infrastructure;

/// <summary>Gerçek SMTP yerine gönderilen e-postaları bellekte tutar. Testler asla gerçek alıcıya e-posta göndermez.</summary>
public class CapturingEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> _messages = new();

    public IReadOnlyCollection<EmailMessage> Messages => _messages;

    public virtual Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        _messages.Enqueue(message);
        return Task.CompletedTask;
    }

    private IEnumerable<EmailMessage> For(string to, string? subjectContains) =>
        _messages.Where(m => string.Equals(m.To, to, StringComparison.OrdinalIgnoreCase)
            && (subjectContains is null || m.Subject.Contains(subjectContains, StringComparison.OrdinalIgnoreCase)));

    public async Task<EmailMessage> WaitForAsync(string to, string? subjectContains = null, int expectedCount = 1, int timeoutMs = 15_000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            var matching = For(to, subjectContains).ToList();
            if (matching.Count >= expectedCount)
            {
                return matching[expectedCount - 1];
            }

            await Task.Delay(50);
        }

        throw new TimeoutException($"Beklenen e-posta gelmedi ({subjectContains ?? "herhangi"}).");
    }

    public int CountFor(string to, string? subjectContains = null) => For(to, subjectContains).Count();
}

/// <summary>İlk N gönderimde geçici SMTP hatası verir, sonra başarılı olur.</summary>
public sealed class FlakyEmailSender(int failures) : CapturingEmailSender
{
    private int _calls;

    public int Calls => _calls;

    public override Task SendAsync(EmailMessage message, CancellationToken ct) =>
        Interlocked.Increment(ref _calls) <= failures
            ? throw new IOException("Geçici SMTP hatası (test)")
            : base.SendAsync(message, ct);
}

/// <summary>Her zaman başarısız olan SMTP.</summary>
public sealed class FailingEmailSender : IEmailSender
{
    private int _calls;

    public int Calls => _calls;

    public Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        Interlocked.Increment(ref _calls);
        throw new IOException("SMTP erişilemiyor (test)");
    }
}
