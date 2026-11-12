using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Ohrbob.App.Models;

public enum DownloadState
{
    NotDownloaded,
    Queued,
    Downloading,
    Downloaded,
    Failed
}

public partial class PlaylistItem : ObservableObject
{
    public string EpisodeId { get; init; } = "";
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public string Artist { get; init; } = "";
    public string? CoverUrl { get; init; }
    public string? StreamUri { get; init; }
    public DateTime? PublishedAt { get; init; }

    [ObservableProperty]
    private bool _isCurrent;

    [ObservableProperty]
    private bool _showDownloadStateLabel = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateLabel))]
    [NotifyPropertyChangedFor(nameof(IsDownloaded))]
    [NotifyPropertyChangedFor(nameof(IsNotDownloaded))]
    [NotifyPropertyChangedFor(nameof(IsDownloading))]
    [NotifyPropertyChangedFor(nameof(IsQueued))]
    [NotifyPropertyChangedFor(nameof(IsFailed))]
    private DownloadState _downloadState;

    public bool IsDownloaded => DownloadState == DownloadState.Downloaded;
    public bool IsNotDownloaded => DownloadState == DownloadState.NotDownloaded;
    public bool IsDownloading => DownloadState == DownloadState.Downloading;
    public bool IsQueued => DownloadState == DownloadState.Queued;
    public bool IsFailed => DownloadState == DownloadState.Failed;

    public string StateLabel => DownloadState switch
    {
        DownloadState.Downloaded => "Downloaded",
        DownloadState.Downloading => "Downloading…",
        DownloadState.Queued => "Queued",
        DownloadState.Failed => "Download failed",
        _ => "Not downloaded"
    };

    public string? PublishedAtLabel => PublishedAt?.ToString("MMM d, yyyy", System.Globalization.CultureInfo.InvariantCulture);
}
