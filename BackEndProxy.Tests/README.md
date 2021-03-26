# BackEndProxy.Tests

Integration and authentication testing is implemented in [BackendTest.cs](BackEndProxy.Tests/BackendTest.cs). Current scenarios that are tested:

* Integration Testing:
    * `SimpleTest`: short end-to-end test
    * `MockGameSessionTest`: end-to-end test that mimics a standard game session

Integration testing requires a database to be setup and seeded, as well as a server with no client authentication to be setup

* Authentication Testing:
    * `TestWithNoCredentials`
    * `DatabaseTestWithCorrectCredentials`
    * `DatabaseTestWithWrongCredentials`
    * `Auth0TestWithCorrectCredentials`
    * `Auth0TestWithWrongCredentials`

## Prerequisites to running tests

1. Setup and seed database if required
2. Setup a proxy server with the appropriate config settings e.g. whether to authenticate clients, which type of client authentication method to use
3. Update [test_config.json](BackEndProxy.Tests/test_config.json) with required config settings
4. update `SLN_PATH` field in `.runsettings` with absolute path to solution directory

## Run tests

```bash
$ # run all tests
$ dotnet test -s BackEndProxy.Tests/.runsettings

$ # filter and run tests
$ dotnet test -s BackEndProxy.Tests/.runsettings --filter simple
```
