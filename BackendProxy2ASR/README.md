# BackendProxy2ASR
 backend source code for ASR project

## Authentication

Two types of client authentication are available: `database` and `auth0`. To set the type of authentication, update the `Auth`.`AuthMethod` field in the config file.

### Database

Authentication using the `database` method uses a simple username and password system supported by the [pgcrypto](https://www.postgresql.org/docs/9.4/pgcrypto.html) extension in PostgreSQL.

To generate a new password:

```sql
-- Calculates a crypt(3)-style hash of password that is stored in the db
crypt('<password>', gen_salt('bf'))
```

To check a password:

```sql
SELECT user_id
FROM
    "user"
WHERE
    "user".username = username
    AND "user"."password" = crypt('<password>', "user"."password"));
```

Username and password is to be Base64 encoded and included in the `Authorization` field of the websocket connection HTTP header in the following format:

```
Basic <username>:<password>
```

An example is available in [BackendTest.cs](BackEndProxy.Tests/BackendTest.cs) under the `DatabaseTestWithCorrectCredentials` test:

```cs
Dictionary<string, string> headerOptions = new Dictionary<string, string>();
var plainTextBytes = System.Text.Encoding.UTF8.GetBytes("username:password");
headerOptions["Authorization"] = $"Basic {System.Convert.ToBase64String(plainTextBytes)}";
```

### Auth0

Client authentication using [Auth0](https://auth0.com/) is also supported. A access token will have to obtained by the client that is included in the `Authorization` field of the websocket connection HTTP header in the following format:

```
Bearer <accessToken>
```

An example is available in [BackendTest.cs](BackEndProxy.Tests/BackendTest.cs) under the `Auth0TestWithCorrectCredentials` test:

```cs
// get access token from Auth0 authentication server
HttpClient client = new HttpClient();
var stringContent = new StringContent(content, Encoding.UTF8, "application/json");

HttpResponseMessage authResponse = await client.PostAsync(authURL, stringContent);
authResponse.EnsureSuccessStatusCode();
string responseBody = await authResponse.Content.ReadAsStringAsync();
string accessToken = (string)JObject.Parse(responseBody)["access_token"];

// test proxy server
headerOptions["Authorization"] = $"Bearer {accessToken}";
```

For client authentication using Auth0, `Auth`.`Auth0Domain` and `Auth`.`Audience` fields are to be included in the config.

## ASR Engine Simulator

An ASR Engine simulator implemented in Python can be found at this [repository](https://github.com/kw01sg/websocket).

## Testing

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

Before running the tests:

1. Setup and seed database if required
2. Setup a proxy server with the appropriate config settings e.g. whether to authenticate clients, which type of client authentication method to use
3. Update [test_config.json](BackEndProxy.Tests/test_config.json) with required config settings
4. Run tests

Additional details on how to run tests can be found [here](BackEndProxy.Tests/README.md).

## Database

A PostgreSQL database for this application is setup using docker in another code repository [here](https://github.com/kw01sg/ai_toolbox_db).

## Reporting

A web application used for simple statistics reporting of the `asr_audio_stream_info` table in the database is available at this repository: [usage_api](https://github.com/kw01sg/usage_api)
