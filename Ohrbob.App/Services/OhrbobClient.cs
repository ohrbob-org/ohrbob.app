using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

namespace Ohrbob.App.Services;

public class Station
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

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

    public async Task<List<Station>> GetStationsAsync()
    {
        var serverUrl = _configService.ServerUrl?.TrimEnd('/');
        if (string.IsNullOrEmpty(serverUrl))
        {
            throw new InvalidOperationException("Server URL is not configured.");
        }

        var url = $"{serverUrl}/stream/stations";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        ApplyHeaders(request);

        using var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var stations = await response.Content.ReadFromJsonAsync<List<Station>>(options);
        return stations ?? new List<Station>();
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
