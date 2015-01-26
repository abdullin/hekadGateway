Library for sending logs and statistics to [Mozilla Hekad](https://hekad.readthedocs.org).

> This library is probably too specific for you needs, but you can use it as an example of sending logs and statistics to Hekad daemon or any other devops tool.

Internally HekadGateway launches an embedded version of hekad executable, preconfigured to forward all logs and statistics to a remote hekad daemon via TLS channel. It also wires `StatsdClient` library for statistics reporting to hekad and a `Serilog` logger for logging.

> This library can work on Windows Azure (as a part of Worker Role).

Before use, you need to setup your Hekad server, get private key and certificate for the client. Then, on the startup, initialize:

```
_hekad = Hekad.ConfigureAndLaunch(
				logPath.RootPath,
				"my-deployment-name",
				RoleEnvironment.CurrentRoleInstance.Id,
				"logs.yourserver.com",
				authority,
				certificate,
				privateKey,
				loggerConfiguration,
				minLevel);
```

**Important**: you would need to run your process as Admin (elevated for WorkerRole)
in order for gateway to work. Probably this is related to the fact that Hekad binds to UDP
port for statistics (8125).

After the initialization you could use:

* Serilog.Log for logging (directly or through SkuVault logging adapter)
* StatsdClient.Metrics for statistics reporting