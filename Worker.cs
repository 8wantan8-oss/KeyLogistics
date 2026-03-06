namespace ProcesadorXmlPedidos;

using Microsoft.Extensions.Options;
using ProcesadorXmlPedidos.Services;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly WorkerConfig _config;
    private readonly FileProcessingService _fileService;
    private readonly OrderFileParser _parser;

    public Worker(
        ILogger<Worker> logger,
        IOptions<WorkerConfig> options,
        FileProcessingService fileService,
        OrderFileParser parser)
    {
        _logger = logger;
        _config = options.Value;
        _fileService = fileService;
        _parser = parser;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Worker starting with interval {interval}s, input={input}",
            _config.IntervalSeconds, _config.InputFolder);

        // ensure directories exist at startup
        _fileService.EnsureDirectories();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var files = Directory.GetFiles(_config.InputFolder, "*.xml");
                foreach (var file in files)
                {
                    if (stoppingToken.IsCancellationRequested)
                        break;

                    ProcessFile(file);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while scanning input folder");
            }

            await Task.Delay(TimeSpan.FromSeconds(_config.IntervalSeconds), stoppingToken);
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
                _logger.LogInformation("Order {orderNo} parsed successfully with {count} items",
                    order.OrderNumber, order.Items.Count);
                _fileService.MoveToProcessed(path);
            }
            else
            {
                _logger.LogWarning("Parsing returned null for file {file}", path);
                _fileService.MoveToError(path);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception while processing file {file}", path);
            _fileService.MoveToError(path);
        }
    }
}
