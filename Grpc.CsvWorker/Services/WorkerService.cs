using CsvHelper;
using Google.Protobuf;
using Grpc.Core;
using MessageContract.Worker;
using System.Globalization;
using System.Text;

namespace Grpc.CsvWorker.Services
{
    public class WorkerService : Worker.WorkerBase
    {
        private readonly ILogger<WorkerService> _logger;

        public WorkerService(ILogger<WorkerService> logger)
        {
            _logger = logger;
        }

        public override async Task CreateStream(IAsyncStreamReader<Batch> requestStream, IServerStreamWriter<DataChunk> responseStream, ServerCallContext context)
        {
            var stream = new MemoryStream();
            using var writer = new StreamWriter(stream, Encoding.UTF8, 1024, true);
            using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture, true);

            // Initializing excel headers
            csv.WriteField("Id");
            csv.WriteField("Name");
            csv.WriteField("Description");
            csv.WriteField("UpdatedDate");
            csv.WriteField("CreatedDate");
            await csv.NextRecordAsync();

            int bytesRead = 0;
            await foreach (var batch in requestStream.ReadAllAsync())
            {
                _logger.LogInformation("Processing batch number: {batch}", batch.BatchNumber);
                foreach (var data in batch.DataSet)
                {
                    csv.WriteField(data.Id.ToString());
                    csv.WriteField(data.Name);
                    csv.WriteField(data.Description);
                    csv.WriteField(data.UpdatedDate.ToDateTime());
                    csv.WriteField(data.CreatedDate.ToDateTime());
                    await csv.NextRecordAsync();
                }

                stream.Position = bytesRead;
                byte[] excelData = new byte[(int)stream.Length - bytesRead];
                await stream.ReadAsync(excelData, 0, (int)stream.Length - bytesRead, context.CancellationToken);
                await responseStream.WriteAsync(new DataChunk()
                {
                    Chunk = UnsafeByteOperations.UnsafeWrap(excelData)
                });

                bytesRead = (int)stream.Position;
            }
        }
    }
}
