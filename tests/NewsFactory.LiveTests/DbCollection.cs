using NewsFactory.TestSupport;

namespace NewsFactory.LiveTests;

[CollectionDefinition("db")]
public class DbCollection : ICollectionFixture<PostgresFixture>;
