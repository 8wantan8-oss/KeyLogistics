using Microsoft.Extensions.Options;
using ProcesadorXmlPedidos;
using ProcesadorXmlPedidos.Services;

var builder = WebApplication.CreateBuilder(args);

// bind settings from appsettings.json
builder.Services.Configure<WorkerConfig>(builder.Configuration.GetSection("Worker"));

// register our helper/services
builder.Services.AddSingleton<OrderFileParser>();
builder.Services.AddSingleton<FileProcessingService>();

// Add WorkerControlService as singleton, initialized with IntervalSeconds from config
builder.Services.AddSingleton<WorkerControlService>(sp =>
{
    var config = sp.GetRequiredService<IOptions<WorkerConfig>>();
    return new WorkerControlService(config.Value.IntervalSeconds);
});

// Register KeyLogisticsApiClient with HttpClient
builder.Services.AddHttpClient<KeyLogisticsApiClient>();

// worker itself
builder.Services.AddHostedService<Worker>();

var app = builder.Build();

// Minimal API endpoints
app.MapGet("/status", (WorkerControlService control, IOptions<WorkerConfig> config) =>
{
    var pending = control.GetPendingCount(config.Value.InputFolder);
    return Results.Json(new
    {
        isAlive = true,
        processingEnabled = control.ProcessingEnabled,
        processedOk = control.ProcessedOk,
        processedError = control.ProcessedError,
        pending = pending,
        intervalSeconds = control.IntervalSeconds
    });
});

app.MapPost("/status", (WorkerControlService control, StatusUpdateRequest request) =>
{
    if (request.ProcessingEnabled.HasValue)
        control.ProcessingEnabled = request.ProcessingEnabled.Value;
    if (request.IntervalSeconds.HasValue)
        control.IntervalSeconds = request.IntervalSeconds.Value;
    return Results.Ok();
});

// Serve simple HTML dashboard
app.MapGet("/", () => Results.Content(GetDashboardHtml(), "text/html"));

static string GetDashboardHtml() => @"
<!DOCTYPE html>
<html>
<head>
    <title>Worker Dashboard</title>
</head>
<body>
    <h1>Worker Control Dashboard</h1>
    <div id=""status"">Loading...</div>
    <button id=""onBtn"">Turn ON</button>
    <button id=""offBtn"">Turn OFF</button>
    <br><br>
    <label>Interval (seconds): <input type=""number"" id=""intervalInput"" /></label>
    <button id=""updateIntervalBtn"">Update Interval</button>

    <script>
        async function loadStatus() {
            const response = await fetch('/status');
            const data = await response.json();
            document.getElementById('status').innerHTML = `
                <p>Processing: ${data.processingEnabled ? 'ON' : 'OFF'}</p>
                <p>Processed OK: ${data.processedOk}</p>
                <p>Processed Error: ${data.processedError}</p>
                <p>Pending: ${data.pending}</p>
                <p>Interval: ${data.intervalSeconds}s</p>
            `;
            document.getElementById('intervalInput').value = data.intervalSeconds;
        }

        document.getElementById('onBtn').addEventListener('click', async () => {
            await fetch('/status', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ processingEnabled: true })
            });
            loadStatus();
        });

        document.getElementById('offBtn').addEventListener('click', async () => {
            await fetch('/status', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ processingEnabled: false })
            });
            loadStatus();
        });

        document.getElementById('updateIntervalBtn').addEventListener('click', async () => {
            const interval = parseInt(document.getElementById('intervalInput').value);
            await fetch('/status', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ intervalSeconds: interval })
            });
            loadStatus();
        });

        loadStatus();
    </script>
</body>
</html>
";

app.Run();
