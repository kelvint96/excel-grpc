using EFCore.BulkExtensions;
using Microsoft.EntityFrameworkCore;

namespace WebApi.Data
{
    public static class SeedDatabaseExtension
    {
        public static WebApplication SeedDatabase(this WebApplication app)
        {
            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                //Same as the question
                db.Database.Migrate();

                if (!db.TestData.Any())
                {
                    db.BulkInsert(SeedData.TestDataSeed());
                }
            }
            return app;
        }
    }
}
