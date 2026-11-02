using System;
using System.ComponentModel;
using Android.Content;
using Android.Media;
using Ohrbob.App.ViewModels;

namespace Ohrbob.App.Android.Media;

/// <summary>
/// Requests Android audio focus while the player is running. Without this request the
/// system never sends an interruption for incoming calls, so playback keeps going.
/// Transient losses (calls) pause playback and resume when focus returns; a permanent
/// loss stops it.
/// </summary>
public sealed class AndroidAudioFocus : Java.Lang.Object, AudioManager.IOnAudioFocusChangeListener
{
    private readonly AudioManager _audioManager;
    private readonly MainViewModel _viewModel;
    private AudioFocusRequestClass? _request;
    private bool _resumeOnGain;
    private bool _handlingFocusChange;

    public AndroidAudioFocus(Context context, MainViewModel viewModel)
    {
        _audioManager = (AudioManager)context.GetSystemService(Context.AudioService)!;
        _viewModel = viewModel;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        if (_viewModel.IsPlaying && !RequestFocus())
        {
            SetPlaying(false);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.IsPlaying) || _handlingFocusChange)
        {
            return;
        }

        if (_viewModel.IsPlaying)
        {
            if (!RequestFocus())
            {
                // Someone else (e.g. an ongoing call) holds focus.
                SetPlaying(false);
            }
        }
        else
        {
            _resumeOnGain = false;
            AbandonFocus();
        }
    }

    public void OnAudioFocusChange(AudioFocus focusChange)
    {
        switch (focusChange)
        {
            case AudioFocus.Loss:
                _resumeOnGain = false;
                SetPlaying(false);
                AbandonFocus();
                break;

            case AudioFocus.LossTransient:
            case AudioFocus.LossTransientCanDuck:
                _resumeOnGain = _viewModel.IsPlaying;
                SetPlaying(false);
                break;

            case AudioFocus.Gain:
                if (_resumeOnGain)
                {
                    _resumeOnGain = false;
                    SetPlaying(true);
                }
                break;
        }
    }

    private void SetPlaying(bool playing)
    {
        // The property change caused by an interruption must not abandon the focus
        // request, otherwise there would be no Gain callback to resume on.
        _handlingFocusChange = true;
        try
        {
            _viewModel.IsPlaying = playing;
        }
        finally
        {
            _handlingFocusChange = false;
        }
    }

    private bool RequestFocus()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            _request ??= new AudioFocusRequestClass.Builder(AudioFocus.Gain)
                .SetAudioAttributes(new AudioAttributes.Builder()!
                    .SetUsage(AudioUsageKind.Media)!
                    .SetContentType(AudioContentType.Music)!
                    .Build()!)
                .SetOnAudioFocusChangeListener(this)
                .Build()!;

            return _audioManager.RequestAudioFocus(_request) == AudioFocusRequest.Granted;
        }

        return _audioManager.RequestAudioFocus(this, Stream.Music, AudioFocus.Gain) == AudioFocusRequest.Granted;
    }

    private void AbandonFocus()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            if (_request != null)
            {
                _audioManager.AbandonAudioFocusRequest(_request);
            }
        }
        else
        {
            _audioManager.AbandonAudioFocus(this);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            AbandonFocus();
            _request?.Dispose();
            _request = null;
        }

        base.Dispose(disposing);
    }
}
