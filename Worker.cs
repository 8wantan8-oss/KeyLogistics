namespace ProcesadorXmlPedidos;

using Microsoft.Extensions.Options;
using ProcesadorXmlPedidos.Services;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly WorkerConfig _config;
    private readonly FileProcessingService _fileService;
    private readonly OrderFileParser _parser;
    private readonly WorkerControlService _control;

    // Se inyecta el cliente, pero NO se usa todavía
    private readonly KeyLogisticsApiClient _keyLogisticsApiClient;

    public Worker(
        ILogger<Worker> logger,
        IOptions<WorkerConfig> options,
        FileProcessingService fileService,
        OrderFileParser parser,
        WorkerControlService control,
        KeyLogisticsApiClient keyLogisticsApiClient)
    {
        _logger = logger;
        _config = options.Value;
        _fileService = fileService;
        _parser = parser;
        _control = control;
        _keyLogisticsApiClient = keyLogisticsApiClient;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Worker starting with interval {interval}s, input={input}",
            _control.IntervalSeconds,
            _config.InputFolder);

        // ensure directories exist at startup
        _fileService.EnsureDirectories();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_control.ProcessingEnabled)
                {
                    var files = Directory.GetFiles(_config.InputFolder, "*.xml");
                    foreach (var file in files)
                    {
                        if (stoppingToken.IsCancellationRequested)
                            break;

                        ProcessFile(file);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while scanning input folder");
            }

            await Task.Delay(
                TimeSpan.FromSeconds(_control.IntervalSeconds),
                stoppingToken);
        }
    }

    private void ProcessFile(string path)
    {
        try
        {
            _logger.LogInformation("Parsing file {file}", path);

            var order = _parser.Parse(path);

            if (order != null)
            {
                _logger.LogInformation(
                    "Order {orderNo} parsed successfully with {count} items",
                    order.OrderNumber,
                    order.Items.Count);

                _fileService.MoveToProcessed(path);
                _control.ProcessedOk++;
            }
            else
            {
                _logger.LogWarning(
                    "Parsing returned null for file {file}",
                    path);

                _fileService.MoveToError(path);
                _control.ProcessedError++;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception while processing file {file}", path);
            _fileService.MoveToError(path);
            _control.ProcessedError++;
        }
    }
}
