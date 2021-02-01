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
Usage: demo configPath

$ dotnet run ../config.json
01-02-2021 17:56:29 database_and_log.DatabaseHelper [INF] Opening database connection...
01-02-2021 17:56:29 database_and_log.Program [INF] Opening connection success? : True
01-02-2021 17:56:29 database_and_log.DatabaseHelper [INF] Storing prediction for session session_id_3, seq 10.
01-02-2021 17:56:29 database_and_log.DatabaseHelper [INF] Database insertion complete.
01-02-2021 17:56:29 database_and_log.DatabaseHelper [INF] Storing stream info for session session_id_3, seq 10.
01-02-2021 17:56:29 database_and_log.DatabaseHelper [INF] Database insertion complete.
01-02-2021 17:56:29 database_and_log.DatabaseHelper [INF] Closing database connection...
01-02-2021 17:56:29 database_and_log.Program [INF] Closing connection success? : True

```

## `DatabaseHelper` API

* Database functionality implemented using [Npgsql](https://www.npgsql.org/)
* For an example on how `DatabaseHelper` can be used, look at [Program.cs](Program.cs)

```cs
IConfiguration config = new ConfigurationBuilder()
    .AddJsonFile(path: configPath, optional: false, reloadOnChange: true)
    .Build();

// Create dbhelper
DatabaseHelper databaseHelper = new DatabaseHelper(config);

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
* Log config defined in a json file that is passed into `LogHelper.Initialize`
* Call `LogHelper.Initialize` first to initialize the LogHelper class
* Init `LogHelper` as soon as possible (when program starts) so that other classes can get use `LogHelper.GetLogger`

```cs
using Serilog;

IConfiguration config = new ConfigurationBuilder()
    .AddJsonFile(path: configPath, optional: false, reloadOnChange: true)
    .Build();

// Init LogHelper
LogHelper.Initialize(config);
ILogger logger = LogHelper.GetLogger<Program>();

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

* import `database_and_log` namespace and use `DatabaseHelper` and `LogHelper`

```cs
using database_and_log;
using Serilog;

IConfiguration config = new ConfigurationBuilder()
    .AddJsonFile(path: configPath, optional: false, reloadOnChange: true)
    .Build();

LogHelper.Initialize(config);
ILogger logger = LogHelper.GetLogger<Program>();
logger.Information("Hello World!");

DatabaseHelper database = new DatabaseHelper(config);
```
