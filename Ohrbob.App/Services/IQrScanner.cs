using System.Threading;
using System.Threading.Tasks;

namespace Ohrbob.App.Services;

public interface IQrScanner
{
    bool IsAvailable { get; }

    /// <summary>Shows the live scanner. Returns the decoded text, or null when cancelled.</summary>
    Task<string?> ScanAsync(CancellationToken cancellationToken = default);
}

/// <summary>Set by each platform entry point before Avalonia creates the view model.</summary>
public static class QrScannerProvider
{
    public static IQrScanner? Current { get; set; }
}
