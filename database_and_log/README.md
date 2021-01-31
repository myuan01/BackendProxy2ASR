# .NET Core PostgreSQL

## Install Required Packages

```bash
$ dotnet add package Npgsql --version 5.0.1.1
$ dotnet add package Serilog --version 2.10.0
$ dotnet add package Serilog.Sinks.Console --version 4.0.0-dev-00839
$ dotnet add package Serilog.Sinks.File --version 5.0.0-dev-00909
$ dotnet add package Microsoft.Extensions.Configuration.Json --version 5.0.0
$ dotnet add package Serilog.Settings.Configuration --version 3.1.0
```

OR

```bash
$ dotnet build
```

## Usage

* Update connection details in [config.xml](config.xml)
* Update `test_session_id` and `test_seq_id` in [Program.cs](Program.cs)
* Run program

```bash
$ dotnet run
Usage: demo inputPath outputPat

$ dotnet run ./sample_input.wav ./output.wav
21-01-2021 16:50:23 database_and_log.DatabaseHelper [INF] Opening database connection...
21-01-2021 16:50:23 database_and_log.Program [INF] Opening connection success? : True
21-01-2021 16:50:23 database_and_log.DatabaseHelper [INF] Storing prediction for session session_id_3, seq 10.
21-01-2021 16:50:23 database_and_log.DatabaseHelper [INF] Database insertion complete.
21-01-2021 16:50:23 database_and_log.DatabaseHelper [INF] Storing stream info for session session_id_3, seq 10.
21-01-2021 16:50:23 database_and_log.DatabaseHelper [INF] Database insertion complete.
21-01-2021 16:50:23 database_and_log.DatabaseHelper [INF] Reading audio info for session session_id_3, seq 10.
21-01-2021 16:50:23 database_and_log.DatabaseHelper [INF] Writing audio file to ./output.wav
21-01-2021 16:50:23 database_and_log.DatabaseHelper [INF] Closing database connection...
21-01-2021 16:50:23 database_and_log.Program [INF] Closing connection success? : True

```

## `DatabaseHelper` API

* Database functionality implemented using [Npgsql](https://www.npgsql.org/)
* For an example on how `DatabaseHelper` can be used, look at [Program.cs](Program.cs)

```cs
// Create dbhelper
DatabaseHelper databaseHelper = new DatabaseHelper("../config.json");

// open connection
databaseHelper.Open();  

// performs db operations
databaseHelper.InsertAudioStreamPrediction(...);
databaseHelper.InsertAudioStreamInfo(...);

// close connection
databaseHelper.Close();
```


## `LogHelper` API

* Logging functionality implemented using [Serilog](https://github.com/serilog/serilog)
* Log config defined in a json file that is passed into the `LogHelper` constructor

```cs
using Serilog;

// pass in your class context so that it can be printed in the logs
ILogger logger = new LogHelper<DatabaseHelper>("../config.json").Logger;

logger.Information(...);   // "... [DatabaseHelper:INF] ..."
logger.Error(...);         // "... [DatabaseHelper:ERR] ..."
```

## Using in Other Projects

* [Reference](https://stackoverflow.com/questions/41982643/how-to-organize-multiple-projects-in-an-asp-net-core-solution-like-ddd)
* copy and clone this project to a folder on the same level as your project
* add reference to this project using dotnet CLI or otherwise

```bash
$ dotnet add reference ../database_and_log/database_and_log.csproj
$ dotnet build
```

* add a copy of [config.json](config.json) to your project directory with the relevant credentials
* import `database_and_log` namespace and use `DatabaseHelper` and `LogHelper`

```cs
using database_and_log;
using Serilog;

ILogger logger = new LogHelper<Program>("../config.json").Logger;
logger.Information("Hello World!");

DatabaseHelper database = new DatabaseHelper("../config.json");
```
