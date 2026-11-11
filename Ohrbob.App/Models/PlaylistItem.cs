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
    [NotifyPropertyChangedFor(nameof(StateLabel))]
    private DownloadState _downloadState;

    public string StateLabel => DownloadState switch
    {
        DownloadState.Downloaded => "Downloaded",
        DownloadState.Downloading => "Downloading…",
        DownloadState.Queued => "Queued",
        DownloadState.Failed => "Download failed",
        _ => "Not downloaded"
    };
}
