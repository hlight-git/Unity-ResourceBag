using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace Hlight.ResourceBag.Tests
{
    /// Agent-only runner — MCP can invoke a menu item but not TestRunnerApi directly.
    public static class AgentTestRunner
    {
        private const string RESULT_PATH = "Temp/resource-bag-tests.txt";
        private const string ASSEMBLY = "Hlight.ResourceBag.Tests";

        // Must stay static: TestRunnerApi is a ScriptableObject. If it is collected the
        // registered callback dies and RunFinished never fires.
        private static TestRunnerApi api;

        // Registered exactly once. RegisterCallbacks is additive, so calling it per run
        // stacked a fresh writer every time.
        private static ResultWriter writer;

        public static void Run()
        {
            if (File.Exists(RESULT_PATH)) File.Delete(RESULT_PATH);
            if (File.Exists(RESULT_PATH + ".started")) File.Delete(RESULT_PATH + ".started");

            if (api == null) api = ScriptableObject.CreateInstance<TestRunnerApi>();
            if (writer == null)
            {
                writer = new ResultWriter();
                api.RegisterCallbacks(writer);
            }

            api.Execute(new ExecutionSettings(new Filter
            {
                testMode = TestMode.EditMode,
                assemblyNames = new[] { ASSEMBLY }
            }));
        }

        // TestRunnerApi callbacks are global — they fire for EVERY run in the editor,
        // including ones another package's runner or the Test Runner window started, and this
        // project has five such runners. So a result has to be identified, not assumed.
        //
        // Identify it from the callback argument, never from static state: a domain reload
        // mid-run wipes statics, and a flag-based guard then discards the real result in
        // silence. Both failure modes were observed here — another suite's numbers landing in
        // our file, and then a lost run after adding a file to the Core assembly.
        private static bool IsOurs(ITestAdaptor test)
        {
            if (test == null) return false;
            if (!test.HasChildren) return test.FullName != null && test.FullName.StartsWith(ASSEMBLY);
            if (test.Children == null) return false;
            foreach (var child in test.Children)
            {
                if (IsOurs(child)) return true;
            }

            return false;
        }

        private class ResultWriter : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun)
            {
                if (!IsOurs(testsToRun)) return;
                File.WriteAllText(RESULT_PATH + ".started", testsToRun.TestCaseCount.ToString());
            }

            public void RunFinished(ITestResultAdaptor testResults)
            {
                if (!IsOurs(testResults?.Test)) return;

                var builder = new StringBuilder();
                builder.Append("PASS=").Append(testResults.PassCount)
                       .Append(" FAIL=").Append(testResults.FailCount)
                       .Append(" SKIP=").Append(testResults.SkipCount).Append('\n');
                Collect(testResults, builder);
                File.WriteAllText(RESULT_PATH, builder.ToString());
            }

            private static void Collect(ITestResultAdaptor node, StringBuilder builder)
            {
                if (!node.HasChildren)
                {
                    builder.Append(node.TestStatus).Append(' ').Append(node.FullName).Append('\n');
                    if (node.TestStatus == TestStatus.Failed)
                        builder.Append("  ").Append(node.Message).Append('\n');
                }

                if (node.Children == null) return;
                foreach (var child in node.Children) Collect(child, builder);
            }

            public void TestStarted(ITestAdaptor test) { }
            public void TestFinished(ITestResultAdaptor result) { }
        }
    }
}
