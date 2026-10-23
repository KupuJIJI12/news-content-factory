using NewsFactory.TestSupport;

namespace NewsFactory.Api.Tests;

[CollectionDefinition("db")]
public class DbCollection : ICollectionFixture<PostgresFixture>;
