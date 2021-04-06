﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Connections;
using Rxns;
using Rxns.Cloud;
using Rxns.DDD;
using Rxns.Health;
using Rxns.Health.AppStatus;
using Rxns.Hosting;
using Rxns.Hosting.Updates;
using Rxns.Interfaces;
using Rxns.Logging;
using Rxns.Metrics;
using Rxns.NewtonsoftJson;
using Rxns.Playback;
using Rxns.WebApiNET5;
using Rxns.WebApiNET5.NET5WebApiAdapters;
using theBFG.TestDomainAPI;

namespace theBFG
{
    //todo:
    //need to push bfg to nuget
    //need to push rxns webapi to nuget
    //need to allow the webapi to startup in isolation or with config options to turn off rxns services, allow appstatus portal to be overriden?
    public static class theBFGDef
    {
        public static IObservable<StartUnitTest[]> Cfg;

        public static string DetectTestFramework(string[] args)
        {
            return args.ToStringEach(",").Split("using")[1].Split(',')[1];
        }

        public static Func<string[], Action<IRxnLifecycle>> TestArena = (args) =>
        {
            RxnExtensions.DeserialiseImpl = (t, json) => JsonExtensions.FromJson(json, t);
            RxnExtensions.SerialiseImpl = (json) => JsonExtensions.ToJson(json);
            string ver = string.Empty;

            if (args.Contains("using"))
            {
                ver = DetectTestFramework(args);
            }

            return dd =>
            {
                //DistributedBackingChannel.For(typeof(ITestDomainEvent))(dd);
                if (Cfg != null)
                    dd.CreatesOncePerApp(_ => Cfg);

                dd.CreatesOncePerApp(_ => theBFGAspNetCoreAdapter.AspnetCfg);

                //d(dd);
                dd.CreatesOncePerApp<theBfg>()
                    .CreatesOncePerApp<bfgCluster>()
                    .CreatesOncePerApp<SsdpDiscoveryService>()
                    .Includes<RxnsModule>()
                    .Includes<AppStatusClientModule>()
                    .CreatesOncePerApp<NestedInAppDirAppUpdateStore>()
                    .Includes<AspNetCoreWebApiAdapterModule>()
                    .CreatesOncePerApp<bfgWorkerDoWorkOrchestrator>()
                    .CreatesOncePerApp<bfgTestArenaProgressView>()
                    .CreatesOncePerApp<bfgTestArenaProgressHub>()
                    .RespondsToSvcCmds<Reload>()
                    .Emits<UnitTestResult>()
                    .Emits<UnitTestPartialResult>()
                    .Emits<UnitTestPartialLogResult>()
                    .CreatesOncePerApp<RxnManagerCommandService>() //fixes svccmds
                    .CreatesOncePerApp(_ => new AspnetCoreCfg()
                    {
                        Cfg = aspnet =>
                        {
                            aspnet.UseEndpoints(e =>
                            {
                                e.MapHub<bfgTestArenaProgressHub>("/testArena", o =>
                                {
                                    o.Transports =
                                        HttpTransportType.WebSockets |
                                        HttpTransportType.LongPolling;
                                });
                            });
                        }
                    })
                    .CreatesOncePerApp(_ => new AppStatusCfg()
                    {
                        ShouldAutoUnzipLogs = true
                    })
                    //cfg specific
                    .CreatesOncePerApp(() => new AggViewCfg()
                    {
                        ReportDir = "reports"
                    })
                    .CreatesOncePerApp(() => new AppServiceRegistry()
                    {
                        AppStatusUrl = $"http://{RxnApp.GetIpAddress()}:888"
                    })
                    .CreatesOncePerApp(_ => new RxnDebugLogger("bfgCluster"))
                    .CreatesOncePerApp<INSECURE_SERVICE_DEBUG_ONLY_MODE>()
                    .CreatesOncePerApp<UseDeserialiseCodec>()
                    .CreatesOncePerApp(_ => new DynamicStartupTask((log, resolver) =>
                    {
                        TimeSpan.FromSeconds(1).Then().Do(_ =>
                        {

                            if (theBFGDef.Cfg == null)
                                theBFGDef.Cfg = theBFG.theBfg.DetectAndWatchTargets(args, resolver.Resolve<ITestArena>());

                            var theBfg = resolver.Resolve<theBfg>();
                            var stopArena = theBfg.StartTestArena(args, Cfg, resolver);
                            var stopWorkers = theBfg.StartTestArenaWorkers(args, Cfg, resolver).Until();
                        }).Until();

                    }))
                    .CreatesOncePerApp<InMemoryTapeRepo>()
                    ;

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    dd.CreatesOncePerApp<WindowsSystemInformationService>();
                }

                if (ver == string.Empty || ver.StartsWith("dotnet", StringComparison.InvariantCultureIgnoreCase))
                {

                    dd.CreatesOncePerApp<DotNetTestArena>();
                }
                else
                {
                    dd.CreatesOncePerApp<VsTestArena>();
                }

            };
        };

        public static Func<string, string, Action<IRxnLifecycle>, Action<IRxnLifecycle>> TestWorker = (apiName, testHostUrl, d) =>
        {
            return dd =>
            {
                d(dd);

                dd.
                CreatesOncePerApp<theBfg>()
                .CreatesOncePerApp<SsdpDiscoveryService>()
                    .CreatesOncePerApp<TaggedServiceRxnManagerRegistry>()
                    .CreatesOncePerApp<bfgCluster>()
                    .RespondsToSvcCmds<StartUnitTest>()
                    .CreatesOncePerApp<AppStatusClientModule>()
                    .CreatesOncePerApp<NestedInAppDirAppUpdateStore>()
                    .CreatesOncePerApp<DotNetTestArena>()
                  //      .CreatesOncePerApp<VsTestArena>()
                    .Emits<UnitTestResult>()
                    .Emits<UnitTestPartialResult>()
                    .Emits<UnitTestPartialLogResult>()
                    .Emits<UnitTestOutcome>()
                    .Emits<UnitTestsStarted>()
                .CreatesOncePerApp(_ => new RxnDebugLogger("bfgWorker"))
                    .CreatesOncePerApp(_ => new AppServiceRegistry()
                    {
                        AppStatusUrl = testHostUrl
                    })
                    .CreatesOncePerApp(_ => new DynamicStartupTask((log, resolver) =>
                    {
                        var theBfg = resolver.Resolve<theBfg>();
                        var stopWorkers = theBfg.StartTestArenaWorkers(theBfg.Args, Cfg, resolver).Until();
                    }));
                
                //forward all test events to the test arena
                DistributedBackingChannel.For(typeof(ITestDomainEvent))(dd);

                if (theBfg.Args.Contains("using"))
                {
                    var ver = DetectTestFramework(theBfg.Args);
                    if (ver == string.Empty || ver.StartsWith("dotnet", StringComparison.InvariantCultureIgnoreCase))
                    {

                        dd.CreatesOncePerApp<DotNetTestArena>();
                    }
                    else
                    {
                        dd.CreatesOncePerApp<VsTestArena>();
                    }
                }
            };
        };
    }
}
