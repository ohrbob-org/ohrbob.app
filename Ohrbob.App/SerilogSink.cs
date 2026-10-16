using Avalonia.Logging;
using Serilog;
using Serilog.Events;
using LogEventLevel = Avalonia.Logging.LogEventLevel;

public class SerilogSink : ILogSink
{
    private readonly Serilog.ILogger _logger;

    public SerilogSink(Serilog.ILogger logger)
    {
        _logger = logger;
    }

    public bool IsEnabled(LogEventLevel level, string area)
    {
        // Map Avalonia's level to Serilog's level check
        var serilogLevel = MapLevel(level);
        return _logger.IsEnabled(serilogLevel);
    }

    void ILogSink.Log(LogEventLevel level, string area, object? source, string messageTemplate)
    {
        Log(level, area, source, messageTemplate);
    }

    void ILogSink.Log(LogEventLevel level, string area, object? source, string messageTemplate, params object?[] propertyValues)
    {
        Log(level, area, source, messageTemplate, propertyValues);
    }

    public void Log(LogEventLevel level, string area, object? source, string messageTemplate)
    {
        _logger.Write(MapLevel(level), "({Area}) {Message}", area, messageTemplate);
    }

    public void Log(LogEventLevel level, string area, object? source, string messageTemplate, params object?[] propertyValues)
    {
        // Convert property values array for Serilog formatting
        _logger.Write(MapLevel(level), "({Area}) " + messageTemplate, [area, .. propertyValues]);
    }

    private static global::Serilog.Events.LogEventLevel MapLevel(Avalonia.Logging.LogEventLevel level) => level switch
    {
        Avalonia.Logging.LogEventLevel.Verbose => global::Serilog.Events.LogEventLevel.Verbose,
        Avalonia.Logging.LogEventLevel.Debug => global::Serilog.Events.LogEventLevel.Debug,
        Avalonia.Logging.LogEventLevel.Information => global::Serilog.Events.LogEventLevel.Information,
        Avalonia.Logging.LogEventLevel.Warning => global::Serilog.Events.LogEventLevel.Warning,
        Avalonia.Logging.LogEventLevel.Error => global::Serilog.Events.LogEventLevel.Error,
        Avalonia.Logging.LogEventLevel.Fatal => global::Serilog.Events.LogEventLevel.Fatal,
        _ => global::Serilog.Events.LogEventLevel.Information
    };

}