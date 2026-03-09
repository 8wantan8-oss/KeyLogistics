namespace ProcesadorXmlPedidos;

public record StatusUpdateRequest(bool? ProcessingEnabled, int? IntervalSeconds);