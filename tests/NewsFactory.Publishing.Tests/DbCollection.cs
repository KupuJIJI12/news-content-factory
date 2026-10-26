using NewsFactory.TestSupport;

namespace NewsFactory.Publishing.Tests;

[CollectionDefinition("db")]
public class DbCollection : ICollectionFixture<PostgresFixture>;
