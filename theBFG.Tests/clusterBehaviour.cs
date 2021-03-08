﻿using System;
using System.Diagnostics;
using System.Linq;
using System.Net.WebSockets;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Rxns;
using Rxns.Cloud;
using Rxns.Logging;
using Rxns.NewtonsoftJson;
using theBFG.TestDomainAPI;

namespace theBFG.Tests
{
    [TestClass]
    public class clusterBehaviour
    {
        [TestInitialize]
        public void Setup()
        {
            
        }
        
        [TestMethod]
        public void should_keep_updated()
        {
            //todo:
            //theBfg.ReloadAnd("launch testApp ../../../../theBfgtestApp/bin/debug/testApp.dll".Split(' ')).WaitR();
        }

        [TestMethod]
        public void should_auto_detect_workers()
        {
            ReportStatus.Log.ReportToConsole();
            var foundWorker = new Subject<Unit>();

            var autoDiscovery = new SsdpDiscoveryService();
            bfgTestApi.AdvertiseForWorkers(autoDiscovery, "compete");

            bfgTestApi
                .DiscoverWork(autoDiscovery, "compete")
                .FirstAsync()
                .Select(_ => new Unit())
                .Subscribe(foundWorker);

            foundWorker.FirstAsync().WaitR();
        }
        
        [TestMethod]
        public void should_detect_testagents()
        {
            try
            {
                var workerLifetime = new Subject<Unit>();
                theBfg.ReloadWithTestWorker(args: "fire".Split(' ')).LastAsync().Until();

                theBfg.ReloadWithTestArena(args: "target C:/svn/bfg/theTestGimp/bin/Debug/netcoreapp3.1/theTestGimp.dll".Split(' ')).LastAsync().Until();

                "waiting for test to complete".LogDebug();
                workerLifetime.WaitR();
            }
            catch (Exception e)
            {
               Assert.Fail(e.ToString());
            }
        }

        [TestMethod]
        public void should_support_rapid_fire_mode()
        {
            //this is failing now because the .dll is being downloaded incorrectly when
            //rapidly is appended download function
            //need to detect local vs remote worker

            theBfg.ReloadAnd(args: @"target C:/svn/bfg/theTestGimp/bin/Debug/netcoreapp3.1/theTestGimp.dll and fire rapidly".Split(' ')).LastAsync().Until();

            "waiting for test to complete".LogDebug();
            new Subject<int>().Wait();
        }


        [TestMethod]
        public void should_support_dotnet_test_arena()
        {
            var ta = new DotNetTestArena();

            var allTest = ta.ListTests(new StartUnitTest()
            {
                Dll = "C:/svn/bfg/theTestGimp/bin/Debug/netcoreapp3.1/theTestGimp.dll",
            }).WaitR().ToArray();

            allTest.Count().Should().Be(6, "4 tests should be found in theTestGimp");

            //todo: write unit test for start
            //ta.Start()
        }

        [TestMethod]
        public void should_upload_logs_on_completion()
        {

        }


        [TestMethod]
        public void should_support()
        {

        }
    }
}