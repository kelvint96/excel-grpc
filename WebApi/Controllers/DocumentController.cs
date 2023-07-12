using Google.Protobuf.Collections;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using MessageContract.Worker;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApi.Data;

namespace WebApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class DocumentController : ControllerBase
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly ILogger<DocumentController> _logger;
        private readonly IConfiguration _configuration;

        public DocumentController(ApplicationDbContext dbContext, ILogger<DocumentController> logger, IConfiguration configuration)
        {
            _dbContext = dbContext;
            _logger = logger;
            _configuration = configuration;
        }

        [HttpPost]
        public async Task<IActionResult> Generate(DocumentType documentType, CancellationToken ct)
        {
            (string grpcAddress, string fileName) = documentType switch
            {
                DocumentType.Excel => (_configuration["Grpc:ExcelWorker"]!, "test.xslx"),
                DocumentType.Csv => (_configuration["Grpc:CsvWorker"]!, "test.csv"),
                _ => throw new NotSupportedException("Unsupported document type.")
            };

            var channel = GrpcChannel.ForAddress(grpcAddress);
            var client = new Worker.WorkerClient(channel);
            using var stream = client.CreateStream(cancellationToken: ct);

            var dataStream = _dbContext.TestData.AsNoTracking().AsAsyncEnumerable();

            var batchSize = 1000;
            var batchData = new RepeatedField<BatchData>();
            var batchIteration = 1;

            var response = stream.ResponseStream;
            var ms = new MemoryStream();

            // Initialize background task to write to memory stream for processed chunks
            var streamWriter = Task.Run(async () =>
            {
                await foreach (var data in response.ReadAllAsync(cancellationToken: ct))
                {
                    _logger.LogInformation("Receiving chunked data");

                    await ms.WriteAsync(data.Chunk.ToByteArray().AsMemory(0, data.Chunk.Length), ct);

                    if (data.IsFinalChunk)
                    {
                        return;
                    }
                }
            }, ct);

            _logger.LogInformation("Start query records from database with batchSize {batchSize}", batchSize);
            await foreach (var testData in dataStream.WithCancellation(ct))
            {
                if (ct.IsCancellationRequested)
                    break;

                batchData.Add(new BatchData()
                {
                    Id = testData.Id.ToString(),
                    Name = testData.Name,
                    Description = testData.Description,
                    CreatedDate = testData.CreatedDate.ToUniversalTime().ToTimestamp(),
                    UpdatedDate = testData.UpdatedDate.ToUniversalTime().ToTimestamp()
                });

                if (batchData.Count >= batchSize)
                {
                    _logger.LogInformation("Sending batch no: {batchNo} with {itemCount} items inside", batchIteration, batchData.Count);
                    await stream.RequestStream.WriteAsync(new Batch()
                    {
                        BatchNumber = batchIteration,
                        DataSet = { batchData }
                    }, ct);
                    batchIteration++;
                    batchData.Clear();
                }
            }

            if (batchData.Count > 0)
            {
                _logger.LogInformation("Sending remainder batch data with {itemCount} inside", batchData.Count);
                await stream.RequestStream.WriteAsync(new Batch()
                {
                    DataSet = { batchData }
                }, ct);
                batchData.Clear();
            }

            await stream.RequestStream.CompleteAsync();
            await streamWriter;
            ms.Position = 0;
            return File(ms, "application/octet-stream", fileName);
        }
    }
}
