# BackEndProxy.Tests

## Prerequisites to running test

* update `test_config.json` with relevant information for `Proxy` and `Database` fields
* update `SLN_PATH` field in `.runsettings` with absolute path to solution directory
* Populate database with playback details
* Run ProxyASR server

## Run tests

```bash
$ # run all tests
$ dotnet test -s BackEndProxy.Tests/.runsettings

$ # filter and run tests
$ dotnet test -s BackEndProxy.Tests/.runsettings --filter simple
```
