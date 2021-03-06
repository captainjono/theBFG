using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Runtime.InteropServices;
using Rxns;
using Rxns.Cloud;
using Rxns.Cloud.Intelligence;
using Rxns.Collections;
using Rxns.DDD.Commanding;
using Rxns.Health;
using Rxns.Hosting;
using Rxns.Hosting.Updates;
using Rxns.Interfaces;
using Rxns.Logging;
using theBFG.TestDomainAPI;

namespace theBFG
{
    /// <summary>
    /// 
    /// </summary>
    public class bfgWorker : IClusterWorker<StartUnitTest, UnitTestResult>, IServiceCommandHandler<TagWorker>, IServiceCommandHandler<UntagWorker>, IServiceCommandHandler<Run>
    {
        private readonly IAppServiceDiscovery _services;
        private readonly IAppStatusServiceClient _appStatus;
        private readonly IRxnManager<IRxn> _rxnManager;
        private readonly IUpdateServiceClient _updateService;
        private readonly IAppStatusCfg _cfg;
        private readonly Func<ITestArena[]> _arena;
        private readonly IAppServiceRegistry _registry;
        private int _runId;
        public void Update(IDictionary<string, string> eventInfo)
        {
            Info = eventInfo;
        }

        public string Name { get; }
        public string Route { get; }
        public IDictionary<string, string> Info { get; set; } = new Dictionary<string, string>();
        public IObservable<bool> IsBusy => _isBusy;
        private readonly ISubject<bool> _isBusy = new BehaviorSubject<bool>(false);
        private readonly IZipService _zipService;

        public IEnumerable<string> Tags => bfgTagWorkflow.GetTagsFromString(Info[bfgTagWorkflow.WorkerTag]);

        public bfgWorker(string name, string route, string[] tags, IAppServiceRegistry registry, IAppServiceDiscovery services, IZipService zipService, IAppStatusServiceClient appStatus, IRxnManager<IRxn> rxnManager, IUpdateServiceClient updateService, IAppStatusCfg cfg, Func<ITestArena[]> arena)
        {
            _registry = registry;
            _services = services;
            _zipService = zipService;
            _appStatus = appStatus;
            _rxnManager = rxnManager;
            _updateService = updateService;
            _cfg = cfg;
            _arena = arena;
            Name = name;
            Route = route;
            Info.Add(bfgTagWorkflow.WorkerTag, tags.ToStringEach());
        }

        public IObservable<CommandResult> Handle(TagWorker command)
        {
            return Rxn.Create(() =>
            {
                Info[bfgTagWorkflow.WorkerTag] = $"{Info[bfgTagWorkflow.WorkerTag]},{command.Tags.ToStringEach()}";

                return CommandResult.Success().AsResultOf(command).ToObservable();
            });
        }

        public IObservable<CommandResult> Handle(UntagWorker command)
        {
            return Rxn.Create(() =>
            {
                Info[bfgTagWorkflow.WorkerTag] = Info[bfgTagWorkflow.WorkerTag].Split(',').Except(command.Tags).ToStringEach();

                return CommandResult.Success().AsResultOf(command).ToObservable();
            });
        }

        public IObservable<UnitTestResult> DoWork(StartUnitTest work)
        {
            work.Dll = work.Dll.EnsureRooted();

            var logId = $"{Name.Replace("#", "")}_{++_runId}_{DateTime.Now:dd-MM-yy-hhmmssfff}";
            var logDir = Path.Combine(_cfg.AppRoot, "logs", logId).AsCrossPlatformPath();
            StreamWriter testLog = null;
            FileStream logFile = null;

            return Rxn.Create(() => //setup dirs for test
            {
                if (!Directory.Exists(logDir))
                {
                    Directory.CreateDirectory(logDir);
                }

                logFile = File.Create(Path.Combine(logDir, "testArena.log"));
                testLog = new StreamWriter(logFile, leaveOpen: true);
            })
            .SelectMany(_ =>  //keep the test updated while we are running it
            {
                _isBusy.OnNext(true);
                
                return RunTestSuiteInTestArena(work, testLog, logDir);
            })
            .LastOrDefaultAsync()
            .Delay(TimeSpan.FromSeconds(1))//test is finished, wait for log file to unlock
            .SelectMany(_ => 
            {
                try
                {
                    testLog?.Dispose();
                    logFile?.Dispose();
                }
                catch (Exception e)
                {
                    $"While closing log {e}".LogDebug();
                }

                //send the log to the 
                return ShipLogForTest(logDir, logId, work.Id); //send logs to testArena
            })
            .Select(_ => //return result of process
            {
                return (UnitTestResult)new UnitTestResult()
                {
                    WasSuccessful = true
                }.AsResultOf(work);
            })
            .Catch<UnitTestResult, Exception>(e =>
            {
                return ((UnitTestResult) new UnitTestResult()
                {
                    WasSuccessful = false
                }.AsResultOf(work)).ToObservable();
            })
            .FinallyR(() =>
            {
                _isBusy.OnNext(false);
            });
        }

        public IObservable<Unit> RunTestSuiteInTestArena(StartUnitTest work, StreamWriter testLog, string logDir)
        {
            $"Preparing to run {(work.RunAllTest ? "All" : work.RunThisTest)} in {work.Dll}".LogDebug();

            if (!Directory.Exists(logDir))
            {
                Directory.CreateDirectory(logDir);
            }

            var keepTestUpdatedIfRequested =
                work.UseAppUpdate.ToObservable(); //if not using updates, the dest folder is our root

            if (!File.Exists(work.Dll))
            {
                if (work.UseAppUpdate.IsNullOrWhitespace())
                {
                    _rxnManager.Publish(new UnitTestResult() { WasSuccessful = false, Message = $"Cannot find target @{work.Dll}".LogDebug() }.AsResultOf(work));
                    return Rxn.Empty<Unit>();
                }

                keepTestUpdatedIfRequested = _updateService.KeepUpdated(work.UseAppUpdate, work.UseAppVersion,  theBfg.GetTestSuiteDir(work.UseAppUpdate, work.UseAppVersion), new RxnAppCfg()
                {
                    AppStatusUrl = work.AppStatusUrl.IsNullOrWhiteSpace("http://localhost:888"),
                    SystemName = work.UseAppUpdate,
                    KeepUpdated = true
                }, true);
            }
            else
            {
                work.Dll = FindIfNotExists(work.Dll);
            }
            
            return keepTestUpdatedIfRequested //run the test
                .Select(testPath =>
                {
                    $"Running {work.Dll}".LogDebug();

                    foreach (var arena in _arena())
                    {
                        try
                        {
                            var tests = arena.ListTests(work.Dll).WaitR();
                            if (tests.AnyItems())
                                return arena.Start(Name, work, testLog, logDir).SelectMany(_ => _rxnManager.Publish(_));
                        }
                        catch (Exception)
                        {
                            continue;
                        }
                    }

                    "Argh, couldnt find a test arena to run this test".LogDebug();
                    _rxnManager.Publish(new UnitTestResult(){ WasSuccessful = false, Message = $"No test arena on host is compatible with {work.Dll}"}.AsResultOf(work));
                    return Rxn.Empty<Unit>();
                })
                .Switch();
        }
        

        /// <summary>
        /// Sends a set of logs to the test arena, returning a url to access the test via
        /// </summary>
        /// <param name="logDir"></param>
        /// <param name="logId"></param>
        /// <param name="testId"></param>
        /// <returns></returns>
        public IObservable<string> ShipLogForTest(string logDir, string logId, string testId)
        {
            var logsZip = ZipAndTruncate(logDir, $"{logId}_{DateTime.Now:dd-MM-yy-hhmmssfff}");
            var sendingLogs = File.OpenRead(logsZip);
            return _appStatus.PublishLog(sendingLogs).Do(logUrl =>
            {
                _rxnManager.Publish(new UnitTestAssetResult()
                {
                    LogUrl = logUrl,
                    TestId = testId,
                    Worker = Name
                }).Until();

                sendingLogs.Dispose();
                File.Delete(logsZip);
            });
        }

        private string ZipAndTruncate(string dir, string logId)
        {
            if (dir.IsNullOrWhitespace() || (dir.Length < 3 && dir[0] == '/' || dir[1] == ':'))
            {
                throw new Exception($"Cant achieve {dir}");
            }

            
            var logFile = Path.Combine(_cfg.AppRoot, "logs", $"{logId}.zip");

            if (!Directory.Exists(dir))
            {
                return logFile;
            }



            using (var file = File.Create(logFile))
                _zipService.Zip(dir).CopyTo(file);

            Directory.Delete(dir, true);

            return logFile;
        }

        private string FindIfNotExists(string workDll, string parent = null)
        {
            if (File.Exists(workDll))
                return workDll;

            throw new Exception($"Test Not Found: {workDll}");
        }

        public IDisposable DiscoverAndDoWork(string apiName = null, string testHostUrl = null)
        {
            $"Attempting to discover work {apiName ?? "any"}@{testHostUrl ?? "any"}".LogDebug();

            var allDiscoveredApiRequests = bfgTestArenaApi.DiscoverWork(_services).Do(apiFound =>

            {
                $"Discovered Api Hosting: {apiFound.Name}@{apiFound.Url}".LogDebug();
                _registry.AppStatusUrl = apiFound.Url;

                TimeSpan.FromSeconds(1).Then().Do(_ =>
                {
                    _rxnManager.Publish(new PerformAPing()).Until();

                });

                //from here on, heartbeats will return work for us to do
            });

            return apiName.IsNullOrWhitespace()
                ? allDiscoveredApiRequests.Until()
                : allDiscoveredApiRequests.Where(foundApi => apiName == null || foundApi.Name.BasicallyEquals(apiName)).Until();
        }



        public IObservable<CommandResult> Handle(Run command)
        {
            var tokens = command.Cmd.Split(' ');
            var cmd = tokens[0];
            var worker = Name;

            _rxnManager.Publish(new UnitTestsStarted()
            {
                TestId = command.Id,
                At = DateTime.Now,
                Tests = new[] { cmd },
                Worker = "", //_appInfo.Name,
                WorkerId = worker
            }).Until();

            var unitTestId = Guid.NewGuid().ToString();

            _rxnManager.Publish(new UnitTestPartialResult(command.Id, "passed", cmd, "0", worker)
            {
                UnitTestId = unitTestId
            }).Until();

            return Rxn.Create
            (
                RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "cmd" : "/bin/bash",
                $"{(RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "/c" : "-c")} {command.Cmd}",
                i =>
                {
                    if (i == null) return;


                    _rxnManager.Publish(new UnitTestPartialLogResult
                    {
                        LogMessage = i.ToString(),
                        TestId = command.Id,
                        UnitTestId = unitTestId,
                        Worker = worker
                    }).Until();

                },
                e =>
                {
                    _rxnManager.Publish(new UnitTestPartialLogResult
                    {
                        LogMessage = e.ToString(),
                        TestId = command.Id,
                        UnitTestId = unitTestId,
                        Worker = worker
                    }).Until();
                }
            )
            .FinallyR(() =>
            {
                _rxnManager.Publish(new UnitTestOutcome()
                {
                    Passed = 1,
                    Failed = 0,
                    InResponseTo = command.Id,
                    UnitTestId = unitTestId,
                    Dll = cmd
                }).Until();
            })
            .Select(_ => CommandResult.Success());

        }
    }
}