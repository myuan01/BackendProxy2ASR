# BackendProxy2ASR
 Backend source code for ASR project

**Agenda**
1)	[Start ASR Engine Docker](#Start-ASR-Engine-Docker)
2)	[Configuration Setting](#Configuration-Setting)
3)	[Start Backend Proxy Service](#Start-Backend-Proxy-Service)
4)	[Authentication](#Authentication)
5)	[Client Simulator](#Client-Simulator)
6)	[ASR Engine Simulator](#Start-ASR-Simulator)
7)	[Reporting](#Reporting)
8)  [Database](#Database)


##	Start ASR Engine Docker

* Download zip file for ASR Engine from given link. If the file is saved as `*.tgz`, unzip it. Make sure the engine file is end with `*.tar` extension
* Docker service is required to load docker file. 
  * For Windows: [install docker desktop](https://docs.docker.com/docker-for-windows/install/)
  * For Linux: [install docker](https://docs.docker.com/engine/install/)
* Go to docker tar file directory. Run
```bash
$ docker load --input [asrfile].tar
```
* Check if image is loaded
```bash
$ docker images

REPOSITORY          TAG                 IMAGE ID            CREATED             SIZE
[asrfile]           X.X										1 second ago         XX GB
```
* Start Docker container 
    * [Linux](https://docs.docker.com/engine/reference/commandline/start/) 
    * [Windows](https://docs.docker.com/docker-for-windows/)

##	Configuration Setting

Configuration file `config.json` contains below items:

* **"Proxy"**: 
  * "proxyPort": Port for Proxy service
  *  "asrIP": ASR Engine IP address e.g.'localhost'
  *  "asrPort": ASR Engine Port
  *  "samplerate": ASR Engine samplerate
  *  "maxConnection": Maximum number of websocket to ASR engine in connection pool

*  **"DummyServer"**:
   * "usingDummy": Whether use dummy ASR Engine
   * "dummyAsrIP": IP address for dummy ASR Engine
   * "dummyAsrPort": Port for dummy ASR Engine
   * "maxConnection": Maximum number of websocket to dummy ASR engine in connection pool
  
*  **"Database"**: 
   * "ToConnect": Whether need to send data to database
   * "Host": IP address for database
   * "Username": Username for database
   * "Password": Password for database user
   * "Database": Database name

*  **"Auth"**:
   *  "ToAuthenticate": Whether enable authentication check

*  **"Serilog"**: Please find in Reporting session

##	Start Backend Proxy Service
### Windows
* Start Docker ASR contianer
* Change relavent session in `config.json` and put `config.json` in same directory with `BackendProxy2ASR.exe`
* Double click `BackendProxy2ASR.exe` to start proxy service

### Linux
* Start Docker ASR contianer
* Install **.NET Core 3.1** 
* Change relavent session in `config.json` and put `config.json` in same directory with `BackendProxy2ASR.zip`
* Put `deploy.py` in same directory with `BackendProxy2ASR.zip`
* Run below command
```bash
python deploy.py BackendProxy2ASR.zip config.json
```

##	Client Simulator
A light-weight windows-based client simluator can be found [here](https://github.com/myuan01/DemoFEWs).
 * Open `DemoFEWs.sln` with Visual Studio
 * Install required packages
 * Change `url` in `Main` to correct Backend Proxy address and port
 * Build solution and run
