using NewsFactory.TestSupport;

namespace NewsFactory.Infrastructure.Tests;

[CollectionDefinition("db")]
public class DbCollection : ICollectionFixture<PostgresFixture>;
