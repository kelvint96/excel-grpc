namespace WebApi.Data
{
    public static class SeedData
    {
        public static IEnumerable<TestData> TestDataSeed()
        {
            for (var i = 0; i < 300000; i++)
            {
                yield return new TestData()
                {
                    Id = Guid.NewGuid(),
                    Name = $"Name_{i}",
                    Description = $"Description_{i}",
                    CreatedDate = DateTime.Now,
                    UpdatedDate = DateTime.Now
                };
            }

        }
    }
}
