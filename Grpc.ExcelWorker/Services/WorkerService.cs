using Google.Protobuf;
using Grpc.Core;
using LargeXlsx;
using MessageContract.Worker;

namespace Grpc.ExcelWorker.Services
{
    public class WorkerService : Worker.WorkerBase
    {
        private readonly ILogger<WorkerService> _logger;

        public WorkerService(ILogger<WorkerService> logger)
        {
            _logger = logger;
        }

        public override async Task CreateExcelStream(IAsyncStreamReader<Batch> requestStream, IServerStreamWriter<DataChunk> responseStream, ServerCallContext context)
        {
            var stream = new MemoryStream();
            var chunkSize = 64 * 1024;
            using var writer = new XlsxWriter(stream, useZip64: true);

            // Initializing excel headers
            writer.BeginWorksheet("Sheet 1")
                .BeginRow()
                .Write("Id")
                .Write("Name")
                .Write("Description")
                .Write("UpdatedDate")
                .Write("CreatedDate");

            int iterations = 0;
            await foreach (var batch in requestStream.ReadAllAsync())
            {
                byte[] buffer = new byte[chunkSize];

                _logger.LogInformation("Processing batch number: {batch}", batch.BatchNumber);
                foreach (var data in batch.DataSet)
                {
                    writer.BeginRow();

                    writer.Write(data.Id.ToString());
                    writer.Write(data.Name);
                    writer.Write(data.Description);
                    writer.Write(data.UpdatedDate.ToDateTime());
                    writer.Write(data.CreatedDate.ToDateTime());
                }

                while (stream.Read(buffer, iterations * chunkSize, buffer.Length) > 0)
                {
                    await responseStream.WriteAsync(new DataChunk()
                    {
                        Chunk = ByteString.CopyFrom(buffer)
                    });
                }
            }
        }
    }
}
