using NewsFactory.TestSupport;

namespace NewsFactory.Llm.Tests;

[CollectionDefinition("db")]
public class DbCollection : ICollectionFixture<PostgresFixture>;
