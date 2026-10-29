using System;
using System.Linq;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Serilog;

using Ohrbob.App.Models;

namespace Ohrbob.App.Services;

public class OhrbobClient
{
    private readonly HttpClient _httpClient;
    private readonly ConfigService _configService;

    public OhrbobClient(ConfigService configService) : this(configService, null)
    {
    }

    public OhrbobClient(ConfigService configService, HttpMessageHandler? handler)
    {
        _configService = configService;
        _httpClient = handler != null ? new HttpClient(handler) : new HttpClient();
    }

    private void ApplyHeaders(HttpRequestMessage request)
    {
        if (!string.IsNullOrEmpty(_configService.StreamToken))
        {
            request.Headers.Add("X-Stream-Token", _configService.StreamToken);
        }
    }

    public async Task<List<StationWithFeedsDto>> GetStationsAsync()
    {
        var serverUrl = _configService.ServerUrl?.TrimEnd('/');
        if (string.IsNullOrEmpty(serverUrl))
        {
            throw new InvalidOperationException("Server URL is not configured.");
        }

        var url = $"{serverUrl}/stream/stations";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        ApplyHeaders(request);
        
        Log.Debug($"Sending request to {url}");
        
        using var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"HTTP error! Status: {response.StatusCode}");
        }

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var stations = await response.Content.ReadFromJsonAsync<List<StationWithFeedsDto>>(options);
        if (stations != null)
        {
            foreach (var s in stations)
            {
                if (s.Feeds != null && s.Feeds.Count > 4)
                {
                    s.Feeds = s.Feeds.Take(4).ToList();
                }
            }
        }
        return stations ?? new List<StationWithFeedsDto>();
    }

    public async Task<JsonElement> GetStatusAsync()
    {
        var serverUrl = _configService.ServerUrl?.TrimEnd('/');
        if (string.IsNullOrEmpty(serverUrl))
        {
            throw new InvalidOperationException("Server URL is not configured.");
        }

        var url = $"{serverUrl}/stream/status";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        ApplyHeaders(request);

        using var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var status = await response.Content.ReadFromJsonAsync<JsonElement>(options);
        return status;
    }

    public async Task<string?> GetWebUriAsync()
    {
        var serverUrl = _configService.ServerUrl?.TrimEnd('/');
        if (string.IsNullOrEmpty(serverUrl))
            return null;

        var url = $"{serverUrl}/weburi";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        ApplyHeaders(request);

        using var response = await _httpClient.SendAsync(request);
        if (response.IsSuccessStatusCode)
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var result = await response.Content.ReadFromJsonAsync<WebUriResponse>(options);
            return result?.Uri;
        }

        return null;
    }

    private record WebUriResponse(string Uri);

    public async Task PlayStationAsync(string stationId)
    {
        var serverUrl = _configService.ServerUrl?.TrimEnd('/');
        if (string.IsNullOrEmpty(serverUrl))
        {
            throw new InvalidOperationException("Server URL is not configured.");
        }

        var url = $"{serverUrl}/stream/play-station/{stationId}";
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        ApplyHeaders(request);

        using var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    public async Task SyncStatusAsync(string episodeId, string stationId, int positionSeconds)
    {
        var serverUrl = _configService.ServerUrl?.TrimEnd('/');
        if (string.IsNullOrEmpty(serverUrl))
        {
            throw new InvalidOperationException("Server URL is not configured.");
        }

        var url = $"{serverUrl}/stream/status";
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        ApplyHeaders(request);

        var payload = new
        {
            episodeId = episodeId,
            stationId = stationId,
            positionSeconds = positionSeconds
        };
        request.Content = JsonContent.Create(payload);

        using var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    public async Task NextAsync()
    {
        var serverUrl = _configService.ServerUrl?.TrimEnd('/');
        if (string.IsNullOrEmpty(serverUrl))
        {
            throw new InvalidOperationException("Server URL is not configured.");
        }

        var url = $"{serverUrl}/stream/next";
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        ApplyHeaders(request);

        using var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }
}
