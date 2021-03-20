<div><h1>TheBFG
<img src="thebfg.png"/>
</h1>
</div> 

> A cloud-native C#  *unit* / *integration* / *load* testing **tool** that *`clusters`* *automatically*


Feature | ...
-|-
`Zero-configuration` | <font size=2> the host will automatically detect listeners using the `SSDP` protocal (*but yes, you can provide config in multiple ways if required*)</font>
`Supercharged CD` | <font size=2>Verfy light-weight. Deploys apps in `ms`. Save time, and frustration, doing more testing, and less waiting or configuring.</font>
`Real-time reporting` | <font size=2>Use the `theBFG` `host console` to monitor in real-time all aspects of your tests including errors, CPU, MEM and throughput</font>
`NO DSL or JAVA or other` scripting to learn | Use all the same tools you love & pure C# to define complex intergation test scenarios</font>
`Continious Fuzzy testing` |<font size=2> `theBFG` can execute your test suites in different ways, not just in different orders</font>
`Create worker clusters` with ease | <font size=2>Simply run `theBFG` on as many instances as you required, 
`Supercharge EXISTING test-suites` | <font size=2>theBGF supports distributing work to a cluster that will work together to execute it as quickly as possible</font>
`Cloud scaleout` | <font size=2>To really push the boundaries of your App, Cloud functions can be used to host workers</font>
`NO API bleed` | <font size=2>If you out-grow `theBFG` or otherwise dont require its sevices anymore, delete its reference, its config classes and its gone. No refactor required.</font>


# TheBFG *flow*

1. Code your `app`

2. Create a `unit-test`

3. Install bfg

    `dotnet install tool thebfg`

5. Configure your `IDE` to deploy your `unit-test` on compile:
 
*AfterBuild:* `dotnet tool thebfg target {path/To/unit.test.dll}`
> Pro Tip: supports *wildcards* `*.tests.dll`, will search recursively

5. Spin up worker on the *same computer*, **or** **many *others***
   
`dotnet tool theBFG fire`

... and thats it. Each time you `compile` your `unit-test` will be deployed to all the workers you have setup and they will run the tests as you commanded.

*<h2>Integration or Automation test? Start the SUT and monitor it for failures</h2>*

6. Configure your `IDE` to deploy the  `app-under-test` on compile:
   
`dotnet thebfg launch {app} {path/To/unit.test.dll}`

7. Update your `test.dll` to download the App and get a reference to it

`nuget install thebfg`

```c#
// will install App into version folders nested -> app/target/dir/{version}/*.*
// will continuely monitor for fresh app updates and automatically update
// and launch it without having to restart your test - great for continuious
// stress testing or hyper CI/CD scenarios
var stop = theBFG.launch("app", "app/target/dir").Until(); 
```

....
repeat for as many `Apps` as your integration test depends on.

# Clustering

By default, your tests will run in the same ordering and sequence as your `IDE`

To alter the execution mode of a test, simply end any launch command with a mode

Mode | Application
-|-
`Continious` | Runs a test on a loop over and over, logging any exceptions and restarting after any crash
  Options| **exitAfter**: *timespan* 
`Burst` | cluster will run test in loop modes at intervals that will ebb-and-flow over time
Options | **rampUp**: *timespan* \| **minBurst**: *timespan* \| **maxBurst**: *timespan*
`Compete` | makes the cluster work through an entire test-suite in a *first* **exit**, *first* **wins** scenario
Options | **batchSize**: *number* \| *reactive* <font size=1>(watches the consumption rate and auto-adjustes for fastest speed)</font>
`Rapid` | each test agent in the cluster will spawn a number of worker processes, and run the test on each thread. Combine this with `dotnet` thread multi-threading and to expose any bottlenecks in your architectureƒs
  Options| **runsInParallal**: *timespan* 
  ^
> **Pro tip:** use the bfgAPI to better control and *instead* feed a `IObservable<Timespan>` to any of these *options*!

# The bfgConsole

The console allows you to understand whats going on *in realtime*. It a minimal layer that is designed to be plugged into other systems / charts / CD pipelines

## Own your data

All metics can be exported at the end of a run

`dotnet thebfg reload {export/Path}`

## Monitor Errors

Any exception that is thrown in your test is surfaced through the portal

<img src=broken.jpg>

## Real-time logs

Watch application output as it happens

Augment the stream with your own logging

`Cluster.Log.OnInfo/OnError/etc(msg)`

<img src=broken.jpg>

## Real-time metrics

Access Test Arena metrics and other bfg cluster vitals through a web-browser

<img src=broken.jpg>

 ... and you can add your own data points through the `bfgAPI`

 ```
 ```

## Real-time ports 

use [SignalR](http://dotnet.microsoft.com/) to subscribe to a real-time event feed using the `bfgAPI` to customise the integration for your specific  `use-case` ie. logging to [AppInsights](http://azure.microsoft.com/)


```
```

<img src=broken.jpg>

## Manage libraries of Apps

<img src=broken.jpg>


Changelog|comment
-|-
1.0-beta | (in progress) Baseline API implemention