using NewsFactory.TestSupport;

namespace NewsFactory.Collection.Tests;

[CollectionDefinition("db")]
public class DbCollection : ICollectionFixture<PostgresFixture>;
