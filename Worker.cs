namespace ProcesadorXmlPedidos;

using Microsoft.Extensions.Options;
using ProcesadorXmlPedidos.Services;
using System.Globalization;
using System.Xml.Linq;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly WorkerConfig _config;
    private readonly FileProcessingService _fileService;
    private readonly OrderFileParser _parser;
    private readonly WorkerControlService _control;
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

                        await ProcessFile(file, stoppingToken);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while scanning input folder");
            }

            await Task.Delay(TimeSpan.FromSeconds(_control.IntervalSeconds), stoppingToken);
        }
    }

    private async Task ProcessFile(string path, CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("Parsing file {file}", path);

            // Parse XML
            XDocument doc;
            try
            {
                doc = XDocument.Load(path);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to load XML file: {path}", ex);
            }

            // PEDIDO (header)
            var pedidoNode = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "PEDIDO");
            if (pedidoNode is null)
                throw new InvalidOperationException($"XML missing <PEDIDO> node. File: {path}");

            // Build products catalog (PRODUTOS/PRODUTO)
            var productsByCode = new Dictionary<string, (string Desc, string Uom)>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in doc.Descendants().Where(e => e.Name.LocalName == "PRODUTO"))
            {
                var code = GetChildValue(p, "CODIGO");
                if (string.IsNullOrWhiteSpace(code))
                    continue;

                var desc = GetChildValue(p, "DESCRICAO") ?? "";
                var uom = GetChildValue(p, "UNIDADEMEDIDA") ?? "";
                productsByCode[code] = (desc, uom);
            }

            // Extract header fields
            var orderNumber = GetChildValue(pedidoNode, "NUM_PEDIDO") ?? "";
            var supplierCode = GetChildValue(pedidoNode, "FORNECEDOR") ?? "";

            var orderDateRaw = GetChildValue(pedidoNode, "DATA_PEDIDO");
            var orderDate = ParseYyyyMmDdToIso(orderDateRaw);

            var deliveryDateRaw = GetChildValue(pedidoNode, "DATA_ENTREGA");
            var deliveryDate = ParseYyyyMmDdToIso(deliveryDateRaw);

            var paymentCondition = GetChildValue(pedidoNode, "CONDICAO_PGTO") ?? "";
            var deliveryLocationCode = GetChildValue(pedidoNode, "LOCAL_ENTREGA") ?? "";
            var invoiceLocationCode = GetChildValue(pedidoNode, "LOCAL_FATURA") ?? "";
            var billingLocationCode = GetChildValue(pedidoNode, "LOCAL_COBRANCA") ?? "";
            var notes = GetChildValue(pedidoNode, "OBS") ?? "";

            // Build items (PEDIDO/ITENS/ITEM)
            var items = new List<Dictionary<string, object?>>();
            var itensNode = pedidoNode.Elements().FirstOrDefault(e => e.Name.LocalName == "ITENS");

            var itemNodes = itensNode?.Elements().Where(e => e.Name.LocalName == "ITEM") ?? Enumerable.Empty<XElement>();
            foreach (var item in itemNodes)
            {
                var materialCode = GetChildValue(item, "COD_MAT") ?? "";
                if (string.IsNullOrWhiteSpace(materialCode))
                {
                    _logger.LogWarning("Skipping ITEM with empty COD_MAT. File={file} Order={order}", path, orderNumber);
                    continue;
                }

                var qtyStr = GetChildValue(item, "QTDE_PEDIDA") ?? "0";
                var priceStr = GetChildValue(item, "VALOR_UNIT") ?? "0";

                var quantity = ParseDecimalInvariant(qtyStr, defaultValue: 0m);
                var unitPrice = ParseDecimalInvariant(priceStr, defaultValue: 0m);

                var itemDeliveryRaw = GetChildValue(item, "DATA_ENTREGA");
                var requestedDate = ParseYyyyMmDdToIso(itemDeliveryRaw) ?? deliveryDate;

                var itemDict = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["materialCode"] = materialCode,
                    ["quantity"] = quantity,
                    ["unitPrice"] = unitPrice,
                    ["requestedDate"] = requestedDate
                };

                if (productsByCode.TryGetValue(materialCode, out var prod))
                {
                    itemDict["description"] = prod.Desc;
                    itemDict["uom"] = prod.Uom;
                }
                else
                {
                    // Mantener robusto: no revienta, pero deja evidencia.
                    itemDict["description"] = "";
                    itemDict["uom"] = "";
                    _logger.LogWarning("Product {code} not found in catalog. File={file} Order={order}", materialCode, path, orderNumber);
                }

                items.Add(itemDict);
            }

            // Validate required fields (fail-fast con mensaje claro)
            var missing = new List<string>();
            if (string.IsNullOrWhiteSpace(orderNumber)) missing.Add("orderNumber");
            if (string.IsNullOrWhiteSpace(supplierCode)) missing.Add("supplierCode");
            if (string.IsNullOrWhiteSpace(paymentCondition)) missing.Add("paymentCondition");
            if (string.IsNullOrWhiteSpace(deliveryLocationCode)) missing.Add("deliveryLocationCode");
            if (string.IsNullOrWhiteSpace(invoiceLocationCode)) missing.Add("invoiceLocationCode");
            if (string.IsNullOrWhiteSpace(billingLocationCode)) missing.Add("billingLocationCode");
            if (string.IsNullOrWhiteSpace(orderDate)) missing.Add("orderDate");
            if (string.IsNullOrWhiteSpace(deliveryDate)) missing.Add("deliveryDate");
            if (items.Count == 0) missing.Add("items");

            if (missing.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Order {orderNumber} missing required fields: {string.Join(", ", missing)}. File={path}");
            }

            // Build parametros (camelCase)
            var parametros = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["orderNumber"] = orderNumber,
                ["supplierCode"] = supplierCode,
                ["orderDate"] = orderDate,
                ["deliveryDate"] = deliveryDate,
                ["paymentCondition"] = paymentCondition,
                ["deliveryLocationCode"] = deliveryLocationCode,
                ["invoiceLocationCode"] = invoiceLocationCode,
                ["billingLocationCode"] = billingLocationCode,
                ["notes"] = notes,
                ["items"] = items
            };

            // Logs (Debug) - robustos y sin variables inexistentes
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(
                    "CHECK header orderNumber={OrderNumber} supplierCode={SupplierCode} orderDate={OrderDate} deliveryDate={DeliveryDate}",
                    orderNumber, supplierCode, orderDate, deliveryDate
                );

                _logger.LogDebug(
                    "CHECK locations delivery={Delivery} invoice={Invoice} billing={Billing}",
                    deliveryLocationCode, invoiceLocationCode, billingLocationCode
                );

                if (items.Count > 0)
                {
                    var item0 = items[0];

                    _logger.LogDebug(
                        "CHECK item0 materialCode={materialCode} qty={quantity} price={unitPrice} requestedDate={requestedDate}",
                        item0["materialCode"],
                        item0["quantity"],
                        item0["unitPrice"],
                        item0["requestedDate"]
                    );

                    _logger.LogDebug(
                        "CHECK catalog item0 desc={desc} uom={uom}",
                        item0["description"],
                        item0["uom"]
                    );
                }
            }

            // Call API
            var resp = await _keyLogisticsApiClient.CrearOrdenAsync(parametros, ct);

            if (resp.Exito && resp.Respuesta == "OK")
            {
                _fileService.MoveToProcessed(path);
                _control.ProcessedOk++;
            }
            else
            {
                _logger.LogError("API returned error for file {file}: {error}", path, resp.ErrorTrace);
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

    // -----------------------------
    // Helpers (robustos)
    // -----------------------------

    private static string? GetChildValue(XElement parent, string childLocalName)
        => parent.Elements().FirstOrDefault(e => e.Name.LocalName == childLocalName)?.Value;

    private static string? ParseYyyyMmDdToIso(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        // XML viene como yyyymmdd, ej: 20260311
        if (DateTime.TryParseExact(raw.Trim(), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        // fallback: intenta parse normal (por si viene con guiones)
        if (DateTime.TryParse(raw.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out dt))
            return dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        return null;
    }

    private static decimal ParseDecimalInvariant(string? raw, decimal defaultValue)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return defaultValue;

        // Tolerar coma/punto
        var normalized = raw.Trim().Replace(',', '.');

        if (decimal.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
            return d;

        return defaultValue;
    }

    private static object? GetDictObject(Dictionary<string, object?> dict, string key)
        => dict.TryGetValue(key, out var val) ? val : null;

    private static string? GetDictString(Dictionary<string, object?> dict, string key)
        => dict.TryGetValue(key, out var val) ? val?.ToString() : null;
}