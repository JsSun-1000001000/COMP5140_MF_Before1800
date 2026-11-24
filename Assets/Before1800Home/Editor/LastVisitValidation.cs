using System;
using System.IO;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace Before1800.Home.Editor
{
    [InitializeOnLoad]
    public static class LastVisitValidation
    {
        static TestRunnerApi runner;
        static LastVisitValidation()
        {
            runner = ScriptableObject.CreateInstance<TestRunnerApi>();
            runner.RegisterCallbacks(new ResultWriter());
        }
        [MenuItem("Before 18/Validate last visit")]
        public static void Run()
        {
            Unity.Collections.NativeLeakDetection.Mode = Unity.Collections.NativeLeakDetectionMode.EnabledWithStackTrace;
            runner.Execute(new ExecutionSettings(new Filter { testMode = TestMode.PlayMode, assemblyNames = new[] { "Before1800.PartB.Tests" } }));
        }
        sealed class ResultWriter : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun) { Debug.Log("LAST_VISIT_TESTS_STARTED"); }
            public void RunFinished(ITestResultAdaptor result)
            {
                Directory.CreateDirectory("Logs");
                File.WriteAllText(Path.GetFullPath("Logs/last-visit-tests.xml"), result.ToXml().OuterXml);
                Debug.Log("LAST_VISIT_TESTS_FINISHED pass=" + result.PassCount + " fail=" + result.FailCount + " skip=" + result.SkipCount);
            }
            public void TestStarted(ITestAdaptor test) { }
            public void TestFinished(ITestResultAdaptor result)
            {
                if (result.TestStatus == TestStatus.Failed) Debug.LogWarning("LAST_VISIT_TEST_FAILED " + result.Test.FullName + " " + result.Message);
            }
        }
    }
}
