using Microsoft.Extensions.Options;
using ProcesadorXmlPedidos;
using ProcesadorXmlPedidos.Services;

var builder = Host.CreateApplicationBuilder(args);

// bind settings from appsettings.json
builder.Services.Configure<WorkerConfig>(builder.Configuration.GetSection("Worker"));

// register our helper/services
builder.Services.AddSingleton<OrderFileParser>();
builder.Services.AddSingleton<FileProcessingService>();

// worker itself
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
