﻿using Google.Protobuf;
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

        public override async Task CreateStream(IAsyncStreamReader<Batch> requestStream, IServerStreamWriter<DataChunk> responseStream, ServerCallContext context)
        {
            var stream = new MemoryStream();
            using var writer = new XlsxWriter(stream, useZip64: true);

            // Initializing excel headers
            writer.BeginWorksheet("Sheet 1")
                .BeginRow()
                .Write("Id")
                .Write("Name")
                .Write("Description")
                .Write("UpdatedDate")
                .Write("CreatedDate");

            int bytesRead = 0;
            await foreach (var batch in requestStream.ReadAllAsync())
            {
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
