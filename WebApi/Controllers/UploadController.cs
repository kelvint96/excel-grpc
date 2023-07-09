using Google.Protobuf.Collections;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using MessageContract.Worker;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Threading;
using WebApi.Data;

namespace WebApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class UploadController : ControllerBase
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly ILogger<UploadController> _logger;
        private readonly IConfiguration _configuration;

        public UploadController(ApplicationDbContext dbContext, ILogger<UploadController> logger, IConfiguration configuration)
        {
            _dbContext = dbContext;
            _logger = logger;
            _configuration = configuration;
        }

        [HttpPost]
        public async Task<IActionResult> GenerateReport(CancellationToken ct)
        {
            var channel = GrpcChannel.ForAddress(_configuration["Grpc:ExcelWorker"]!);
            var client = new Worker.WorkerClient(channel);

            var dataStream = _dbContext.TestData.AsNoTracking().AsAsyncEnumerable();

            var stream = client.CreateExcelStream(cancellationToken: ct);

            var batchSize = 1000;
            var batchData = new RepeatedField<BatchData>();
            var batchIteration = 1;

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

            var response = stream.ResponseStream;
            var ms = new MemoryStream();
            while (await response.MoveNext())
            {
                _logger.LogInformation("Receiving chunked data");

                var chunkData = response.Current;
                await ms.WriteAsync(chunkData.Chunk.ToByteArray().AsMemory(0, chunkData.Chunk.Length), ct);
            }

            ms.Position = 0;
            return File(ms, "application/octet-stream", "test.xlsx");
        }
    }
}
