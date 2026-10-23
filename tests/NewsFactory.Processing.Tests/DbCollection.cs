using NewsFactory.TestSupport;

namespace NewsFactory.Processing.Tests;

[CollectionDefinition("db")]
public class DbCollection : ICollectionFixture<PostgresFixture>;
