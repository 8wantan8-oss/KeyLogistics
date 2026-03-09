namespace ProcesadorXmlPedidos.Services;

public class WorkerControlService
{
    public bool ProcessingEnabled { get; set; } = true;
    public int IntervalSeconds { get; set; }
    public int ProcessedOk { get; set; } = 0;
    public int ProcessedError { get; set; } = 0;

    public WorkerControlService(int initialIntervalSeconds)
    {
        IntervalSeconds = initialIntervalSeconds;
    }

    public int GetPendingCount(string inputFolder)
    {
        if (!Directory.Exists(inputFolder))
            return 0;
        return Directory.GetFiles(inputFolder, "*.xml").Length;
    }
}