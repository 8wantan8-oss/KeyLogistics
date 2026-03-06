using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProcesadorXmlPedidos.Models;

namespace ProcesadorXmlPedidos.Services
{
    public class FileProcessingService
    {
        private readonly ILogger<FileProcessingService> _logger;
        private readonly WorkerConfig _config;

        public FileProcessingService(ILogger<FileProcessingService> logger, IOptions<WorkerConfig> options)
        {
            _logger = logger;
            _config = options.Value;
        }

        public void EnsureDirectories()
        {
            try
            {
                Directory.CreateDirectory(_config.InputFolder);
                Directory.CreateDirectory(_config.ProcessedFolder);
                Directory.CreateDirectory(_config.ErrorFolder);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unable to create one of the required directories");
                throw;
            }
        }

        public void MoveToProcessed(string filePath)
        {
            Move(filePath, _config.ProcessedFolder);
        }

        public void MoveToError(string filePath)
        {
            Move(filePath, _config.ErrorFolder);
        }

        private void Move(string filePath, string destinationFolder)
        {
            try
            {
                var fileName = Path.GetFileName(filePath);
                if (string.IsNullOrEmpty(fileName))
                    return;

                var dest = Path.Combine(destinationFolder, fileName);
                if (File.Exists(dest))
                {
                    // overwrite so we don't fail because of duplicates
                    File.Delete(dest);
                }
                File.Move(filePath, dest);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to move file {file} to {dest}", filePath, destinationFolder);
            }
        }
    }
}